using BuildingBlocks.Http;
using Refit;
using Serilog;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Email;
using TiendaApi.Api.Services.Identity;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
///     Registra los servicios de <b>orders</b> y sus conexiones con otros microservicios.
/// </summary>
public static class ServicesConfig {
    /// <summary>
    ///     Registra los clients Refit (R4 — REST con Refit): direcciones por
    ///     configuración (<c>Services:*</c>) — en Docker, el nombre del contenedor;
    ///     en desarrollo, localhost.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración (direcciones en Services:*).</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration) {
        Log.Information("Registrando servicios de orders...");

        // ?? Conexiones con otros servicios (REST con Refit).
        services.AddRefitClient<ICatalogApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Catalog"] ?? "http://localhost:5105"))
            .AddTiendaResilience();

        services.AddRefitClient<IUsersApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Identity"] ?? "http://localhost:5101"))
            .AddTiendaResilience();

        services.AddRefitClient<IEmailApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Email"] ?? "http://localhost:5104"))
            .AddTiendaResilience();

        return services;
    }
}
