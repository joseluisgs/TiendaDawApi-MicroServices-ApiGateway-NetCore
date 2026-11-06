using FluentAssertions;
using Moq;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Features.Productos.Notifications;
using TiendaApi.Api.Services.Historial;

namespace TiendaApi.Tests.Unit.Features.Productos;

/// <summary>
/// Tests de <see cref="PrecioHistoricoHandler"/>: la semántica del dual-write de
/// precios que el monolito hacía contra Cassandra y que ahora se delega a
/// historical por Refit (fachada <see cref="IHistorialService"/>).
///
/// Reglas del monolito a preservar:
/// - Alta → punto de NACIMIENTO siempre.
/// - Actualización → punto SOLO si el precio cambió de verdad.
/// - Si el servicio remoto falla → no se propaga (modo degradado).
/// </summary>
public class PrecioHistoricoHandlerTests
{
    // Se recrean en [SetUp]: cada test parte de mocks limpios, sin arrastrar
    // invocaciones de los tests anteriores (NUnit reutiliza la instancia).
    private Mock<IHistorialService> _historial = null!;
    private PrecioHistoricoHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _historial = new Mock<IHistorialService>();
        _handler = new PrecioHistoricoHandler(_historial.Object);
    }

    private static ProductoDto Producto(decimal precio) => new(
        7, "Laptop", "desc", precio, 10, null,
        new CategoriaResumenDto(1, "Electrónica"),
        null, DateTime.UtcNow, DateTime.UtcNow);

    [Test]
    public async Task Handle_ProductoCreado_RegistraElPrecioDeNacimiento()
    {
        await _handler.Handle(new ProductoCreadoNotification(Producto(1299.99m)), CancellationToken.None);

        _historial.Verify(h => h.RegistrarPrecioAsync(7, 1299.99d), Times.Once);
    }

    [Test]
    public async Task Handle_Actualizado_PrecioCambiado_RegistraElNuevoPrecio()
    {
        await _handler.Handle(new ProductoActualizadoNotification(
            Producto(999.99m), PrecioAnterior: 1299.99m), CancellationToken.None);

        _historial.Verify(h => h.RegistrarPrecioAsync(7, 999.99d), Times.Once);
    }

    [Test]
    public async Task Handle_Actualizado_PrecioIgual_NoRegistraNada()
    {
        // Solo cambió el stock: el historial registra cambios de precio, no ediciones.
        await _handler.Handle(new ProductoActualizadoNotification(
            Producto(1299.99m), PrecioAnterior: 1299.99m), CancellationToken.None);

        _historial.Verify(h => h.RegistrarPrecioAsync(It.IsAny<long>(), It.IsAny<double>()), Times.Never);
    }

    [Test]
    public async Task Handle_ServicioNoDisponible_NoPropagaLaExcepcion()
    {
        _historial.Setup(h => h.RegistrarPrecioAsync(It.IsAny<long>(), It.IsAny<double>()))
            .ThrowsAsync(new InvalidOperationException("historical caído"));

        var act = async () => await _handler.Handle(
            new ProductoCreadoNotification(Producto(10m)), CancellationToken.None);

        await act.Should().NotThrowAsync("el dual-write es degradado: nunca rompe la escritura del producto");
    }
}
