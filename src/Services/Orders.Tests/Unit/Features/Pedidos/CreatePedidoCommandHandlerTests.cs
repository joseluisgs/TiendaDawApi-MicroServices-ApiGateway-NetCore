using CSharpFunctionalExtensions;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;
using Moq;
using Refit;
using System.Net;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Features.Pedidos.Commands;
using TiendaApi.Api.Features.Pedidos.Notifications;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Pedidos;
using TiendaApi.Api.Services.Cache;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Identity;

namespace TiendaApi.Tests.Unit.Features.Pedidos;

/// <summary>
/// Tests del comando de creación de pedido — el flujo con Refit de T8.
///
/// 🎓 Equivalente al del monolito, pero la transacción Serializable pasa a ser
/// <b>validación + decremento atómico en catalog</b>: aquí se mockean los
/// clients Refit (<see cref="ICatalogApi"/> y <see cref="IUsersApi"/>) en vez
/// de los repositorios de PostgreSQL.
/// </summary>
public class CreatePedidoCommandHandlerTests
{
    // Se recrean en [SetUp]: cada test parte de mocks limpios (NUnit reutiliza
    // la instancia del fixture).
    private Mock<IPedidosRepository> _pedidos = null!;
    private Mock<ICatalogApi> _catalog = null!;
    private Mock<IUsersApi> _users = null!;
    private Mock<IValidator<PedidoRequestDto>> _pedidoValidator = null!;
    private Mock<IValidator<PedidoItemRequestDto>> _itemValidator = null!;
    private Mock<IMediator> _mediator = null!;
    private Mock<ICacheService> _cache = null!;
    private CreatePedidoCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _pedidos = new Mock<IPedidosRepository>();
        _catalog = new Mock<ICatalogApi>();
        _users = new Mock<IUsersApi>();
        _pedidoValidator = new Mock<IValidator<PedidoRequestDto>>();
        _itemValidator = new Mock<IValidator<PedidoItemRequestDto>>();
        _mediator = new Mock<IMediator>();
        _cache = new Mock<ICacheService>();

        _pedidoValidator
            .Setup(v => v.ValidateAsync(It.IsAny<PedidoRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _itemValidator
            .Setup(v => v.ValidateAsync(It.IsAny<PedidoItemRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _pedidos.Setup(r => r.SaveAsync(It.IsAny<Pedido>())).ReturnsAsync((Pedido p) => p);

        _handler = new CreatePedidoCommandHandler(
            _pedidos.Object, _catalog.Object, _users.Object,
            _pedidoValidator.Object, _itemValidator.Object,
            _mediator.Object, _cache.Object, Mock.Of<IOutputCacheStore>());
    }

    private static PedidoRequestDto Request() => new()
    {
        Destinatario = new DestinatarioDto { NombreCompleto = "User DAW", Email = "u@t.com" },
        Items = [new PedidoItemRequestDto { ProductoId = 1, Cantidad = 2 }]
    };

    /// <summary>Excepción Refit con el status indicado (como la que lanza el client real).</summary>
    private static async Task<ApiException> ApiExceptionCon(HttpStatusCode status) =>
        await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Get, "http://test"),
            HttpMethod.Get,
            new HttpResponseMessage(status),
            new RefitSettings());

    /// <summary>El producto existe en catalog con el snapshot de la semilla.</summary>
    private void ProductoExiste() =>
        _catalog.Setup(c => c.GetProductoAsync(1))
            .ReturnsAsync(new ProductoSnapshotDto(1, "Laptop Dell XPS 15", 1299.99m, 10));

    [Test]
    public async Task Handle_PedidoValido_DevuelveSuccessYPublicaNotification()
    {
        ProductoExiste();

        var result = await _handler.Handle(new CreatePedidoCommand(1, Request()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Estado.Should().Be(PedidoEstado.PENDIENTE, "un pedido nuevo nace PENDIENTE");
        result.Value.Total.Should().Be(2599.98m, "total = precio de catalog x cantidad");
        result.Value.Items.Should().ContainSingle()
            .Which.NombreProducto.Should().Be("Laptop Dell XPS 15",
                "el nombre se congela desde catalog al crear el pedido");
        _mediator.Verify(m => m.Publish(It.IsAny<PedidoCreadoNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_PedidoValido_DescuentaStockEnCatalog()
    {
        ProductoExiste();

        await _handler.Handle(new CreatePedidoCommand(1, Request()), CancellationToken.None);

        // La transacción Serializable del monolito → decremento atómico en el dueño.
        _catalog.Verify(c => c.DecrementarStockAsync(1, new DecrementoStock(2)), Times.Once);
    }

    [Test]
    public async Task Handle_UsuarioNoEncontrado_DevuelveNotFoundError()
    {
        _users.Setup(u => u.GetUsuarioAsync(1)).ThrowsAsync(await ApiExceptionCon(HttpStatusCode.NotFound));

        var result = await _handler.Handle(new CreatePedidoCommand(1, Request()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
        _pedidos.Verify(r => r.SaveAsync(It.IsAny<Pedido>()), Times.Never,
            "sin usuario no se debe crear el pedido");
    }

    [Test]
    public async Task Handle_ProductoInexistente_DevuelvePedidoError404()
    {
        _catalog.Setup(c => c.GetProductoAsync(999)).ThrowsAsync(await ApiExceptionCon(HttpStatusCode.NotFound));

        var dto = new PedidoRequestDto
        {
            Destinatario = new DestinatarioDto { NombreCompleto = "X", Email = "x@t.com" },
            Items = [new PedidoItemRequestDto { ProductoId = 999, Cantidad = 1 }]
        };

        var result = await _handler.Handle(new CreatePedidoCommand(1, dto), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
        _pedidos.Verify(r => r.SaveAsync(It.IsAny<Pedido>()), Times.Never);
    }

    [Test]
    public async Task Handle_StockInsuficiente_DevuelveElMensajeDeCatalog()
    {
        ProductoExiste();
        // catalog responde 422 con el MISMO texto que el monolito.
        _catalog.Setup(c => c.DecrementarStockAsync(It.IsAny<long>(), It.IsAny<DecrementoStock>()))
            .ThrowsAsync(await ApiExceptionCon(HttpStatusCode.UnprocessableEntity));

        var result = await _handler.Handle(new CreatePedidoCommand(1, Request()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BusinessRuleError>(
            "el 422 de catalog es una regla de negocio, no un error de validación");
        _pedidos.Verify(r => r.SaveAsync(It.IsAny<Pedido>()), Times.Never,
            "si no hay stock, no se guarda el pedido");
    }

    [Test]
    public async Task Handle_DatosInvalidos_DevuelveValidationError_SinTocarCatalog()
    {
        _pedidoValidator.Setup(v => v.ValidateAsync(It.IsAny<PedidoRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Items", "vacío") }));

        var result = await _handler.Handle(new CreatePedidoCommand(1, Request()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        _catalog.Verify(c => c.GetProductoAsync(It.IsAny<long>()), Times.Never,
            "la validación local va antes de cualquier llamada remota");
    }
}
