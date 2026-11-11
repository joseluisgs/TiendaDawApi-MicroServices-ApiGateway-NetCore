using BuildingBlocks.Http;
using Refit;
using Serilog;
using TiendaApi.Api.Services.Backup;
using TiendaApi.Api.Services.Catalog;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
///     Registra los servicios de <b>backup</b> y su conexión con catalog.
///     🎓 Backup no tiene BD (R5): lee por Refit en las rutas públicas de catalog y
///     escribe en sus endpoints internos. El upsert y la validación los hace el dueño.
/// </summary>
public static class ServicesConfig {
    /// <summary>
    ///     Registra el client Refit (R4 — REST con Refit): dirección por
    ///     configuración (<c>Services:Catalog</c>) — en Docker, el nombre del
    ///     contenedor; en desarrollo, localhost.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración (dirección en Services:*).</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration) {
        Log.Information("Registrando servicios de backup...");

        // Contexto HTTP para propagar el JWT del llamante (los listados de
        // proveedores y productos exigen ADMIN: sin el token, 401).
        services.AddHttpContextAccessor();
        services.AddTransient<AuthorizationForwardingHandler>();

        services.AddRefitClient<ICatalogApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Catalog"] ?? "http://localhost:5105"))
            .AddHttpMessageHandler<AuthorizationForwardingHandler>()
            .AddTiendaResilience();

        // Fachada interna (dominio de este servicio).
        services.AddScoped<IBackupService, BackupService>();

        return services;
    }
}
