using Refit;

namespace TiendaApi.Api.Services.Orders;

/// <summary>
/// Contrato Refit del microservicio <b>orders</b> (rutas públicas, R7).
///
/// 🎓 Statistics no tiene <c>orders_db</c>: pide TODOS los pedidos (el endpoint
/// admin no pagina) y acota la ventana en memoria, con el mismo resultado
/// numérico que <c>FindByRangoFechasAsync</c> del monolito.
/// </summary>
public interface IOrdersApi
{
    /// <summary>Todos los pedidos, ordenados por fecha descendente.</summary>
    [Get("/api/v1/pedidos")]
    Task<List<PedidoSnapshot>> GetPedidosAsync();
}

/// <summary>Campos de pedido que usa el cálculo de estadísticas.</summary>
public sealed record PedidoSnapshot(
    string Id,
    long UserId,
    List<PedidoItemSnapshot> Items,
    decimal Total,
    string Estado,
    bool IsDeleted,
    DateTime CreatedAt);

/// <summary>Campos de línea de pedido (producto congelado al comprar).</summary>
public sealed record PedidoItemSnapshot(long ProductoId, string NombreProducto, int Cantidad, decimal Precio);
