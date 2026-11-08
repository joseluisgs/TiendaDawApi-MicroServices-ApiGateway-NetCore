using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using Serilog;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Orders;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Registra los services Refit del BFF GraphQL y su propagación de autenticación.
///
/// 🎓 R4 — GraphQL no tiene BD: todo lo consulta por REST a los dueños. Los
/// endpoints de usuarios y pedidos exigen ADMIN, así que el JWT del llamante
/// viaja en la cabecera (mismo criterio que statistics y backup).
/// </summary>
public static class ServicesConfig
{
    /// <summary>
    /// Registra los clients Refit (R4 — REST con Refit): direcciones por
    /// configuración (<c>Services:*</c>) — en Docker, el nombre del contenedor;
    /// en desarrollo, localhost.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración (direcciones en Services:*).</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        Log.Information("Registrando servicios de GraphQL (BFF)...");

        // Contexto HTTP para propagar el JWT del llamante (pedidos exige ADMIN).
        services.AddHttpContextAccessor();
        services.AddTransient<AuthorizationForwardingHandler>();

        services.AddRefitClient<ICatalogApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Catalog"] ?? "http://localhost:5105"))
            .AddHttpMessageHandler<AuthorizationForwardingHandler>();

        services.AddRefitClient<IOrdersApi>()
            .ConfigureHttpClient(c => c.BaseAddress =
                new Uri(configuration["Services:Orders"] ?? "http://localhost:5106"))
            .AddHttpMessageHandler<AuthorizationForwardingHandler>();

        return services;
    }
}
