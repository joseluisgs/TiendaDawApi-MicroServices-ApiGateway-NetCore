using Microsoft.AspNetCore.Builder;
using Serilog;
using TiendaApi.Api.Realtime.Pedidos;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Extensiones de WebSockets de <b>orders</b>.
///
/// ⚠️ <b>Transicional (se retira en T12 con el monolito)</b>: <c>/ws/pedidos</c>.
/// Los WebSockets puros <c>/ws/*</c> no se replican en R1/R2 (solo SignalR), pero
/// el E2E espera el broadcast al cambiar el estado de un pedido.
/// </summary>
public static class WebSocketExtensions
{
    /// <summary>
    /// Mapea el endpoint WebSocket de pedidos (/ws/pedidos).
    /// </summary>
    /// <param name="app">Application builder.</param>
    /// <returns>Application builder.</returns>
    public static IApplicationBuilder MapWebSocketEndpoints(this IApplicationBuilder app)
    {
        Log.Information("Configurando endpoints WebSocket (transicional, T12)...");
        var webApp = (WebApplication)app;

        webApp.Map("/ws/pedidos", async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                // using: si el handler lanza (cliente caído, error de red), el
                // WebSocket se libera igualmente - el catch del handler solo loguea.
                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                var handler = context.RequestServices.GetRequiredService<PedidosWebSocketHandler>();
                await handler.HandleConnectionAsync(context, ws);
            }
            else context.Response.StatusCode = 400;
        });

        return app;
    }
}
