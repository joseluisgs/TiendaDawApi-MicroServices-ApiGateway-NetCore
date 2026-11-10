using FluentAssertions;
using HotChocolate;
using Moq;
using Refit;
using System.Net;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.GraphQL.Inputs;
using TiendaApi.Api.GraphQL.Mutations;
using TiendaApi.Api.Services.Catalog;

namespace TiendaApi.Tests.Unit.GraphQL;

/// <summary>
/// Tests de las mutations GraphQL con el client Refit de catalog mockeado.
///
/// 🎓 la validación de negocio la hace catalog y aquí
/// solo se traduce su respuesta a un error de GraphQL (para que salga en
/// <c>errors[]</c> con el mismo texto que daría REST).
/// </summary>
public class ProductoMutationTests
{
    // Se recrean en [SetUp]: cada test parte de mocks limpios (NUnit reutiliza
    // la instancia del fixture).
    private Mock<ICatalogApi> _catalog = null!;
    private ProductoMutation _mutation = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new Mock<ICatalogApi>();
        _mutation = new ProductoMutation();
    }

    private static CreateProductoInput Input(string nombre = "Laptop", long categoriaId = 1) => new()
    {
        Nombre = nombre,
        Precio = 9.99m,
        Stock = 1,
        CategoriaId = categoriaId,
    };

    private static ProductoDto Producto(long id, string nombre) => new(
        id, nombre, "desc", 9.99m, 1, null,
        new CategoriaResumenDto(1, "Electrónica", null), null,
        DateTime.UtcNow, DateTime.UtcNow);

    // ── create ─────────────────────────────────────────────────────────

    [Test]
    public async Task CreateProducto_MapeaElInput_YDevuelveElDtoDeCatalog()
    {
        _catalog.Setup(c => c.CreateProductoAsync(It.IsAny<ProductoRequestDto>()))
            .ReturnsAsync(Producto(7, "Laptop"));

        var resultado = await _mutation.CreateProducto(Input(), _catalog.Object);

        resultado.Id.Should().Be(7);
        // Se comprueba que viaja el DTO correcto, no el input de GraphQL.
        _catalog.Verify(c => c.CreateProductoAsync(It.Is<ProductoRequestDto>(d =>
            d.Nombre == "Laptop" && d.Precio == 9.99m && d.CategoriaId == 1)), Times.Once);
    }

    [Test]
    public async Task CreateProducto_CatalogDevuelve400_LanzaGraphQLExceptionConSuMensaje()
    {
        // El E2E busca "validaci" en errors[]: el mensaje debe ser el de catalog.
        _catalog.Setup(c => c.CreateProductoAsync(It.IsAny<ProductoRequestDto>()))
            .ThrowsAsync(await ApiExceptionCon(HttpStatusCode.BadRequest,
                """{"message":"Errores de validación","errors":{"CategoriaId":["no existe"]}}"""));

        var lanzada = await CapturarAsync(() =>
            _mutation.CreateProducto(Input(categoriaId: 999999), _catalog.Object));

        lanzada.Should().NotBeNull("el error de negocio debe salir en errors[] de GraphQL");
        lanzada!.Message.Should().Contain("validaci",
            "el mensaje de catalog se reenvía tal cual");
    }

    [Test]
    public async Task CreateProducto_CatalogResponde500_NoSeComeElError()
    {
        _catalog.Setup(c => c.CreateProductoAsync(It.IsAny<ProductoRequestDto>()))
            .ThrowsAsync(await ApiExceptionCon(HttpStatusCode.InternalServerError, ""));

        var lanzada = await CapturarAsync(() => _mutation.CreateProducto(Input(), _catalog.Object));

        lanzada.Should().NotBeNull("un 500 de catalog no debe devolver null en silencio");
    }

    // ── update ─────────────────────────────────────────────────────────

    [Test]
    public async Task UpdateProducto_UsaElPatch_ParaNoPisarLosCamposNull()
    {
        _catalog.Setup(c => c.UpdateProductoAsync(It.IsAny<long>(), It.IsAny<ProductoPatchDto>()))
            .ReturnsAsync(Producto(7, "Laptop"));

        // Solo se envía el precio: el resto debe llegar como null (merge en catalog).
        await _mutation.UpdateProducto(7, new UpdateProductoInput { Precio = 11.11m }, _catalog.Object);

        _catalog.Verify(c => c.UpdateProductoAsync(7, It.Is<ProductoPatchDto>(p =>
            p.Precio == 11.11m && p.Nombre == null && p.Stock == null)),
            Times.Once, "el PATCH solo lleva los campos enviados");
    }

    [Test]
    public async Task UpdateProducto_ProductoInexistente_LanzaGraphQLException()
    {
        _catalog.Setup(c => c.UpdateProductoAsync(It.IsAny<long>(), It.IsAny<ProductoPatchDto>()))
            .ThrowsAsync(await ApiExceptionCon(HttpStatusCode.NotFound,
                """{"message":"Recurso con ID 7 no encontrado"}"""));

        var lanzada = await CapturarAsync(() =>
            _mutation.UpdateProducto(7, new UpdateProductoInput(), _catalog.Object));

        lanzada.Should().NotBeNull();
        lanzada!.Message.Should().Contain("7");
    }

    // ── delete ─────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteProducto_Ok_DevuelveTrue()
    {
        _catalog.Setup(c => c.DeleteProductoAsync(7)).Returns(Task.CompletedTask);

        var resultado = await _mutation.DeleteProducto(7, _catalog.Object);

        resultado.Should().BeTrue();
        _catalog.Verify(c => c.DeleteProductoAsync(7), Times.Once);
    }

    [Test]
    public async Task DeleteProducto_Fallo_LanzaGraphQLException()
    {
        _catalog.Setup(c => c.DeleteProductoAsync(7))
            .ThrowsAsync(await ApiExceptionCon(HttpStatusCode.NotFound, "{}"));

        var lanzada = await CapturarAsync(() => _mutation.DeleteProducto(7, _catalog.Object));

        lanzada.Should().NotBeNull();
    }

    /// <summary>
    /// Ejecuta y devuelve la <see cref="GraphQLException"/> lanzada (o null si no
    /// lanzó nada). Más explícito que FluentAssertions aquí: se comprueba el
    /// MENSAJE, que es lo que el E2E va a leer en <c>errors[]</c>.
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

    /// <summary>Excepción Refit con el status indicado (así se comporta el client real).</summary>
    private static async Task<ApiException> ApiExceptionCon(HttpStatusCode status, string body) =>
        await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Post, "http://catalog"),
            HttpMethod.Post,
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            },
            new RefitSettings());
}
