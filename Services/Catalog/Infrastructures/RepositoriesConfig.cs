using Serilog;
using TiendaApi.Api.Repositories.Categorias;
using TiendaApi.Api.Repositories.Productos;
using TiendaApi.Api.Repositories.Proveedores;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
///     Configuración de repositorios de catalog.
///     🎓 R5 · cada servicio consulta SU base de datos: aquí solo están los
///     repositorios de <c>catalog_db</c> (escritura) y el read model de MongoDB
///     (lectura). Los datos de usuarios, pedidos o históricos NO se consultan:
///     se piden por Refit al servicio que los dueña.
/// </summary>
public static class RepositoriesConfig {
    /// <summary>
    ///     Registra todos los repositorios.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración.</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddRepositories(this IServiceCollection services, IConfiguration configuration) {
        Log.Information("Registrando repositorios de catalog...");

        services.AddScoped<ICategoriaRepository, CategoriaRepository>();
        services.AddScoped<IProveedorRepository, ProveedorRepository>();
        services.AddScoped<IProductoRepository, ProductoRepository>();

        // Read model CQRS: lectura de productos desde MongoDB (productos_read).
        services.AddScoped<IProductoReadRepository, ProductoReadRepository>();

        return services;
    }
}
