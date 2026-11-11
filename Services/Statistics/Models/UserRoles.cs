namespace TiendaApi.Api.Models;

/// <summary>
///     Roles de usuario (constantes de cadena).
///     🎓 Copiado de <c>Models/User.cs</c> del monolito: catalog no tiene el modelo
///     de usuario (eso es de identity), pero SÍ necesita las constantes para
///     declarar la autorización de sus endpoints
///     (<c>[Authorize(Roles = UserRoles.ADMIN)]</c>). El rol real vive en el JWT
///     que emite identity.
/// </summary>
public static class UserRoles {
    /// <summary>Rol de administrador con acceso total al sistema.</summary>
    public const string ADMIN = "ADMIN";

    /// <summary>Rol de usuario estándar con permisos básicos.</summary>
    public const string USER = "USER";
}
