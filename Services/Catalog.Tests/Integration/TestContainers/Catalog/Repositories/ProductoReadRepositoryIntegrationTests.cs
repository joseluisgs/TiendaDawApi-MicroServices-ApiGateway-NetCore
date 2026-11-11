using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Models.Read;
using TiendaApi.Api.Repositories.Productos;

namespace TiendaApi.Tests.Integration.TestContainers.Catalog.Repositories;

/// <summary>
///     Tests de integración del <b>read model</b> de catalog sobre MongoDB real.
///     🎓 Es el lado de LECTURA del CQRS interno: aquí no hay PostgreSQL. Cada test
///     parte de una colección limpia (la fixture hace drop al obtener la BD), así
///     que los assertions no dependen del orden de ejecución.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class ProductoReadRepositoryIntegrationTests {
    [SetUp]
    public void Setup() {
        // GetMongoDatabase hace drop: cada test arranca de colección vacía.
        _database = AssemblyContainerFixture.GetMongoDatabase(DatabaseName);

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                { "MongoDbSettings:ProductosCollection", "productos_read" }
            })
            .Build();

        _repo = new ProductoReadRepository(
            _database, configuration, NullLogger<ProductoReadRepository>.Instance);
    }

    private const string DatabaseName = "it_catalog_read";
    private IMongoDatabase _database = null!;
    private ProductoReadRepository _repo = null!;

    private static ProductoRead Producto(long id, string nombre = "Laptop Dell XPS 15") {
        return new ProductoRead {
            Id = id,
            Nombre = nombre,
            Descripcion = "Laptop de alto rendimiento",
            Precio = 1299.99m,
            Stock = 10,
            CategoriaId = 1,
            Categoria = new CategoriaRead { Id = 1, Nombre = "Electrónica" },
            ProveedorId = "Distribuc01",
            Proveedor = new ProveedorRead { Codigo = "Distribuc01", Nombre = "Distribuciones DAW" },
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // ------------------------------------------------------------- lecturas

    [Test]
    public async Task FindById_SinDocumentos_DevuelveNull() {
        ProductoRead? resultado = await _repo.FindByIdAsync(1);

        resultado.Should().BeNull("una colección vacía no es un error: es un miss");
    }

    [Test]
    public async Task Upsert_LuegoFindById_DevuelveElDocumento() {
        await _repo.UpsertAsync(Producto(7));

        ProductoRead? resultado = await _repo.FindByIdAsync(7);

        resultado.Should().NotBeNull();
        resultado!.Nombre.Should().Be("Laptop Dell XPS 15");
        resultado.Categoria.Nombre.Should().Be("Electrónica", "los subdocumentos se guardan embebidos");
        resultado.SyncAt.Should().NotBe(default, "el upsert siempre estampa SyncAt");
    }

    [Test]
    public async Task Upsert_MismoId_ActualizaEnLugarDeDuplicar() {
        await _repo.UpsertAsync(Producto(7));
        await _repo.UpsertAsync(Producto(7, "Laptop Actualizada"));

        var todas = (await _repo.FindAllAsync()).ToList();

        todas.Should().ContainSingle(p => p.Id == 7, "el _id es Id: no puede haber duplicados");
        todas.Single().Nombre.Should().Be("Laptop Actualizada");
    }

    [Test]
    public async Task Upsert_VersionAntigua_NoSobrescribeLaMasReciente() {
        // Guardia de versión del upsert: gana el dato MÁS reciente, no el último en llegar.
        ProductoRead nueva = Producto(7, "Mas reciente");
        await _repo.UpsertAsync(nueva);

        ProductoRead vieja = Producto(7, "Mas antigua");
        vieja.UpdatedAt = nueva.UpdatedAt.AddHours(-2);
        await _repo.UpsertAsync(vieja);

        ProductoRead? resultado = await _repo.FindByIdAsync(7);

        resultado!.Nombre.Should().Be("Mas reciente",
            "un update retrasado no debe pisar uno más nuevo");
    }

    [Test]
    public async Task SoftDelete_DejaDeSerVisiblePorId_Y_SaleDelListadoPorDefecto() {
        await _repo.UpsertAsync(Producto(7));
        await _repo.SoftDeleteAsync(7);

        (await _repo.FindByIdAsync(7)).Should().BeNull(
            "FindById filtra IsDeleted, como el HasQueryFilter de EF en PG");

        // El listado por defecto (IsDeleted=false) ya no lo ve…
        (IEnumerable<ProductoRead> visibles, int totalVisibles) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto(null, null, null, null, null));
        totalVisibles.Should().Be(0, "el soft-delete global excluye los borrados");

        // …pero pidiéndolo explícitamente sí aparece, marcado.
        (IEnumerable<ProductoRead> borrados, int totalBorrados) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto(null, null, true, null, null));
        totalBorrados.Should().Be(1);
        borrados.Should().ContainSingle(p => p.Id == 7 && p.IsDeleted);
    }

    // ------------------------------------------------- búsquedas paginadas

    [Test]
    public async Task FindAllPaged_SinFiltros_DevuelveTodo() {
        await _repo.UpsertAsync(Producto(1));
        await _repo.UpsertAsync(Producto(2, "Camiseta Nike"));
        await _repo.UpsertAsync(Producto(3, "Clean Code"));

        (IEnumerable<ProductoRead> items, int total) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto(null, null, null, null, null));

        total.Should().Be(3);
        items.Should().HaveCount(3);
    }

    [Test]
    public async Task FindAllPaged_FiltroDeSoftDelete_PorDefecto_ExcluyeEliminados() {
        await _repo.UpsertAsync(Producto(1, "Vivo"));
        await _repo.UpsertAsync(Producto(2, "Borrado"));
        await _repo.SoftDeleteAsync(2);

        // IsDeleted = null → el repo aplica el mismo filtro global que EF.
        (IEnumerable<ProductoRead> items, int total) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto(null, null, null, null, null));

        total.Should().Be(1, "el soft-delete global es el comportamiento por defecto");
        items.Should().ContainSingle().Which.Id.Should().Be(1);

        // Explícito → se respeta la petición.
        (IEnumerable<ProductoRead> todos, int totalTodos) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto(null, null, true, null, null));
        totalTodos.Should().Be(1, "pidiendo isDeleted=true solo sale el borrado");
        todos.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Test]
    public async Task FindAllPaged_FiltrosDeContenido_Y_Paginacion() {
        await _repo.UpsertAsync(Producto(1));
        await _repo.UpsertAsync(Producto(2, "Laptop Lenovo ThinkPad"));
        await _repo.UpsertAsync(Producto(3, "Camiseta Nike"));

        (IEnumerable<ProductoRead> conLaptop, int totalLaptop) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto("Laptop", null, false, null, null));
        totalLaptop.Should().Be(2, "el LIKE es sensible a mayúsculas: casa dentro del nombre");
        conLaptop.Should().OnlyContain(p => p.Nombre.Contains("Laptop"));

        (IEnumerable<ProductoRead> porCategoria, int totalCategoria) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto(null, "Electrónica", false, null, null));
        totalCategoria.Should().Be(3, "los tres productos de la semilla son de Electrónica");

        (IEnumerable<ProductoRead> pagina, int totalPagina) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto(null, null, false, null, null, 1, 2));
        totalPagina.Should().Be(3, "el total es SIEMPRE el del filtro, no el de la página");
        pagina.Should().HaveCount(1, "página 1 (base 0) con size 2 deja 1 item de 3");
    }

    [Test]
    public async Task FindAllPaged_PrecioMax_Y_StockMin() {
        await _repo.UpsertAsync(Producto(1, "Laptop"));
        await _repo.UpsertAsync(Producto(2, "Camiseta"));

        ProductoRead? camiseta = await _repo.FindByIdAsync(2);
        camiseta!.Precio = 29.99m;
        camiseta.Stock = 50;
        await _repo.UpsertAsync(camiseta);

        (IEnumerable<ProductoRead> items, int total) = await _repo.FindAllPagedAsync(
            new ProductoFilterDto(null, null, false, 100m, 10));

        total.Should().Be(1, "solo la Camiseta cumple precio ≤ 100 y stock ≥ 10");
        items.Single().Id.Should().Be(2);
    }

    // -------------------------------------------------- filtro por categoría

    [Test]
    public async Task FindByCategoriaId_SoloDevuelveLosDeEsaCategoria() {
        ProductoRead a = Producto(1);
        a.CategoriaId = 1;
        await _repo.UpsertAsync(a);

        ProductoRead b = Producto(2, "Camiseta Nike");
        b.CategoriaId = 2;
        b.Categoria = new CategoriaRead { Id = 2, Nombre = "Ropa" };
        await _repo.UpsertAsync(b);

        var resultado = (await _repo.FindByCategoriaIdAsync(2)).ToList();

        resultado.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Test]
    public async Task GetRecentlyCreatedAsync_SoloDevuelveLosDelRango() {
        ProductoRead viejo = Producto(1, "Viejo");
        viejo.CreatedAt = DateTime.UtcNow.AddDays(-30);
        await _repo.UpsertAsync(viejo);

        await _repo.UpsertAsync(Producto(2, "Nuevo"));

        var recientes = (await _repo.GetRecentlyCreatedAsync(7)).ToList();

        recientes.Should().ContainSingle(p => p.Id == 2, "el de hace 30 días queda fuera de 7 días");
    }

    // ------------------------------------- sincronización del nombre de categoría

    [Test]
    public async Task UpdateCategoriaNombreAsync_TocaSoloLosDeEsaCategoria() {
        ProductoRead a = Producto(1);
        a.CategoriaId = 1;
        await _repo.UpsertAsync(a);

        ProductoRead b = Producto(2, "Camiseta");
        b.CategoriaId = 2;
        b.Categoria = new CategoriaRead { Id = 2, Nombre = "Ropa" };
        await _repo.UpsertAsync(b);

        await _repo.UpdateCategoriaNombreAsync(1, "Electrónica y gadgets", "renombrada");

        ProductoRead? leido = await _repo.FindByIdAsync(1);
        leido!.Categoria.Nombre.Should().Be("Electrónica y gadgets");
        leido.Categoria.Descripcion.Should().Be("renombrada");
        leido.SyncAt.Should().NotBe(default, "la sincronización estampa SyncAt");

        ProductoRead? otro = await _repo.FindByIdAsync(2);
        otro!.Categoria.Nombre.Should().Be("Ropa", "los de otra categoría no se tocan");
    }

    // ------------------------------------- sincronización del nombre de proveedor

    [Test]
    public async Task UpdateProveedorNombreAsync_TocaSoloLosDeEseProveedor() {
        ProductoRead a = Producto(1);
        a.ProveedorId = "PROV1234567";
        a.Proveedor = new ProveedorRead { Codigo = "PROV1234567", Nombre = "Viejo", Descripcion = "desc vieja" };
        await _repo.UpsertAsync(a);

        ProductoRead b = Producto(2, "Camiseta");
        b.ProveedorId = "PROV7654321";
        b.Proveedor = new ProveedorRead { Codigo = "PROV7654321", Nombre = "Otro", Descripcion = null };
        await _repo.UpsertAsync(b);

        await _repo.UpdateProveedorNombreAsync("PROV1234567", "Proveedor Nuevo", "nueva descripción");

        ProductoRead? leido = await _repo.FindByIdAsync(1);
        leido!.Proveedor.Nombre.Should().Be("Proveedor Nuevo");
        leido.Proveedor.Descripcion.Should().Be("nueva descripción");
        leido.SyncAt.Should().NotBe(default, "la sincronización estampa SyncAt");

        ProductoRead? otro = await _repo.FindByIdAsync(2);
        otro!.Proveedor.Nombre.Should().Be("Otro", "los de otro proveedor no se tocan");
    }
}
