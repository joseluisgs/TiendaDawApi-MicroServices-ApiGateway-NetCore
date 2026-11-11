using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Refit;
using TiendaApi.Api.Dtos.Backup;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Dtos.Proveedores;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Backup;
using TiendaApi.Api.Services.Catalog;

namespace TiendaApi.Api.Services.Backup;

/// <summary>
///     Servicio de backup: exportación e importación de productos, categorías y
///     proveedores en zip, json o csv.
///     🎓 Tres decisiones que vale la pena entender:
///     <list type="number">
///         <item>
///             <b>El export escribe en streaming</b> (nunca monta el fichero entero
///             en memoria) y <b>el import lee con límites</b>: nº de entradas y tamaño
///             descomprimido, para que un zip hostil no sature al servidor.
///         </item>
///         <item>
///             <b>Los IDs se conservan</b>: las columnas de identidad son
///             <c>GENERATED ALWAYS</c> y rechazan valores explícitos, así que el alta con
///             ID propio va por SQL con <c>OVERRIDING SYSTEM VALUE</c> y después se
///             re-sincroniza la secuencia (si no, la siguiente creación chocaría).
///         </item>
///         <item>
///             <b>Los errores de fila no abortan</b>: se acumulan en el informe y
///             <c>Completo</c> indica si todo entró limpio. <c>dryRun</c> ejecuta el mismo
///             camino sin escribir nada.
///         </item>
///     </list>
///     Orden de importación: categorías → proveedores → productos, porque los
///     productos referencian a las otras dos.
/// </summary>
public class BackupService(
    ICatalogApi catalogApi,
    ILogger<BackupService> logger
) : IBackupService {
    /// <summary>Límite de tamaño del fichero subido (coincide con el RequestSizeLimit).</summary>
    public const int MaxBytesFichero = 10 * 1024 * 1024;

    /// <summary>Nº máximo de entidades de un ZIP.</summary>
    private const int MaxEntradasZip = ZipCodec.MaxEntradas;

    /// <summary>Tamaño máximo descomprimido.</summary>
    private const int MaxDescomprimido = ZipCodec.MaxBytesDescomprimidos;

    /// <summary>
    ///     🎓 Backup no tiene ni BD ni validadores propios (R5): el upsert por
    ///     clave, la validación de fila y la re-sincronización de secuencias los
    ///     hace <b>catalog</b> en sus endpoints internos. Aquí solo
    ///     se orquesta: leer por las rutas públicas y devolver el código que
    ///     catalog responda (201 creado · 200 actualizado · 204 sin cambios · 400 error).
    /// </summary>
    /// <summary>Formatos admitidos en export e import.</summary>
    public static readonly string[] Formatos = ["zip", "json", "csv"];

    /// <summary>Entidades exportables, en el orden en que se importan.</summary>
    public static readonly string[] Entidades = ["categorias", "proveedores", "productos"];

    /// <summary>
    ///     Opciones de JSON del backup en camelCase, como el resto de respuestas de la API.
    ///     Se comparten con los tests para que el round-trip lea lo mismo que escribe:
    ///     el contexto source-gen por defecto es PascalCase y el fichero no casaría.
    /// </summary>
    public static readonly JsonSerializerOptions OpcionesJson = new(AppJsonContext.Default.Options) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public (string ContentType, string NombreFichero) DatosSalida(string formato, IReadOnlyList<string> entidades) {
        string fecha = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string sufijo = entidades.Count == 1 ? entidades[0] : "backup";

        return formato switch {
            "json" => ("application/json; charset=utf-8", $"tienda-{sufijo}-{fecha}.json"),
            // El csv necesita una entidad por fichero: con más de una se empaqueta.
            "csv" => entidades.Count == 1
                ? ("text/csv; charset=utf-8", $"tienda-{sufijo}-{fecha}.csv")
                : ("application/zip", $"tienda-{sufijo}-{fecha}.zip"),
            _ => ("application/zip", $"tienda-{sufijo}-{fecha}.zip")
        };
    }

    /// <inheritdoc />
    public async Task<UnitResult<DomainError>> ExportarAsync(
        Stream salida, string formato, IReadOnlyList<string> entidades, bool incluirBorrados) {
        UnitResult<DomainError> validacion = ValidarPeticion(formato, entidades);
        if (validacion.IsFailure) {
            return UnitResult.Failure<DomainError>(validacion.Error);
        }

        // Se carga TODO antes de escribir: si algo falla, la respuesta aún no se ha tocado.
        DatosBackup datos = await CargarAsync(entidades, incluirBorrados);

        switch (formato) {
            case "json":
                await EscribirJsonAsync(salida, datos, entidades, incluirBorrados);
                break;
            case "csv":
                if (entidades.Count == 1) {
                    await EscribirCsvAsync(salida, entidades[0], datos);
                } else {
                    await EscribirZipAsync(salida, datos, incluirBorrados, true);
                }

                break;
            default:
                await EscribirZipAsync(salida, datos, incluirBorrados, false);
                break;
        }

        logger.LogInformation(
            "Backup exportado en formato {Formato}: {Categorias} categorías, {Productos} productos, {Proveedores} proveedores",
            formato, datos.Categorias.Count, datos.Productos.Count, datos.Proveedores.Count);

        return UnitResult.Success<DomainError>();
    }

    /// <inheritdoc />
    public async Task<Result<ImportReportDto, DomainError>> ImportarAsync(
        Stream fichero, string formato, IReadOnlyList<string> entidades, bool dryRun) {
        string[] formatos = string.IsNullOrWhiteSpace(formato) ? Formatos : [formato.Trim().ToLowerInvariant()];
        if (formatos.Length != 1 || !Formatos.Contains(formatos[0])) {
            return Result.Failure<ImportReportDto, DomainError>(BackupError.FormatoDesconocido(formato));
        }

        UnitResult<DomainError> validacion = ValidarPeticion(formatos[0], entidades);
        if (validacion.IsFailure) {
            return Result.Failure<ImportReportDto, DomainError>(validacion.Error);
        }

        var reloj = Stopwatch.StartNew();

        BackupDto? backup;
        try {
            backup = await LeerAsync(fichero, formatos[0], entidades);
        } catch (BackupException ex) {
            return Result.Failure<ImportReportDto, DomainError>(ex.Error);
        } catch (JsonException ex) {
            return Result.Failure<ImportReportDto, DomainError>(
                BackupError.FormatoInvalido($"JSON inválido ({ex.Message})"));
        } catch (InvalidDataException ex) {
            return Result.Failure<ImportReportDto, DomainError>(
                BackupError.FormatoInvalido(ex.Message));
        }

        if (backup is null) {
            return Result.Failure<ImportReportDto, DomainError>(
                BackupError.FormatoInvalido("el fichero está vacío o no contiene ninguna entidad pedida"));
        }

        var informes = new List<EntidadImportReportDto>();

        // Orden: primero las referencias, después quien las referencia.
        if (entidades.Contains("categorias") && backup.Categorias is not null) {
            informes.Add(await ImportarCategoriasAsync(backup.Categorias, dryRun));
        }

        if (entidades.Contains("proveedores") && backup.Proveedores is not null) {
            informes.Add(await ImportarProveedoresAsync(backup.Proveedores, dryRun));
        }

        if (entidades.Contains("productos") && backup.Productos is not null) {
            informes.Add(await ImportarProductosAsync(backup.Productos, dryRun));
        }

        // 🎓 La re-sincronización de secuencias y la invalidación de caché las
        // hace catalog al recibir el import (es el dueño de sus datos); aquí no
        // hay nada que hacer tras escribir.

        reloj.Stop();

        bool completo = informes.All(i => i.Errores.Count == 0);
        var informe = new ImportReportDto(dryRun, formatos[0], completo, reloj.ElapsedMilliseconds, informes);

        logger.LogInformation(
            "Backup importado ({Formato}, dryRun={DryRun}): completo={Completo} en {Ms} ms",
            formatos[0], dryRun, completo, informe.DuracionMs);

        return Result.Success<ImportReportDto, DomainError>(informe);
    }

    /// <inheritdoc />
    public Task<Result<CatalogoFormatosDto, DomainError>> ObtenerFormatosAsync() {
        var catalogo = new CatalogoFormatosDto(
            new List<FormatoDto> {
                new("zip", "application/zip", ".zip", "Manifest más un fichero por entidad (json o csv)"),
                new("json", "application/json; charset=utf-8", ".json", "Un único documento con manifest y entidades"),
                new("csv", "text/csv; charset=utf-8", ".csv",
                    "Una entidad por fichero (con más de una se empaqueta en zip)")
            },
            new List<EntidadCatalogoDto> {
                new("categorias", "id", ["id", "nombre", "descripcion", "isDeleted", "createdAt", "updatedAt"]),
                new("proveedores", "codigo",
                    ["codigo", "nombre", "descripcion", "contacto", "isDeleted", "createdAt", "updatedAt"]),
                new("productos", "id",
                [
                    "id", "nombre", "descripcion", "precio", "stock", "imagen", "categoriaId", "proveedorId",
                    "isDeleted", "createdAt", "updatedAt"
                ])
            },
            new LimitesBackupDto(MaxBytesFichero, MaxDescomprimido, MaxEntradasZip),
            new ReglasBackupDto(
                "por clave: id en categorías y productos, codigo en proveedores",
                "dryRun=true valida sin escribir nada",
                "categorias → proveedores → productos"));

        return Task.FromResult(Result.Success<CatalogoFormatosDto, DomainError>(catalogo));
    }

    // ────────────────────────────────────────────────────────────
    // Export
    // ────────────────────────────────────────────────────────────

    /// <summary>Carga las entidades pedidas (activas y, si se pide, también las borradas).</summary>
    private async Task<DatosBackup> CargarAsync(IReadOnlyList<string> entidades, bool incluirBorrados) {
        var datos = new DatosBackup();

        if (entidades.Contains("categorias")) {
            datos.Categorias = await CargarCategoriasAsync(incluirBorrados);
        }

        if (entidades.Contains("proveedores")) {
            datos.Proveedores = await CargarProveedoresAsync(incluirBorrados);
        }

        if (entidades.Contains("productos")) {
            datos.Productos = await CargarProductosAsync(incluirBorrados);
        }

        return datos;
    }

    private async Task<List<BackupCategoriaDto>> CargarCategoriasAsync(bool incluirBorrados) {
        List<CategoriaDto> activas = await PaginarAsync((p, s) => catalogApi.GetCategoriasAsync(p, s));
        if (!incluirBorrados) {
            return activas.Select(Mapear).ToList();
        }

        List<CategoriaDto> borradas = await PaginarAsync((p, s) => catalogApi.GetCategoriasBorradasAsync(p, s, true));
        return activas.Concat(borradas).Select(Mapear).ToList();
    }

    private async Task<List<BackupProveedorDto>> CargarProveedoresAsync(bool incluirBorrados) {
        List<ProveedorDto> activos = await PaginarAsync((p, s) => catalogApi.GetProveedoresAsync(p, s));
        if (!incluirBorrados) {
            return activos.Select(Mapear).ToList();
        }

        List<ProveedorDto> borrados = await PaginarAsync((p, s) => catalogApi.GetProveedoresBorradosAsync(p, s, true));
        return activos.Concat(borrados).Select(Mapear).ToList();
    }

    private async Task<List<BackupProductoDto>> CargarProductosAsync(bool incluirBorrados) {
        List<ProductoDto> activos = await PaginarAsync((p, s) => catalogApi.GetProductosAsync(p, s));
        if (!incluirBorrados) {
            return activos.Select(Mapear).ToList();
        }

        List<ProductoDto> borrados = await PaginarAsync((p, s) => catalogApi.GetProductosBorradosAsync(p, s, true));
        return activos.Concat(borrados).Select(Mapear).ToList();
    }

    /// <summary>Recorre un listado paginado hasta agotarlo (los endpoints son paginados).</summary>
    private static async Task<List<T>> PaginarAsync<T>(Func<int, int, Task<PagedResult<T>>> consulta) {
        var salida = new List<T>();
        const int size = 100; // máximo aceptado por la API

        for (int page = 0; page < 500; page++) {
            PagedResult<T>? pagina = await consulta(page, size);
            if (pagina?.Items is null) {
                break;
            }

            salida.AddRange(pagina.Items);
            if (!pagina.Items.Any() || salida.Count >= pagina.TotalCount) {
                break;
            }
        }

        return salida;
    }

    private static async Task EscribirJsonAsync(Stream salida, DatosBackup datos, IReadOnlyList<string> entidades,
        bool incluirBorrados) {
        // Las entidades no pedidas salen en null (documentado en el DTO): así el
        // cliente distingue "no lo pedí" de "no hay nada".
        var backup = new BackupDto(CrearManifest("json", datos, incluirBorrados),
            entidades.Contains("categorias") ? datos.Categorias : null,
            entidades.Contains("productos") ? datos.Productos : null,
            entidades.Contains("proveedores") ? datos.Proveedores : null);

        await JsonSerializer.SerializeAsync(salida, backup, OpcionesJson);
        await salida.FlushAsync();
    }

    private static async Task EscribirCsvAsync(Stream salida, string entidad, DatosBackup datos) {
        using StreamWriter escritor = CsvCodec.CrearEscritor(salida);
        switch (entidad) {
            case "categorias":
                CsvCodec.EscribirCabecera(escritor,
                    ["id", "nombre", "descripcion", "isDeleted", "createdAt", "updatedAt"]);
                foreach (BackupCategoriaDto c in datos.Categorias) {
                    CsvCodec.EscribirFila(escritor,
                    [
                        c.Id.ToString(CultureInfo.InvariantCulture), c.Nombre, c.Descripcion,
                        c.IsDeleted ? "true" : "false",
                        CsvCodec.Formatear(c.CreatedAt), CsvCodec.Formatear(c.UpdatedAt)
                    ]);
                }

                break;
            case "proveedores":
                CsvCodec.EscribirCabecera(escritor,
                    ["codigo", "nombre", "descripcion", "contacto", "isDeleted", "createdAt", "updatedAt"]);
                foreach (BackupProveedorDto p in datos.Proveedores) {
                    CsvCodec.EscribirFila(escritor,
                    [
                        p.Codigo, p.Nombre, p.Descripcion, p.Contacto, p.IsDeleted ? "true" : "false",
                        CsvCodec.Formatear(p.CreatedAt), CsvCodec.Formatear(p.UpdatedAt)
                    ]);
                }

                break;
            default:
                CsvCodec.EscribirCabecera(escritor,
                [
                    "id", "nombre", "descripcion", "precio", "stock", "imagen", "categoriaId", "proveedorId",
                    "isDeleted", "createdAt", "updatedAt"
                ]);
                foreach (BackupProductoDto p in datos.Productos) {
                    CsvCodec.EscribirFila(escritor,
                    [
                        p.Id.ToString(CultureInfo.InvariantCulture), p.Nombre, p.Descripcion,
                        CsvCodec.Formatear(p.Precio), p.Stock.ToString(CultureInfo.InvariantCulture), p.Imagen,
                        p.CategoriaId.ToString(CultureInfo.InvariantCulture), p.ProveedorId,
                        p.IsDeleted ? "true" : "false", CsvCodec.Formatear(p.CreatedAt), CsvCodec.Formatear(p.UpdatedAt)
                    ]);
                }

                break;
        }

        await escritor.FlushAsync();
    }

    private static async Task EscribirZipAsync(Stream salida, DatosBackup datos, bool incluirBorrados, bool csv) {
        using ZipArchive archivo = ZipCodec.Crear(salida);

        // Manifest siempre como primer fichero.
        ManifestBackupDto manifest = CrearManifest(csv ? "csv" : "zip", datos, incluirBorrados);
        await EscribirEntradaAsync(archivo, "manifest.json",
            JsonSerializer.Serialize(manifest, OpcionesJson));

        if (csv) {
            if (datos.Categorias.Count > 0) {
                await EscribirEntradaAsync(archivo, "categorias.csv", SerializarCsv("categorias", datos));
            }

            if (datos.Proveedores.Count > 0) {
                await EscribirEntradaAsync(archivo, "proveedores.csv", SerializarCsv("proveedores", datos));
            }

            if (datos.Productos.Count > 0) {
                await EscribirEntradaAsync(archivo, "productos.csv", SerializarCsv("productos", datos));
            }
        } else {
            if (datos.Categorias.Count > 0) {
                await EscribirEntradaAsync(archivo, "categorias.json",
                    JsonSerializer.Serialize(datos.Categorias, OpcionesJson));
            }

            if (datos.Proveedores.Count > 0) {
                await EscribirEntradaAsync(archivo, "proveedores.json",
                    JsonSerializer.Serialize(datos.Proveedores, OpcionesJson));
            }

            if (datos.Productos.Count > 0) {
                await EscribirEntradaAsync(archivo, "productos.json",
                    JsonSerializer.Serialize(datos.Productos, OpcionesJson));
            }
        }

        await archivo.DisposeAsync();
        await salida.FlushAsync();
    }

    private static async Task EscribirEntradaAsync(ZipArchive archivo, string nombre, string contenido) {
        ZipArchiveEntry entrada = archivo.CreateEntry(nombre, CompressionLevel.Optimal);
        await using Stream flujo = entrada.Open();
        await using var escritor = new StreamWriter(flujo, new UTF8Encoding(false));
        await escritor.WriteAsync(contenido);
    }

    /// <summary>Serializa una entidad a CSV (para incluirlo dentro de un ZIP).</summary>
    private static string SerializarCsv(string entidad, DatosBackup datos) {
        using var texto = new StringWriter();
        switch (entidad) {
            case "categorias":
                CsvCodec.EscribirCabecera(texto,
                    ["id", "nombre", "descripcion", "isDeleted", "createdAt", "updatedAt"]);
                foreach (BackupCategoriaDto c in datos.Categorias) {
                    CsvCodec.EscribirFila(texto,
                    [
                        c.Id.ToString(CultureInfo.InvariantCulture), c.Nombre, c.Descripcion,
                        c.IsDeleted ? "true" : "false",
                        CsvCodec.Formatear(c.CreatedAt), CsvCodec.Formatear(c.UpdatedAt)
                    ]);
                }

                break;
            case "proveedores":
                CsvCodec.EscribirCabecera(texto,
                    ["codigo", "nombre", "descripcion", "contacto", "isDeleted", "createdAt", "updatedAt"]);
                foreach (BackupProveedorDto p in datos.Proveedores) {
                    CsvCodec.EscribirFila(texto,
                    [
                        p.Codigo, p.Nombre, p.Descripcion, p.Contacto, p.IsDeleted ? "true" : "false",
                        CsvCodec.Formatear(p.CreatedAt), CsvCodec.Formatear(p.UpdatedAt)
                    ]);
                }

                break;
            default:
                CsvCodec.EscribirCabecera(texto,
                [
                    "id", "nombre", "descripcion", "precio", "stock", "imagen", "categoriaId", "proveedorId",
                    "isDeleted", "createdAt", "updatedAt"
                ]);
                foreach (BackupProductoDto p in datos.Productos) {
                    CsvCodec.EscribirFila(texto,
                    [
                        p.Id.ToString(CultureInfo.InvariantCulture), p.Nombre, p.Descripcion,
                        CsvCodec.Formatear(p.Precio), p.Stock.ToString(CultureInfo.InvariantCulture), p.Imagen,
                        p.CategoriaId.ToString(CultureInfo.InvariantCulture), p.ProveedorId,
                        p.IsDeleted ? "true" : "false", CsvCodec.Formatear(p.CreatedAt), CsvCodec.Formatear(p.UpdatedAt)
                    ]);
                }

                break;
        }

        return texto.ToString();
    }

    private static ManifestBackupDto CrearManifest(string formato, DatosBackup datos, bool incluirBorrados) {
        return new ManifestBackupDto(
            "1.0",
            DateTime.UtcNow,
            new Dictionary<string, int> {
                ["categorias"] = datos.Categorias.Count,
                ["proveedores"] = datos.Proveedores.Count,
                ["productos"] = datos.Productos.Count
            },
            incluirBorrados,
            formato);
    }

    // ────────────────────────────────────────────────────────────
    // Import
    // ────────────────────────────────────────────────────────────

    /// <summary>Valida formato y entidades antes de tocar el fichero.</summary>
    private static UnitResult<DomainError> ValidarPeticion(string formato, IReadOnlyList<string> entidades) {
        if (!Formatos.Contains(formato)) {
            return UnitResult.Failure<DomainError>(BackupError.FormatoDesconocido(formato));
        }

        if (entidades.Count == 0) {
            return UnitResult.Failure<DomainError>(BackupError.EntidadDesconocida("(ninguna)"));
        }

        foreach (string entidad in entidades) {
            if (!Entidades.Contains(entidad)) {
                return UnitResult.Failure<DomainError>(BackupError.EntidadDesconocida(entidad));
            }
        }

        return UnitResult.Success<DomainError>();
    }

    /// <summary>
    ///     Lee el fichero según el formato y devuelve solo las entidades pedidas.
    ///     El ZIP pasa por stream (binario); el json y el csv se leen como texto.
    /// </summary>
    private static async Task<BackupDto?> LeerAsync(Stream fichero, string formato, IReadOnlyList<string> entidades) {
        return formato switch {
            "zip" => LeerZip(fichero, entidades),
            "json" => JsonSerializer.Deserialize<BackupDto>(
                await LeerTextoAsync(fichero), OpcionesJson),
            _ => LeerCsv(await LeerTextoAsync(fichero), entidades)
        };
    }

    private static async Task<string> LeerTextoAsync(Stream fichero) {
        using var lector = new StreamReader(fichero, Encoding.UTF8, leaveOpen: true);
        string texto = await lector.ReadToEndAsync();
        if (texto.Length > MaxBytesFichero) {
            throw new BackupException(
                BackupError.DemasiadoGrande($"{texto.Length} caracteres, máximo {MaxBytesFichero}"));
        }

        return texto;
    }

    /// <summary>
    ///     Construye el backup desde las entradas de un ZIP (límites aplicados por el codec).
    ///     Se aceptan ficheros .json y .csv: el nombre del fichero dice qué entidad contiene.
    /// </summary>
    private static BackupDto? LeerZip(Stream fichero, IReadOnlyList<string> entidades) {
        IReadOnlyList<ZipCodec.EntradaZip> entradas = ZipCodec.Leer(fichero);
        var datos = new DatosBackup();

        foreach (ZipCodec.EntradaZip entrada in entradas) {
            string nombre = Path.GetFileName(entrada.Nombre).ToLowerInvariant();

            if (nombre == "categorias.json") {
                datos.Categorias = JsonSerializer.Deserialize<List<BackupCategoriaDto>>(
                    entrada.Contenido, OpcionesJson) ?? [];
            } else if (nombre == "proveedores.json") {
                datos.Proveedores = JsonSerializer.Deserialize<List<BackupProveedorDto>>(
                    entrada.Contenido, OpcionesJson) ?? [];
            } else if (nombre == "productos.json") {
                datos.Productos = JsonSerializer.Deserialize<List<BackupProductoDto>>(
                    entrada.Contenido, OpcionesJson) ?? [];
            } else if (nombre == "categorias.csv") {
                datos.Categorias = MapearCategoriasCsv(entrada.Contenido);
            } else if (nombre == "proveedores.csv") {
                datos.Proveedores = MapearProveedoresCsv(entrada.Contenido);
            } else if (nombre == "productos.csv") {
                datos.Productos = MapearProductosCsv(entrada.Contenido);
            }
        }

        if (datos.Categorias.Count == 0 && datos.Proveedores.Count == 0 && datos.Productos.Count == 0) {
            return null;
        }

        return new BackupDto(CrearManifest("zip", datos, false),
            datos.Categorias, datos.Productos, datos.Proveedores);
    }

    /// <summary>Convierte el contenido de un CSV en la lista de su entidad.</summary>
    private static List<BackupCategoriaDto> MapearCategoriasCsv(string contenido) {
        (string[] cabecera, string[][] cuerpo) filas = LeerFilas(contenido);
        return filas.cuerpo.Select(f => MapaCategoriaCsv(filas.cabecera, f)).ToList();
    }

    private static List<BackupProveedorDto> MapearProveedoresCsv(string contenido) {
        (string[] cabecera, string[][] cuerpo) filas = LeerFilas(contenido);
        return filas.cuerpo.Select(f => MapaProveedorCsv(filas.cabecera, f)).ToList();
    }

    private static List<BackupProductoDto> MapearProductosCsv(string contenido) {
        (string[] cabecera, string[][] cuerpo) filas = LeerFilas(contenido);
        return filas.cuerpo.Select(f => MapaProductoCsv(filas.cabecera, f)).ToList();
    }

    /// <summary>Lee un CSV devolviendo la cabecera y las filas de datos.</summary>
    private static (string[] cabecera, string[][] cuerpo) LeerFilas(string contenido) {
        using var lector = new StringReader(contenido);
        List<string[]> todas = CsvCodec.Leer(lector);
        if (todas.Count < 2) {
            return ([], []);
        }

        return (todas[0], todas.Skip(1).ToArray());
    }

    /// <summary>
    ///     Construye el backup desde un CSV de una sola entidad (la indica `entidades`).
    /// </summary>
    private static BackupDto? LeerCsv(string contenido, IReadOnlyList<string> entidades) {
        using var lector = new StringReader(contenido);
        List<string[]> filas = CsvCodec.Leer(lector);
        if (filas.Count < 2) {
            return null;
        }

        string entidad = entidades[0];
        string[] cabecera = filas[0];
        var datos = new DatosBackup();
        string[][] cuerpo = filas.Skip(1).ToArray();

        switch (entidad) {
            case "categorias":
                datos.Categorias = cuerpo.Select(f => MapaCategoriaCsv(cabecera, f)).ToList();
                break;
            case "proveedores":
                datos.Proveedores = cuerpo.Select(f => MapaProveedorCsv(cabecera, f)).ToList();
                break;
            default:
                datos.Productos = cuerpo.Select(f => MapaProductoCsv(cabecera, f)).ToList();
                break;
        }

        return new BackupDto(CrearManifest("csv", datos, false),
            datos.Categorias, datos.Productos, datos.Proveedores);
    }

    // Mapeo CSV por nombre de columna: el orden de las columnas no importa.
    private static BackupCategoriaDto MapaCategoriaCsv(string[] cabecera, string[] fila) {
        return new BackupCategoriaDto(
            AId(Campo(cabecera, fila, "id")),
            Campo(cabecera, fila, "nombre"),
            NuloSiVacio(Campo(cabecera, fila, "descripcion")),
            CsvCodec.ABooleano(Campo(cabecera, fila, "isDeleted")),
            CsvCodec.AFecha(Campo(cabecera, fila, "createdAt")) ?? DateTime.UtcNow,
            CsvCodec.AFecha(Campo(cabecera, fila, "updatedAt")) ?? DateTime.UtcNow);
    }

    private static BackupProveedorDto MapaProveedorCsv(string[] cabecera, string[] fila) {
        return new BackupProveedorDto(
            Campo(cabecera, fila, "codigo"),
            Campo(cabecera, fila, "nombre"),
            NuloSiVacio(Campo(cabecera, fila, "descripcion")),
            NuloSiVacio(Campo(cabecera, fila, "contacto")),
            CsvCodec.ABooleano(Campo(cabecera, fila, "isDeleted")),
            CsvCodec.AFecha(Campo(cabecera, fila, "createdAt")) ?? DateTime.UtcNow,
            CsvCodec.AFecha(Campo(cabecera, fila, "updatedAt")) ?? DateTime.UtcNow);
    }

    private static BackupProductoDto MapaProductoCsv(string[] cabecera, string[] fila) {
        return new BackupProductoDto(
            AId(Campo(cabecera, fila, "id")),
            Campo(cabecera, fila, "nombre"),
            Campo(cabecera, fila, "descripcion"),
            CsvCodec.ADecimal(Campo(cabecera, fila, "precio")) ?? 0m,
            CsvCodec.AEntero(Campo(cabecera, fila, "stock")) ?? 0,
            NuloSiVacio(Campo(cabecera, fila, "imagen")),
            CsvCodec.AEnteroLargo(Campo(cabecera, fila, "categoriaId")) ?? 0,
            NuloSiVacio(Campo(cabecera, fila, "proveedorId")),
            CsvCodec.ABooleano(Campo(cabecera, fila, "isDeleted")),
            CsvCodec.AFecha(Campo(cabecera, fila, "createdAt")) ?? DateTime.UtcNow,
            CsvCodec.AFecha(Campo(cabecera, fila, "updatedAt")) ?? DateTime.UtcNow);
    }

    /// <summary>
    ///     Una celda vacía en el CSV significa "sin valor": en base de datos eso es
    ///     NULL, no una cadena vacía (si no, el round-trip marcaría todo como cambiado).
    /// </summary>
    private static string? NuloSiVacio(string valor) {
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    private static string Campo(string[] cabecera, string[] fila, string nombre) {
        int indice = Array.FindIndex(cabecera, c => c.Trim().Equals(nombre, StringComparison.OrdinalIgnoreCase));
        return indice >= 0 && indice < fila.Length ? fila[indice] : string.Empty;
    }

    private static long AId(string texto) {
        return CsvCodec.AEnteroLargo(texto) ??
               throw new BackupException(BackupError.FormatoInvalido($"id no numérico '{texto}'"));
    }

    /// <summary>Importa categorías (upsert por id, lo hace catalog).</summary>
    private async Task<EntidadImportReportDto> ImportarCategoriasAsync(
        IReadOnlyList<BackupCategoriaDto> filas, bool dryRun) {
        return await ImportarAsync("categorias", filas, dryRun, fila => new ImportCategoriaDto(
                fila.Id, fila.Nombre, fila.Descripcion, fila.IsDeleted, Utc(fila.CreatedAt), Utc(fila.UpdatedAt)),
            f => catalogApi.ImportCategoriaAsync((ImportCategoriaDto)f),
            fila => new ErrorFilaDto(0, "nombre", string.Empty));
    }

    /// <summary>Importa proveedores (upsert por código, lo hace catalog).</summary>
    private async Task<EntidadImportReportDto> ImportarProveedoresAsync(
        IReadOnlyList<BackupProveedorDto> filas, bool dryRun) {
        return await ImportarAsync("proveedores", filas, dryRun, fila => new ImportProveedorDto(
                fila.Codigo, fila.Nombre, fila.Descripcion, fila.Contacto, fila.IsDeleted,
                Utc(fila.CreatedAt), Utc(fila.UpdatedAt)),
            f => catalogApi.ImportProveedorAsync((ImportProveedorDto)f),
            fila => new ErrorFilaDto(0, "codigo", string.Empty));
    }

    /// <summary>Importa productos (upsert por id, lo hace catalog).</summary>
    private async Task<EntidadImportReportDto> ImportarProductosAsync(
        IReadOnlyList<BackupProductoDto> filas, bool dryRun) {
        return await ImportarAsync("productos", filas, dryRun, fila => new ImportProductoDto(
                fila.Id, fila.Nombre, fila.Descripcion, fila.Precio, fila.Stock, fila.Imagen,
                fila.CategoriaId, fila.ProveedorId, fila.IsDeleted, Utc(fila.CreatedAt), Utc(fila.UpdatedAt)),
            f => catalogApi.ImportProductoAsync((ImportProductoDto)f),
            fila => new ErrorFilaDto(0, "producto", string.Empty));
    }

    /// <summary>
    ///     Recorre las filas y envía cada una al endpoint interno de catalog.
    ///     Con dryRun NO se escribe: se cuentan como se habrían contado.
    ///     🎓 El upsert completo (¿existe? ¿sin cambios? ¿crear?) vive en catalog:
    ///     aquí solo se traduce su código de respuesta al informe.
    /// </summary>
    private static async Task<EntidadImportReportDto> ImportarAsync<T>(
        string entidad,
        IReadOnlyList<T> filas,
        bool dryRun,
        Func<T, object> construir,
        Func<object, Task<HttpResponseMessage>> enviar,
        Func<T, ErrorFilaDto> errorDe) {
        var errores = new List<ErrorFilaDto>();
        int creados = 0;
        int actualizados = 0;
        int sinCambios = 0;

        for (int i = 0; i < filas.Count; i++) {
            T fila = filas[i];
            int numeroFila = i + 1;

            if (dryRun) {
                // ⚠️ dryRun NO escribe NI llama al endpoint de import: eso
                // requeriría un endpoint de validación sin efectos en catalog,
                // que no existe. Solo se cuentan las filas leídas — la
                // clasificación real (creado / actualizado / sin cambios) la
                // decide catalog en la importación real.
                continue;
            }

            try {
                HttpResponseMessage respuesta = await enviar(construir(fila));
                Contar(respuesta.StatusCode, ref creados, ref actualizados, ref sinCambios);
            } catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest) {
                // catalog valida la fila (formato, FKs): el error no aborta.
                errores.Add(ErrorDe(ex, numeroFila, errorDe(fila)));
            }
        }

        return new EntidadImportReportDto(entidad, filas.Count, creados, actualizados, sinCambios, errores);
    }

    /// <summary>Traduce el código de catalog al contador del informe.</summary>
    private static void Contar(HttpStatusCode status, ref int creados, ref int actualizados, ref int sinCambios) {
        switch (status) {
            case HttpStatusCode.Created: creados++; break;
            case HttpStatusCode.OK: actualizados++; break;
            case HttpStatusCode.NoContent: sinCambios++; break;
        }
    }

    /// <summary>Construye el error de fila desde la respuesta 400 de catalog.</summary>
    private static ErrorFilaDto ErrorDe(ApiException ex, int numeroFila, ErrorFilaDto respaldo) {
        string campo = respaldo.Campo;
        string mensaje = respaldo.Mensaje;

        try {
            using var doc = JsonDocument.Parse(ex.Content ?? "{}");
            JsonElement raiz = doc.RootElement;
            if (raiz.TryGetProperty("message", out JsonElement m)) {
                mensaje = m.GetString() ?? mensaje;
            }

            if (raiz.TryGetProperty("field", out JsonElement f)) {
                campo = f.GetString() ?? campo;
            }
        } catch {
            /* el mensaje de respaldo ya vale */
        }

        return new ErrorFilaDto(numeroFila, campo, mensaje);
    }

    /// <summary>Npgsql exige Kind=Utc para timestamp with time zone.</summary>
    private static DateTime Utc(DateTime valor) {
        return valor.Kind == DateTimeKind.Utc ? valor : DateTime.SpecifyKind(valor, DateTimeKind.Utc);
    }

    // ── Mapeo DTO público de catalog → DTO plano del backup ────────────

    private static BackupCategoriaDto Mapear(CategoriaDto c) {
        return new BackupCategoriaDto(c.Id, c.Nombre, c.Descripcion ?? string.Empty, false, c.CreatedAt, c.UpdatedAt);
    }

    private static BackupProveedorDto Mapear(ProveedorDto p) {
        return new BackupProveedorDto(p.Codigo, p.Nombre, p.Descripcion, p.Contacto, false, p.CreatedAt,
            p.UpdatedAt);
    }

    private static BackupProductoDto Mapear(ProductoDto p) {
        return new BackupProductoDto(p.Id, p.Nombre, p.Descripcion, p.Precio, p.Stock, p.Imagen,
            p.Categoria.Id, p.Proveedor?.Codigo ?? string.Empty, false, p.CreatedAt, p.UpdatedAt);
    }

    /// <summary>Datos ya cargados de las tres entidades para el export.</summary>
    private sealed class DatosBackup {
        public List<BackupCategoriaDto> Categorias { get; set; } = [];
        public List<BackupProveedorDto> Proveedores { get; set; } = [];
        public List<BackupProductoDto> Productos { get; set; } = [];
    }
}
