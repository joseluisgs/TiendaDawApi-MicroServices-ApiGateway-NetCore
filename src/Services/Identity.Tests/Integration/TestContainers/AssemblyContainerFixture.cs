using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace TiendaApi.Tests.Integration.TestContainers;

/// <summary>
/// Contenedor PostgreSQL compartido por assembly para los tests de integración
/// del microservicio identity (una base propia por clase dentro del contenedor).
///
/// 🎓 Versión reducida del fixture del monolito: identity solo usa PostgreSQL
/// (usuarios, roles, logins) — aquí no hay Mongo ni Cassandra.
/// </summary>
[SetUpFixture]
public sealed class AssemblyContainerFixture
{
    private static PostgreSqlContainer? _postgres;

    /// <summary>Contenedor PostgreSQL compartido (arrancado una vez por assembly).</summary>
    internal static PostgreSqlContainer Postgres =>
        _postgres ?? throw new InvalidOperationException("El contenedor PostgreSQL no está arrancado.");

    /// <summary>
    /// Crea (o recrea) una base de datos PostgreSQL con el nombre indicado y
    /// devuelve la cadena de conexi��n apuntando a ella.
    /// </summary>
    internal static async Task<string> CreatePostgresDatabaseAsync(string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Database = "postgres",
        };

        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();

            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Database = databaseName,
        };
        return target.ConnectionString;
    }

    /// <summary>Elimina una base de datos PostgreSQL creada por una clase de tests.</summary>
    internal static async Task DropPostgresDatabaseAsync(string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Database = "postgres",
        };

        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Arranca el contenedor una sola vez por assembly.</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
                        .WithDatabase("identity_tests")
            .WithUsername("admin")
            .WithPassword("admin123")
            .Build();
        await _postgres.StartAsync();
    }

    /// <summary>Para el contenedor al terminar el assembly.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}
