using BuildingBlocks.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using Serilog;
using TiendaApi.Api.Services.Email;
using TiendaApi.Api.Services.Historial;
using TiendaApi.Api.Services.Identity;
using TiendaApi.Api.Services.Productos;
using TiendaApi.Api.Services.Proveedores;
using TiendaApi.Api.Services.Storage;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Registra los servicios de catalog y sus conexiones con otros microservicios.
/// </summary>
public static class ServicesConfig
{
    /// <summary>
    /// Registra servicios internos y clients Refit (R4 · REST con Refit).
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración (direcciones en Services:*).</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        Log.Information("Registrando servicios de catalog...");

        // Fachadas internas (dominio de este servicio).
        services.AddScoped<IProductoService, ProductoService>();
        services.AddScoped<IProveedorService, ProveedorService>();

        // 🎓 Conexiones con otros servicios (REST con Refit): direcciones por
        // configuración (Services:*) — en Docker, el nombre del contenedor; en
        // desarrollo, localhost.
        services.AddRefitClient<IEmailApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Email"] ?? "http://localhost:5104"))
            .AddTiendaResilience();

        services.AddRefitClient<IHistorialApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Historical"] ?? "http://localhost:5103"))
            .AddTiendaResilience();

        services.AddRefitClient<IUsersApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Identity"] ?? "http://localhost:5101"))
            .AddTiendaResilience();

        services.AddRefitClient<IStorageApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Storage"] ?? "http://localhost:5102"))
            .AddTiendaResilience();

        // Fachadas que envuelven a los clients con la semántica del monolito
        // (mejor esfuerzo: si el servicio remoto no responde, no se rompe la
        // operación del usuario donde el monolito era tolerante).
        services.AddScoped<IHistorialService, HistorialRefitService>();

        return services;
    }
}
