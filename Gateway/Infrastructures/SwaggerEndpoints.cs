using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Swagger <b>agregado</b> del gateway: fusiona los <c>swagger.json</c> de todos
/// los servicios en UN solo documento, para conservar la experiencia del monolito
/// (una única UI con toda la API).
///
/// La API está repartida: cada servicio expone SU spec (privada, solo
/// detrás del gateway) y aquí se fusionan todas en un único documento,
/// conservando la experiencia de una sola API.
///
/// 🎓 Por qué no colisiona: cada servicio posee su prefijo de rutas
/// (<c>/api/v1/auth</c>, <c>/api/v1/productos</c>, <c>/graphql</c>…), así que los
/// <c>paths</c> son disjuntos. Los <c>schemas</c> sí pueden repetir nombre con
/// distinta forma (p. ej. <c>PagedResult…</c>): ante un conflicto se renombra con
/// el prefijo del servicio para no pisar ninguno.
///
/// Solo en desarrollo (igual que el monolito): en producción la introspección
/// completa del API es información para un atacante.
/// </summary>
public static class SwaggerEndpoints {
    /// <summary>Servicios cuya spec se fusiona (nombre lógico y puerto por defecto en local).</summary>
    private static readonly (string Nombre, int Puerto)[] Servicios =
    [
        ("identity", 5101),
        ("storage", 5102),
        ("historical", 5103),
        ("email", 5104),
        ("catalog", 5105),
        ("orders", 5106),
        ("statistics", 5107),
        ("backup", 5108),
        ("graphql", 5109),
    ];

    /// <summary>
    /// ?? URL base de un servicio. En local, <c>http://localhost:{puerto}</c>;
    /// en Docker se sobreescribe con <c>Backends__{nombre}</c> (misma clave que
    /// usa el health: la red interna del compose es la única fuente de verdad).
    /// </summary>
    private static string BaseUrl(IConfiguration configuracion, string nombre, int puerto)
        => configuracion[$"Backends:{nombre}"] ?? $"http://localhost:{puerto}";

    /// <summary>Caché del documento agregado: se reconstruye si tiene más de 5 minutos.</summary>
    private static readonly object Candado = new();
    private static string? _documento;
    private static DateTime _generadoEn = DateTime.MinValue;

    /// <summary>
    /// Mapea <c>/swagger/v1/swagger.json</c> (spec agregado) y <c>/swagger</c> (UI).
    /// </summary>
    /// <param name="app">Aplicación en marcha.</param>
    /// <returns>Application builder.</returns>
    public static WebApplication MapSwaggerEndpoints(this WebApplication app) {
        app.MapGet("/swagger/v1/swagger.json", async () => {
            var json = await ObtenerSpecAsync(app.Configuration);
            return Results.Text(json, "application/json; charset=utf-8");
        });

        // UI apuntando al spec agregado que servimos arriba. Una sola ruta:
        // "/swagger" y "/swagger/" a la vez producen AmbiguousMatchException.
        app.MapGet("/swagger", () => Results.Content(UiHtml, "text/html; charset=utf-8"));

        return app;
    }

    /// <summary>Spec agregado (con caché corta para no re-fetchear en cada carga).</summary>
    private static async Task<string> ObtenerSpecAsync(IConfiguration configuracion) {
        lock (Candado) {
            if (_documento is not null && DateTime.UtcNow - _generadoEn < TimeSpan.FromMinutes(5))
                return _documento;
        }

        var documento = await ConstruirAsync(configuracion);

        lock (Candado) {
            _documento = documento;
            _generadoEn = DateTime.UtcNow;
        }

        return documento;
    }

    /// <summary>Recorre los servicios y fusiona sus specs.</summary>
    private static async Task<string> ConstruirAsync(IConfiguration configuracion) {
        using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var raiz = new JsonObject {
            ["openapi"] = "3.0.1",
            ["info"] = new JsonObject {
                ["title"] = "Tienda DAW · API (agregada por el gateway)",
                ["description"] =
                    "Fusión de las especificaciones de los microservicios. " +
                    "Cada servicio expone la suya propia detrás del gateway; " +
                    "aquí se sirven juntas para conservar un único punto de documentación.",
                ["version"] = "1.0.0",
            },
            ["paths"] = new JsonObject(),
            ["components"] = new JsonObject { ["schemas"] = new JsonObject() },
        };

        var paths = (JsonObject)raiz["paths"]!;
        var schemas = (JsonObject)raiz["components"]!["schemas"]!;
        var serviciosOk = new List<string>();

        foreach (var (nombre, puerto) in Servicios) {
            JsonNode? spec;
            try {
                var texto = await cliente.GetStringAsync($"{BaseUrl(configuracion, nombre, puerto)}/swagger/v1/swagger.json");
                spec = JsonNode.Parse(texto);
            } catch {
                // Servicio caído o sin Swagger (producción): se omite, no tumble.
                continue;
            }

            serviciosOk.Add(nombre);

            if (spec?["paths"] is JsonObject servicioPaths) {
                foreach (var (clave, valor) in servicioPaths) {
                    // Sin colisión esperada (cada servicio posee su prefijo);
                    // si la hubiera, gana la primera y se registra en x-gateway.
                    if (!paths.ContainsKey(clave))
                        paths[clave] = valor?.DeepClone();
                }
            }

            if (spec?["components"]?["schemas"] is JsonObject servicioSchemas) {
                foreach (var (clave, valor) in servicioSchemas) {
                    if (!schemas.ContainsKey(clave)) {
                        schemas[clave] = valor?.DeepClone();
                        continue;
                    }

                    // Mismo nombre, distinta forma: se renombra con el servicio.
                    var renombrado = $"{nombre}_{clave}";
                    if (!schemas.ContainsKey(renombrado))
                        schemas[renombrado] = valor?.DeepClone();
                }
            }
        }

        raiz["x-gateway-servicios"] = new JsonArray(serviciosOk.Select(s => (JsonNode)s).ToArray());
        raiz["servers"] = new JsonArray(new JsonObject { ["url"] = "/" });

        return raiz.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// UI de Swagger servida desde el propio gateway (sin dependencias CDN: la
    /// página debe cargar también sin red externa y sin chocar con la CSP).
    /// </summary>
    private const string UiHtml = """
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Tienda DAW · API</title>
            <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css">
            <style>
                body { margin: 0; background: #fff; }
                #banner { background: #1f2933; color: #fff; padding: 14px 24px;
                          font-family: 'Segoe UI', Arial, sans-serif; }
                #banner a { color: #91f5cd; text-decoration: none; }
            </style>
        </head>
        <body>
            <div id="banner">
                <strong>Tienda DAW · API</strong> — especificación agregada por el gateway
                · <a href="/">volver</a>
            </div>
            <div id="swagger-ui"></div>
            <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
            <script>
                window.onload = () => {
                    SwaggerUIBundle({
                        url: '/swagger/v1/swagger.json',
                        dom_id: '#swagger-ui',
                        deepLinking: true,
                        presets: [SwaggerUIBundle.presets.apis],
                        layout: 'BaseLayout'
                    });
                };
            </script>
        </body>
        </html>
        """;
}
