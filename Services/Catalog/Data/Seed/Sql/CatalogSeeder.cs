using Microsoft.EntityFrameworkCore;
using TiendaApi.Api.Models;

namespace TiendaApi.Api.Data.Seed.Sql;

/// <summary>
///     Seeder para datos iniciales de <c>catalog_db</c>: categorías, proveedores y
///     productos de ejemplo.
///     🎓 Los usuarios NO se siembran aquí: pertenecen a <c>identity_db</c> y los
///     crea el seeder de identity (R5 · una BD por microservicio). Los IDs de
///     categoría son fijos (1, 2, 3) porque la semilla de productos los referencia
///     y los tests E2E cuentan con ellos.
///     Copiado del <c>SqlSeeder</c> del monolito y podado de los usuarios.
/// </summary>
public class CatalogSeeder {
    private readonly CatalogDbContext _context;
    private readonly ILogger<CatalogSeeder> _logger;

    /// <summary>
    ///     Crea una instancia del seeder de PostgreSQL de catalog.
    /// </summary>
    /// <param name="context">Contexto de base de datos.</param>
    /// <param name="logger">Logger de la aplicación.</param>
    public CatalogSeeder(CatalogDbContext context, ILogger<CatalogSeeder> logger) {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    ///     Sembrar datos iniciales si no existen.
    /// </summary>
    public async Task SeedAsync() {
        try {
            _logger.LogInformation("Sembrando datos iniciales en catalog_db...");

            await SeedCategoriasAsync();
            await SeedProveedoresAsync();
            await SeedProductosAsync();

            _logger.LogInformation("Datos de catalog_db sembrados correctamente");
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Error al sembrar datos en catalog_db");
        }
    }

    private async Task SeedCategoriasAsync() {
        if (await _context.Categorias.AnyAsync()) {
            _logger.LogInformation("Categorías ya presentes, semilla omitida");
            return;
        }

        var categorias = new List<Categoria> {
            new() {
                Nombre = "Electrónica",
                Descripcion = "Dispositivos electrónicos y gadgets",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new() {
                Nombre = "Ropa",
                Descripcion = "Prendas de vestir para todas las edades",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new() {
                Nombre = "Libros",
                Descripcion = "Libros de todos los géneros y temáticas",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        await _context.Categorias.AddRangeAsync(categorias);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Insertadas {Count} categorías de ejemplo", categorias.Count);
    }

    private async Task SeedProveedoresAsync() {
        if (await _context.Proveedores.AnyAsync()) {
            _logger.LogInformation("Proveedores ya presentes, semilla omitida");
            return;
        }

        // Los códigos son fijos y no aleatorios: la semilla debe ser determinista para que
        // los tests E2E puedan referenciarlos. La generación aleatoria estilo YouTube es el
        // camino por defecto cuando un cliente crea un proveedor sin enviar código.
        var proveedores = new List<Proveedor> {
            new() {
                Codigo = "TiendaDAW01",
                Nombre = "Tienda DAW Interna",
                Descripcion = "Proveedor interno para material de laboratorio",
                Contacto = "lab@tiendadaw.example",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new() {
                Codigo = "Distribuc01",
                Nombre = "Distribuciones DAW",
                Descripcion = "Distribuidor general de electrónica y periféricos",
                Contacto = "pedidos@distribucionesdaw.example",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new() {
                Codigo = "Editorial01",
                Nombre = "Editorial Código Limpio",
                Descripcion = "Librería técnica especializada",
                Contacto = "ventas@editorialcodigolimpio.example",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        await _context.Proveedores.AddRangeAsync(proveedores);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Insertados {Count} proveedores de ejemplo", proveedores.Count);
    }

    private async Task SeedProductosAsync() {
        if (await _context.Productos.AnyAsync()) {
            _logger.LogInformation("Productos ya presentes, semilla omitida");
            return;
        }

        var productos = new List<Producto> {
            new() {
                Nombre = "Laptop Dell XPS 15",
                Descripcion = "Laptop de alto rendimiento",
                Precio = 1299.99m,
                Stock = 10,
                CategoriaId = 1,
                ProveedorId = "Distribuc01",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
            },
            new() {
                Nombre = "Camiseta Nike",
                Descripcion = "Camiseta deportiva",
                Precio = 29.99m,
                Stock = 50,
                CategoriaId = 2,
                ProveedorId = "Distribuc01",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RowVersion = new byte[] { 2, 3, 4, 5, 6, 7, 8, 9 }
            },
            new() {
                Nombre = "Clean Code",
                Descripcion = "Libro de Robert C. Martin",
                Precio = 42.99m,
                Stock = 25,
                CategoriaId = 3,
                ProveedorId = "Editorial01",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                RowVersion = new byte[] { 3, 4, 5, 6, 7, 8, 9, 10 }
            }
        };

        await _context.Productos.AddRangeAsync(productos);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Insertados {Count} productos de ejemplo", productos.Count);
    }
}
