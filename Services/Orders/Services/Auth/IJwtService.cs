using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace TiendaApi.Api.Services.Auth;

/// <summary>
/// Contrato mínimo de validación JWT que necesita orders.
///
/// 🎓 El monolito usaba <c>IJwtService</c> completo (generar + validar), pero
/// orders solo <b>valida</b>: la emisión la hace identity (R6 — firma única).
/// Se recorta la interfaz a lo que consume <c>PedidosWebSocketHandler</c>, para
/// no arrastrar el modelo <c>User</c> (dominio de identity) a este servicio.
/// </summary>
public interface IJwtService {
    /// <summary>
    /// Valida firma, issuer, audience y lifetime del token.
    /// </summary>
    /// <param name="token">Token a validar.</param>
    /// <returns>Username del token, o null si no es válido.</returns>
    string? ValidateToken(string token);
}
