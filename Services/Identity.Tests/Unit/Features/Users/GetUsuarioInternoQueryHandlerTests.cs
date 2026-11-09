using CSharpFunctionalExtensions;
using FluentAssertions;
using Moq;
using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Features.Users.Queries;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Usuarios;

namespace TiendaApi.Tests.Unit.Features.Users;

/// <summary>
/// Tests de GetUsuarioInternoQueryHandler: la comprobación de existencia que
/// hace orders al crear un pedido (equivalente al FindByIdAsync de
/// CreatePedidoCommand). Lectura fresca SIN caché — un usuario borrado no
/// debe "seguir existiendo" durante el TTL de la ruta pública.
/// </summary>
public class GetUsuarioInternoQueryHandlerTests
{
    private readonly Mock<IUserRepository> _repository = new();
    private readonly GetUsuarioInternoQueryHandler _handler;

    public GetUsuarioInternoQueryHandlerTests()
    {
        _handler = new GetUsuarioInternoQueryHandler(_repository.Object);
    }

    [Test]
    public async Task Handle_UsuarioExistente_DevuelveSuccessConElDto()
    {
        _repository.Setup(r => r.FindByIdAsync(1))
            .ReturnsAsync(new User { Id = 1, UserName = "juan", Email = "juan@test.com", IsDeleted = false });

        var result = await _handler.Handle(new GetUsuarioInternoQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(1);
        result.Value.Username.Should().Be("juan");
    }

    [Test]
    public async Task Handle_UsuarioNoExiste_DevuelveNotFound()
    {
        _repository.Setup(r => r.FindByIdAsync(999)).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new GetUsuarioInternoQuery(999), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
    }

    [Test]
    public async Task Handle_UsuarioBorrado_DevuelveNotFound()
    {
        // Semántica idéntica a `user is null || user.IsDeleted` del monolito.
        _repository.Setup(r => r.FindByIdAsync(1))
            .ReturnsAsync(new User { Id = 1, IsDeleted = true });

        var result = await _handler.Handle(new GetUsuarioInternoQuery(1), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
    }

    [Test]
    public async Task Handle_LeeDelRepositorio_CadaInvocacion()
    {
        // Sin caché: dos consultas seguidas leen dos veces (estado real).
        _repository.Setup(r => r.FindByIdAsync(1))
            .ReturnsAsync(new User { Id = 1, IsDeleted = false });

        await _handler.Handle(new GetUsuarioInternoQuery(1), CancellationToken.None);
        await _handler.Handle(new GetUsuarioInternoQuery(1), CancellationToken.None);

        _repository.Verify(r => r.FindByIdAsync(1), Times.Exactly(2));
    }
}
