using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using Serilog;
using TiendaApi.Api.Data;
using TiendaApi.Api.Data.Seed.Mongo;
using TiendaApi.Api.Data.Seed.Sql;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
///     Configuración de bases de datos de catalog (PostgreSQL + MongoDB).
///     🎓 R5 · una BD por microservicio:
///     - <b>PostgreSQL</b> → <c>catalog_db</c>: fuente de verdad (categorías, productos,
///     proveedores). Solo escribe aquí.
///     - <b>MongoDB</b> → <c>catalog_db.productos_read</c>: read model CQRS (proyección
///     de los productos). Base de datos PROPIA: no comparte con <c>orders</c> (que
///     tendrá la suya) ni con el monolito.
///     Sin Cassandra (las series viven en historical, se consumen por Refit).
/// </summary>
public static class DatabaseConfig {
    /// <summary>
    ///     Configura PostgreSQL y MongoDB según configuración.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración de la app.</param>
    /// <param name="environment">Entorno (producción exige configuración explícita).</param>
    /// <returns>IServiceCollection para encadenar.</returns>
    public static IServiceCollection AddDatabases(this IServiceCollection services, IConfiguration configuration,
        IWebHostEnvironment environment) {
        Log.Information("Configurando PostgreSQL (catalog_db)...");

        // ⚠️ Fail-fast: en producción, si falta la configuración, la app NO debe arrancar.
        // En desarrollo, se usan los fallbacks para poder trabajar localmente.
        string connectionString = ResolverCadenaObligatoria(
            configuration.GetConnectionString("DefaultConnection"),
            "Host=localhost;Database=catalog_db;Username=admin;Password=admin123",
            "ConnectionStrings:DefaultConnection",
            environment);

        services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(connectionString));

        // MongoDB: el cliente y la base de datos se registran SIEMPRE (read model).
        Log.Information("Configurando MongoDB (cliente + base de datos de catalog)...");
        string mongoConnectionString = ResolverCadenaObligatoria(
            configuration["MongoDbSettings:ConnectionString"],
            "mongodb://admin:admin123@localhost:27017/catalog_db?authSource=admin",
            "MongoDbSettings:ConnectionString",
            environment);
        string mongoDatabaseName = configuration["MongoDbSettings:DatabaseName"] ?? "catalog_db";

        services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
        services.AddSingleton(sp => {
            IMongoClient client = sp.GetRequiredService<IMongoClient>();
            return client.GetDatabase(mongoDatabaseName);
        });

        Log.Information("Registrando seeders...");
        services.AddScoped<CatalogSeeder>();
        services.AddScoped<ProductoReadSeeder>();

        return services;
    }

    /// <summary>
    ///     Resuelve una cadena de conexión obligatoria sin repetir el mismo
    ///     bloque if/else en cada almacén: en desarrollo cae al default local con
    ///     warning; en producción, si falta, lanza (fail-fast: la app NO arranca
    ///     con credenciales por defecto).
    /// </summary>
    /// <param name="valor">Valor de configuración (puede venir nulo/vacío).</param>
    /// <param name="fallbackDesarrollo">Default local solo para Development.</param>
    /// <param name="claveConfig">Nombre de la clave, para los mensajes (mismos textos que antes).</param>
    /// <param name="environment">Entorno actual.</param>
    private static string ResolverCadenaObligatoria(
        string? valor, string fallbackDesarrollo, string claveConfig, IWebHostEnvironment environment) {
        if (!string.IsNullOrEmpty(valor)) {
            return valor;
        }

        if (environment.IsDevelopment()) {
            Log.Warning("⚠️ Usando credenciales por defecto de desarrollo ({Clave} no definida)", claveConfig);
            return fallbackDesarrollo;
        }

        throw new InvalidOperationException(
            $"{claveConfig} no está definida. " +
            "En producción es obligatorio configurarla (appsettings.json o variables de entorno).");
    }
}
