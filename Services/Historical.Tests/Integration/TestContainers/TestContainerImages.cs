namespace TiendaApi.Tests.Integration.TestContainers;

/// <summary>
///     Imágenes Docker fijadas para los tests con Testcontainers.
///     Deben ser EXACTAMENTE la misma tag que usa docker-compose.local.yml
///     (cassandra:5.0). Si cada vía usa una tag distinta, Docker acumula entradas
///     duplicadas en `docker images` y se descargan más bytes de los necesarios.
/// </summary>
public static class TestContainerImages {
    /// <summary>Tag unificada con docker-compose.*.yml.</summary>
    public const string Cassandra = "cassandra:5.0";
}
