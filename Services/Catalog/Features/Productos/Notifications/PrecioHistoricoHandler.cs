using MediatR;
using Serilog;
using TiendaApi.Api.Services.Historial;

namespace TiendaApi.Api.Features.Productos.Notifications;

/// <summary>
/// Handler que escribe los puntos de precio en las series (histórico de precios
/// Q-A + informe del día Q-C). Escucha DOS eventos:
///
/// 📌 ProductoCreadoNotification → punto de NACIMIENTO: el precio inicial
///    también es un punto de la serie (sin él, un producto que nunca se
///    actualiza tendría el historial vacío para siempre).
/// 📌 ProductoActualizadoNotification → solo si el precio DE VERDAD cambió
///    (compara con PrecioAnterior): el historial registra cambios, no
///    eventos de edición - un update de solo stock o de imagen no genera
///    punto, y el informe Q-C solo cuenta cambios reales.
///
/// ✅ Se AWAIT la escritura (no fire-and-forget: quien acaba de hacer el PATCH
/// puede leer el punto recién escrito, y el E2E no depende de carreras).
///
/// 🎓 En el monolito esto hablaba directo con Cassandra (<c>IHistorialRepository</c>);
/// aquí la fachada <c>IHistorialService</c> se lo pide por Refit al microservicio
/// <b>historical</b> (dueño de las series), con la MISMA semántica degradada.
/// </summary>
public class PrecioHistoricoHandler(IHistorialService historialService)
    : INotificationHandler<ProductoCreadoNotification>
    , INotificationHandler<ProductoActualizadoNotification>
{
    /// <summary>
    /// Registra el precio de nacimiento del producto en las tablas de serie.
    /// </summary>
    public async Task Handle(ProductoCreadoNotification notification, CancellationToken cancellationToken)
    {
        await RegistrarPrecioAsync(notification.Producto.Id, (double)notification.Producto.Precio);
    }

    /// <summary>
    /// Registra el precio resultante de la actualización, SOLO si cambió.
    /// </summary>
    public async Task Handle(ProductoActualizadoNotification notification, CancellationToken cancellationToken)
    {
        if (notification.PrecioAnterior == notification.Producto.Precio)
            return;   // no es un cambio de precio: nada que registrar

        await RegistrarPrecioAsync(notification.Producto.Id, (double)notification.Producto.Precio);
    }

    /// <summary>
    /// Escritura compartida por ambos eventos: crea el punto de precio en las
    /// dos tablas de serie (Q-A histórico + Q-C informe del día).
    /// </summary>
    private async Task RegistrarPrecioAsync(long productoId, double precio)
    {
        try
        {
            await historialService.RegistrarPrecioAsync(productoId, precio);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "historical: no se pudo registrar el precio del producto {ProductoId} (modo degradado)",
                productoId);
        }
    }
}
