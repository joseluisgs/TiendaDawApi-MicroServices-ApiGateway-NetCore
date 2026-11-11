using CSharpFunctionalExtensions;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Errors;

namespace TiendaApi.Api.Services.Historial;

/// <summary>
///     Fachada del histórico de precios y el feed de actividad (Cassandra) del
///     microservicio <c>historical</c>.
///     🎓 Reparto de responsabilidades en microservicios: este servicio NO consulta
///     la BD de nadie. La existencia del producto la certifica el
///     <b>
///         dueño del
///         producto (catalog) antes de llamar
///     </b>
///     — aquí solo se guardan y sirven las
///     SERIES (dato derivado): "sin puntos" = lista vacía, una respuesta válida
///     como otra.
/// </summary>
public interface IHistorialService {
    /// <summary>
    ///     Serie histórica de precios de un producto (sin validar existencia:
    ///     la valida el consumidor dueño del producto).
    /// </summary>
    Task<Result<IReadOnlyList<HistorialPrecioDto>, DomainError>> GetHistorialPreciosAsync(
        long productoId, DateTime? desde, DateTime? hasta, int limit);

    /// <summary>
    ///     Registra la vista de un usuario sobre un producto. El
    ///     <b>
    ///         nombre del
    ///         producto llega en la petición
    ///     </b>
    ///     (lo aporta el dueño) y se denormaliza
    ///     en la fila: el feed no hará cruces. El TTL (180 días) lo aplica la tabla.
    /// </summary>
    Task<UnitResult<DomainError>> RegistrarVistaAsync(long usuarioId, long productoId, string nombreProducto);

    /// <summary>
    ///     Registra un punto de precio en las series (dual-write Q-A + Q-C): el
    ///     punto de NACIMIENTO al crear el producto y los cambios reales de precio
    ///     al actualizarlo. La decisión de cuándo llamar aquí (y el try/catch
    ///     degradado) la toma el consumidor, <b>catalog</b>, con la misma semántica
    ///     que <c>PrecioHistoricoHandler</c> en el monolito.
    /// </summary>
    Task<UnitResult<DomainError>> RegistrarPrecioAsync(long productoId, double precio);

    /// <summary>
    ///     Feed de actividad: últimos productos vistos por un usuario. Una
    ///     partición vacía es una respuesta válida (lista vacía), no un error.
    /// </summary>
    Task<IReadOnlyList<VistaFeedDto>> GetFeedVistasAsync(long usuarioId, int limit);
}
