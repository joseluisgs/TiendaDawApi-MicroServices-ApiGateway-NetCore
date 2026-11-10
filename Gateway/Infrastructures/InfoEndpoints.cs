using System.Diagnostics;
using System.Reflection;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Endpoints de información del propio gateway: <c>/version</c> y <c>/</c>.
///
/// El gateway sirve LO SUYO (info, version, health) con el MISMO shape que
/// daba el monolito, para que el cliente y las suites no noten cambio.
/// </summary>
public static class InfoEndpoints
{
    /// <summary>
    /// Mapea <c>/version</c> (información del assembly) y <c>/</c> (índice).
    /// </summary>
    /// <param name="app">Aplicación en marcha.</param>
    /// <returns>Application builder.</returns>
    public static WebApplication MapInfoEndpoints(this WebApplication app)
    {
        // ── /version ──
        // Mismo shape que el monolito: version (con el hash de SourceLink), fileVersion,
        // runtime y framework. El E2E solo exige `version` no vacío, pero se
        // conserva el resto por si hay consumidores.
        app.MapGet("/version", () =>
        {
            var ensamblado = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = ensamblado.GetCustomInformationalVersion()
                ?? ensamblado.GetName().Version?.ToString()
                ?? "0.0.0";

            return Results.Ok(new
            {
                version,
                fileVersion = ensamblado.GetCustomFileVersion() ?? "0.0.0.0",
                runtime = Environment.Version.ToString(),
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            });
        });

        // ── / (root) ──
        // El monolito servía aquí su Swagger UI. El gateway no tiene API propia
        // que documentar, así que sirve un índice enlazando a la documentación
        // agregada y a los servicios.
        app.MapGet("/", () => Results.Content(Indice, "text/html; charset=utf-8"));

        return app;
    }

    /// <summary>Índice mínimo en HTML (sin dependencias externas, sin CSP conflictiva).</summary>
    private const string Indice = """
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Tienda DAW · API Gateway</title>
            <style>
                body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; padding: 40px;
                       background: #f0f2f5; color: #1f2933; }
                main { max-width: 760px; margin: 0 auto; background: #fff; border-radius: 12px;
                       padding: 32px; box-shadow: 0 4px 6px rgba(0,0,0,.08); }
                h1 { margin-top: 0; font-size: 24px; }
                table { width: 100%; border-collapse: collapse; margin-top: 16px; }
                th, td { text-align: left; padding: 8px 12px; border-bottom: 1px solid #e4e7eb; }
                th { background: #f8fafc; font-size: 13px; text-transform: uppercase; color: #616e7c; }
                code { background: #f1f3f5; padding: 2px 6px; border-radius: 4px; font-size: 13px; }
                a { color: #3b5bdb; text-decoration: none; }
                a:hover { text-decoration: underline; }
                .nota { margin-top: 20px; font-size: 14px; color: #616e7c; }
            </style>
        </head>
        <body>
            <main>
                <h1>Tienda DAW · API Gateway</h1>
                <p>Única puerta pública de la API. Todo lo demás vive detrás de este
                   proceso y <strong>no debe exponerse</strong>.</p>
                <table>
                    <tr><th>Recurso</th><th>Valor</th></tr>
                    <tr><td>Documentación</td><td><a href="/swagger">/swagger</a> (solo en desarrollo)</td></tr>
                    <tr><td>Health</td><td><code>/health</code> · <code>/health/live</code> · <code>/health/ready</code></td></tr>
                    <tr><td>Versión</td><td><code>/version</code></td></tr>
                    <tr><td>GraphQL</td><td><code>POST /graphql</code></td></tr>
                    <tr><td>API versionada</td><td><code>/api/v1/{recurso}</code></td></tr>
                </table>
                <p class="nota">Autenticación: <code>POST /api/v1/auth/signin</code> → token JWT
                   (<code>Bearer</code>). Las rutas internas de servicio a servicio
                   (<code>/internal/*</code>) <strong>no</strong> pasan por aquí.</p>
            </main>
        </body>
        </html>
        """;
}

/// <summary>Lectores de atributos de assembly ausentes en el SDK minimal.</summary>
file static class AssemblyExtensions
{
    /// <summary>InformationalVersion (incluye el hash de SourceLink).</summary>
    public static string? GetCustomInformationalVersion(this Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>FileVersion del assembly.</summary>
    public static string? GetCustomFileVersion(this Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
}
