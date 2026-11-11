using CSharpFunctionalExtensions;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Productos;
using TiendaApi.Api.Repositories.Productos;

namespace TiendaApi.Api.Services.Historial;

/// <inheritdoc />
/// <remarks>
/// 🎓 Consistencia eventual en un almacén de series: la existencia del producto
/// la certifica PostgreSQL (fuente de verdad → su 404 manda) y la serie la sirve
/// <b>historical</b> por Refit como dato derivado. Por eso cada método de LECTURA
/// valida primero contra PostgreSQL (existencia, rango de fechas, límite) y solo
/// después pide la serie; la serie se expone como lista, donde "sin puntos" =
/// lista vacía.
///
/// Si <c>historical</c> no responde, la operación NO falla: el monolito también
/// trataba las series como dato derivado tolerante a fallos (modo degradado).
/// </remarks>
public class HistorialRefitService(
    IProductoRepository productoRepository,
    IHistorialApi api,
    ILogger<HistorialRefitService> logger) : IHistorialService {
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<HistorialPrecioDto>, DomainError>> GetHistorialPreciosAsync(
        long productoId, DateTime? desde, DateTime? hasta, int limit) {
        // El 404 lo decide PostgreSQL: catalog es el dueño del producto.
        var producto = await productoRepository.FindByIdAsync(productoId);
        if (producto is null)
            return Result.Failure<IReadOnlyList<HistorialPrecioDto>, DomainError>(
                ProductoError.NotFound(productoId));

        if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
            return Result.Failure<IReadOnlyList<HistorialPrecioDto>, DomainError>(
                new ValidationError(
                    "El rango de fechas no puede estar invertido",
                    new Dictionary<string, string[]> { ["desde"] = ["No puede ser posterior a 'hasta'"] }));

        var limitEfectivo = Math.Clamp(limit, 1, 200);

        try {
            var serie = await api.GetHistorialPreciosAsync(productoId, desde, hasta, limitEfectivo);
            return Result.Success<IReadOnlyList<HistorialPrecioDto>, DomainError>(serie);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "historical no disponible: el historial del producto {Id} responde vacío (modo degradado)", productoId);
            return Result.Success<IReadOnlyList<HistorialPrecioDto>, DomainError>(Array.Empty<HistorialPrecioDto>());
        }
    }

    /// <inheritdoc />
    public async Task<UnitResult<DomainError>> RegistrarVistaAsync(long usuarioId, long productoId) {
        // El 404 lo decide PostgreSQL (como en el monolito).
        var producto = await productoRepository.FindByIdAsync(productoId);
        if (producto is null)
            return UnitResult.Failure<DomainError>(ProductoError.NotFound(productoId));

        try {
            // El nombre se copia a la fila (denormalización): el feed no hará cruces.
            await api.RegistrarVistaAsync(
                new RegistrarVistaRequest(usuarioId, producto.Id, producto.Nombre));
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "historical: no se pudo registrar la vista del producto {Id} por el usuario {UsuarioId} (modo degradado)",
                productoId, usuarioId);
        }

        return UnitResult.Success<DomainError>();
    }

    /// <inheritdoc />
    public async Task RegistrarPrecioAsync(long productoId, double precio) {
        try {
            // Dual-write al dueño de las series. historical valida y es degradado
            // por sí mismo; aquí solo se asegura el mejor esfuerzo, igual que el
            // try/catch de PrecioHistoricoHandler en el monolito.
            await api.RegistrarPrecioAsync(new RegistrarPrecioRequest(productoId, precio));
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "historical: no se pudo registrar el precio {Precio} del producto {Id} (modo degradado)",
                precio, productoId);
        }
    }
}
