using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Repositories.Historial;
using TiendaApi.Api.Services.Historial;

namespace TiendaApi.Tests.Unit.Services.Historial;

/// <summary>
/// Tests de HistorialService, la fachada del microservicio historical.
///
/// 🎓 Reparto de responsabilidades: este servicio NO valida la existencia del
/// producto (la certifica catalog, el dueño, antes de llamar) — aquí solo se
/// validan los parámetros (rango de fechas, límite, nombre) y se sirven las
/// series. "Cassandra caído" nunca lanza: responde vacío (modo degradado).
/// </summary>
public class HistorialServiceTests {
    private Mock<IHistorialRepository> _repository = null!;
    private HistorialService _service = null!;

    [SetUp]
    public void Setup() {
        _repository = new Mock<IHistorialRepository>();
        _service = new HistorialService(_repository.Object, Mock.Of<ILogger<HistorialService>>());
    }

    #region GetHistorialPreciosAsync

    [Test]
    public async Task GetHistorialPrecios_SerieDisponible_DevuelveLaSerieDelRepositorio() {
        var serie = new List<HistorialPrecioDto> { new(DateTime.UtcNow, 1299.99) };
        _repository.Setup(r => r.GetHistorialPreciosAsync(1, null, null, 50))
            .ReturnsAsync(serie);

        var result = await _service.GetHistorialPreciosAsync(1, null, null, 50);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(serie);
    }

    [Test]
    public async Task GetHistorialPrecios_RangoInvertido_DevuelveValidationError_NoConsultaCassandra() {
        var result = await _service.GetHistorialPreciosAsync(
            1, DateTime.UtcNow, DateTime.UtcNow.AddDays(-7), 50);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        // Un rango imposible no debe costarle una consulta al clúster.
        _repository.Verify(r => r.GetHistorialPreciosAsync(
            It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetHistorialPrecios_CassandraCaida_NoLanza_DevuelveSerieVacia() {
        _repository.Setup(r => r.GetHistorialPreciosAsync(It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("Nodo caído"));

        var act = async () => await _service.GetHistorialPreciosAsync(1, null, null, 50);
        var result = await act.Should().NotThrowAsync();

        result.Which.IsSuccess.Should().BeTrue();
        result.Which.Value.Should().BeEmpty("sin puntos = lista vacía, una respuesta válida como otra");
    }

    [Test]
    public async Task GetHistorialPrecios_LimitFueraDeRango_SeClampaEntre1Y200() {
        _repository.Setup(r => r.GetHistorialPreciosAsync(It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(new List<HistorialPrecioDto>());

        await _service.GetHistorialPreciosAsync(1, null, null, 0);
        _repository.Verify(r => r.GetHistorialPreciosAsync(1, null, null, 1), Times.Once);

        await _service.GetHistorialPreciosAsync(1, null, null, 500);
        _repository.Verify(r => r.GetHistorialPreciosAsync(1, null, null, 200), Times.Once);
    }

    #endregion

    #region GetFeedVistasAsync

    [Test]
    public async Task GetFeedVistas_FeedDisponible_DevuelveLaListaDelRepositorio() {
        var feed = new List<VistaFeedDto> { new(DateTime.UtcNow, 1, "Laptop Dell XPS 15") };
        _repository.Setup(r => r.GetFeedVistasAsync(2, 20)).ReturnsAsync(feed);

        var result = await _service.GetFeedVistasAsync(2, 20);

        result.Should().BeEquivalentTo(feed);
    }

    [Test]
    public async Task GetFeedVistas_SinActividad_DevuelveVacia_NoError() {
        _repository.Setup(r => r.GetFeedVistasAsync(2, 20))
            .ReturnsAsync(new List<VistaFeedDto>());

        var result = await _service.GetFeedVistasAsync(2, 20);

        result.Should().BeEmpty();
    }

    [Test]
    public async Task GetFeedVistas_CassandraCaida_NoLanza_DevuelveVacia() {
        _repository.Setup(r => r.GetFeedVistasAsync(It.IsAny<long>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("Nodo caído"));

        var act = async () => await _service.GetFeedVistasAsync(2, 20);
        var result = await act.Should().NotThrowAsync();

        result.Which.Should().BeEmpty();
    }

    [Test]
    public async Task GetFeedVistas_LimitFueraDeRango_SeClampaEntre1Y100() {
        _repository.Setup(r => r.GetFeedVistasAsync(It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync(new List<VistaFeedDto>());

        await _service.GetFeedVistasAsync(2, 0);
        _repository.Verify(r => r.GetFeedVistasAsync(2, 1), Times.Once);

        await _service.GetFeedVistasAsync(2, 999);
        _repository.Verify(r => r.GetFeedVistasAsync(2, 100), Times.Once);
    }

    #endregion

    #region RegistrarVistaAsync

    [Test]
    public async Task RegistrarVista_Ok_DevuelveSuccess_YDenormalizaElNombre() {
        var result = await _service.RegistrarVistaAsync(2, 1, "Laptop Dell XPS 15");

        result.IsSuccess.Should().BeTrue();
        // El nombre viaja a la fila: el feed se pinta después sin cruces.
        _repository.Verify(r => r.RegistrarVistaAsync(2, 1, "Laptop Dell XPS 15"), Times.Once);
    }

    [Test]
    public async Task RegistrarVista_SinNombre_DevuelveValidationError_NoEscribe() {
        var result = await _service.RegistrarVistaAsync(2, 1, "  ");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        _repository.Verify(r => r.RegistrarVistaAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task RegistrarVista_CassandraCaida_NoLanza_DevuelveSuccess() {
        // Mejor esfuerzo: una vista perdida no rompe la operación del usuario.
        _repository.Setup(r => r.RegistrarVistaAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Nodo caído"));

        var act = async () => await _service.RegistrarVistaAsync(2, 1, "Laptop");
        var result = await act.Should().NotThrowAsync();

        result.Which.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region RegistrarPrecioAsync

    [Test]
    public async Task RegistrarPrecio_Ok_DevuelveSuccess_YEscribeElPunto() {
        var result = await _service.RegistrarPrecioAsync(1, 1299.99);

        result.IsSuccess.Should().BeTrue();
        // Dual-write: la escritura pasa por el repositorio (Q-A + Q-C).
        _repository.Verify(r => r.RegistrarPrecioAsync(1, 1299.99), Times.Once);
    }

    [Test]
    public async Task RegistrarPrecio_ProductoIdInvalido_DevuelveValidationError_NoEscribe() {
        var result = await _service.RegistrarPrecioAsync(0, 1299.99);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        _repository.Verify(r => r.RegistrarPrecioAsync(It.IsAny<long>(), It.IsAny<double>()), Times.Never);
    }

    [Test]
    public async Task RegistrarPrecio_PrecioNegativo_DevuelveValidationError_NoEscribe() {
        var result = await _service.RegistrarPrecioAsync(1, -5);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        _repository.Verify(r => r.RegistrarPrecioAsync(It.IsAny<long>(), It.IsAny<double>()), Times.Never);
    }

    [Test]
    public async Task RegistrarPrecio_CassandraCaida_NoLanza_DevuelveSuccess() {
        // Degradado igual que PrecioHistoricoHandler del monolito: el alta o el
        // cambio de precio del consumidor NUNCA debe fallar por Cassandra.
        _repository.Setup(r => r.RegistrarPrecioAsync(It.IsAny<long>(), It.IsAny<double>()))
            .ThrowsAsync(new InvalidOperationException("Nodo caído"));

        var act = async () => await _service.RegistrarPrecioAsync(1, 1299.99);
        var result = await act.Should().NotThrowAsync();

        result.Which.IsSuccess.Should().BeTrue();
    }

    #endregion
}
