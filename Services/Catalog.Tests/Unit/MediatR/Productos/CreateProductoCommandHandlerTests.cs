using CSharpFunctionalExtensions;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;
using Moq;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Errors;
using TiendaApi.Api.MediatR.Productos.Commands;
using TiendaApi.Api.MediatR.Productos.Notifications;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Categorias;
using TiendaApi.Api.Repositories.Productos;
using TiendaApi.Api.Services.Cache;

namespace TiendaApi.Tests.Unit.MediatR.Productos;

/// <summary>
///     Tests del command de alta de producto (CQRS: lado de escritura).
///     🎓 Orden que hay que preservar (el monolito lo documenta): PRIMERO replicar
///     (publicar → sync a MongoDB) y DESPUÉS invalidar las cachés; si se invierte,
///     un lector puede repoblar la caché con el modelo de lectura viejo.
/// </summary>
public class CreateProductoCommandHandlerTests {
    private Mock<ICacheService> _cache = null!;
    private Mock<ICategoriaRepository> _categorias = null!;
    private CreateProductoCommandHandler _handler = null!;
    private Mock<IMediator> _mediator = null!;

    private Mock<IOutputCacheStore> _outputCache = null!;

    // Se recrean en [SetUp]: cada test parte de mocks limpios, sin arrastrar
    // invocaciones de los tests anteriores (NUnit reutiliza la instancia).
    private Mock<IProductoRepository> _productos = null!;
    private Mock<IValidator<ProductoRequestDto>> _validador = null!;

    [SetUp]
    public void SetUp() {
        _productos = new Mock<IProductoRepository>();
        _categorias = new Mock<ICategoriaRepository>();
        _mediator = new Mock<IMediator>();
        _cache = new Mock<ICacheService>();
        _outputCache = new Mock<IOutputCacheStore>();
        _validador = new Mock<IValidator<ProductoRequestDto>>();

        _validador.Setup(v => v.ValidateAsync(It.IsAny<ProductoRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _handler = new CreateProductoCommandHandler(
            _productos.Object, _categorias.Object, _validador.Object,
            _mediator.Object, _cache.Object, _outputCache.Object);
    }

    private static ProductoRequestDto Request(long categoriaId = 1) {
        return new ProductoRequestDto {
            Nombre = "Laptop Dell XPS 15",
            Descripcion = "Alto rendimiento",
            Precio = 1299.99m,
            Stock = 10,
            CategoriaId = categoriaId
        };
    }

    [Test]
    public async Task Handle_Invalido_Devuelve400ConErroresPorCampo() {
        _validador.Setup(v => v.ValidateAsync(It.IsAny<ProductoRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] {
                new ValidationFailure("Precio", "El precio debe ser mayor a 0")
            }));

        Result<ProductoDto, DomainError> result =
            await _handler.Handle(new CreateProductoCommand(Request()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        _productos.Verify(r => r.SaveAsync(It.IsAny<Producto>()), Times.Never);
    }

    [Test]
    public async Task Handle_CategoriaNoExiste_Devuelve400ConElIdEnElCampo() {
        // El handler comprueba la FK con FindByIdAsync (no con ExistsByIdAsync).
        _categorias.Setup(r => r.FindByIdAsync(999)).ReturnsAsync((Categoria?)null);

        Result<ProductoDto, DomainError> result =
            await _handler.Handle(new CreateProductoCommand(Request(999)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        ValidationError error = result.Error.Should().BeOfType<ValidationError>().Subject;
        error.ValidationErrors.Should().ContainKey("CategoriaId");
        error.ValidationErrors["CategoriaId"][0].Should().Contain("999",
            "el mensaje debe identificar la categoría que falta (lo reenvía el E2E)");
        _productos.Verify(r => r.SaveAsync(It.IsAny<Producto>()), Times.Never);
    }

    [Test]
    public async Task Handle_Ok_DevuelveElDtoCreado() {
        _categorias.Setup(r => r.FindByIdAsync(1))
            .ReturnsAsync(new Categoria { Id = 1, Nombre = "Electrónica" });
        _productos.Setup(r => r.SaveAsync(It.IsAny<Producto>()))
            .ReturnsAsync(new Producto { Id = 15, Nombre = "Laptop Dell XPS 15", CategoriaId = 1 });

        Result<ProductoDto, DomainError> result =
            await _handler.Handle(new CreateProductoCommand(Request()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(15);
    }

    [Test]
    public async Task Handle_Ok_PublicaLaNotificacion_YDespuesInvalidaCaches() {
        _categorias.Setup(r => r.FindByIdAsync(1))
            .ReturnsAsync(new Categoria { Id = 1, Nombre = "Electrónica" });
        _productos.Setup(r => r.SaveAsync(It.IsAny<Producto>()))
            .ReturnsAsync(new Producto { Id = 15, CategoriaId = 1 });

        var orden = new List<string>();
        _mediator.Setup(m => m.Publish(It.IsAny<ProductoCreadoNotification>(), It.IsAny<CancellationToken>()))
            .Callback(() => orden.Add("publish"));
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Callback(() => orden.Add("cache"));
        _outputCache.Setup(o => o.EvictByTagAsync("productos", It.IsAny<CancellationToken>()))
            .Callback(() => orden.Add("outputcache"));

        await _handler.Handle(new CreateProductoCommand(Request()), CancellationToken.None);

        orden.Should().NotBeEmpty();
        orden.First().Should().Be("publish",
            "hay que replicar ANTES de invalidar: si no, un lector repuebla la caché con datos viejos");
    }

    [Test]
    public async Task Handle_Ok_InvalidaLaCachedeLaCategoria() {
        _categorias.Setup(r => r.FindByIdAsync(3))
            .ReturnsAsync(new Categoria { Id = 3, Nombre = "Libros" });
        _productos.Setup(r => r.SaveAsync(It.IsAny<Producto>()))
            .ReturnsAsync(new Producto { Id = 15, CategoriaId = 3 });

        await _handler.Handle(new CreateProductoCommand(Request(3)), CancellationToken.None);

        _cache.Verify(c => c.RemoveAsync("productos:categoria:3"), Times.Once);
        _outputCache.Verify(o => o.EvictByTagAsync("productos", It.IsAny<CancellationToken>()), Times.Once);
    }
}
