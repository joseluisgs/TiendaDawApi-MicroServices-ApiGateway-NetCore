using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Serilog;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Configuración de base de datos de <b>orders</b>.
///
/// 🎓 R5 — una BD por microservicio: <c>orders_db</c> en MongoDB, con la colección
/// <c>pedidos</c>. Los documentos llevan los items DESNORMALIZADOS (nombre y precio
/// del producto al momento de la compra), igual que en el monolito.
///
/// Orders NO tiene PostgreSQL: la fuente de verdad de stock y productos es
/// <b>catalog</b> y se consulta por Refit (<c>ICatalogApi</c>), lo mismo que
/// ocurre con los usuarios en <b>identity</b>.
/// </summary>
public static class DatabaseConfig
{
    /// <summary>
    /// Configura el cliente y la base de datos de MongoDB.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración de la app.</param>
    /// <param name="environment">Entorno (producción exige configuración explícita).</param>
    /// <returns>IServiceCollection para encadenar.</returns>
    public static IServiceCollection AddDatabases(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        Log.Information("Configurando MongoDB (orders_db)...");

        // Fail-fast: en producción, si falta la configuración, la app NO debe
        // arrancar. En desarrollo se usa el fallback local con warning.
        var connectionString = ResolverCadenaObligatoria(
            configuration["MongoDbSettings:ConnectionString"],
            "mongodb://admin:admin123@localhost:27017/orders_db?authSource=admin",
            "MongoDbSettings:ConnectionString",
            environment);

        var databaseName = configuration["MongoDbSettings:DatabaseName"] ?? "orders_db";

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            return client.GetDatabase(databaseName);
        });

        // Colección de pedidos: la usa el seeder (misma inyección que el monolito).
        services.AddScoped(sp =>
            sp.GetRequiredService<IMongoDatabase>().GetCollection<Models.Pedido>("pedidos"));
        services.AddScoped<Data.Seed.Mongo.MongoDbSeeder>();

        return services;
    }

    /// <summary>
    /// Resuelve una cadena de conexión obligatoria: en desarrollo cae al default
    /// local con warning; en producción, si falta, lanza (fail-fast).
    /// </summary>
    /// <param name="valor">Valor de configuración (puede venir nulo/vacío).</param>
    /// <param name="fallbackDesarrollo">Default local solo para Development.</param>
    /// <param name="claveConfig">Nombre de la clave, para los mensajes.</param>
    /// <param name="environment">Entorno actual.</param>
    private static string ResolverCadenaObligatoria(
        string? valor, string fallbackDesarrollo, string claveConfig, IWebHostEnvironment environment)
    {
        if (!string.IsNullOrEmpty(valor))
            return valor;

        if (environment.IsDevelopment())
        {
            Log.Warning("⚠ Usando credenciales por defecto de desarrollo ({Clave} no definida)", claveConfig);
            return fallbackDesarrollo;
        }

        throw new InvalidOperationException(
            $"{claveConfig} no está definida. " +
            "En producción es obligatorio configurarla (appsettings.json o variables de entorno).");
    }
}
