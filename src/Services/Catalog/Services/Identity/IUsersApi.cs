using Refit;

namespace TiendaApi.Api.Services.Identity;

/// <summary>
/// Contrato Refit del microservicio <b>identity</b> (rutas internas, R7).
///
/// 🎓 Catalog NO consulta <c>identity_db</c> (R5): los datos de usuarios se
/// piden al dueño. Se usa para el reporte semanal de productos (equivale al
/// <c>GetActiveUsersAsync</c> de <c>ProductoReportTask</c> en el monolito).
/// </summary>
public interface IUsersApi
{
    /// <summary>Usuarios activos (<c>!IsDeleted</c>, ordenados por email).</summary>
    [Get("/internal/users/activos")]
    Task<List<UsuarioActivoDto>> GetActivosAsync();
}

/// <summary>Datos mínimos de un usuario activo para el reporte (email y nombre).</summary>
public sealed record UsuarioActivoDto(long Id, string Username, string Email, string? Avatar, string Role, DateTime CreatedAt);
