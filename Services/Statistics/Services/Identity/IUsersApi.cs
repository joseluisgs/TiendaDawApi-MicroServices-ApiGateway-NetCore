using Refit;
using TiendaApi.Api.Dtos.Common;

namespace TiendaApi.Api.Services.Identity;

/// <summary>
/// Contrato Refit del microservicio <b>identity</b> (rutas públicas, R7).
///
/// 🎓 Statistics no consulta <c>identity_db</c> (R5): pide el listado de
/// usuarios al dueño para cruzarlo con los pedidos (ranking de clientes y
/// cifras de "usuarios nuevos en el periodo").
/// </summary>
public interface IUsersApi {
    /// <summary>Página de usuarios (solo ADMIN, igual que en el monolito).</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    [Get("/api/v1/users")]
    Task<PagedResult<UsuarioSnapshot>> GetUsuariosAsync([Query] int page, [Query] int size);
}

/// <summary>Campos de usuario que usa el cálculo de estadísticas.</summary>
public sealed record UsuarioSnapshot(long Id, string UserName, DateTime CreatedAt);
