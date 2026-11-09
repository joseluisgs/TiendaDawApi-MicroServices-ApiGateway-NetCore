using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using TiendaApi.Api.Realtime.Pedidos;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Configuración de SignalR de <b>orders</b>.
///
/// 🎓 R4 · SignalR solo hacia el navegador: el hub de pedidos vive en el
/// servicio DUEÑO del dato (aquí) y el gateway hace de proxy en
/// <c>/hubs/pedidos</c>.
/// </summary>
public static class SignalRExtensions
{
    /// <summary>
    /// Configura el hub de SignalR de pedidos.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddRealtimeSignalR(this IServiceCollection services)
    {
        services.AddSignalR()
            .AddHubOptions<PedidosHub>(options =>
            {
                options.EnableDetailedErrors = true;
                options.MaximumReceiveMessageSize = 1024 * 4;
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            });

        return services;
    }

    /// <summary>
    /// Mapea el endpoint de SignalR de pedidos (/hubs/pedidos).
    /// </summary>
    /// <param name="app">Application builder.</param>
    /// <returns>Application builder.</returns>
    public static IApplicationBuilder MapSignalRHubs(this IApplicationBuilder app)
    {
        var webApp = (WebApplication)app;

        webApp.MapHub<PedidosHub>("/hubs/pedidos");

        return app;
    }
}
