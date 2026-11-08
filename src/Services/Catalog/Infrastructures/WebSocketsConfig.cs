using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TiendaApi.Api.Realtime.Productos;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Configuración de WebSockets de catalog.
///
/// ⚠️ <b>Transicional (se retira en T12)</b>: R1 documenta que los WebSockets
/// puros <c>/ws/*</c> NO se replican (solo SignalR), pero el E2E espera el
/// broadcast de <c>/ws/productos</c> al crear un producto. Como en T7 el
/// <c>POST /api/v1/productos</c> pasa a catalog, el monolito deja de ver la
/// creación — así que este handler se copia AQUÍ temporalmente para mantener
/// los 147 E2E en verde. En T12 se borra junto con el monolito y sus 3 tests.
/// </summary>
public static class WebSocketsConfig
{
    /// <summary>
    /// Registra el handler de WebSocket de productos.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddWebSockets(this IServiceCollection services)
    {
        Log.Information("Registrando handlers de WebSocket (transicional, T12)...");
        return services.AddSingleton<ProductosWebSocketHandler>();
    }
}
