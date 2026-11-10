using System.Net;
using System.Security.Claims;
using FluentAssertions;
using HotChocolate;
using Moq;
using Refit;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.GraphQL.Queries;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Orders;

namespace TiendaApi.Tests.Unit.GraphQL;

/// <summary>
/// Tests de las queries GraphQL con los clients Refit mockeados.
///
/// ðŸŽ“ En el monolito las queries pasaban por MediatR;
/// aquÃ­ son la llamada REST equivalente. Se cubre tambiÃ©n el mapeo de errores
/// (404 â†’ null) y el hecho de que <c>misPedidos</c> delega el filtro en orders.
/// </summary>
public class TiendaQueryTests
{
    // Se recrean en [SetUp]: cada test parte de mocks limpios (NUnit reutiliza
    // la instancia del fixture).
    private Mock<ICatalogApi> _catalog = null!;
    private Mock<IOrdersApi> _orders = null!;
    private TiendaQuery _query = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new Mock<ICatalogApi>();
        _orders = new Mock<IOrdersApi>();
        _query = new TiendaQuery();

        _catalog.Setup(c => c.GetProductosAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Pagina(new List<ProductoDto>
            {
                new(1, "Laptop", "desc", 1299.99m, 10, null,
                    new CategoriaResumenDto(1, "ElectrÃ³nica", null),
                    new ProveedorResumenDto("Distribuc01", "Dist", null),
                    DateTime.UtcNow, DateTime.UtcNow),
            }));
        _catalog.Setup(c => c.GetCategoriasAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Pagina(new List<CategoriaDto>
            {
                new(1, "ElectrÃ³nica", "dispositivos", DateTime.UtcNow, DateTime.UtcNow),
            }));
    }

    private static PagedResult<T> Pagina<T>(List<T> items) => new()
    {
        Items = items,
        TotalCount = items.Count,
        Page = 1,
        PageSize = 100,
    };

    // â”€â”€ productos â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Test]
    public async Task GetProductos_DevuelveLosDeLaPrimeraPagina()
    {
        var resultado = await _query.GetProductos(_catalog.Object);

        resultado.Should().HaveCount(1);
        resultado[0].Nombre.Should().Be("Laptop");
        _catalog.Verify(c => c.GetProductosAsync(0, 100), Times.Once,
            "GraphQL pide la primera pÃ¡gina completa, sin filtros");
    }

    [Test]
    public async Task GetProducto_Existente_DevuelveElDto()
    {
        _catalog.Setup(c => c.GetProductoAsync(1))
            .ReturnsAsync(new ProductoDto(1, "Laptop", "d", 10m, 5, null,
                new CategoriaResumenDto(1, "ElectrÃ³nica", null), null,
                DateTime.UtcNow, DateTime.UtcNow));

        var resultado = await _query.GetProducto(1, _catalog.Object);

        resultado.Should().NotBeNull();
        resultado!.Id.Should().Be(1);
    }

    [Test]
    public async Task GetProducto_CatalogResponde404_DevuelveNull()
    {
        // GraphQL devuelve null (no error) cuando el recurso no existe.
        _catalog.Setup(c => c.GetProductoAsync(999999))
            .ThrowsAsync(await ApiExceptionCon(HttpStatusCode.NotFound));

        var resultado = await _query.GetProducto(999999, _catalog.Object);

        resultado.Should().BeNull();
    }

    [Test]
    public async Task GetProductosPaged_ConvierteABase1()
    {
        // GraphQL expone base 1; la API REST trabaja con base 0.
        await _query.GetProductosPaged(_catalog.Object, page: 3, size: 25);

        _catalog.Verify(c => c.GetProductosAsync(2, 25), Times.Once,
            "page=3 en GraphQL = page=2 en la API");
    }

    [Test]
    public async Task GetCategoria_Existente_DevuelveElDto()
    {
        _catalog.Setup(c => c.GetCategoriaAsync(1))
            .ReturnsAsync(new CategoriaDto(1, "ElectrÃ³nica", "d", DateTime.UtcNow, DateTime.UtcNow));

        var resultado = await _query.GetCategoria(1, _catalog.Object);

        resultado!.Nombre.Should().Be("ElectrÃ³nica");
    }

    // â”€â”€ pedidos â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Test]
    public async Task GetPedidos_DevuelveLoQueOrdersDiga()
    {
        _orders.Setup(o => o.GetPedidosAsync())
            .ReturnsAsync(new List<PedidoDto>
            {
                new("6af0b4ed01265a8f9a0bebe8", 2, null!, [], 10m, "PENDIENTE", null, DateTime.UtcNow),
            });

        var resultado = await _query.GetPedidos(_orders.Object);

        resultado.Should().ContainSingle();
        _orders.Verify(o => o.GetPedidosAsync(), Times.Once);
    }

    [Test]
    public async Task GetMisPedidos_SinClaim_DejaDeLlamar()
    {
        var sinClaims = new ClaimsPrincipal(new ClaimsIdentity());

        var lanzada = await CapturarAsync(() => _query.GetMisPedidos(sinClaims, _orders.Object));

        lanzada.Should().NotBeNull("un token sin identidad no debe llegar a orders");
        _orders.Verify(o => o.GetMisPedidosAsync(), Times.Never);
    }

    [Test]
    public async Task GetMisPedidos_ConClaim_DelegaElFiltroEnOrders()
    {
        // El filtro por usuario lo aplica orders con el JWT: GraphQL solo propaga.
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "2") }, "test");
        var user = new ClaimsPrincipal(identity);

        _orders.Setup(o => o.GetMisPedidosAsync())
            .ReturnsAsync(new List<PedidoDto>
            {
                new("6af0b4ed01265a8f9a0bebe9", 2, null!, [], 5m, "PENDIENTE", null, DateTime.UtcNow),
            });

        var resultado = await _query.GetMisPedidos(user, _orders.Object);

        resultado.Should().ContainSingle(p => p.UserId == 2);
        _orders.Verify(o => o.GetMisPedidosAsync(), Times.Once,
            "el filtro por usuario es responsabilidad de orders (/me)");
    }

    [Test]
    public async Task GetMisPedidos_ClaimNoNumerico_DejaDeLlamar()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "abc") }, "test");
        var user = new ClaimsPrincipal(identity);

        var lanzada = await CapturarAsync(() => _query.GetMisPedidos(user, _orders.Object));

        lanzada.Should().NotBeNull("un claim no numérico no identifica a nadie");
        _orders.Verify(o => o.GetMisPedidosAsync(), Times.Never);
    }

    /// <summary>
    /// Ejecuta y devuelve la <see cref="GraphQLException"/> lanzada (o null si no
    /// lanzó nada): se comprueba que se lanza Y que no se llamó al servicio remoto.
    /// </summary>
    private static async Task<GraphQLException?> CapturarAsync(Func<Task> operacion)
    {
        try
        {
            await operacion();
            return null;
        }
        catch (GraphQLException ex)
        {
            return ex;
        }
    }

    /// <summary>ExcepciÃ³n Refit con el status indicado.</summary>
    private static async Task<ApiException> ApiExceptionCon(HttpStatusCode status) =>
        await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Get, "http://catalog"),
            HttpMethod.Get,
            new HttpResponseMessage(status),
            new RefitSettings());
}
