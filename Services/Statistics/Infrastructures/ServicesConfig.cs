using BuildingBlocks.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using Serilog;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Historial;
using TiendaApi.Api.Services.Identity;
using TiendaApi.Api.Services.Orders;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Registra los servicios de <b>statistics</b> y sus conexiones con otros microservicios.
///
/// 🎓 Statistics no tiene BD (R5): TODOS los datos se piden por Refit. Las
/// lecturas de catálogo se hacen con <c>Task.WhenAll</c> porque son independientes
/// entre sí (mismo criterio que el monolito, que lanzaba varias consultas en paralelo).
/// </summary>
public static class ServicesConfig {
    /// <summary>
    /// Registra los clients Refit (R4 — REST con Refit): direcciones por
    /// configuración (<c>Services:*</c>) — en Docker, el nombre del contenedor;
    /// en desarrollo, localhost.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración (direcciones en Services:*).</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration) {
        Log.Information("Registrando servicios de statistics...");

        // Contexto HTTP para propagar el JWT del llamante (los endpoints de
        // usuarios y pedidos exigen ADMIN: sin el token, 401).
        services.AddHttpContextAccessor();
        services.AddTransient<AuthorizationForwardingHandler>();

        // ?? Conexiones con otros servicios (REST con Refit) + reenvío de auth.
        services.AddRefitClient<ICatalogApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Catalog"] ?? "http://localhost:5105"))
            .AddHttpMessageHandler<AuthorizationForwardingHandler>()
            .AddTiendaResilience();

        services.AddRefitClient<IUsersApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Identity"] ?? "http://localhost:5101"))
            .AddHttpMessageHandler<AuthorizationForwardingHandler>()
            .AddTiendaResilience();

        services.AddRefitClient<IOrdersApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Orders"] ?? "http://localhost:5106"))
            .AddHttpMessageHandler<AuthorizationForwardingHandler>()
            .AddTiendaResilience();

        services.AddRefitClient<IHistorialApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Historical"] ?? "http://localhost:5103"))
            .AddHttpMessageHandler<AuthorizationForwardingHandler>()
            .AddTiendaResilience();

        // Fachada interna (dominio de este servicio).
        services.AddScoped<TiendaApi.Api.Services.Estadisticas.IEstadisticasService,
            TiendaApi.Api.Services.Estadisticas.EstadisticasService>();

        return services;
    }
}
