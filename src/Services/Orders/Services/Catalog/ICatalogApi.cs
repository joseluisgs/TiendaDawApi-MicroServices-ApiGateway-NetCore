using Refit;

namespace TiendaApi.Api.Services.Catalog;

/// <summary>
/// Contrato Refit del microservicio <b>catalog</b> (rutas internas y públicas, R7).
///
/// 🎓 Orders NO tiene PostgreSQL (R5): el nombre, el precio y el stock de un
/// producto se piden al dueño. La transacción Serializable del monolito pasa a
/// ser <b>validación + decremento atómico en catalog</b> (su endpoint interno
/// hace el descuento con la misma semántica de 204 · 404 · 422).
/// </summary>
public interface ICatalogApi
{
    /// <summary>
    /// Snapshot del producto para congelar nombre y precio en el pedido.
    /// </summary>
    /// <param name="id">ID del producto.</param>
    [Get("/api/v1/productos/{id}")]
    Task<ProductoSnapshotDto> GetProductoAsync(long id);

    /// <summary>
    /// Descuento atómico de stock (lo ejecuta catalog con su propia transacción).
    /// </summary>
    /// <param name="id">ID del producto.</param>
    /// <param name="body">Cantidad a descontar.</param>
    /// <remarks>204 · 404 (producto inexistente) · 422 (stock insuficiente) · 400.</remarks>
    [Post("/internal/productos/{id}/stock/decremento")]
    Task DecrementarStockAsync(long id, [Body] DecrementoStock body);
}

/// <summary>Campos de producto que orders necesita para congelar el item.</summary>
public sealed record ProductoSnapshotDto(long Id, string Nombre, decimal Precio, int Stock);

/// <summary>Cuerpo del descuento de stock (idéntico al de catalog).</summary>
public sealed record DecrementoStock(int Cantidad);
