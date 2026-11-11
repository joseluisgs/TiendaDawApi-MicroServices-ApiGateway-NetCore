using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Configuración de CORS en el gateway (R11: CORS, rate limit y security
/// headers se resuelven en el borde).
///
/// 🎓 Sin CORS, el navegador del cliente bloquea las respuestas: la API
/// responde 200 pero sin la cabecera Access-Control-Allow-Origin el cliente
/// (por ejemplo el Blazor en http://localhost:5234) nunca la ve. Las suites
/// API no lo notan porque no son navegador — lo detecta un cliente real.
/// </summary>
public static class CorsConfig {
    /// <summary>
    /// Configura la política CORS según el entorno.
    /// Desarrollo: AllowAll (permite todo).
    /// Producción: solo los orígenes de Cors:AllowedOrigins (falla si no están).
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración (sección Cors:AllowedOrigins).</param>
    /// <param name="isDevelopment">true si el entorno es Development.</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration, bool isDevelopment) {
        Log.Information("Configurando CORS para {Environment}...", isDevelopment ? "DESARROLLO" : "PRODUCCION");

        return services.AddCors(options => {
            if (isDevelopment) {
                options.AddPolicy("AllowAll", policy => {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
                Log.Information("CORS: AllowAll (desarrollo)");
            } else {
                var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? throw new InvalidOperationException("Cors:AllowedOrigins no configurado");

                options.AddPolicy("ProductionPolicy", policy => {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                });
                Log.Information("CORS: ProductionPolicy con {Count} origenes", allowedOrigins.Length);
            }
        });
    }
}
