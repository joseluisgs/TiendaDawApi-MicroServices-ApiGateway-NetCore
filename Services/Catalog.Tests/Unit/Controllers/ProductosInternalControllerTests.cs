using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using System.Data;
using TiendaApi.Api.Controllers;
using TiendaApi.Api.Dtos.Backup;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Productos;

namespace TiendaApi.Tests.Unit.Controllers;

/// <summary>
/// Tests del endpoint interno de <b>descuento de stock</b> (lo consume orders).
///
/// Reparte la transacción Serializable del monolito: aquí se ejecuta el
/// descuento atómico con validación, con los mismos códigos que devolvía el
/// monolito (204 · 404 · 422 con el mensaje de <c>StockInsuficiente</c> · 400).
/// </summary>
public class ProductosInternalControllerTests
{
    // Se recrean en [SetUp]: cada test parte de mocks limpios, sin arrastrar
    // invocaciones de los tests anteriores (NUnit reutiliza la instancia).
    private Mock<IProductoRepository> _productos = null!;
    private Mock<IDbContextTransaction> _transaccion = null!;
    private ProductosInternalController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _productos = new Mock<IProductoRepository>();
        _transaccion = new Mock<IDbContextTransaction>();

        _productos
            .Setup(r => r.BeginTransactionAsync(It.IsAny<IsolationLevel>()))
            .ReturnsAsync(_transaccion.Object);

        _controller = new ProductosInternalController(
            _productos.Object, Mock.Of<ILogger<ProductosInternalController>>());
    }

    private void Existe(long id, int stock) =>
        _productos.Setup(r => r.FindByIdAsync(id))
            .ReturnsAsync(new Producto { Id = id, Nombre = "Laptop Dell XPS 15", Stock = stock });

    [Test]
    public async Task DecrementarStock_Suficiente_Devuelve204YDescuenta()
    {
        Existe(id: 1, stock: 10);

        var result = await _controller.DecrementarStock(1, new DecrementoStockDto(3));

        result.Result.Should().BeOfType<NoContentResult>();
        _productos.Verify(r => r.UpdateAsync(It.Is<Producto>(p => p.Id == 1 && p.Stock == 7)), Times.Once);
        _transaccion.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DecrementarStock_ProductoNoExiste_Devuelve404()
    {
        _productos.Setup(r => r.FindByIdAsync(999)).ReturnsAsync((Producto?)null);

        var result = await _controller.DecrementarStock(999, new DecrementoStockDto(1));

        // El controlador hace `NotFound(new { message = ... })` → NotFoundObjectResult.
        var noEncontrado = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        noEncontrado.Value!.ToString()!.Should().Contain("999");
        _productos.Verify(r => r.UpdateAsync(It.IsAny<Producto>()), Times.Never);
        _transaccion.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DecrementarStock_Insuficiente_Devuelve422ConElMensajeDelMonolito()
    {
        Existe(id: 1, stock: 2);

        var result = await _controller.DecrementarStock(1, new DecrementoStockDto(5));

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(422);
        objectResult.Value!.ToString()!.Should()
            .Contain("Stock insuficiente para el producto 'Laptop Dell XPS 15'. Disponible: 2, Solicitado: 5");
        _productos.Verify(r => r.UpdateAsync(It.IsAny<Producto>()), Times.Never);
        _transaccion.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DecrementarStock_CantidadNoValida_Devuelve400_SinTransaccion()
    {
        var result = await _controller.DecrementarStock(1, new DecrementoStockDto(0));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _productos.Verify(
            r => r.BeginTransactionAsync(It.IsAny<IsolationLevel>()), Times.Never,
            "una cantidad inválida no debe abrir transacción");
    }
}
