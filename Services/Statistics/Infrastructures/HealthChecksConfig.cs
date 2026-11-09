using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Configuración de Health Checks de <b>statistics</b>.
///
/// 🎓 Statistics no tiene base de datos: agrega al vuelo lo que pide por Refit.
/// Solo se expone el health del propio proceso (los servicios de los que depende
/// se sondean en SU /health, no aquí — sondearlos desde aquí acoplaría el
/// despliegue de statistics al de todos los demás).
/// </summary>
public static class HealthChecksConfig
{
    /// <summary>
    /// Registra los health checks (solo el del proceso).
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="environment">Entorno de la app.</param>
    /// <returns>IServiceCollection para encadenar.</returns>
    public static IServiceCollection AddHealthChecks(this IServiceCollection services, IWebHostEnvironment environment)
    {
        Log.Information("Configurando Health Checks (/health)...");

        services.AddHealthChecks()
            .AddCheck<ProcesoCheck>("proceso", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Expone endpoints de salud: /health, /health/ready, /health/live.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthReportAsync
        });

        // /health/live - liveness: solo responde OK si el proceso está vivo
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthReportAsync
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthReportAsync
        });

        return endpoints;
    }

    /// <summary>
    /// Escribe el informe de salud como JSON: status, totalDuration y checks[].
    /// </summary>
    private static Task WriteHealthReportAsync(HttpContext context, HealthReport report)
    {
        var payload = new
        {
            status = MapStatus(report.Status),
            totalDuration = report.TotalDuration,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = MapStatus(entry.Value.Status),
                duration = entry.Value.Duration,
                description = entry.Value.Description,
                error = entry.Value.Exception?.Message
            })
        };

        return context.Response.WriteAsJsonAsync(payload);
    }

    /// <summary>
    /// Traduce el estado interno al semáforo de la API: OK / DEGRADED / ERROR.
    /// </summary>
    private static string MapStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "OK",
        HealthStatus.Degraded => "DEGRADED",
        _ => "ERROR"
    };

    /// <summary>Comprobación trivial: el proceso responde.</summary>
    public class ProcesoCheck : IHealthCheck
    {
        /// <inheritdoc />
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy("statistics respondiendo"));
    }
}
