using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;

namespace TiendaApi.Api.Services.Auth;

/// <summary>
/// Validación de JWT para orders: MISMA configuración que la API HTTP
/// (sección <c>Jwt</c> idéntica a la de identity y a <c>BuildingBlocks/Jwt</c>),
/// así un token aceptado por el gateway también lo es por el WebSocket.
///
/// 🎓 Sin <c>GenerateToken</c>: la emisión vive en identity (R6 — firma única).
/// </summary>
public class JwtService(IConfiguration configuration, ILogger<JwtService> logger) : IJwtService
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<JwtService> _logger = logger;

    /// <inheritdoc />
    public string? ValidateToken(string token)
    {
        try
        {
            var key = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT Key no configurada");
            var issuer = _configuration["Jwt:Issuer"] ?? "TiendaApi";
            var audience = _configuration["Jwt:Audience"] ?? "TiendaApi";

            var tokenHandler = new JwtSecurityTokenHandler();
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = securityKey,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var username = jwtToken.Claims.First(x => x.Type == "username").Value;

            return username;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Validación de token JWT fallida");
            return null;
        }
    }
}
