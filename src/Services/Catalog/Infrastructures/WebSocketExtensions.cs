using Microsoft.AspNetCore.Builder;
using Serilog;
using TiendaApi.Api.Realtime.Productos;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Extensiones de WebSockets de catalog.
///
/// ⚠️ <b>Transicional (se retira en T12)</b>: solo <c>/ws/productos</c>. El
/// endpoint <c>/ws/pedidos</c> es de <b>orders</b> (T8) y los WebSockets puros
/// se eliminan con el monolito (excepción documentada de R1/R2).
/// </summary>
public static class WebSocketExtensions
{
    /// <summary>
    /// Mapea el endpoint WebSocket de productos (/ws/productos).
    /// </summary>
    /// <param name="app">Application builder.</param>
    /// <returns>Application builder.</returns>
    public static IApplicationBuilder MapWebSocketEndpoints(this IApplicationBuilder app)
    {
        Log.Information("Configurando endpoints WebSocket (transicional, T12)...");
        var webApp = (WebApplication)app;

        webApp.Map("/ws/productos", async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                // using: si el handler lanza (cliente caído, error de red), el
                // WebSocket se libera igualmente - el catch del handler solo loguea.
                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                var handler = context.RequestServices.GetRequiredService<ProductosWebSocketHandler>();
                await handler.HandleConnectionAsync(context, ws);
            }
            else context.Response.StatusCode = 400;
        });

        return app;
    }
}
