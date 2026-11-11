using Refit;
using TiendaApi.Api.Dtos.Historial;

namespace TiendaApi.Api.Services.Historial;

/// <summary>
///     Contrato Refit del microservicio <c>historical</c> (rutas internas, R7).
///     🎓 En el monolito, el feed vivía en el mismo proceso con Cassandra; aquí
///     identity solo <b>pide</b> el feed al dueño de los datos. Los consumidores
///     de la serie (catalog) validan antes la existencia del producto.
/// </summary>
public interface IHistorialApi {
    /// <summary>Serie histórica de precios de un producto.</summary>
    [Get("/internal/historial/productos/{productoId}")]
    Task<List<HistorialPrecioDto>> GetHistorialPreciosAsync(
        long productoId,
        [Query] DateTime? desde,
        [Query] DateTime? hasta,
        [Query] int limit);

    /// <summary>Registra la vista de un usuario (el nombre lo aporta el dueño del producto).</summary>
    [Post("/internal/historial/vistas")]
    Task RegistrarVistaAsync([Body] RegistrarVistaRequest body);

    /// <summary>Feed de actividad (últimos productos vistos) de un usuario.</summary>
    [Get("/internal/historial/feed")]
    Task<List<VistaFeedDto>> GetFeedVistasAsync([Query] long usuarioId, [Query] int limit);
}

/// <summary>Cuerpo de la escritura de una vista (nombre aportado por el dueño del producto).</summary>
public sealed record RegistrarVistaRequest(long UsuarioId, long ProductoId, string NombreProducto);
