using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Refit;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Estadisticas;
using TiendaApi.Api.Services.Historial;
using TiendaApi.Api.Services.Identity;
using TiendaApi.Api.Services.Orders;

namespace TiendaApi.Tests.Unit.Services.Estadisticas;

/// <summary>
/// Tests del servicio de estadísticas con los clients Refit mockeados.
///
/// 🎓 en el monolito el servicio leía repositorios con
/// los modelos de dominio ya construidos; aquí llegan <i>snapshots</i> por JSON
/// y hay que convertirlos. Los errores de mapeo son SILENCIOSOS (el cálculo
/// devuelve ceros en vez de fallar), así que se cubren explícitamente.
/// </summary>
public class EstadisticasServiceTests {
    // Se recrean en [SetUp]: cada test parte de mocks limpios (NUnit reutiliza
    // la instancia del fixture).
    private Mock<IOrdersApi> _orders = null!;
    private Mock<ICatalogApi> _catalog = null!;
    private Mock<IUsersApi> _users = null!;
    private Mock<IHistorialApi> _historial = null!;
    private EstadisticasService _service = null!;

    [SetUp]
    public void SetUp() {
        _orders = new Mock<IOrdersApi>();
        _catalog = new Mock<ICatalogApi>();
        _users = new Mock<IUsersApi>();
        _historial = new Mock<IHistorialApi>();

        _service = new EstadisticasService(
            _orders.Object, _catalog.Object, _users.Object, _historial.Object,
            NullLogger<EstadisticasService>.Instance);

        // Catálogos por defecto: página 0 con TODO y página 1 vacía — así el
        // bucle de paginación corta bien y además se comprueba.
        var productos = Productos();
        var categorias = Categorias();
        var proveedores = Proveedores();
        var usuarios = Usuarios();

        _catalog.Setup(c => c.GetProductosAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((int p, int s) => Pagina(productos, p));
        _catalog.Setup(c => c.GetProductosBorradosAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new PagedResult<ProductoSnapshot> { Items = [], TotalCount = 0 });
        _catalog.Setup(c => c.GetCategoriasAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((int p, int s) => Pagina(categorias, p));
        _catalog.Setup(c => c.GetProveedoresAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((int p, int s) => Pagina(proveedores, p));
        _users.Setup(u => u.GetUsuariosAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((int p, int s) => Pagina(usuarios, p));
        _orders.Setup(o => o.GetPedidosAsync()).ReturnsAsync(Pedidos());
    }

    /// <summary>Página con todo en la 0 y vacía en las demás (corta el bucle).</summary>
    private static PagedResult<T> Pagina<T>(List<T> items, int page) => new() {
        Items = page == 0 ? items : Enumerable.Empty<T>(),
        TotalCount = items.Count,
        Page = page + 1,
        PageSize = 100,
    };

    private static List<ProductoSnapshot> Productos() =>
    [
        new(1, "Laptop Dell XPS 15", 1299.99m, 10, new CategoriaRef(1), new ProveedorRef("Distribuc01"), DateTime.UtcNow),
        new(2, "Camiseta Nike", 29.99m, 50, new CategoriaRef(2), new ProveedorRef("Distribuc01"), DateTime.UtcNow),
        new(3, "Clean Code", 42.99m, 25, new CategoriaRef(3), new ProveedorRef("Editorial01"), DateTime.UtcNow),
    ];

    private static List<CategoriaSnapshot> Categorias() =>
    [
        new(1, "Electrónica"),
        new(2, "Ropa"),
        new(3, "Libros"),
    ];

    private static List<ProveedorSnapshot> Proveedores() =>
    [
        new("Distribuc01", "Distribuciones DAW"),
        new("Editorial01", "Editorial Código Limpio"),
    ];

    private static List<UsuarioSnapshot> Usuarios() =>
    [
        new(1, "admin", DateTime.UtcNow.AddDays(-30)),
        new(2, "userdaw", DateTime.UtcNow.AddDays(-10)),
    ];

    private static List<PedidoSnapshot> Pedidos() =>
    [
        new("6af0b4ed01265a8f9a0bebe8", 2,
            [new PedidoItemSnapshot(1, "Laptop Dell XPS 15", 2, 1299.99m)],
            2599.98m, "PENDIENTE", false, DateTime.UtcNow.AddDays(-1)),
    ];

    // ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Inventario_CruzaProductoConCategoria_YProveedor() {
        var result = await _service.InventarioAsync(stockMax: 5);

        result.IsSuccess.Should().BeTrue();
        // Si CategoriaId llegara a 0 (mapeo roto), porCategoria saldría vacío.
        result.Value.PorCategoria.Should().HaveCount(3,
            "cada producto debe colgar de su categoría (la referencia viene ANIDADA en el JSON)");
    }

    [Test]
    public async Task Inventario_AgotaLosQueTienenStockCero() {
        var result = await _service.InventarioAsync(stockMax: 5);

        result.IsSuccess.Should().BeTrue();
        result.Value.Productos.Total.Should().Be(3);
        result.Value.Productos.StockTotal.Should().Be(85, "10 + 50 + 25");
    }

    [Test]
    public async Task Resumen_CuentaPedidos_YRecaudacion() {
        _orders.Setup(o => o.GetPedidosAsync()).ReturnsAsync(Pedidos());

        var result = await _service.ResumenAsync(null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Ventas.Pedidos.Should().Be(1);
        result.Value.Ventas.Recaudacion.Should().Be(2599.98m);
        result.Value.Usuarios.Total.Should().Be(2);
    }

    [Test]
    public async Task Resumen_LasCategoriasSinProducto_SaleConCero() {
        // Con los 3 productos mapeados bien, ninguna categoría queda "sin producto".
        var result = await _service.ResumenAsync(null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Categorias.SinProductos.Should().Be(0,
            "si CategoriaId llegara a 0, las 3 categorías parecerían vacías");
        result.Value.Categorias.Total.Should().Be(3);
    }

    [Test]
    public async Task Clientes_ProductoFavorito_VieneDelItemDesnormalizado() {
        _orders.Setup(o => o.GetPedidosAsync()).ReturnsAsync(Pedidos());

        var result = await _service.ClientesAsync(null, null, top: 5);

        result.IsSuccess.Should().BeTrue();
        result.Value.Clientes.Should().ContainSingle();
        // Depende de PedidoItem.NombreProducto, que viaja desnormalizado en orders.
        result.Value.Clientes[0].ProductoFavorito.Should().Be("Laptop Dell XPS 15");
        result.Value.Clientes[0].Gasto.Should().Be(2599.98m);
    }

    [Test]
    public async Task Proveedores_AgrupaPorCodigoDelProducto() {
        // Pedidos que cubren los DOS proveedores, para comprobar el cruce.
        _orders.Setup(o => o.GetPedidosAsync()).ReturnsAsync(
        [
            new("6af0b4ed01265a8f9a0bebe8", 2,
                [new PedidoItemSnapshot(1, "Laptop Dell XPS 15", 1, 1299.99m)],
                1299.99m, "PENDIENTE", false, DateTime.UtcNow.AddDays(-1)),
            new("6af0b4ed01265a8f9a0bebe9", 2,
                [new PedidoItemSnapshot(3, "Clean Code", 2, 42.99m)],
                85.98m, "PENDIENTE", false, DateTime.UtcNow.AddDays(-1)),
        ]);

        var result = await _service.ProveedoresAsync(null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Proveedores.Should().HaveCount(2,
            "el cruce producto→proveedor usa ProveedorId, que viene anidado en el JSON");
        result.Value.Proveedores.Select(p => p.Codigo).Should().BeEquivalentTo(
            new[] { "Distribuc01", "Editorial01" });
    }

    [Test]
    public async Task Ventas_AgrupacionInvalida_Devuelve400() {
        var result = await _service.VentasAsync(null, null, "trimestre");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("trimestre");
    }

    [Test]
    public async Task Precios_ProductoInexistente_Devuelve404() {
        // catalog responde 404 al pedir el producto → el dueño manda.
        _catalog.Setup(c => c.GetProductosAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(await ApiExceptionCon(System.Net.HttpStatusCode.NotFound));

        var result = await _service.PreciosAsync(999999, null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>(
            "el 404 lo decide catalog, igual que FindByIdAsync en el monolito");
    }

    /// <summary>Excepción Refit con el status indicado.</summary>
    private static async Task<ApiException> ApiExceptionCon(System.Net.HttpStatusCode status) =>
        await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Get, "http://test"),
            HttpMethod.Get,
            new HttpResponseMessage(status),
            new RefitSettings());
}
