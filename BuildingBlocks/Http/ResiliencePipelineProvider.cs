using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Polly;

namespace BuildingBlocks.Http;

/// <summary>
/// Cachea los pipelines de resiliencia <b>por autoridad</b> (esquema://host:puerto)
/// — el equivalente al <c>SelectPipelineByAuthority</c> de los docs de Polly.
///
/// 🎓 Por qué por autoridad y no uno global: el cortacircuitos mide la salud de un
/// backend concreto. Con un único pipeline compartido, si catalog cae, el circuito
/// saltaría también para las llamadas a identity (sanos) y statistics empezaría a
/// fallar contra todos. Cada destino tiene su propio circuito.
///
/// Singleton: el estado del circuito debe sobrevivir a la rotación de cadenas de
/// handlers de <c>IHttpClientFactory</c> (~10 min), si no se resetearía solo.
/// </summary>
public sealed class ResiliencePipelineProvider
{
    private readonly ConcurrentDictionary<string, Pipelines> _cache = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeSpan? _delay;

    /// <summary>
    /// Crea el proveedor. <paramref name="delay"/> solo para tests (backoff instantáneo).
    /// </summary>
    public ResiliencePipelineProvider(ILoggerFactory loggerFactory, TimeSpan? delay = null)
    {
        _loggerFactory = loggerFactory;
        _delay = delay;
    }

    /// <summary>
    /// Devuelve los pipelines (lectura/escritura) del destino, creándolos a la
    /// primera llamada. Cada destino comparte su circuito entre todos los clients
    /// del servicio que apunten a él.
    /// </summary>
    public Pipelines Obtener(Uri? destino)
    {
        // Guarda contra URIs relativas (en teoría HttpClient ya resuelve el
        // BaseAddress antes de la cadena de handlers, pero si acaso llegara una
        // relativa, Scheme lanzaría): cae al bucket compartido "desconocido".
        var clave = destino is { IsAbsoluteUri: true } d
            ? d.Scheme + "://" + d.Authority
            : "desconocido";
        return _cache.GetOrAdd(clave, k =>
        {
            // Categoría de log con la autoridad: se ve qué backend está fallando.
            var logger = _loggerFactory.CreateLogger($"Polly.Refit[{k}]");
            return new Pipelines(
                RefitResilience.ConstruirLectura(logger, _delay),
                RefitResilience.ConstruirEscritura(logger, _delay));
        });
    }
}

/// <summary>Par de pipelines de un destino: la política por verbo decide cuál usar.</summary>
public sealed record Pipelines(
    ResiliencePipeline<HttpResponseMessage> Lectura,
    ResiliencePipeline<HttpResponseMessage> Escritura);
