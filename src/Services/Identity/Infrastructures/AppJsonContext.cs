using System.Text.Json.Serialization;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Usuarios;

namespace TiendaApi.Api;

/// <summary>
/// Contexto source-gen de JSON del microservicio identity.
///
/// 🎓 Es el equivalente reducido al del monolito: solo los tipos que este
/// servicio serializa (cachea o devuelve). El RedisCacheService combina este
/// contexto con reflexión, así que lo que no aparezca aquí igual se serializa.
/// </summary>
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(PagedResult<UserDto>))]
public partial class AppJsonContext : JsonSerializerContext;
