using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TiendaApi.Api.Data;
using TiendaApi.Api.Data.Seed.Sql;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Categorias;
using TiendaApi.Api.Repositories.Productos;

namespace TiendaApi.Tests.Integration.TestContainers.Catalog.Repositories;

/// <summary>
///     Tests de integración de los repositorios de PostgreSQL con la semilla real.
///     🎓 Recorren el camino de escritura de catalog: <see cref="CatalogSeeder" />
///     (mismo SQL que en producción) y después CRUD sobre el mismo contexto.
///     ⚠️ Cada test arranca con una BD PROPIA (<see cref="Setup" />): los tests
///     escriben (altas, imports, soft-delete) y sin aislamiento se contaminarían
///     entre sí — NUnit reutiliza la instancia de la clase.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CatalogRepositoryIntegrationTests {
    [OneTimeSetUp]
    public async Task OneTimeSetUp() {
        // Una sola BD por clase: el DROP/CREATE en cada test rompería el pool de
        // Npgsql (DROP ... WITH (FORCE) mata conexiones vividas → "transient failure").
        _connectionString = await AssemblyContainerFixture.CreatePostgresDatabaseAsync(DatabaseName);

        await using CatalogDbContext context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    [SetUp]
    public async Task Setup() {
        // Estado limpio y determinista por test: TRUNCATE + RESTART IDENTITY (la
        // secuencia vuelve a 1, así la semilla recupera los IDs fijos 1, 2 y 3)
        // y después la MISMA semilla que usa el servicio al arrancar.
        await using CatalogDbContext context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE productos, replica_marcas, categorias, proveedores RESTART IDENTITY CASCADE");

        var seeder = new CatalogSeeder(context, NullLogger<CatalogSeeder>.Instance);
        await seeder.SeedAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() {
        await AssemblyContainerFixture.DropPostgresDatabaseAsync(DatabaseName);
    }

    private const string DatabaseName = "it_catalog_repo";
    private string _connectionString = string.Empty;

    private CatalogDbContext CreateContext() {
        DbContextOptions<CatalogDbContext> options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new CatalogDbContext(options);
    }

    // ------------------------------------------------------------- categorías

    [Test]
    public async Task Seeded_Categorias_ExisteLaSemillaDeterminista() {
        await using CatalogDbContext context = CreateContext();
        var repo = new CategoriaRepository(context, NullLogger<CategoriaRepository>.Instance);

        IEnumerable<Categoria> todas = await repo.FindAllAsync();

        // IDs fijos: el E2E (y la semilla de productos) cuentan con 1, 2 y 3.
        todas.Should().HaveCount(3, "la semilla inserta Electrónica, Ropa y Libros");
        todas.Select(c => c.Id).Should().BeEquivalentTo(new long[] { 1, 2, 3 });
        todas.Should().NotContain(c => c.IsDeleted);
    }

    [Test]
    public async Task CreateAsync_Luego_ExistePorIdYporNombre() {
        await using CatalogDbContext context = CreateContext();
        var repo = new CategoriaRepository(context, NullLogger<CategoriaRepository>.Instance);

        Categoria creada = await repo.CreateAsync(new Categoria {
            Nombre = "Deportes",
            Descripcion = "Material deportivo",
            IsDeleted = false
        });

        creada.Id.Should().BeGreaterThan(0, "la columna es identidad: PG asigna el ID");
        (await repo.ExistsByIdAsync(creada.Id)).Should().BeTrue();
        (await repo.ExistsByNombreAsync("Deportes")).Should().BeTrue();
        (await repo.ExistsByNombreAsync("Deportes", creada.Id)).Should()
            .BeFalse("al excluir su propio ID, el nombre queda libre");

        // Soft delete: el query filter de EF la oculta para el repositorio…
        (await repo.DeleteAsync(creada.Id)).Should().BeTrue();
        (await repo.ExistsByIdAsync(creada.Id)).Should().BeFalse(
            "el HasQueryFilter(!IsDeleted) la oculta tras el borrado lógico");

        // …pero la fila sigue en la tabla (no es un delete físico).
        context.Categorias.IgnoreQueryFilters()
            .Should().Contain(c => c.Id == creada.Id && c.IsDeleted,
                "el delete es lógico: la fila persiste marcada");
    }

    [Test]
    public async Task FindAllPagedAsync_FiltraPorNombre_YDevuelveTotal() {
        await using CatalogDbContext context = CreateContext();
        var repo = new CategoriaRepository(context, NullLogger<CategoriaRepository>.Instance);

        (IEnumerable<Categoria> items, int total) = await repo.FindAllPagedAsync(
            new CategoriaFilterDto { Nombre = "Ropa", Page = 0, Size = 10 });

        total.Should().Be(1, "solo 'Ropa' casa con el filtro");
        items.Should().ContainSingle().Which.Nombre.Should().Be("Ropa");
    }

    // -------------------------------------------------------------- productos

    [Test]
    public async Task Seeded_Productos_SonLosTresConSusFks() {
        await using CatalogDbContext context = CreateContext();
        var repo = new ProductoRepository(context, NullLogger<ProductoRepository>.Instance);

        var todos = (await repo.FindAllAsync()).ToList();

        todos.Should().HaveCount(3);
        todos.Should().OnlyContain(p => !p.IsDeleted);
        todos.Should().OnlyContain(p => p.CategoriaId == 1 || p.CategoriaId == 2 || p.CategoriaId == 3,
            "la FK de categoría debe apuntar a la semilla");
        todos.Should().OnlyContain(p => p.ProveedorId == "Distribuc01" || p.ProveedorId == "Editorial01");
        todos.Select(p => p.Precio).Should().Contain(1299.99m, "la semilla fija el precio de la laptop");
    }

    [Test]
    public async Task FindByIdsAsync_DevuelveSoloLosQueExisten_YConLaCategoriaCargada() {
        await using CatalogDbContext context = CreateContext();
        var repo = new ProductoRepository(context, NullLogger<ProductoRepository>.Instance);

        var encontrados = (await repo.FindByIdsAsync(new long[] { 1, 2, 999_999 })).ToList();

        encontrados.Should().HaveCount(2, "el ID inexistente no aparece (sin excepción)");
        encontrados.Should().OnlyContain(p => p.Categoria != null,
            "el Include evita una recarga por item al descontar stock");
    }

    [Test]
    public async Task UpdateAsync_CambiandoStock_PersisteElCambio() {
        await using CatalogDbContext context = CreateContext();
        var repo = new ProductoRepository(context, NullLogger<ProductoRepository>.Instance);

        Producto? producto = await repo.FindByIdAsync(1);
        producto.Should().NotBeNull();
        int stockAnterior = producto!.Stock;

        producto.Stock = stockAnterior + 5;
        await repo.UpdateAsync(producto);

        Producto? recargado = await repo.FindByIdAsync(1);
        recargado!.Stock.Should().Be(stockAnterior + 5, "el cambio debe sobrevivir al SaveChanges");
    }

    [Test]
    public async Task FindAllPagedAsync_PrecioMax_Y_SoftDeleteGlobal() {
        await using CatalogDbContext context = CreateContext();
        var repo = new ProductoRepository(context, NullLogger<ProductoRepository>.Instance);

        (IEnumerable<Producto> items, int total) = await repo.FindAllPagedAsync(
            new ProductoFilterDto(null, null, false, 100m, null));

        total.Should().Be(2, "Camiseta (29.99) y Clean Code (42.99) pasan el filtro; la laptop no");
        items.Should().OnlyContain(p => p.Precio <= 100m);
    }

    // ------------------------------------------------------- restore (backup)

    [Test]
    public async Task CrearConIdExplicito_Y_Resincronizar_LasSecuencias_NoChocanConLaSiguienteAlta() {
        await using CatalogDbContext context = CreateContext();
        var categorias = new CategoriaRepository(context, NullLogger<CategoriaRepository>.Instance);
        var productos = new ProductoRepository(context, NullLogger<ProductoRepository>.Instance);

        // Altas con ID explícito, como hace el restore de backup.
        await categorias.CrearConIdExplicitoAsync(new Categoria {
            Id = 9001,
            Nombre = "Restore",
            Descripcion = "ID explícito",
            IsDeleted = false
        });
        await categorias.ResincronizarSecuenciaAsync();

        await productos.CrearConIdExplicitoAsync(new Producto {
            Id = 9001,
            Nombre = "Producto restore",
            Descripcion = "ID explícito",
            Precio = 1m,
            Stock = 1,
            CategoriaId = 9001,
            IsDeleted = false
        });
        await productos.ResincronizarSecuenciaAsync();

        (await categorias.ExistsByIdAsync(9001)).Should().BeTrue("la alta con ID explícito debe persistir");

        // La siguiente alta SIN ID debe salir por encima de 9001 (si no, chocaría).
        Categoria nueva = await categorias.CreateAsync(new Categoria {
            Nombre = "Post-restore",
            IsDeleted = false
        });
        nueva.Id.Should().BeGreaterThan(9001,
            "sin ResincronizarSecuenciaAsync la secuencia seguiría en 9001 y el siguiente alta fallaría");
    }
}
