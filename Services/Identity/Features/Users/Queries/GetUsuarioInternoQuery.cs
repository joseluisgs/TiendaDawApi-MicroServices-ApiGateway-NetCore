using CSharpFunctionalExtensions;
using MediatR;
using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Usuarios;
using TiendaApi.Api.Mappers;
using TiendaApi.Api.Repositories.Usuarios;

namespace TiendaApi.Api.Features.Users.Queries;

/// <summary>
/// Query INTERNA (rutas <c>/internal/*</c>, R7): comprueba que un usuario
/// exista y no esté borrado, para que <b>orders</b> valide al crear un pedido
/// (equivalente al <c>FindByIdAsync</c> de <c>CreatePedidoCommand</c> en el
/// monolito) sin exponer la BD.
/// </summary>
public record GetUsuarioInternoQuery(long Id)
    : IRequest<Result<UserDto, DomainError>>;

/// <summary>
/// Handler de GetUsuarioInternoQuery. Lectura <b>FRESCA sin caché</b>: el
/// consumidor decide con el estado REAL del usuario (un usuario borrado no
/// debe seguir "existiendo" en el caché durante el TTL de la ruta pública).
/// </summary>
public class GetUsuarioInternoQueryHandler(IUserRepository repository)
    : IRequestHandler<GetUsuarioInternoQuery, Result<UserDto, DomainError>>
{
    /// <inheritdoc/>
    public async Task<Result<UserDto, DomainError>> Handle(
        GetUsuarioInternoQuery request, CancellationToken cancellationToken)
    {
        var user = await repository.FindByIdAsync(request.Id);
        if (user is null or { IsDeleted: true })
            return Result.Failure<UserDto, DomainError>(UsuarioError.NotFound(request.Id));

        return Result.Success<UserDto, DomainError>(user.ToDto());
    }
}
