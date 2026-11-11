using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace BuildingBlocks.Http;

/// <summary>
/// Políticas de resiliencia (Polly v8) para las llamadas Refit entre microservicios.
///
/// 🎓 El problema sin esto: un backend caído o colgado bloquea al cliente hasta el
/// timeout por defecto de HttpClient (<b>100 s</b>), y statistics encadena páginas
/// (peor aún). Aquí cada intento tiene su propio timeout y, según el verbo, se
/// reintenta solo lo seguro.
///
/// ⚠️ La política decide <b>por verbo HTTP de la petición</b>, no por cliente:
/// <list type="bullet">
/// <item><b>GET/HEAD → reintento 2</b> (backoff exponencial 0,25 s → 0,5 s + jitter):
/// repetir una lectura nunca cambia estado.</item>
/// <item><b>POST/PUT/DELETE → SIN reintento</b>, salvo que la conexión NUNCA se
/// haya establecido (ConnectionRefused/HostNotFound): si el request llegó al
/// servidor, el reintento puede duplicar un decremento de stock o un email.</item>
/// <item><b>4xx → nunca se reintenta</b>: es error de negocio, el backend está sano
/// (y los códigos 404/422 que traducen orders/catalog deben llegar a la primera).</item>
/// </list>
///
/// Cadena (de fuera hacia dentro, igual que <c>PollyConfig</c> de email):
/// <b>Retry → CircuitBreaker → Timeout(por intento)</b>.
/// El cortacircuitos cuenta fallos de transporte y 5xx (nunca 4xx); tras 5 fallos
/// consecutivos deja de llamar durante 15 s → las llamadas siguientes fallan al
/// instante (<c>BrokenCircuitException</c>) en vez de colgarse.
/// </summary>
public static class RefitResilience {
    /// <summary>Timeout por intento en lecturas (GET/HEAD): 10 s.</summary>
    public static readonly TimeSpan TimeoutLectura = TimeSpan.FromSeconds(10);

    /// <summary>Timeout por intento en escrituras: 5 s.</summary>
    public static readonly TimeSpan TimeoutEscritura = TimeSpan.FromSeconds(5);

    /// <summary>Cuánto tiempo deja de llamar el circuito tras saltar.</summary>
    public static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(15);

    /// <summary>Fallos consecutivos que saltan el cortacircuitos (ventana de 60 s para dar tiempo a los intentos lentos).</summary>
    public const int FallosCircuito = 5;

    /// <summary>
    /// ¿Es una llamada idempotente que admite reintento completo? Solo GET y HEAD.
    /// </summary>
    public static bool EsLectura(HttpMethod metodo)
        => metodo == HttpMethod.Get || metodo == HttpMethod.Head;

    /// <summary>
    /// ¿El fallo significa que la petición NUNCA salió de este proceso?
    /// ConnectionRefused/HostNotFound/NetworkUnreachable ocurren antes de enviar:
    /// reintentar no puede duplicar nada. Un ConnectionReset a mitad de respuesta
    /// NO cuenta (el servidor pudo procesar el request) → no se reintenta.
    /// </summary>
    public static bool EsConexionNuncaEstablecida(Exception ex)
        => ex is HttpRequestException { InnerException: SocketException se }
            && se.SocketErrorCode is SocketError.ConnectionRefused
                or SocketError.HostNotFound
                or SocketError.NetworkUnreachable;

    /// <summary>5xx y 408 = fallo transitorio del servidor (reintento seguro en lecturas; sí cuenta para el circuito).</summary>
    public static bool EsFalloTransitorio(HttpStatusCode codigo)
        => (int)codigo is >= 500 and <= 599 || codigo == HttpStatusCode.RequestTimeout;

    /// <summary>
    /// Opciones del reintento de LECTURAS: 2 reintentos con backoff exponencial.
    /// <paramref name="delay"/> a cero para tests instantáneos (patrón PollyConfig).
    /// </summary>
    public static RetryStrategyOptions<HttpResponseMessage> LecturaRetryOptions(
        ILogger? logger = null, TimeSpan? delay = null) => new() {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            Delay = delay ?? TimeSpan.FromMilliseconds(250),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .HandleResult(r => EsFalloTransitorio(r.StatusCode)),
            OnRetry = args => {
                logger?.LogWarning(
                    "Polly[refit] reintento {Attempt}/2 tras fallo - próximo intento en {Delay}",
                    args.AttemptNumber,
                    args.RetryDelay);
                return ValueTask.CompletedTask;
            },
        };

    /// <summary>
    /// Opciones del reintento de ESCRITURAS: solo si la conexión nunca se
    /// establecida (ver <see cref="EsConexionNuncaEstablecida"/>). Un timeout o
    /// un 5xx son ambiguos: el servidor pudo recibir el request → no se repite.
    /// </summary>
    public static RetryStrategyOptions<HttpResponseMessage> EscrituraRetryOptions(
        ILogger? logger = null, TimeSpan? delay = null) => new() {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            Delay = delay ?? TimeSpan.FromMilliseconds(250),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>(ex => EsConexionNuncaEstablecida(ex)),
            OnRetry = args => {
                logger?.LogWarning(
                    "Polly[refit] reintento {Attempt}/2 de ESCRITURA (conexión nunca establecida) - próximo en {Delay}",
                    args.AttemptNumber,
                    args.RetryDelay);
                return ValueTask.CompletedTask;
            },
        };

    /// <summary>
    /// Cortacircuitos compartido: 5 fallos consecutivos (transporte o 5xx, nunca 4xx)
    /// → abierto 15 s → half-open automático al reabrir (patrón PollyConfig de email).
    /// </summary>
    public static CircuitBreakerStrategyOptions<HttpResponseMessage> CircuitoOptions(
        ILogger? logger = null) => new() {
            FailureRatio = 1.0,
            MinimumThroughput = FallosCircuito,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = BreakDuration,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .HandleResult(r => EsFalloTransitorio(r.StatusCode)),
            OnOpened = args => {
                logger?.LogWarning(
                    "Polly[refit] circuito ABIERTO tras {Throughput} fallos: {BreakDuration}s sin llamar al backend",
                    FallosCircuito,
                    args.BreakDuration);
                return ValueTask.CompletedTask;
            },
            OnClosed = _ => {
                logger?.LogInformation("Polly[refit] circuito CERRADO: vuelve a permitir llamadas");
                return ValueTask.CompletedTask;
            },
        };

    /// <summary>
    /// Pipeline de lectura: Retry(2) → Circuito → Timeout(10 s por intento).
    /// </summary>
    public static ResiliencePipeline<HttpResponseMessage> ConstruirLectura(
        ILogger? logger = null, TimeSpan? delay = null, TimeSpan? timeout = null)
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(LecturaRetryOptions(logger, delay))
            .AddCircuitBreaker(CircuitoOptions(logger))
            .AddTimeout(timeout ?? TimeoutLectura)
            .Build();

    /// <summary>
    /// Pipeline de escritura: Retry(solo conexión nunca establecida) → Circuito → Timeout(5 s por intento).
    /// </summary>
    public static ResiliencePipeline<HttpResponseMessage> ConstruirEscritura(
        ILogger? logger = null, TimeSpan? delay = null, TimeSpan? timeout = null)
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(EscrituraRetryOptions(logger, delay))
            .AddCircuitBreaker(CircuitoOptions(logger))
            .AddTimeout(timeout ?? TimeoutEscritura)
            .Build();
}
