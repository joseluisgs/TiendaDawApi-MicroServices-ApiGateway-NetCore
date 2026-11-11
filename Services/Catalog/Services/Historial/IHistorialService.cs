using CSharpFunctionalExtensions;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Errors;

namespace TiendaApi.Api.Services.Historial;

/// <summary>
/// Fachada del histórico de precios y las vistas para <b>catalog</b>.
///
/// 🎓 Reparto de responsabilidades al estilo de la casa:
/// - PostgreSQL (vía <c>IProductoRepository</c>) decide si el producto EXISTE:
///   es la fuente de verdad, y su 404 manda. Catalog es el dueño del producto.
/// - <c>historical</c> guarda las SERIES: dato derivado que se consulta por
///   Refit DESPUÉS de validar contra PostgreSQL, y cuya API trabaja con listas —
///   sin puntos = lista vacía, una respuesta válida como otra.
///
/// En el monolito esto hablaba directo con Cassandra; aquí es el mismo contrato
/// pero contra el microservicio <c>historical</c> (R4 · REST con Refit).
/// </summary>
public interface IHistorialService {
    /// <summary>
    /// Serie histórica de precios de un producto (consulta Q-A).
    /// </summary>
    /// <param name="productoId">Producto cuya serie se consulta.</param>
    /// <param name="desde">Fecha mínima opcional (rango sobre clustering key).</param>
    /// <param name="hasta">Fecha máxima opcional.</param>
    /// <param name="limit">Puntos máximos (se acota a 1..200; por defecto pide 50).</param>
    /// <returns>200 con la serie (más reciente primero), 404 si el producto no existe, 400 si el rango está invertido.</returns>
    Task<Result<IReadOnlyList<HistorialPrecioDto>, DomainError>> GetHistorialPreciosAsync(
        long productoId, DateTime? desde, DateTime? hasta, int limit);

    /// <summary>
    /// Registra la vista de un usuario sobre un producto (escritura Q-B).
    /// Denormaliza el nombre del producto en la fila; el TTL (180 días) lo aplica la tabla.
    /// </summary>
    /// <param name="usuarioId">Usuario autenticado (partition key del feed).</param>
    /// <param name="productoId">Producto visto.</param>
    /// <returns>204 siempre que el producto exista; 404 si no existe.</returns>
    Task<UnitResult<DomainError>> RegistrarVistaAsync(long usuarioId, long productoId);

    /// <summary>
    /// Registra un punto de precio en las series (dual-write Q-A + Q-C): el
    /// punto de NACIMIENTO al crear el producto y los cambios reales de precio
    /// al actualizarlo. La decisión de cuándo llamar aquí (y el try/catch
    /// degradado) la toma el consumidor — <c>PrecioHistoricoHandler</c> — con la
    /// misma semántica que en el monolito.
    /// </summary>
    /// <param name="productoId">Producto cuyo precio cambia.</param>
    /// <param name="precio">Precio resultante.</param>
    Task RegistrarPrecioAsync(long productoId, double precio);
}
