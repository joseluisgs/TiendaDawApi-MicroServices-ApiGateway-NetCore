using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using TiendaApi.Api.Dtos.Backup;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Productos;
using TiendaApi.Api.Extensions;
using TiendaApi.Api.Repositories.Productos;

namespace TiendaApi.Api.Controllers;

/// <summary>
/// API interna de <c>catalog</c> (rutas <c>/internal/*</c>, R7): la consume
/// <b>orders</b> (T8) por Refit para descontar stock al crear un pedido.
///
/// 🎓 Reparto de la transacción: en el monolito, <c>CreatePedidoCommand</c>
/// hacía <c>producto.Stock -= cantidad</c> DENTRO de una transacción
/// <c>Serializable</c> junto con el alta del pedido. En microservicios esa
/// transacción se parte en dos: orders escribe SU pedido (Mongo) y pide aquí el
/// descuento atómico — que se ejecuta con el MISMO nivel de aislamiento, así
/// que dos pedidos simultáneos no pueden dejar el stock en negativo.
///
/// El cliente NUNCA llega aquí (no hay ruta en el gateway) y solo es accesible
/// por la red interna (R12).
/// </summary>
[ApiController]
[Route("internal/productos")]
[Produces("application/json")]
public class ProductosInternalController(
    IProductoRepository productoRepository,
    ILogger<ProductosInternalController> logger) : ControllerBase
{
    /// <summary>
    /// Descuenta stock de forma atómica con validación.
    /// </summary>
    /// <param name="id">Producto a descontar.</param>
    /// <param name="body">Cantidad a restar (mayor que 0).</param>
    /// <returns>
    /// 204 descontado · 404 si el producto no existe · 422 si no hay stock
    /// suficiente (mismo mensaje que el monolito) · 400 si la cantidad no es válida.
    /// </returns>
    [HttpPost("{id:long}/stock/decremento")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> DecrementarStock(
        long id,
        [FromBody] DecrementoStockDto body)
    {
        if (body.Cantidad <= 0)
            return BadRequest(new { message = "La cantidad debe ser mayor que 0" });

        // Mismo nivel de aislamiento que la transacción del monolito: dos
        // descuentos simultáneos se serializan en vez de perder stock.
        await using var transaction = await productoRepository.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            // Tracking (el FindByIdAsync lo trae con tracking) y filtro de
            // soft-delete global: un producto borrado "no existe" → 404.
            var producto = await productoRepository.FindByIdAsync(id);
            if (producto is null)
            {
                await transaction.RollbackAsync();
                return NotFound(new { message = ProductoError.NotFound(id).Message });
            }

            if (producto.Stock < body.Cantidad)
            {
                await transaction.RollbackAsync();
                // Mismo 422 y mismo texto que PedidoError.StockInsuficiente
                // del monolito: orders reenvía este mensaje al cliente.
                return ProductoError
                    .StockInsuficiente(producto.Nombre, producto.Stock, body.Cantidad)
                    .ToHttpResult<object>();
            }

            producto.Stock -= body.Cantidad;
            await productoRepository.UpdateAsync(producto);
            await transaction.CommitAsync();

            logger.LogInformation(
                "Stock decrementado: producto {Id} -{Cantidad} (quedan {Stock})",
                id, body.Cantidad, producto.Stock);
            return NoContent();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error al decrementar stock del producto {Id}", id);
            throw;
        }
    }
}
