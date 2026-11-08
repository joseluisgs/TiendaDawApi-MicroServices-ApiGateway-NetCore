using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Models.Read;
using TiendaApi.Api.Repositories.Productos;
using TiendaApi.Api.Services.Cache;
using TiendaApi.Api.Services.Productos;

namespace TiendaApi.Tests.Unit.Services.Productos;

/// <summary>
/// Tests de la fachada de lectura de productos (CQRS: lado de lectura).
///
/// 🎓 Camino real: caché → MongoDB (<c>productos_read</c>). PostgreSQL solo
/// interviene en las escrituras, por eso aquí NUNCA se toca el repositorio PG.
/// </summary>
public class ProductoServiceTests
{
    // Se recrean en [SetUp]: cada test parte de mocks limpios, sin arrastrar
    // invocaciones de los tests anteriores (NUnit reutiliza la instancia).
    private Mock<IProductoReadRepository> _read = null!;
    private Mock<ICacheService> _cache = null!;
    private ProductoService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _read = new Mock<IProductoReadRepository>();
        _cache = new Mock<ICacheService>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:ProductoCacheTTLMinutes"] = "10"
            })
            .Build();

        _service = new ProductoService(_read.Object, _cache.Object, configuration);
    }

    private static ProductoRead Producto(long id, string nombre = "Laptop") => new()
    {
        Id = id,
        Nombre = nombre,
        Descripcion = "desc",
        Precio = 10m,
        Stock = 1,
        CategoriaId = 1,
        Categoria = new CategoriaRead { Id = 1, Nombre = "Electrónica" },
    };

    /// <summary>
    /// Espera a que el <c>Task.Run</c> fire-and-forget del servicio termine de
    /// escribir en caché (la invalidación/escritura no es awaitable por diseño).
    /// </summary>
    private static async Task EsperarAsync(Func<bool> condicion)
    {
        for (var i = 0; i < 40 && !condicion(); i++)
            await Task.Delay(25);
    }

    [Test]
    public async Task GetById_CacheHit_NoTocaMongo()
    {
        var enCache = new ProductoDto(1, "En caché", "d", 1m, 1, null,
            new CategoriaResumenDto(1, "Cat"), null, DateTime.UtcNow, DateTime.UtcNow);
        _cache.Setup(c => c.GetAsync<ProductoDto>("productos:1")).ReturnsAsync(enCache);

        var result = await _service.GetByIdAsync(1);

        result!.Id.Should().Be(1, "la caché manda y no se consulta Mongo");
        _read.Verify(r => r.FindByIdAsync(It.IsAny<long>()), Times.Never);
    }

    [Test]
    public async Task GetById_SinCache_LeeDeMongo_YLoCachea()
    {
        _cache.Setup(c => c.GetAsync<ProductoDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductoDto?)null);
        _read.Setup(r => r.FindByIdAsync(7)).ReturnsAsync(Producto(7));

        var result = await _service.GetByIdAsync(7);

        result!.Id.Should().Be(7);
        _read.Verify(r => r.FindByIdAsync(7), Times.Once);

        // La escritura va en Task.Run (fire & forget): se espera a que ocurra.
        await EsperarAsync(() => _cache.Invocations.Any(i =>
            i.Method.Name == nameof(ICacheService.SetAsync)));
        _cache.Verify(c => c.SetAsync("productos:7", It.IsAny<ProductoDto>(), It.IsAny<TimeSpan?>()),
            Times.Once, "el resultado debe cachearse para la siguiente lectura");
    }

    [Test]
    public async Task GetById_NoExiste_DevuelveNull()
    {
        _cache.Setup(c => c.GetAsync<ProductoDto>(It.IsAny<string>()))
            .ReturnsAsync((ProductoDto?)null);
        _read.Setup(r => r.FindByIdAsync(999)).ReturnsAsync((ProductoRead?)null);

        var result = await _service.GetByIdAsync(999);

        result.Should().BeNull("un producto inexistente no se cachea: hay que poder distinguirlo");
        _cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<ProductoDto>(), It.IsAny<TimeSpan?>()),
            Times.Never, "no se debe cachear un fallo (bloquearía la corrección posterior)");
    }

    [Test]
    public async Task GetPaged_SinCache_LeeDeMongo_YDevuelvePagedResult()
    {
        _cache.Setup(c => c.GetAsync<PagedResult<ProductoDto>>(It.IsAny<string>()))
            .ReturnsAsync((PagedResult<ProductoDto>?)null);
        _read.Setup(r => r.FindAllPagedAsync(It.IsAny<ProductoFilterDto>()))
            .ReturnsAsync((new List<ProductoRead> { Producto(1), Producto(2) }, 2));

        var result = await _service.GetPagedAsync(new ProductoFilterDto(
            null, null, null, null, null, 0, 10, "id", "asc"));

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(1, "Page del PagedResult es base-1 (base-0 en el filtro)");
    }

    [Test]
    public async Task GetPaged_CacheHit_NoTocaMongo()
    {
        var enCache = new PagedResult<ProductoDto> { TotalCount = 5, Page = 1, PageSize = 10 };
        _cache.Setup(c => c.GetAsync<PagedResult<ProductoDto>>(It.IsAny<string>()))
            .ReturnsAsync(enCache);

        var result = await _service.GetPagedAsync(new ProductoFilterDto(
            null, null, null, null, null, 0, 10, "id", "asc"));

        result.TotalCount.Should().Be(5);
        _read.Verify(r => r.FindAllPagedAsync(It.IsAny<ProductoFilterDto>()), Times.Never);
    }
}
