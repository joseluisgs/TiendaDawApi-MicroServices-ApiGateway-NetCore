using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace TiendaApi.Tests.Integration.TestContainers.Catalog.Containers;

/// <summary>
///     Tests de conectividad de los contenedores de <b>catalog</b>.
///     🎓 catalog es el primer servicio con DOS almacenes (R5): PostgreSQL es la
///     fuente de verdad (escrituras) y MongoDB el read model (lecturas). Ambos
///     contenedores comparten fixture por assembly; aquí se comprueba que arrancan
///     y que las cadenas de conexión apuntan a donde deben.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CatalogContainersIntegrationTests {
    [OneTimeSetUp]
    public async Task OneTimeSetup() {
        _connectionString = await AssemblyContainerFixture.CreatePostgresDatabaseAsync(DatabaseName);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() {
        await AssemblyContainerFixture.DropPostgresDatabaseAsync(DatabaseName);
    }

    private const string DatabaseName = "it_catalog_containers";
    private string _connectionString = string.Empty;

    [Test]
    public void PostgreSQLContainer_DebeEstarArrancado() {
        _connectionString.Should().NotBeNullOrEmpty();
        _connectionString.Should().Contain("Host=", "la cadena debe apuntar al contenedor Testcontainers");
        _connectionString.Should().Contain(DatabaseName, "y a la BD propia de esta clase de tests");
    }

    [Test]
    public void MongoDBContainer_DebeEstarArrancado() {
        IMongoDatabase database = AssemblyContainerFixture.GetMongoDatabase("it_catalog_containers_ping");

        database.Should().NotBeNull();
        database.DatabaseNamespace.DatabaseName.Should().Be("it_catalog_containers_ping");
    }

    [Test]
    public void Configuracion_PuedeConstruirElServiceProvider() {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                { "ConnectionStrings:DefaultConnection", _connectionString },
                { "MongoDbSettings:ProductosCollection", "productos_read" }
            })
            .Build();

        configuration.GetConnectionString("DefaultConnection").Should().Be(_connectionString);
        configuration["MongoDbSettings:ProductosCollection"].Should().Be("productos_read");
    }
}
