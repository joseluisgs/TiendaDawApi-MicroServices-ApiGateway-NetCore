using Refit;

namespace TiendaApi.Api.Services.Identity;

/// <summary>
/// Contrato Refit del microservicio <b>identity</b> (rutas internas, R7).
///
/// 🎓 Orders NO consulta <c>identity_db</c> (R5): la existencia del usuario se
/// comprueba contra el dueño, equivalente al <c>FindByIdAsync</c> del monolito
/// (que devolvía <c>null</c> o un usuario borrado → 404).
/// </summary>
public interface IUsersApi {
    /// <summary>
    /// Usuario por ID (<c>GET /internal/users/{id}</c>).
    /// </summary>
    /// <param name="id">ID del usuario.</param>
    /// <remarks>200 con el usuario · 404 si no existe o está borrado.</remarks>
    [Get("/internal/users/{id}")]
    Task<UsuarioInternoDto> GetUsuarioAsync(long id);
}

/// <summary>Datos mínimos del usuario que orders necesita (equivalente al FindByIdAsync).</summary>
public sealed record UsuarioInternoDto(long Id, string Username, string Email, string? Avatar, string Role, DateTime CreatedAt);
