using Refit;
using TiendaApi.Api.Dtos.Historial;

namespace TiendaApi.Api.Services.Historial;

/// <summary>
/// Contrato Refit del microservicio <b>historical</b> (rutas internas, R7).
///
/// 🎓 En el monolito, las series vivían en el mismo proceso con Cassandra; aquí
/// catalog solo <b>pide</b> y <b>escribe</b> en el dueño de los datos. Los
/// consumidores de la serie (catalog) validan antes la existencia del producto
/// contra su propia PostgreSQL.
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

    /// <summary>
    /// Registra un punto de precio en las series (dual-write del catálogo:
    /// nacimiento al crear y cambios reales al actualizar).
    /// </summary>
    [Post("/internal/historial/precios")]
    Task RegistrarPrecioAsync([Body] RegistrarPrecioRequest body);
}

/// <summary>Cuerpo de la escritura de una vista (nombre aportado por el dueño del producto).</summary>
public sealed record RegistrarVistaRequest(long UsuarioId, long ProductoId, string NombreProducto);

/// <summary>Cuerpo de la escritura de un punto de precio (lo aporta el dueño del producto).</summary>
public sealed record RegistrarPrecioRequest(long ProductoId, double Precio);
