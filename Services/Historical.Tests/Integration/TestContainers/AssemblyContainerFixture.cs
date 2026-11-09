using Cassandra;
using NUnit.Framework;
using Testcontainers.Cassandra;
using TiendaApi.Api.Infrastructures;

namespace TiendaApi.Tests.Integration.TestContainers;

/// <summary>
/// Contenedor Cassandra compartido por assembly, arrancado SOLO bajo demanda
/// (<see cref="GetCassandraSessionAsync"/>): es una JVM y tarda ~1 min, coste
/// que no deben pagar los tests unitarios.
///
/// La sesión se crea con el MISMO esquema de producción
/// (<c>InitializeSchemaAsync</c>: keyspace + tablas, CREATE IF NOT EXISTS sin
/// DROP) sobre el contenedor efímero.
///
/// Al estar declarada como <c>[SetUpFixture]</c> en este namespace, gobierna
/// todos los tests de <c>TiendaApi.Tests.Integration.TestContainers.*</c>.
/// </summary>
[SetUpFixture]
public sealed class AssemblyContainerFixture
{
    // NUnit1032: SÍ se disposea en el [OneTimeTearDown] DisposeAsync de este
    // fixture; el analyzer no lo empareja porque la asignación está en el
    // método lazy (mismo motivo por el que el fixture del monolito lo omite).
#pragma warning disable NUnit1032
    private static CassandraContainer? _cassandra;
    private static ISession? _cassandraSession;
#pragma warning restore NUnit1032
    private static readonly SemaphoreSlim _cassandraLock = new(1, 1);

    /// <summary>
    /// Sesión de Cassandra compartida por assembly, arrancada bajo demanda.
    /// </summary>
    /// <param name="keyspace">Keyspace a crear/usar (por defecto el de la tienda).</param>
    /// <returns>Sesión conectada al contenedor de tests.</returns>
    internal static async Task<ISession> GetCassandraSessionAsync(string keyspace = "tienda")
    {
        if (_cassandraSession is not null)
            return _cassandraSession;

        await _cassandraLock.WaitAsync();
        try
        {
            if (_cassandraSession is not null)
                return _cassandraSession;

            _cassandra = new CassandraBuilder(TestContainerImages.Cassandra)
                .WithEnvironment("MAX_HEAP_SIZE", "1G")
                .WithEnvironment("HEAP_NEWSIZE", "256M")
                .Build();

            await _cassandra.StartAsync();

            var cluster = Cluster.Builder()
                .AddContactPoints(_cassandra.Hostname)
                .WithPort(_cassandra.GetMappedPublicPort(9042))
                .Build();

            var session = cluster.Connect();
            await session.InitializeSchemaAsync(keyspace);

            _cassandraSession = session;
            return _cassandraSession;
        }
        finally
        {
            _cassandraLock.Release();
        }
    }

    /// <summary>Detiene el contenedor compartido al finalizar los tests.</summary>
    [OneTimeTearDown]
    public async Task DisposeAsync()
    {
        if (_cassandraSession is not null)
        {
            _cassandraSession.Dispose();
            _cassandraSession = null;
        }

        if (_cassandra is not null)
        {
            await _cassandra.DisposeAsync();
            _cassandra = null;
        }
    }
}
