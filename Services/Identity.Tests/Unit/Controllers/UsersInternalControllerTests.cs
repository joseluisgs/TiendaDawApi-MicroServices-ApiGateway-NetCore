using CSharpFunctionalExtensions;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TiendaApi.Api.Controllers;
using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Usuarios;
using TiendaApi.Api.Features.Users.Queries;

namespace TiendaApi.Tests.Unit.Controllers;

/// <summary>
///     Tests del contrato interno de identity (GET /internal/users/{id} y
///     /internal/users/activos): delegación pura en MediatR y mapeo de los
///     Result a códigos HTTP (200/404).
/// </summary>
public class UsersInternalControllerTests {
    private UsersInternalController _controller = null!;
    private Mock<IMediator> _mediator = null!;

    [SetUp]
    public void SetUp() {
        _mediator = new Mock<IMediator>();
        _controller = new UsersInternalController(_mediator.Object);
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
    }

    [Test]
    public async Task GetUsuario_Existente_Devuelve200ConElDto() {
        var dto = new UserDto(1, "juan", "juan@test.com", "", "USER", DateTime.UtcNow);
        _mediator.Setup(m => m.Send(It.IsAny<GetUsuarioInternoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserDto, DomainError>(dto));

        ActionResult<UserDto> result = await _controller.GetUsuario(1);

        OkObjectResult ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(dto);
    }

    [Test]
    public async Task GetUsuario_NoExiste_Devuelve404() {
        _mediator.Setup(m => m.Send(It.IsAny<GetUsuarioInternoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<UserDto, DomainError>(
                UsuarioError.NotFound(999)));

        ActionResult<UserDto> result = await _controller.GetUsuario(999);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public async Task GetActivos_ConLista_Devuelve200() {
        var lista = new List<UserDto> {
            new(1, "ana", "ana@test.com", "", "USER", DateTime.UtcNow),
            new(2, "luis", "luis@test.com", "", "USER", DateTime.UtcNow)
        };
        _mediator.Setup(m => m.Send(It.IsAny<GetUsuariosActivosQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lista);

        ActionResult<IReadOnlyList<UserDto>> result = await _controller.GetActivos();

        OkObjectResult ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(lista);
    }

    [Test]
    public async Task GetActivos_SinUsuarios_Devuelve200Vacia() {
        _mediator.Setup(m => m.Send(It.IsAny<GetUsuariosActivosQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>());

        ActionResult<IReadOnlyList<UserDto>> result = await _controller.GetActivos();

        OkObjectResult ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new List<UserDto>());
    }
}
