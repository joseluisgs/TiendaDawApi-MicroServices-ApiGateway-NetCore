using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.Models;

namespace TiendaApi.Api.Mappers;

/// <summary>
/// Mapper para convertir entre User y sus DTOs.
///
/// 🎓 Sin BCrypt: en este servicio solo existe Identity, así que el hash de la
/// contraseña (PBKDF2) lo genera <c>UserManager</c> al crear o cambiar el
/// password — el mapper solo mueve datos.
/// </summary>
public static class UserMapper
{
    /// <summary>Convierte User a UserDto.</summary>
    public static UserDto ToDto(this User user) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, user.GetAvatarUrl(), user.Role, user.CreatedAt);

    /// <summary>Convierte lista de Users a lista de UserDto.</summary>
    public static IEnumerable<UserDto> ToDtoList(this IEnumerable<User> users) =>
        users.Select(u => u.ToDto());

    /// <summary>
    /// Convierte RegisterDto a User sin contraseña: <c>UserManager.CreateAsync</c>
    /// recibe la contraseña en claro y guarda su hash PBKDF2.
    /// </summary>
    public static User ToEntity(this RegisterDto dto) => new()
    {
        UserName = dto.Username,
        Email = dto.Email,
        Role = UserRoles.USER,
        IsDeleted = false,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Actualiza User desde UserUpdateDto. La contraseña NO se toca aquí:
    /// la gestiona el comando con <c>UserManager</c>.
    /// </summary>
    public static void UpdateEntity(this UserUpdateDto dto, User user)
    {
        if (!string.IsNullOrEmpty(dto.Email))
            user.Email = dto.Email;
    }

    /// <summary>
    /// Actualiza User desde UserPatchDto (actualización parcial). La contraseña
    /// NO se toca aquí: la gestiona el comando con <c>UserManager</c>.
    /// </summary>
    public static void UpdateEntity(this UserPatchDto dto, User user)
    {
        if (!string.IsNullOrEmpty(dto.Email))
            user.Email = dto.Email;
        if (dto.Avatar != null)
            user.Avatar = dto.Avatar;
    }
}
