using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace TiendaApi.Tests.Integration.TestContainers;

/// <summary>
///     Contenedor MongoDB compartido por assembly para los tests de integración del
///     microservicio <b>orders</b> (una base propia por clase).
///     🎓 orders es solo MongoDB (R5): <c>orders_db</c> con la colección
///     <c>pedidos</c>. No hay PostgreSQL aquí — el stock es de <b>catalog</b>.
/// </summary>
[SetUpFixture]
public sealed class AssemblyContainerFixture {
    private static MongoDbContainer? _mongo;

    /// <summary>Contenedor MongoDB compartido (arrancado una vez por assembly).</summary>
    internal static MongoDbContainer Mongo =>
        _mongo ?? throw new InvalidOperationException("El contenedor MongoDB no está arrancado.");

    /// <summary>
    ///     Devuelve una base de MongoDB propia para la clase indicada (se limpia al
    ///     entrar: los tests no deben heredar datos entre sí).
    /// </summary>
    internal static IMongoDatabase GetMongoDatabase(string databaseName) {
        var client = new MongoClient(Mongo.GetConnectionString());
        client.DropDatabase(databaseName);
        return client.GetDatabase(databaseName);
    }

    /// <summary>Arranca el contenedor una sola vez por assembly.</summary>
    [OneTimeSetUp]
    public async Task OneTimeSetup() {
        _mongo = new MongoDbBuilder(TestContainerImages.Mongo)
            .WithUsername("admin")
            .WithPassword("admin123")
            .Build();
        await _mongo.StartAsync();
    }

    /// <summary>Para el contenedor al terminar el assembly.</summary>
    [OneTimeTearDown]
    public async Task OneTimeTearDown() {
        if (_mongo is not null) {
            await _mongo.DisposeAsync();
        }
    }
}
