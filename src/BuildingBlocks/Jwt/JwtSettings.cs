namespace BuildingBlocks.Jwt;

/// <summary>
/// 🎓 Configuración JWT compartida por TODOS los servicios.
///
/// Los valores viven en la sección <c>Jwt</c> de la configuración y deben ser
/// <b>idénticos en el gateway y en cada microservicio</b> (en Docker, la misma
/// variable de entorno inyectada a todos; en desarrollo, la misma sección en
/// cada appsettings). Quien firma con ellos es únicamente <c>identity</c>.
/// </summary>
public sealed class JwtSettings
{
    /// <summary>Sección de configuración de la que se leen los valores.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Clave simétrica (HS256) con la que identity firma y los demás validan.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Emisor válido: el mismo nombre que usa identity al firmar.</summary>
    public string Issuer { get; init; } = "TiendaApi";

    /// <summary>Audience válida: la misma que identity al firmar.</summary>
    public string Audience { get; init; } = "TiendaApi";

    /// <summary>Caducidad de los access tokens en minutos (la usa identity al firmar).</summary>
    public int ExpireMinutes { get; init; } = 60;
}
