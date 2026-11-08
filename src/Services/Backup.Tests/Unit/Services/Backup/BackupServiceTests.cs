using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Refit;
using System.Net;
using System.Text;
using TiendaApi.Api.Dtos.Backup;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Dtos.Proveedores;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Services.Backup;
using TiendaApi.Api.Services.Catalog;

namespace TiendaApi.Tests.Unit.Services.Backup;

/// <summary>
/// Tests del servicio de backup con el client Refit de catalog mockeado.
///
/// 🎓 Es la parte NUEVA de T10: en el monolito el servicio escribía en sus
/// propios repositorios; aquí solo orquesta — lee por las rutas públicas y
/// escribe en los endpoints internos de catalog, traduciendo su código de
/// respuesta al informe (201 creado · 200 actualizado · 204 sin cambios · 400 error).
/// </summary>
public class BackupServiceTests
{
    // Se recrean en [SetUp]: cada test parte de mocks limpios (NUnit reutiliza
    // la instancia del fixture).
    private Mock<ICatalogApi> _catalog = null!;
    private BackupService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new Mock<ICatalogApi>();
        _service = new BackupService(_catalog.Object, NullLogger<BackupService>.Instance);

        // Catálogos por defecto: página 0 con todo y página 1 vacía.
        _catalog.Setup(c => c.GetCategoriasAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Pagina(Categorias(), 0));
        _catalog.Setup(c => c.GetProveedoresAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Pagina(Proveedores(), 0));
        _catalog.Setup(c => c.GetProductosAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Pagina(Productos(), 0));
    }

    /// <summary>Página con todo en la 0 y vacía en las demás (corta el bucle).</summary>
    private static PagedResult<T> Pagina<T>(List<T> items, int pagina) => new()
    {
        Items = pagina == 0 ? items : Enumerable.Empty<T>(),
        TotalCount = items.Count,
        Page = pagina + 1,
        PageSize = 100,
    };

    private static List<CategoriaDto> Categorias() =>
        [new(1, "Electrónica", "Dispositivos", DateTime.UtcNow, DateTime.UtcNow)];

    private static List<ProveedorDto> Proveedores() =>
        [new("Distribuc01", "Distribuciones DAW", "General", "pedidos@t.example", DateTime.UtcNow, DateTime.UtcNow)];

    private static List<ProductoDto> Productos() =>
        [new(1, "Laptop", "Alto rendimiento", 1299.99m, 10, null,
            new CategoriaResumenDto(1, "Electrónica", "Dispositivos"),
            new ProveedorResumenDto("Distribuc01", "Distribuciones DAW", "General"),
            DateTime.UtcNow, DateTime.UtcNow)];

    /// <summary>Respuesta de catalog con el código indicado (201/200/204).</summary>
    private static HttpResponseMessage Respuesta(HttpStatusCode code, string body = "{}") =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Excepción Refit con el status indicado — así se comporta el client REAL
    /// ante un 400, y el servicio debe traducirla a un error de fila.
    /// </summary>
    private static async Task<ApiException> ApiExceptionCon(HttpStatusCode code, string body) =>
        await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Post, "http://catalog"),
            HttpMethod.Post,
            new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") },
            new RefitSettings());

    // ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Exportar_Json_EscribeManifestYEntidades()
    {
        using var salida = new MemoryStream();

        var result = await _service.ExportarAsync(salida, "json",
            ["categorias", "proveedores", "productos"], incluirBorrados: false);

        result.IsSuccess.Should().BeTrue();
        salida.Position = 0;
        using var lector = new StreamReader(salida, Encoding.UTF8);
        var json = lector.ReadToEnd();

        json.Should().Contain("\"manifest\"", "el backup siempre lleva manifest");
        json.Should().Contain("Laptop");
        json.Should().Contain("Distribuc01");
        json.Should().Contain("\"categoriaId\":1", "el backup va plano, con las FK tal cual");
        // Ojo: STJ escapa los no-ASCII (\u00F3), por eso no se busca "Electrónica".
    }

    [Test]
    public async Task Exportar_FormatoDesconocido_Devuelve400_SinEscribir()
    {
        using var salida = new MemoryStream();

        var result = await _service.ExportarAsync(salida, "xml",
            ["productos"], incluirBorrados: false);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        salida.Length.Should().Be(0, "no debe escribirse nada si la petición es inválida");
    }

    [Test]
    public async Task Exportar_EntidadDesconocida_Devuelve400()
    {
        using var salida = new MemoryStream();

        var result = await _service.ExportarAsync(salida, "json",
            ["usuarios"], incluirBorrados: false);

        result.IsFailure.Should().BeTrue();
        salida.Length.Should().Be(0);
    }

    [Test]
    public async Task Exportar_Csv_SoloUnaEntidad_EscribeLaCabecera()
    {
        using var salida = new MemoryStream();

        var result = await _service.ExportarAsync(salida, "csv",
            ["productos"], incluirBorrados: false);

        result.IsSuccess.Should().BeTrue();
        salida.Position = 0;
        using var lector = new StreamReader(salida, Encoding.UTF8);
        var csv = lector.ReadToEnd();

        csv.Should().StartWith("id,nombre,descripcion,precio,stock,imagen,categoriaId",
            "el CSV lleva la cabecera con las claves foráneas planas");
        csv.Should().Contain("Laptop");
    }

    // ── import: la traducción de códigos de catalog ──────────────────

    [Test]
    public async Task Importar_Creado_Contado_ComoCreado()
    {
        _catalog.Setup(c => c.ImportCategoriaAsync(It.IsAny<ImportCategoriaDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.Created));
        _catalog.Setup(c => c.ImportProveedorAsync(It.IsAny<ImportProveedorDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.Created));
        _catalog.Setup(c => c.ImportProductoAsync(It.IsAny<ImportProductoDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.Created));

        var informe = await ImportarAsync(BackupService.Entidades, dryRun: false);

        informe.IsSuccess.Should().BeTrue();
        var cats = informe.Value.Entidades.Single(e => e.Entidad == "categorias");
        cats.Creados.Should().Be(1, "201 de catalog = fila creada");
        cats.SinCambios.Should().Be(0);
    }

    [Test]
    public async Task Importar_SinCambios_Contado_ComoSinCambios()
    {
        // El round-trip del E2E: mismas claves → catalog responde 204.
        _catalog.Setup(c => c.ImportCategoriaAsync(It.IsAny<ImportCategoriaDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.NoContent));
        _catalog.Setup(c => c.ImportProveedorAsync(It.IsAny<ImportProveedorDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.NoContent));
        _catalog.Setup(c => c.ImportProductoAsync(It.IsAny<ImportProductoDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.NoContent));

        var informe = await ImportarAsync(BackupService.Entidades, dryRun: false);

        informe.IsSuccess.Should().BeTrue();
        informe.Value.Completo.Should().BeTrue("sin errores = import completo");
        informe.Value.Entidades.Should().OnlyContain(e =>
            e.Creados == 0 && e.Actualizados == 0 && e.SinCambios == e.Leidos,
            "el round-trip no debe crear ni actualizar nada");
    }

    [Test]
    public async Task Importar_Actualizado_Contado_ComoActualizado()
    {
        _catalog.Setup(c => c.ImportCategoriaAsync(It.IsAny<ImportCategoriaDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.OK));

        var informe = await ImportarAsync(["categorias"], dryRun: false);

        informe.Value.Entidades.Single().Actualizados.Should().Be(1, "200 de catalog = fila actualizada");
    }

    [Test]
    public async Task Importar_ErrorDeFila_NoAbortaLaOperacion()
    {
        // La primera fila falla (400), las demás siguen: mismo criterio del monolito.
        // ⚠️ Refit LANZA ApiException ante un 400: el mock debe hacer lo mismo.
        var llamadas = 0;
        _catalog.Setup(c => c.ImportCategoriaAsync(It.IsAny<ImportCategoriaDto>()))
            .Returns<ImportCategoriaDto>(async _ =>
            {
                llamadas++;
                if (llamadas == 1)
                    throw await ApiExceptionCon(HttpStatusCode.BadRequest,
                        """{"message":"El nombre es obligatorio","field":"nombre"}""");
                return Respuesta(HttpStatusCode.Created);
            });
        _catalog.Setup(c => c.ImportProveedorAsync(It.IsAny<ImportProveedorDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.Created));
        _catalog.Setup(c => c.ImportProductoAsync(It.IsAny<ImportProductoDto>()))
            .ReturnsAsync(Respuesta(HttpStatusCode.Created));

        var informe = await ImportarAsync(BackupService.Entidades, dryRun: false);

        informe.IsSuccess.Should().BeTrue("los errores de fila no abortan");
        var cats = informe.Value.Entidades.Single(e => e.Entidad == "categorias");
        cats.Leidos.Should().Be(Categorias().Count);
        cats.Errores.Should().ContainSingle()
            .Which.Mensaje.Should().Contain("El nombre es obligatorio", "el mensaje viene de catalog");
        cats.Errores.Single().Campo.Should().Be("nombre", "el field lo decide catalog");
        informe.Value.Completo.Should().BeFalse("con errores no puede estar completo");
    }

    [Test]
    public async Task Importar_DryRun_NoEscribeNada()
    {
        var informe = await ImportarAsync(BackupService.Entidades, dryRun: true);

        informe.IsSuccess.Should().BeTrue();
        informe.Value.DryRun.Should().BeTrue();
        informe.Value.Entidades.Should().OnlyContain(e => e.Leidos >= 1,
            "dryRun sí cuenta las filas leídas");

        // Lo esencial: dryRun NO debe tocar catalog.
        _catalog.Verify(c => c.ImportCategoriaAsync(It.IsAny<ImportCategoriaDto>()), Times.Never);
        _catalog.Verify(c => c.ImportProveedorAsync(It.IsAny<ImportProveedorDto>()), Times.Never);
        _catalog.Verify(c => c.ImportProductoAsync(It.IsAny<ImportProductoDto>()), Times.Never);
    }

    [Test]
    public async Task ObtenerFormatos_DevuelveLosTresFormatos()
    {
        var result = await _service.ObtenerFormatosAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Formatos.Select(f => f.Nombre).Should().BeEquivalentTo(new[] { "zip", "json", "csv" });
        result.Value.Entidades.Should().HaveCount(3);
        result.Value.Limites.MaxBytes.Should().BeGreaterThan(0);
    }

    /// <summary>Importa un backup mínimo con las entidades indicadas.</summary>
    private async Task<Result<ImportReportDto, DomainError>> ImportarAsync(
        IReadOnlyList<string> entidades, bool dryRun)
    {
        var contenido = """
            {"manifest":{"version":"1.0","generadoEn":"2026-01-01T00:00:00Z"},
             "categorias":[{"id":1,"nombre":"Electrónica","descripcion":"x","isDeleted":false,"createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z"}],
             "proveedores":[{"codigo":"Distribuc01","nombre":"Dist","descripcion":"x","contacto":"c@t.x","isDeleted":false,"createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z"}],
             "productos":[{"id":1,"nombre":"Laptop","descripcion":"x","precio":1299.99,"stock":10,"imagen":null,"categoriaId":1,"proveedorId":"Distribuc01","isDeleted":false,"createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z"}]}
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contenido));
        return await _service.ImportarAsync(stream, "json", entidades, dryRun);
    }
}
