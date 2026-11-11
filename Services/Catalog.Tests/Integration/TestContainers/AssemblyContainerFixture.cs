using MongoDB.Driver;
using Npgsql;
using NUnit.Framework;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;

namespace TiendaApi.Tests.Integration.TestContainers;

/// <summary>
/// Contenedores PostgreSQL y MongoDB compartidos por assembly para los tests de
/// integración del microservicio catalog (una base propia por clase).
///
/// 🎓 catalog usa DOS almacenes (R5): PostgreSQL <c>catalog_db</c> (fuente de
/// verdad, escrituras) y MongoDB <c>productos_read</c> (read model, lecturas) —
/// por eso el fixture levanta los dos, con las MISMAS tags que
/// <c>docker-compose.local.yml</c> (<see cref="TestContainerImages"/>).
/// </summary>
[SetUpFixture]
public sealed class AssemblyContainerFixture {
    private static PostgreSqlContainer? _postgres;
    private static MongoDbContainer? _mongo;

    /// <summary>Contenedor PostgreSQL compartido (arrancado una vez por assembly).</summary>
    internal static PostgreSqlContainer Postgres =>
        _postgres ?? throw new InvalidOperationException("El contenedor PostgreSQL no está arrancado.");

    /// <summary>Contenedor MongoDB compartido (arrancado una vez por assembly).</summary>
    internal static MongoDbContainer Mongo =>
        _mongo ?? throw new InvalidOperationException("El contenedor MongoDB no está arrancado.");

    /// <summary>
    /// Crea (o recrea) una base de datos PostgreSQL con el nombre indicado y
    /// devuelve la cadena de conexión apuntando a ella.
    /// </summary>
    internal static async Task<string> CreatePostgresDatabaseAsync(string databaseName) {
        var admin = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) {
            Database = "postgres",
        };

        await using (var connection = new NpgsqlConnection(admin.ConnectionString)) {
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();

            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) {
            Database = databaseName,
        };
        return target.ConnectionString;
    }

    /// <summary>Elimina una base de datos PostgreSQL creada por una clase de tests.</summary>
    internal static async Task DropPostgresDatabaseAsync(string databaseName) {
        var admin = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) {
            Database = "postgres",
        };

        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Devuelve una base de MongoDB propia para la clase indicada (se limpia al
    /// entrar: los tests no deben heredar datos entre sí).
    /// </summary>
    internal static IMongoDatabase GetMongoDatabase(string databaseName) {
        var client = new MongoClient(Mongo.GetConnectionString());
        client.DropDatabase(databaseName);
        return client.GetDatabase(databaseName);
    }

    /// <summary>Arranca los contenedores una sola vez por assembly.</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetup() {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("catalog_tests")
            .WithUsername("admin")
            .WithPassword("admin123")
            .Build();
        await _postgres.StartAsync();

        _mongo = new MongoDbBuilder(TestContainerImages.Mongo)
            .WithUsername("admin")
            .WithPassword("admin123")
            .Build();
        await _mongo.StartAsync();
    }

    /// <summary>Para los contenedores al terminar el assembly.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown() {
        if (_mongo is not null)
            await _mongo.DisposeAsync();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}
