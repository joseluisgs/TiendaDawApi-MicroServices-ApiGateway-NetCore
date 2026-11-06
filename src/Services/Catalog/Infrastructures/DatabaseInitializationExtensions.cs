using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using Serilog;
using TiendaApi.Api.Data;
using TiendaApi.Api.Data.Seed.Mongo;
using TiendaApi.Api.Data.Seed.Sql;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Extension methods para inicialización de base de datos de catalog.
///
/// 🎓 Mismo patrón que identity: el servicio nace con <c>EnsureCreated</c>
/// (sin migraciones versionadas, como el resto de microservicios) y la siembra
/// es idempotente (omite si ya hay datos). El read model de MongoDB se
/// sincroniza SIEMPRE, en desarrollo y en producción.
/// </summary>
public static class DatabaseInitializationExtensions
{
    /// <summary>
    /// Inicializa la base de datos PostgreSQL (<c>catalog_db</c>) y el read model
    /// de MongoDB.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app, bool isDevelopment)
    {
        Log.Information("🔍 Inicializando base de datos de catalog...");

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (isDevelopment)
        {
            logger.LogWarning("⚠️ [DESARROLLO] Eliminando y recreando base de datos...");
            context.Database.EnsureDeleted();

            // 🎓 Mongo con el MISMO patrón que PostgreSQL: en desarrollo se
            // elimina la BD y el seeder de abajo (read-model de productos) la
            // vuelve a poblar desde cero. Sin este paso, el seeder solo siembra
            // si está vacío y la BD acumularía productos entre corridas.
            try
            {
                var mongoDbName = configuration["MongoDbSettings:DatabaseName"] ?? "catalog_db";
                var mongoClient = scope.ServiceProvider.GetRequiredService<IMongoClient>();
                await mongoClient.DropDatabaseAsync(mongoDbName);
                logger.LogInformation("🗑️ [DESARROLLO] MongoDB '{Database}' eliminado (se siembra a continuación)", mongoDbName);
            }
            catch (Exception ex)
            {
                // Mongo puede no estar disponible aún: mismo criterio que el resto
                // de siembras Mongo, no bloquea el arranque (se reintenta en cada reinicio).
                logger.LogWarning(ex, "No se pudo reiniciar MongoDB en desarrollo");
            }
        }

        // En desarrollo recrea; en producción crea solo si falta (sin tocar datos vivos).
        await context.Database.EnsureCreatedAsync();

        // Semilla idempotente: si ya hay categorías/proveedores/productos, la omite.
        var catalogSeeder = scope.ServiceProvider.GetRequiredService<CatalogSeeder>();
        await catalogSeeder.SeedAsync();

        logger.LogInformation("✅ Base de datos de catalog lista");

        // Sincronizar el read model de productos PostgreSQL → MongoDB (dev y prod).
        // Sin esto, las queries de productos (REST/reporte) no encontrarían datos:
        // la creación por seeder no dispara los Domain Events de sincronización.
        try
        {
            var productoReadSeeder = scope.ServiceProvider.GetRequiredService<ProductoReadSeeder>();
            await productoReadSeeder.SeedAsync(isDevelopment);
        }
        catch (Exception ex)
        {
            // La siembra del read model no bloquea el arranque (mismo criterio
            // que el resto de siembras): es una proyección con log propio.
            Log.Error(ex, "Fallo al sembrar el read model de productos en MongoDB");
        }
    }
}
