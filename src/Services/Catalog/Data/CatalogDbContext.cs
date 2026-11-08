using Microsoft.EntityFrameworkCore;
using TiendaApi.Api.Data.Interceptors;
using TiendaApi.Api.Models;

namespace TiendaApi.Api.Data;

/// <summary>
/// DbContext de Entity Framework Core para PostgreSQL (<c>catalog_db</c>).
///
/// 🎓 R5 · una BD por microservicio: este contexto gestiona SOLO las entidades
/// del catálogo (categorías, productos, proveedores) y la marca de agua de la
/// réplica. Los usuarios viven en <c>identity_db</c> (otro servicio, otra BD):
/// aquí NO hay Identity, ni logins externos, ni pedidos — la relación con
/// usuarios se resuelve por Refit a <c>GET /internal/users/{id}</c>.
///
/// Copiado del <c>TiendaDbContext</c> del monolito y podado de lo ajeno.
/// </summary>
public class CatalogDbContext : DbContext
{
    private static readonly TimestampInterceptor _timestampInterceptor = new();

    /// <summary>
    /// Constructor con opciones de configuración.
    /// </summary>
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    /// <summary>DbSet de Categorías.</summary>
    public DbSet<Categoria> Categorias { get; set; } = null!;

    /// <summary>DbSet de Productos.</summary>
    public DbSet<Producto> Productos { get; set; } = null!;

    /// <summary>DbSet de Proveedores.</summary>
    public DbSet<Proveedor> Proveedores { get; set; } = null!;

    /// <summary>DbSet de marcas de agua para la réplica de productos (job reparador).</summary>
    public DbSet<ReplicaMarca> ReplicaMarcas { get; set; } = null!;

    /// <summary>
    /// Configura el contexto y registra el interceptor de marcas de tiempo.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(_timestampInterceptor);
    }

    /// <summary>
    /// Define el modelo de entidades, tablas, índices y filtros de consulta.
    /// Idéntico al del monolito para las entidades de catálogo (mismas tablas,
    /// mismos índices, mismos filtros de soft-delete): el cliente no nota el reparto.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Categoria>(entity =>
        {
            entity.ToTable("categorias");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).UseIdentityAlwaysColumn();
            entity.Property(c => c.Nombre).IsRequired().HasMaxLength(100);
            entity.Property(c => c.Descripcion).HasMaxLength(500);
            entity.HasIndex(c => c.Nombre).IsUnique();
            entity.Property(c => c.IsDeleted).HasDefaultValue(false);
            entity.ConfigureTimestamps();
            entity.HasQueryFilter(c => !c.IsDeleted);
        });

        modelBuilder.Entity<Producto>(entity =>
        {
            entity.ToTable("productos");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).UseIdentityAlwaysColumn();
            entity.Property(p => p.Nombre).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Descripcion).HasMaxLength(1000);
            entity.Property(p => p.Precio).HasPrecision(10, 2);
            entity.Property(p => p.Stock).IsRequired();
            entity.Property(p => p.IsDeleted).HasDefaultValue(false);
            entity.Property(p => p.RowVersion).IsRequired();
            entity.ConfigureTimestamps();
            entity.HasOne(p => p.Categoria)
                .WithMany(c => c.Productos)
                .HasForeignKey(p => p.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(p => p.CategoriaId);

            // Proveedor opcional: la FK es nullable y Restrict evita borrar un proveedor
            // que todavía tiene productos asignados.
            entity.HasOne(p => p.Proveedor)
                .WithMany(pr => pr.Productos)
                .HasForeignKey(p => p.ProveedorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(p => p.ProveedorId);
            entity.HasIndex(p => new { p.CategoriaId, p.Precio });
            entity.HasIndex(p => p.CreatedAt);
            entity.HasIndex(p => p.IsDeleted);
            entity.HasQueryFilter(p => !p.IsDeleted);
        });

        modelBuilder.Entity<Proveedor>(entity =>
        {
            entity.ToTable("proveedores");
            entity.HasKey(p => p.Codigo);

            // La clave es un string generado por la aplicación en el momento de añadir la
            // entidad. ValueGeneratedOnAdd es obligatorio para claves string: sin él EF Core
            // no invoca al generador y rechaza la entidad por clave nula.
            entity.Property(p => p.Codigo)
                .HasMaxLength(IdGenerators.IdYouTube.Longitud)
                .ValueGeneratedOnAdd()
                .HasValueGenerator<IdGenerators.YouTubeIdValueGenerator>();

            entity.Property(p => p.Nombre).IsRequired().HasMaxLength(100);
            entity.Property(p => p.Descripcion).HasMaxLength(500);
            entity.Property(p => p.Contacto).HasMaxLength(200);
            entity.Property(p => p.IsDeleted).HasDefaultValue(false);
            entity.HasIndex(p => p.Nombre).IsUnique();
            entity.ConfigureTimestamps();
            entity.HasQueryFilter(p => !p.IsDeleted);
        });

        modelBuilder.Entity<ReplicaMarca>(entity =>
        {
            entity.ToTable("replica_marcas");
            entity.HasKey(r => r.Nombre);
            entity.Property(r => r.Nombre).IsRequired().HasMaxLength(50);
            entity.Property(r => r.UltimaPasada).IsRequired();
        });
    }
}
