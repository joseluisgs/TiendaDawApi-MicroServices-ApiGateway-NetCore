using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TiendaApi.Api.Data.Interceptors;
using TiendaApi.Api.Models;

namespace TiendaApi.Api.Data;

/// <summary>
///     Contexto de datos del microservicio <b>identity</b>: solo usuarios, roles
///     (Identity) y vinculaciones OAuth externas. Cada microservicio tiene su
///     propia base de datos y aquí NO existe nada de catálogo, pedidos ni réplicas.
///     Se mantiene el nombre <c>TiendaDbContext</c> para que los repositorios
///     copiados del monolito compilen sin cambios de firma; la clase es distinta:
///     su modelo termina en usuarios.
/// </summary>
public class TiendaDbContext : IdentityDbContext<User, IdentityRole<long>, long> {
    private static readonly TimestampInterceptor _timestampInterceptor = new();

    /// <summary>Crea el contexto con las opciones del contenedor de dependencias.</summary>
    public TiendaDbContext(DbContextOptions<TiendaDbContext> options)
        : base(options) { }

    /// <summary>Usuarios (sombrea el Users heredado de IdentityDbContext).</summary>
    public new DbSet<User> Users { get; set; } = null!;

    /// <summary>Vinculaciones OAuth externas (Google/GitHub/Microsoft).</summary>
    public DbSet<UserLogin> ExternalLogins { get; set; } = null!;

    /// <summary>Registra el interceptor que rellena CreatedAt/UpdatedAt.</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(_timestampInterceptor);
    }

    /// <summary>Mapeo de usuarios y logins externos (idéntico al monolito).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity => {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).UseIdentityAlwaysColumn();
            entity.Property(u => u.UserName).IsRequired().HasMaxLength(50);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(100);
            // PasswordHash nullable: los usuarios OAuth pueden no tener contraseña
            entity.Property(u => u.PasswordHash);
            entity.Property(u => u.Role).IsRequired().HasMaxLength(20);
            entity.Property(u => u.IsDeleted).HasDefaultValue(false);
            entity.Property(u => u.Avatar).HasMaxLength(500);
            entity.ConfigureTimestamps();
            entity.HasIndex(u => u.UserName).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.Role);
            entity.HasQueryFilter(u => !u.IsDeleted);
        });

        modelBuilder.Entity<UserLogin>(entity => {
            entity.ToTable("user_logins");
            entity.HasKey(ul => ul.Id);
            entity.Property(ul => ul.Id).UseIdentityAlwaysColumn();
            entity.Property(ul => ul.LoginProvider).IsRequired().HasMaxLength(100);
            entity.Property(ul => ul.ProviderKey).IsRequired().HasMaxLength(450);
            entity.Property(ul => ul.ProviderDisplayName).HasMaxLength(200);
            entity.ConfigureTimestamps();
            entity.HasIndex(ul => new { ul.LoginProvider, ul.ProviderKey }).IsUnique();
            entity.HasOne(ul => ul.User)
                .WithMany()
                .HasForeignKey(ul => ul.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
