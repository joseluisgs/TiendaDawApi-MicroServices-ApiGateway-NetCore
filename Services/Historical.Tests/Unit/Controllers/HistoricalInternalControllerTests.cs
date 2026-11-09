using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TiendaApi.Api.Controllers;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Services.Historial;

namespace TiendaApi.Tests.Unit.Controllers;

/// <summary>
/// Tests del contrato interno del microservicio historical
/// (GET feed, GET serie, POST vistas): delegación pura en la fachada y
/// mapeo de los Result de dominio a códigos HTTP (200/204/400).
/// </summary>
public class HistoricalInternalControllerTests
{
    private Mock<IHistorialService> _service = null!;
    private HistoricalInternalController _controller = null!;

    [SetUp]
    public void Setup()
    {
        _service = new Mock<IHistorialService>();
        _controller = new HistoricalInternalController(_service.Object);
    }

    #region GET feed

    [Test]
    public async Task GetFeed_ConActividad_Devuelve200ConLaLista()
    {
        var feed = new List<VistaFeedDto> { new(DateTime.UtcNow, 1, "Laptop Dell XPS 15") };
        _service.Setup(s => s.GetFeedVistasAsync(2, 20)).ReturnsAsync(feed);

        var result = await _controller.GetFeed(2, 20);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(feed);
    }

    [Test]
    public async Task GetFeed_SinActividad_Devuelve200Vacia()
    {
        _service.Setup(s => s.GetFeedVistasAsync(2, 20))
            .ReturnsAsync(new List<VistaFeedDto>());

        var result = await _controller.GetFeed(2, 20);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new List<VistaFeedDto>());
    }

    #endregion

    #region GET serie de precios

    [Test]
    public async Task GetHistorial_SerieDisponible_Devuelve200ConLaSerie()
    {
        var serie = new List<HistorialPrecioDto> { new(DateTime.UtcNow, 1299.99) };
        _service.Setup(s => s.GetHistorialPreciosAsync(1, null, null, 50))
            .ReturnsAsync(Result.Success<IReadOnlyList<HistorialPrecioDto>, DomainError>(serie));

        var result = await _controller.GetHistorial(1, null, null, 50);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(serie);
    }

    [Test]
    public async Task GetHistorial_RangoInvertido_Devuelve400()
    {
        _service.Setup(s => s.GetHistorialPreciosAsync(
                It.IsAny<long>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<HistorialPrecioDto>, DomainError>(
                ValidationError.Create("El rango de fechas no puede estar invertido")));

        var result = await _controller.GetHistorial(1, DateTime.UtcNow, DateTime.UtcNow.AddDays(-7), 50);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().NotBeNull();
    }

    #endregion

    #region POST vistas

    [Test]
    public async Task RegistrarVista_Ok_Devuelve204()
    {
        _service.Setup(s => s.RegistrarVistaAsync(2, 1, "Laptop"))
            .ReturnsAsync(UnitResult.Success<DomainError>());

        var result = await _controller.RegistrarVista(new RegistrarVistaRequest(2, 1, "Laptop"));

        result.Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.RegistrarVistaAsync(2, 1, "Laptop"), Times.Once);
    }

    [Test]
    public async Task RegistrarVista_SinNombre_Devuelve400()
    {
        _service.Setup(s => s.RegistrarVistaAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
            .ReturnsAsync(UnitResult.Failure<DomainError>(
                ValidationError.Create("El nombre del producto es obligatorio")));

        var result = await _controller.RegistrarVista(new RegistrarVistaRequest(2, 1, ""));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region POST precios

    [Test]
    public async Task RegistrarPrecio_Ok_Devuelve204()
    {
        _service.Setup(s => s.RegistrarPrecioAsync(1, 1299.99))
            .ReturnsAsync(UnitResult.Success<DomainError>());

        var result = await _controller.RegistrarPrecio(new RegistrarPrecioRequest(1, 1299.99));

        result.Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.RegistrarPrecioAsync(1, 1299.99), Times.Once);
    }

    [Test]
    public async Task RegistrarPrecio_PrecioNegativo_Devuelve400()
    {
        _service.Setup(s => s.RegistrarPrecioAsync(It.IsAny<long>(), It.IsAny<double>()))
            .ReturnsAsync(UnitResult.Failure<DomainError>(
                ValidationError.Create("El precio no puede ser negativo")));

        var result = await _controller.RegistrarPrecio(new RegistrarPrecioRequest(1, -5));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion
}
