using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using TiendaApi.Api.Controllers;
using TiendaApi.Api.Dtos.Backup;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Dtos.Proveedores;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Categorias;
using TiendaApi.Api.Repositories.Productos;
using TiendaApi.Api.Repositories.Proveedores;

namespace TiendaApi.Tests.Unit.Controllers;

/// <summary>
/// Tests de los endpoints internos de <b>import de backups</b> (los consume backup).
///
/// Replican la semántica del <c>BackupService</c> del monolito: upsert por clave,
/// altas con ID explícito (<c>OVERRIDING SYSTEM VALUE</c> + re-sincronización de
/// secuencia) y distinción entre 201 (creado) / 200 (actualizado) / 204 (sin
/// cambios) — el 204 es el que hace que el round-trip del E2E reporte "sin altas".
/// </summary>
public class CatalogImportInternalControllerTests {
    // Se recrean en [SetUp]: cada test parte de mocks limpios, sin arrastrar
    // invocaciones de los tests anteriores (NUnit reutiliza la instancia).
    private Mock<ICategoriaRepository> _categorias = null!;
    private Mock<IProveedorRepository> _proveedores = null!;
    private Mock<IProductoRepository> _productos = null!;
    private CatalogImportInternalController _controller = null!;

    /// <summary>Validador mockeado cuya validación pasa siempre (las reglas se prueban aparte).</summary>
    private static IValidator<T> ValidadorQuePasa<T>() {
        var validador = new Mock<IValidator<T>>();
        validador.Setup(v => v.ValidateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return validador.Object;
    }

    [SetUp]
    public void SetUp() {
        _categorias = new Mock<ICategoriaRepository>();
        _proveedores = new Mock<IProveedorRepository>();
        _productos = new Mock<IProductoRepository>();

        _controller = new CatalogImportInternalController(
            _categorias.Object, _proveedores.Object, _productos.Object,
            ValidadorQuePasa<CategoriaRequestDto>(),
            ValidadorQuePasa<ProveedorRequestDto>(),
            ValidadorQuePasa<ProductoRequestDto>(),
            Mock.Of<ILogger<CatalogImportInternalController>>());
    }

    [Test]
    public async Task ImportCategoria_NoExistia_Devuelve201YResincronizaLaSecuencia() {
        _categorias.Setup(r => r.FindByIdAsync(42)).ReturnsAsync((Categoria?)null);

        var result = await _controller.ImportCategoria(new ImportCategoriaDto(
            42, "Nueva", "desc", false, DateTime.UtcNow, DateTime.UtcNow));

        var creado = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        creado.StatusCode.Should().Be(201);
        _categorias.Verify(r => r.CrearConIdExplicitoAsync(It.Is<Categoria>(c => c.Id == 42)), Times.Once);
        _categorias.Verify(r => r.ResincronizarSecuenciaAsync(), Times.Once,
            "tras una alta con ID explícito la secuencia debe re-sincronizarse");
    }

    [Test]
    public async Task ImportCategoria_YaExistia_SinCambios_Devuelve204() {
        _categorias.Setup(r => r.FindByIdAsync(42))
            .ReturnsAsync(new Categoria { Id = 42, Nombre = "Igual", Descripcion = "igual", IsDeleted = false });

        var result = await _controller.ImportCategoria(new ImportCategoriaDto(
            42, "Igual", "igual", false, DateTime.UtcNow, DateTime.UtcNow));

        result.Should().BeOfType<NoContentResult>(
            "el round-trip export→import del E2E exige 204: sin cambios no se reporta alta");
        _categorias.Verify(r => r.UpdateAsync(It.IsAny<Categoria>()), Times.Never);
    }

    [Test]
    public async Task ImportCategoria_CambioReal_Devuelve200YActualiza() {
        _categorias.Setup(r => r.FindByIdAsync(42))
            .ReturnsAsync(new Categoria { Id = 42, Nombre = "Viejo", IsDeleted = false });

        var result = await _controller.ImportCategoria(new ImportCategoriaDto(
            42, "Nuevo", "nueva", false, DateTime.UtcNow, DateTime.UtcNow));

        result.Should().BeOfType<OkResult>();
        _categorias.Verify(r => r.UpdateAsync(It.Is<Categoria>(c => c.Nombre == "Nuevo")), Times.Once);
    }

    [Test]
    public async Task ImportCategoria_NombreInvalido_Devuelve400_SinEscribir() {
        var validador = new Mock<IValidator<CategoriaRequestDto>>();
        validador.Setup(v => v.ValidateAsync(It.IsAny<CategoriaRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Nombre", "El nombre es obligatorio") }));
        var controller = new CatalogImportInternalController(
            _categorias.Object, _proveedores.Object, _productos.Object,
            validador.Object,
            ValidadorQuePasa<ProveedorRequestDto>(),
            ValidadorQuePasa<ProductoRequestDto>(),
            Mock.Of<ILogger<CatalogImportInternalController>>());

        var result = await controller.ImportCategoria(new ImportCategoriaDto(
            42, "", null, false, DateTime.UtcNow, DateTime.UtcNow));

        result.Should().BeOfType<BadRequestObjectResult>();
        _categorias.Verify(r => r.CrearConIdExplicitoAsync(It.IsAny<Categoria>()), Times.Never);
    }

    [Test]
    public async Task ImportProducto_CategoriaInexistente_Devuelve400() {
        // El controlador valida la FK contra ICategoriaRepository (no contra productos).
        _categorias.Setup(r => r.ExistsByIdAsync(999)).ReturnsAsync(false);

        var result = await _controller.ImportProducto(new ImportProductoDto(
            1, "Prod", "d", 10m, 1, null, 999, null, false, DateTime.UtcNow, DateTime.UtcNow));

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value!.ToString()!.Should().Contain("999");
        _productos.Verify(r => r.CrearConIdExplicitoAsync(It.IsAny<Producto>()), Times.Never);
    }

    [Test]
    public async Task ImportProducto_ProveedorInexistente_Devuelve400() {
        _categorias.Setup(r => r.ExistsByIdAsync(1)).ReturnsAsync(true);
        _proveedores.Setup(r => r.ExistsByIdAsync("NoExiste")).ReturnsAsync(false);

        var result = await _controller.ImportProducto(new ImportProductoDto(
            1, "Prod", "d", 10m, 1, null, 1, "NoExiste", false, DateTime.UtcNow, DateTime.UtcNow));

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value!.ToString()!.Should().Contain("NoExiste");
        _productos.Verify(r => r.CrearConIdExplicitoAsync(It.IsAny<Producto>()), Times.Never);
    }
}
