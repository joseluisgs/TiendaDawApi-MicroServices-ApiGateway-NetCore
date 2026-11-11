using MediatR;
using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.Mappers;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Usuarios;

namespace TiendaApi.Api.MediatR.Users.Queries;

/// <summary>
///     Query INTERNA (rutas <c>/internal/*</c>, R7): lista de usuarios activos
///     (<c>!IsDeleted</c>, ordenados por email), para que <b>catalog</b> envíe el
///     reporte semanal de productos (equivale al <c>GetActiveUsersAsync</c> de
///     <c>ProductoReportTask</c> en el monolito).
/// </summary>
public record GetUsuariosActivosQuery : IRequest<IReadOnlyList<UserDto>>;

/// <summary>
///     Handler de GetUsuariosActivosQuery. Sin caché (lectura fresca) — el reporte
///     no debe mandar emails a cuentas ya borradas.
/// </summary>
public class GetUsuariosActivosQueryHandler(IUserRepository repository)
    : IRequestHandler<GetUsuariosActivosQuery, IReadOnlyList<UserDto>> {
    /// <inheritdoc />
    public async Task<IReadOnlyList<UserDto>> Handle(
        GetUsuariosActivosQuery request, CancellationToken cancellationToken) {
        IEnumerable<User> usuarios = await repository.GetActiveUsersAsync();
        return usuarios.ToDtoList().ToList();
    }
}
