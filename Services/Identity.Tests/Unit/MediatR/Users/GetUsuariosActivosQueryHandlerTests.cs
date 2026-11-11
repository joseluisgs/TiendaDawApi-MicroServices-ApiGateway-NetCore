using FluentAssertions;
using Moq;
using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.MediatR.Users.Queries;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Usuarios;

namespace TiendaApi.Tests.Unit.MediatR.Users;

/// <summary>
///     Tests de GetUsuariosActivosQueryHandler: la lista de usuarios activos que
///     consume catalog para el reporte semanal (equivalente al GetActiveUsersAsync
///     de ProductoReportTask en el monolito). Solo mapea: el filtro !IsDeleted y
///     el orden por email los hace el repositorio (como en el monolito).
/// </summary>
public class GetUsuariosActivosQueryHandlerTests {
    private readonly GetUsuariosActivosQueryHandler _handler;
    private readonly Mock<IUserRepository> _repository = new();

    public GetUsuariosActivosQueryHandlerTests() {
        _handler = new GetUsuariosActivosQueryHandler(_repository.Object);
    }

    [Test]
    public async Task Handle_ConUsuariosActivos_DevuelveLaListaMapeada() {
        _repository.Setup(r => r.GetActiveUsersAsync())
            .ReturnsAsync(new List<User> {
                new() { Id = 1, UserName = "ana", Email = "ana@test.com", IsDeleted = false },
                new() { Id = 2, UserName = "luis", Email = "luis@test.com", IsDeleted = false }
            });

        IReadOnlyList<UserDto> result = await _handler.Handle(new GetUsuariosActivosQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(u => u.Username).Should().Equal("ana", "luis");
    }

    [Test]
    public async Task Handle_SinUsuarios_DevuelveVacia_NoError() {
        _repository.Setup(r => r.GetActiveUsersAsync())
            .ReturnsAsync(new List<User>());

        IReadOnlyList<UserDto> result = await _handler.Handle(new GetUsuariosActivosQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Test]
    public async Task Handle_NoExponeDatosSensibles_SoloElDtoPublico() {
        // El reporte solo necesita Email y UserName: el DTO no lleva hashes
        // ni credenciales (mismo UserDto que la ruta pública).
        _repository.Setup(r => r.GetActiveUsersAsync())
            .ReturnsAsync(new List<User> {
                new() { Id = 1, UserName = "ana", Email = "ana@test.com", PasswordHash = "secreto", IsDeleted = false }
            });

        IReadOnlyList<UserDto> result = await _handler.Handle(new GetUsuariosActivosQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        UserDto dto = result[0];
        dto.Email.Should().Be("ana@test.com");
        // El DTO expuesto no tiene campo de hash: no se filtra la contraseña.
        typeof(UserDto).GetProperty("PasswordHash").Should().BeNull();
    }
}
