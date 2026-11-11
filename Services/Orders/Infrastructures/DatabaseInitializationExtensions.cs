using MongoDB.Driver;
using Serilog;
using TiendaApi.Api.Data.Seed.Mongo;
using TiendaApi.Api.Models;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Inicialización de la base de datos de <b>orders</b> al arrancar.
///
/// 🎓 Mismo comportamiento que el monolito: si <c>orders_db</c> está vacía se
/// siembran los pedidos de ejemplo. Sin ellos, el E2E de paginación no puede
/// comprobar el header <c>Link</c> (necesita más de una página) y
/// <c>GET /api/v1/pedidos</c> devolvería una lista vacía.
///
/// Idempotente: si ya hay pedidos, no se vuelve a sembrar.
/// </summary>
public static class DatabaseInitializationExtensions {
    /// <summary>
    /// Siembra <c>orders_db</c> si está vacía.
    /// </summary>
    /// <param name="app">Aplicación en marcha.</param>
    public static async Task InitializeDatabaseAsync(this WebApplication app) {
        try {
            using var scope = app.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<MongoDbSeeder>();
            await seeder.SeedAsync();
        } catch (Exception ex) {
            // Mismo criterio que el monolito: el sembrado no debe tumbar el arranque
            // (si MongoDB no está, /health lo dirá).
            Log.Warning(ex, "No se pudo sembrar orders_db (continuando)");
        }
    }
}
