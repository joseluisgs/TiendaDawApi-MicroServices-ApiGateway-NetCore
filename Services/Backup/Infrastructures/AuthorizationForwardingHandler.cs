namespace TiendaApi.Api.Infrastructures;

/// <summary>
///     Reenvía el JWT de la petición entrante a los servicios llamados por Refit.
///     🎓 En el monolito, statistics corría en el MISMO proceso que los datos: el
///     <c>[Authorize]</c> del controlador garantizaba el contexto de usuario y las
///     consultas a usuarios/pedidos (ADMIN) veían al llamante. Al separarlo en
///     servicios, el token no viaja solo — hay que propagarlo.
///     Solo se copia si ya venía: si la llamada es interna (sin cabecera), sale sin
///     token y el destino responde 401, que es lo correcto.
/// </summary>
public class AuthorizationForwardingHandler(IHttpContextAccessor accessor) : DelegatingHandler {
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) {
        string? authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization) &&
            !request.Headers.Contains("Authorization")) {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
