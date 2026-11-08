using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TiendaApi.Api.Realtime.Pedidos;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Configuración de WebSockets de <b>orders</b>.
///
/// ⚠️ <b>Transicional (se retira en T12)</b>: R1 documenta que los WebSockets
/// puros <c>/ws/*</c> NO se replican (solo SignalR), pero el E2E espera el
/// broadcast de <c>/ws/pedidos</c> al cambiar el estado de un pedido. El
/// endpoint exige token en el handshake (cierre 1008 si falta), como en el monolito.
/// </summary>
public static class WebSocketsConfig
{
    /// <summary>
    /// Registra el handler de WebSocket de pedidos.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddWebSockets(this IServiceCollection services)
    {
        Log.Information("Registrando handlers de WebSocket (transicional, T12)...");
        return services.AddSingleton<PedidosWebSocketHandler>();
    }
}
