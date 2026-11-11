using Serilog;
using TiendaApi.Api.Repositories.Pedidos;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
///     Configuración de repositorios de <b>orders</b>.
///     🎓 R5 — cada servicio consulta SU base de datos: aquí solo está el repositorio
///     de <c>orders_db</c> (MongoDB). Los datos de usuarios y de productos NO se
///     consultan: se piden por Refit a los servicios que los dueñan (identity, catalog).
/// </summary>
public static class RepositoriesConfig {
    /// <summary>
    ///     Registra los repositorios de pedidos.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración.</param>
    /// <returns>IServiceCollection.</returns>
    public static IServiceCollection AddRepositories(this IServiceCollection services, IConfiguration configuration) {
        Log.Information("Registrando repositorios de orders (MongoDB)...");

        // Implementación nativa con MongoDB Driver (la del monolito).
        services.AddScoped<IPedidosRepository, PedidosNativeRepository>();

        return services;
    }
}
