using CSharpFunctionalExtensions;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Errors;

namespace TiendaApi.Api.Services.Historial;

/// <summary>
/// Implementación de <see cref="IHistorialService"/> para identity: el feed de
/// actividad se pide al microservicio <c>historical</c> por Refit.
///
/// 🎓 Si historical no responde, el feed devuelve lista vacía — el monolito
/// también trataba el feed como dato derivado tolerante a fallos.
/// </summary>
public class HistorialRefitService(IHistorialApi api, ILogger<HistorialRefitService> logger)
    : IHistorialService {
    /// <inheritdoc />
    public async Task<IReadOnlyList<VistaFeedDto>> GetFeedVistasAsync(long usuarioId, int limit) {
        try {
            return await api.GetFeedVistasAsync(usuarioId, limit);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Feed: historical no responde; se devuelve lista vacía");
            return new List<VistaFeedDto>();
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<HistorialPrecioDto>, DomainError>> GetHistorialPreciosAsync(
        long productoId, DateTime? desde, DateTime? hasta, int limit)
        // Este método es de products; identity no consulta históricos.
        => throw new NotSupportedException(
            "identity no consulta históricos: lo hace catalog contra historical directamente.");

    /// <inheritdoc />
    public Task<UnitResult<DomainError>> RegistrarVistaAsync(long usuarioId, long productoId)
        // Este método es de products; identity no registra vistas.
        => throw new NotSupportedException(
            "identity no registra vistas: lo hace catalog contra historical directamente.");
}
