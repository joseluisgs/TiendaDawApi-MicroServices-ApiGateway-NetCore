using CSharpFunctionalExtensions;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Repositories.Historial;

namespace TiendaApi.Api.Services.Historial;

/// <inheritdoc />
/// <remarks>
/// 🎓 Consistencia eventual en un almacén de series: la existencia del producto
/// la certifica el dueño (catalog) ANTES de llamar — aquí solo se sirve
/// Cassandra como dato derivado. Se validan los parámetros (rango de fechas,
/// límite) y "sin puntos" = lista vacía.
/// </remarks>
public class HistorialService(
    IHistorialRepository historialRepository,
    ILogger<HistorialService> logger) : IHistorialService {
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<HistorialPrecioDto>, DomainError>> GetHistorialPreciosAsync(
        long productoId, DateTime? desde, DateTime? hasta, int limit) {
        if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
            return Result.Failure<IReadOnlyList<HistorialPrecioDto>, DomainError>(
                new ValidationError(
                    "El rango de fechas no puede estar invertido",
                    new Dictionary<string, string[]> { ["desde"] = ["No puede ser posterior a 'hasta'"] }));

        var limitEfectivo = Math.Clamp(limit, 1, 200);

        try {
            var serie = await historialRepository.GetHistorialPreciosAsync(
                productoId, desde, hasta, limitEfectivo);
            return Result.Success<IReadOnlyList<HistorialPrecioDto>, DomainError>(serie);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Cassandra no disponible: el historial del producto {Id} responde vacio (modo degradado)", productoId);
            return Result.Success<IReadOnlyList<HistorialPrecioDto>, DomainError>(Array.Empty<HistorialPrecioDto>());
        }
    }

    /// <inheritdoc />
    public async Task<UnitResult<DomainError>> RegistrarVistaAsync(long usuarioId, long productoId, string nombreProducto) {
        if (string.IsNullOrWhiteSpace(nombreProducto))
            return UnitResult.Failure<DomainError>(
                ValidationError.Create("El nombre del producto es obligatorio para denormalizar el feed"));

        try {
            // El nombre se copia a la fila (denormalización): el feed no hará cruces.
            await historialRepository.RegistrarVistaAsync(usuarioId, productoId, nombreProducto);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Cassandra: no se pudo registrar la vista del producto {Id} por el usuario {UsuarioId} (modo degradado)",
                productoId, usuarioId);
        }

        return UnitResult.Success<DomainError>();
    }

    /// <inheritdoc />
    public async Task<UnitResult<DomainError>> RegistrarPrecioAsync(long productoId, double precio) {
        if (productoId <= 0)
            return UnitResult.Failure<DomainError>(
                ValidationError.Create("El identificador del producto no es válido"));

        if (precio < 0)
            return UnitResult.Failure<DomainError>(
                ValidationError.Create("El precio no puede ser negativo"));

        try {
            // Dual-write (Q-A serie del producto + Q-C bucket del día), el MISMO
            // camino que el repositorio del monolito. Mejor esfuerzo: si Cassandra
            // está caído, el alta/cambio de precio del consumidor no debe fallar
            // (misma filosofía degradada que PrecioHistoricoHandler).
            await historialRepository.RegistrarPrecioAsync(productoId, precio);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Cassandra: no se pudo registrar el precio {Precio} del producto {Id} (modo degradado)",
                precio, productoId);
        }

        return UnitResult.Success<DomainError>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VistaFeedDto>> GetFeedVistasAsync(long usuarioId, int limit) {
        var limitEfectivo = Math.Clamp(limit, 1, 100);

        try {
            return await historialRepository.GetFeedVistasAsync(usuarioId, limitEfectivo);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Cassandra no disponible: el feed del usuario {UsuarioId} responde vacio (modo degradado)", usuarioId);
            return Array.Empty<VistaFeedDto>();
        }
    }
}
