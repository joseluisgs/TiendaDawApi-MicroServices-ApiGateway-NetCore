using TiendaApi.Api.Realtime.Productos;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
///     Configuración de SignalR de catalog.
///     🎓 R4 · SignalR solo hacia el navegador: el hub de productos vive en el
///     servicio DUEÑO del dato (aquí) y el gateway hace de proxy en
///     <c>/hubs/productos</c>. El hub de pedidos es de <b>orders</b>.
/// </summary>
public static class SignalRExtensions {
    /// <summary>
    ///     Configura el hub de SignalR de productos.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddRealtimeSignalR(this IServiceCollection services) {
        services.AddSignalR()
            .AddHubOptions<ProductosHub>(options => {
                options.EnableDetailedErrors = true;
                options.MaximumReceiveMessageSize = 1024 * 4;
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            });

        return services;
    }

    /// <summary>
    ///     Mapea el endpoint de SignalR de productos (/hubs/productos).
    /// </summary>
    /// <param name="app">Application builder.</param>
    /// <returns>Application builder.</returns>
    public static IApplicationBuilder MapSignalRHubs(this IApplicationBuilder app) {
        var webApp = (WebApplication)app;

        webApp.MapHub<ProductosHub>("/hubs/productos");

        return app;
    }
}
