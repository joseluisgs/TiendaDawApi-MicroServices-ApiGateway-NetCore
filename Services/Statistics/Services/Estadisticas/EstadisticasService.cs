using System.Net;
using CSharpFunctionalExtensions;
using MongoDB.Bson;
using Refit;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Estadisticas;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Estadisticas;
using TiendaApi.Api.Errors.Productos;
using TiendaApi.Api.Models;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Historial;
using TiendaApi.Api.Services.Identity;
using TiendaApi.Api.Services.Orders;

namespace TiendaApi.Api.Services.Estadisticas;

/// <summary>
///     Servicio de estadísticas.
///     🎓 Flujo de todas las vistas: resolver la ventana → cargar los pedidos de esa
///     ventana → pasarlos por <see cref="EstadisticasCalculator" /> → devolver el DTO.
///     ⚠️ Diferencia con el monolito (R5 — una BD por microservicio): aquí NO hay
///     repositorios, todo se pide por Refit. El <b>calculador no se toca</b>: se
///     mapean los snapshots a los MISMOS modelos de dominio, así los números salen
///     idénticos a los del monolito.
/// </summary>
public class EstadisticasService(
    IOrdersApi ordersApi,
    ICatalogApi catalogApi,
    IUsersApi usersApi,
    IHistorialApi historialApi,
    ILogger<EstadisticasService> logger
) : IEstadisticasService {
    /// <summary>Puntos máximos del histórico de precios (límite de historical).</summary>
    private const int LimiteHistorialPrecios = 200;

    /// <summary>Tamaño de página al recorrer los catálogos (máx. aceptado por la API).</summary>
    private const int TamanoPagina = 100;

    /// <inheritdoc />
    public async Task<Result<ResumenEstadisticasDto, DomainError>> ResumenAsync(DateTime? desde, DateTime? hasta) {
        (IEnumerable<Pedido> pedidos, PeriodoDto periodo) = await CargarAsync(desde, hasta);
        var lista = pedidos.ToList();

        TotalesDto totales = EstadisticasCalculator.CalcularTotales(lista);
        int clientesActuales = ClientesDistintos(lista);

        // Comparativa: la ventana inmediatamente anterior de la misma duración.
        ComparativaDto? comparativa = null;
        if (periodo.Dias > 0) {
            DateTime anteriorDesde = periodo.Desde!.Value.AddDays(-periodo.Dias);
            IEnumerable<Pedido> pedidosAnteriores = await CargarSoloRangoAsync(anteriorDesde, periodo.Desde.Value);
            TotalesDto totalesAnteriores = EstadisticasCalculator.CalcularTotales(pedidosAnteriores);
            comparativa = EstadisticasCalculator.CalcularComparativa(
                totales, totalesAnteriores, clientesActuales, ClientesDistintos(pedidosAnteriores));
        }

        (IReadOnlyDictionary<long, Producto> productos, IReadOnlyDictionary<long, Categoria> categorias, _, _) =
            await CargarCatalogosAsync();

        decimal recaudacionPorDia = periodo.Dias == 0 ? 0 : Math.Round(totales.Recaudacion / periodo.Dias, 2);

        var resumen = new ResumenEstadisticasDto(
            periodo,
            new ResumenVentasDto(
                totales.Pedidos,
                totales.Unidades,
                totales.Recaudacion,
                totales.TicketMedio,
                totales.Pedidos == 0 ? 0 : Math.Round((decimal)totales.Unidades / totales.Pedidos, 2),
                recaudacionPorDia,
                clientesActuales),
            EstadisticasCalculator.PedidosPorEstado(lista),
            await ResumenProductosAsync(),
            new ResumenCategoriasDto(
                categorias.Count,
                categorias.Values
                    .Count(c => !productos.Values.Any(p => p.CategoriaId == c.Id))),
            await ResumenUsuariosAsync(periodo, lista),
            comparativa);

        logger.LogInformation(
            "Estadísticas de resumen: {Pedidos} pedidos y {Recaudacion} de recaudación en {Dias} días",
            resumen.Ventas.Pedidos, resumen.Ventas.Recaudacion, periodo.Dias);

        return Result.Success<ResumenEstadisticasDto, DomainError>(resumen);
    }

    /// <inheritdoc />
    public async Task<Result<VentasSerieDto, DomainError>> VentasAsync(
        DateTime? desde, DateTime? hasta, string agrupar) {
        if (!EstadisticasCalculator.Agrupaciones.Contains(agruparCorrecto(agrupar))) {
            return Result.Failure<VentasSerieDto, DomainError>(
                ErrorEstadisticas.AgrupacionInvalida(agrupar));
        }

        (IEnumerable<Pedido> pedidos, PeriodoDto periodo) = await CargarAsync(desde, hasta);
        IReadOnlyList<PuntoSerieDto> serie =
            EstadisticasCalculator.Serie(pedidos, agrupar, periodo.Desde!.Value, periodo.Hasta!.Value);

        return Result.Success<VentasSerieDto, DomainError>(
            new VentasSerieDto(periodo, agrupar, serie, EstadisticasCalculator.CalcularTotales(pedidos)));
    }

    /// <inheritdoc />
    public async Task<Result<ProductosMasVendidosDto, DomainError>> ProductosMasVendidosAsync(
        DateTime? desde, DateTime? hasta, string orden, int top) {
        string ordenLimpio = orden == "recaudacion" ? "recaudacion" : "unidades";
        (IEnumerable<Pedido> pedidos, PeriodoDto periodo) = await CargarAsync(desde, hasta);
        (IReadOnlyDictionary<long, Producto> productos, IReadOnlyDictionary<long, Categoria> categorias, _, _) =
            await CargarCatalogosAsync();

        IReadOnlyList<FilaProductoVendidoDto> filas = EstadisticasCalculator.RankingProductos(
            pedidos, productos, categorias, ordenLimpio, top);

        return Result.Success<ProductosMasVendidosDto, DomainError>(
            new ProductosMasVendidosDto(periodo, ordenLimpio, filas, EstadisticasCalculator.CalcularTotales(pedidos)));
    }

    /// <inheritdoc />
    public async Task<Result<CategoriasEstadisticasDto, DomainError>> CategoriasAsync(
        DateTime? desde, DateTime? hasta) {
        (IEnumerable<Pedido> pedidos, PeriodoDto periodo) = await CargarAsync(desde, hasta);
        (IReadOnlyDictionary<long, Producto> productos, IReadOnlyDictionary<long, Categoria> categorias, _, _) =
            await CargarCatalogosAsync();

        IReadOnlyList<FilaCategoriaDto> filas =
            EstadisticasCalculator.DesgloseCategorias(pedidos, categorias, productos);

        return Result.Success<CategoriasEstadisticasDto, DomainError>(
            new CategoriasEstadisticasDto(periodo, filas, EstadisticasCalculator.CalcularTotales(pedidos)));
    }

    /// <inheritdoc />
    public async Task<Result<ProveedoresEstadisticasDto, DomainError>> ProveedoresAsync(
        DateTime? desde, DateTime? hasta) {
        (IEnumerable<Pedido> pedidos, PeriodoDto periodo) = await CargarAsync(desde, hasta);
        (IReadOnlyDictionary<long, Producto> productos, _, IReadOnlyDictionary<string, Proveedor> proveedores, _) =
            await CargarCatalogosAsync();

        IReadOnlyList<FilaProveedorDto> filas =
            EstadisticasCalculator.DesgloseProveedores(pedidos, proveedores, productos);

        return Result.Success<ProveedoresEstadisticasDto, DomainError>(
            new ProveedoresEstadisticasDto(periodo, filas, EstadisticasCalculator.CalcularTotales(pedidos)));
    }

    /// <inheritdoc />
    public async Task<Result<EstadosEstadisticasDto, DomainError>> EstadosAsync(
        DateTime? desde, DateTime? hasta) {
        (IEnumerable<Pedido> pedidos, PeriodoDto periodo) = await CargarAsync(desde, hasta);
        return Result.Success<EstadosEstadisticasDto, DomainError>(
            EstadisticasCalculator.EmbudoEstados(pedidos, periodo));
    }

    /// <inheritdoc />
    public async Task<Result<ClientesEstadisticasDto, DomainError>> ClientesAsync(
        DateTime? desde, DateTime? hasta, int top) {
        (IEnumerable<Pedido> pedidos, PeriodoDto periodo) = await CargarAsync(desde, hasta);
        (_, _, _, IReadOnlyDictionary<long, string> usuarios) = await CargarCatalogosAsync();

        IReadOnlyList<FilaClienteDto> filas = EstadisticasCalculator.RankingClientes(pedidos, usuarios, top);

        return Result.Success<ClientesEstadisticasDto, DomainError>(
            new ClientesEstadisticasDto(periodo, filas));
    }

    /// <inheritdoc />
    public async Task<Result<InventarioDto, DomainError>> InventarioAsync(int stockMax) {
        (IReadOnlyDictionary<long, Producto> productos, IReadOnlyDictionary<long, Categoria> categorias, _, _) =
            await CargarCatalogosAsync();

        return Result.Success<InventarioDto, DomainError>(
            EstadisticasCalculator.Inventario(productos, categorias, stockMax));
    }

    /// <inheritdoc />
    public async Task<Result<PreciosEstadisticasDto, DomainError>> PreciosAsync(
        long productoId, DateTime? desde, DateTime? hasta) {
        // El 404 lo decide catalog (dueño del producto), igual que el
        // FindByIdAsync contra PostgreSQL en el monolito.
        if (!await ExisteProductoAsync(productoId)) {
            return Result.Failure<PreciosEstadisticasDto, DomainError>(
                ProductoError.NotFound(productoId));
        }

        List<HistorialPrecioDto>? serie = await historialApi.GetHistorialPreciosAsync(
            productoId, desde, hasta, LimiteHistorialPrecios);

        // El repositorio devuelve la serie de más reciente a más antigua; la
        // calculadora la ordena ascendente para calcular la variación.
        var puntos = (serie ?? [])
            .Select(h => new PuntoPrecioDto(h.Fecha, (decimal)Math.Round(h.Precio, 2)))
            .ToList();

        return Result.Success<PreciosEstadisticasDto, DomainError>(
            EstadisticasCalculator.Precios(productoId, puntos, desde, hasta));
    }

    /// <summary>¿Existe el producto en catalog? (su 404 es el que manda).</summary>
    private async Task<bool> ExisteProductoAsync(long productoId) {
        try {
            // Página 0 con tamaño mínimo: solo se comprueba la existencia.
            PagedResult<ProductoSnapshot>? pagina = await catalogApi.GetProductosAsync(0, 1);
            if (pagina?.Items is null) {
                return false;
            }

            // Recorrido por si la primera página no lo contiene.
            List<ProductoSnapshot> encontrados = await ObtenerTodoAsync((p, s) => catalogApi.GetProductosAsync(p, s));
            return encontrados.Any(p => p.Id == productoId);
        } catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            return false;
        }
    }

    // ────────────────────────────────────────────────────────────
    // Carga de datos (Refit en lugar de repositorios)
    // ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Carga los pedidos de la ventana pedida. Si falta algún límite, se usan los
    ///     extremos de los propios datos (histórico completo).
    ///     🎓 El monolito consultaba <c>FindByRangoFechasAsync</c>; aquí se traen todos
    ///     y se acota en memoria — mismo resultado numérico (y el volumen de pedidos
    ///     cabe de sobra en una respuesta).
    /// </summary>
    private async Task<(IEnumerable<Pedido> Pedidos, PeriodoDto Periodo)> CargarAsync(
        DateTime? desde, DateTime? hasta) {
        if (desde.HasValue && hasta.HasValue) {
            IEnumerable<Pedido> pedidos = await CargarSoloRangoAsync(desde.Value, hasta.Value);
            return (pedidos, EstadisticasCalculator.CrearPeriodo(desde.Value, hasta.Value));
        }

        List<Pedido> todos = await CargarTodosAsync();
        DateTime inicio = desde ?? (todos.Count > 0 ? todos.Min(p => p.CreatedAt).Date : DateTime.UtcNow.Date);
        DateTime fin = hasta ?? DateTime.UtcNow.Date.AddDays(1);

        return (
            todos.Where(p => p.CreatedAt >= inicio && p.CreatedAt < fin),
            EstadisticasCalculator.CrearPeriodo(inicio, fin));
    }

    /// <summary>Carga solo los pedidos de un rango concreto (para la comparativa).</summary>
    private async Task<IEnumerable<Pedido>> CargarSoloRangoAsync(DateTime desde, DateTime hasta) {
        if (desde >= hasta) {
            return [];
        }

        List<Pedido> todos = await CargarTodosAsync();
        return todos.Where(p => p.CreatedAt >= desde && p.CreatedAt < hasta).ToList();
    }

    /// <summary>Todos los pedidos de orders, mapeados a los modelos de dominio.</summary>
    private async Task<List<Pedido>> CargarTodosAsync() {
        // Tolerante a cuerpo nulo: si orders responde vacío, la ventana es cero
        // pedidos (mismo criterio tolerante que el monolito con Cassandra).
        List<PedidoSnapshot>? pedidos = await ordersApi.GetPedidosAsync();
        return (pedidos ?? []).Select(Mapear).ToList();
    }

    /// <summary>
    ///     Catálogos que cruzan las vistas (productos, categorías, proveedores y usuarios),
    ///     indexados por su clave. Se piden en PARALELO: son independientes entre sí.
    /// </summary>
    private async Task<(
        IReadOnlyDictionary<long, Producto> Productos,
        IReadOnlyDictionary<long, Categoria> Categorias,
        IReadOnlyDictionary<string, Proveedor> Proveedores,
        IReadOnlyDictionary<long, string> Usuarios)> CargarCatalogosAsync() {
        // Independientes entre sí → en paralelo. Se lanzan primero y se esperan
        // después (Task.WhenAll con tipos distintos no devuelve los valores).
        Task<List<ProductoSnapshot>> tProductos = ObtenerTodoAsync((p, s) => catalogApi.GetProductosAsync(p, s));
        Task<List<CategoriaSnapshot>> tCategorias = ObtenerTodoAsync((p, s) => catalogApi.GetCategoriasAsync(p, s));
        Task<List<ProveedorSnapshot>> tProveedores = ObtenerTodoAsync((p, s) => catalogApi.GetProveedoresAsync(p, s));
        Task<List<UsuarioSnapshot>> tUsuarios = ObtenerTodoAsync((p, s) => usersApi.GetUsuariosAsync(p, s));

        await Task.WhenAll(tProductos, tCategorias, tProveedores, tUsuarios);

        List<ProductoSnapshot> productos = await tProductos;
        List<CategoriaSnapshot> categorias = await tCategorias;
        List<ProveedorSnapshot> proveedores = await tProveedores;
        List<UsuarioSnapshot> usuarios = await tUsuarios;

        return (
            productos.ToDictionary(p => p.Id, Mapear),
            categorias.ToDictionary(c => c.Id, Mapear),
            proveedores.ToDictionary(p => p.Codigo, Mapear),
            usuarios.ToDictionary(u => u.Id, u => u.UserName ?? string.Empty));
    }

    /// <summary>
    ///     Recorre un listado paginado hasta tenerlo completo (los endpoints de los
    ///     demás servicios son paginados con máximo 100 por página).
    /// </summary>
    private static async Task<List<T>> ObtenerTodoAsync<T>(
        Func<int, int, Task<PagedResult<T>>> pagina) {
        var todos = new List<T>();
        for (int page = 0; page < 500; page++) {
            PagedResult<T> r = await pagina(page, TamanoPagina);
            if (r.Items is not null) {
                todos.AddRange(r.Items);
            }

            // Items es IEnumerable<T>: Any() en vez de Count (que sería el método LINQ).
            if (r.Items is null || !r.Items.Any() || todos.Count >= r.TotalCount) {
                break;
            }
        }

        return todos;
    }

    /// <summary>Cifras de inventario del catálogo actual.</summary>
    private async Task<ResumenProductosDto> ResumenProductosAsync() {
        (IReadOnlyDictionary<long, Producto> productos, _, _, _) = await CargarCatalogosAsync();
        var activos = productos.Values.Where(p => !p.IsDeleted).ToList();

        // Los borrados no salen del listado público (filtro global): se piden
        // aparte por su contador, que sí los incluye.
        int borrados = (await catalogApi.GetProductosBorradosAsync(0, 1, true)).TotalCount;

        return new ResumenProductosDto(
            activos.Count + borrados,
            activos.Count,
            borrados,
            activos.Count(p => p.Stock == 0),
            activos.Count(p => p.Stock > 0 && p.Stock <= 5),
            activos.Sum(p => p.Stock),
            Math.Round(activos.Sum(p => p.Precio * p.Stock), 2),
            activos.Count == 0 ? 0 : Math.Round(activos.Average(p => p.Precio), 2),
            activos.Count == 0 ? 0 : activos.Max(p => p.Precio));
    }

    /// <summary>Cifras de usuarios, con los que compraron en la ventana.</summary>
    private async Task<ResumenUsuariosDto> ResumenUsuariosAsync(PeriodoDto periodo, IEnumerable<Pedido> pedidos) {
        List<UsuarioSnapshot> usuarios = await ObtenerTodoAsync((p, s) => usersApi.GetUsuariosAsync(p, s));

        int conPedidos = pedidos.Where(EstadisticasCalculator.CuentaParaIngresos)
            .Select(p => p.UserId)
            .Distinct()
            .Count();

        return new ResumenUsuariosDto(
            usuarios.Count,
            conPedidos,
            periodo.Desde is null
                ? 0
                : usuarios.Count(u => u.CreatedAt >= periodo.Desde && u.CreatedAt < periodo.Hasta));
    }

    /// <summary>Clientes distintos con compras en la ventana.</summary>
    private static int ClientesDistintos(IEnumerable<Pedido> pedidos) {
        return pedidos.Where(EstadisticasCalculator.CuentaParaIngresos)
            .Select(p => p.UserId)
            .Distinct()
            .Count();
    }

    /// <summary>Normaliza la agrupación: se acepta en cualquier mayúscula/minúscula.</summary>
    private static string agruparCorrecto(string agrupar) {
        return (agrupar ?? "mes").Trim().ToLowerInvariant();
    }

    // ────────────────────────────────────────────────────────────
    // Mapeo snapshot → modelo de dominio (el calculador no cambia)
    // ────────────────────────────────────────────────────────────

    private static Pedido Mapear(PedidoSnapshot s) {
        return new Pedido {
            Id = ObjectId.TryParse(s.Id, out ObjectId oid) ? oid : ObjectId.GenerateNewId(),
            UserId = s.UserId,
            Items = (s.Items ?? []).Select(i => new PedidoItem {
                ProductoId = i.ProductoId,
                NombreProducto = i.NombreProducto,
                Cantidad = i.Cantidad,
                Precio = i.Precio,
                Subtotal = i.Precio * i.Cantidad
            }).ToList(),
            Total = s.Total,
            Estado = s.Estado,
            IsDeleted = s.IsDeleted,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.CreatedAt
        };
    }

    private static Producto Mapear(ProductoSnapshot s) {
        return new Producto {
            Id = s.Id,
            Nombre = s.Nombre,
            Precio = s.Precio,
            Stock = s.Stock,
            // catalog los devuelve anidados (categoria:{id}, proveedor:{codigo}).
            CategoriaId = s.Categoria?.Id ?? 0,
            ProveedorId = s.Proveedor?.Codigo,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.CreatedAt
        };
    }

    private static Categoria Mapear(CategoriaSnapshot s) {
        return new Categoria {
            Id = s.Id,
            Nombre = s.Nombre,
            CreatedAt = default,
            UpdatedAt = default
        };
    }

    private static Proveedor Mapear(ProveedorSnapshot s) {
        return new Proveedor {
            Codigo = s.Codigo,
            Nombre = s.Nombre,
            CreatedAt = default,
            UpdatedAt = default
        };
    }
}
