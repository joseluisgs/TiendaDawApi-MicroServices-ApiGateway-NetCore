namespace TiendaApi.Api.Middleware;

/// <summary>
/// Middleware de headers de seguridad HTTP en el <b>borde</b>.
///
/// 🎓 R11 (ENDPOINTS §11.1): "CORS, rate limit y security headers se resuelven en
/// el gateway". Antes los servía el monolito; con T12 pasan aquí, que es la
/// única puerta pública.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    private static readonly Dictionary<string, string> SecurityHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["X-Content-Type-Options"] = "nosniff",
        ["X-Frame-Options"] = "DENY",
        ["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
        ["Referrer-Policy"] = "strict-origin-when-cross-origin",
        ["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()"
    };

    /// <summary>
    /// Política para las páginas HTML que sirve esta API: la UI de Swagger
    /// (<c>/swagger</c>), la de GraphQL (<c>/graphql</c>, Banana Cake Pop) y el
    /// índice del gateway (<c>/</c>). Son SPAs que cargan sus propios scripts y
    /// estilos; el resto de respuestas (la API JSON) mantiene el
    /// <c>default-src 'none'</c> de arriba.
    ///
    /// ⚠️ Diferencia con el monolito: aquí Swagger UI se sirve con los assets
    /// desde un CDN (<c>unpkg.com</c>), así que esa origen se autoriza
    /// explícitamente para script/style/font/img en las páginas UI. Es la única
    /// relajación y solo afecta a esas rutas.
    /// </summary>
    private const string ContentSecurityPolicyUi =
        "default-src 'self' 'unsafe-inline' https://unpkg.com; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com; " +
        "style-src 'self' 'unsafe-inline' https://unpkg.com; " +
        "img-src 'self' data: blob: https://unpkg.com; " +
        "font-src 'self' data: https://unpkg.com; " +
        "connect-src 'self' ws: wss: https://unpkg.com; " +
        "worker-src 'self' blob:; manifest-src 'self'; base-uri 'self'; frame-ancestors 'none'";

    /// <summary>
    /// Añade los headers de seguridad a la respuesta y continúa con la petición.
    /// X-XSS-Protection no se envía: es obsoleta (OWASP recomienda no enviarla).
    /// </summary>
    /// <param name="context">Contexto HTTP de la petición en curso.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Páginas UI: Swagger, GraphQL (Banana Cake Pop) y el índice del gateway.
        var esPaginaUi = context.Request.Path.StartsWithSegments("/swagger")
                      || context.Request.Path.StartsWithSegments("/graphql")
                      || context.Request.Path == "/";

        foreach (var header in SecurityHeaders)
        {
            if (esPaginaUi && header.Key == "Content-Security-Policy")
                context.Response.Headers[header.Key] = ContentSecurityPolicyUi;
            else
                context.Response.Headers.TryAdd(header.Key, header.Value);
        }

        await _next(context);
    }
}

/// <summary>Extensiones para registro del middleware.</summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>Registra el middleware de headers de seguridad.</summary>
    /// <param name="app">Constructor de la aplicación.</param>
    /// <returns>IApplicationBuilder.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
