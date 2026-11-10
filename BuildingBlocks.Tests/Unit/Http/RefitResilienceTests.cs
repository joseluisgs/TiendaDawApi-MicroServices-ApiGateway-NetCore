using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace BuildingBlocks.Tests.Unit.Http;

/// <summary>
/// Tests educativos de la política de resiliencia Refit.
///
/// 🎓 Cubren los tres comportamientos que la auditoría exigió:
/// <list type="bullet">
/// <item><b>Idempotencia por verbo</b>: GET reintenta, escritura NO salvo
/// conexión nunca establecida (un reintento ciego duplicaría stock/emails).</item>
/// <item><b>Nunca 4xx</b>: los 404/422 que traducen orders/catalog llegan a la
/// primera, intactos.</item>
/// <item><b>Cortacircuitos</b>: tras 5 fallos consecutivos deja de llamar
/// (BrokenCircuit sin invocar el callback) en vez de seguir colgándose.</item>
/// </list>
/// Los delays se anulan (delay: TimeSpan.Zero) para que los tests sean instantáneos,
/// igual que PollyConfigTests de email.
/// </summary>
public class RefitResilienceTests
{
    // ───────────────────────── selección por verbo ─────────────────────────

    [Test]
    public void EsLectura_GetYHead_SonReintentables()
    {
        RefitResilience.EsLectura(HttpMethod.Get).Should().BeTrue();
        RefitResilience.EsLectura(HttpMethod.Head).Should().BeTrue();
    }

    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("PATCH")]
    [TestCase("DELETE")]
    public void EsLectura_Escrituras_NoSonReintentables(string metodo)
    {
        // Escritura: solo reintentaría si la conexión nunca se estableció.
        RefitResilience.EsLectura(new HttpMethod(metodo)).Should().BeFalse();
    }

    // ─────────────────── "conexión nunca establecida" ──────────────────────

    [Test]
    public void ConexionRechazada_SiEsReintentable()
    {
        var ex = new HttpRequestException(
            "Connection refused", new SocketException((int)SocketError.ConnectionRefused));

        RefitResilience.EsConexionNuncaEstablecida(ex).Should().BeTrue(
            "la petición nunca salió: reintentarla no duplica nada");
    }

    [Test]
    public void HostDesconocido_SiEsReintentable()
    {
        var ex = new HttpRequestException(
            "NXDOMAIN", new SocketException((int)SocketError.HostNotFound));

        RefitResilience.EsConexionNuncaEstablecida(ex).Should().BeTrue();
    }

    [Test]
    public void ConexionInterrumpida_NOEsReintentable()
    {
        // El servidor pudo recibir y procesar el request antes de cortar:
        // reintentar un POST podría duplicar un decremento de stock.
        var ex = new HttpRequestException(
            "reset", new SocketException((int)SocketError.ConnectionReset));

        RefitResilience.EsConexionNuncaEstablecida(ex).Should().BeFalse();
    }

    [Test]
    public void ExcepcionSinSocket_NOEsReintentable()
    {
        // Un fallo de lectura de respuesta no es "nunca salió".
        var ex = new HttpRequestException("respuesta truncada");

        RefitResilience.EsConexionNuncaEstablecida(ex).Should().BeFalse();
    }

    // ───────────────────────── qué es fallo transitorio ────────────────────

    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.BadGateway)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.RequestTimeout)]
    public void FallosTransitorios_CuentanParaReintentoYCircuito(HttpStatusCode codigo)
    {
        RefitResilience.EsFalloTransitorio(codigo).Should().BeTrue();
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.UnprocessableEntity)]
    [TestCase(HttpStatusCode.Unauthorized)]
    public void ErroresDeNegocio_NOCuentanComoFallo(HttpStatusCode codigo)
    {
        // El backend está sano: el 4xx lo decide el dominio, no Polly.
        RefitResilience.EsFalloTransitorio(codigo).Should().BeFalse();
    }

    // ─────────────────── ShouldHandle de cada política ─────────────────────

    [Test]
    public async Task Lectura_Retrae5xxYErroresDeRed_YNo4xx()
    {
        var opciones = RefitResilience.LecturaRetryOptions();

        (await Evaluar(opciones.ShouldHandle, HttpStatusCode.InternalServerError)).Should().BeTrue();
        (await Evaluar(opciones.ShouldHandle, HttpStatusCode.NotFound)).Should().BeFalse();
        (await Evaluar(opciones.ShouldHandle, new HttpRequestException("sin conexión"))).Should().BeTrue();
        (await Evaluar(opciones.ShouldHandle, new TimeoutRejectedException("colgado"))).Should().BeTrue(
            "un timeout en GET es reintentalble: no cambió estado");
        (await Evaluar(opciones.ShouldHandle, new OperationCanceledException())).Should().BeFalse(
            "una cancelación del llamante no se reintenta");
    }

    [Test]
    public async Task Escritura_SoloRetraeSiLaConexionNuncaSeEstablecio()
    {
        var opciones = RefitResilience.EscrituraRetryOptions();

        var rechazada = new HttpRequestException(
            "refused", new SocketException((int)SocketError.ConnectionRefused));
        (await Evaluar(opciones.ShouldHandle, rechazada)).Should().BeTrue();

        // 5xx y timeout en escritura: AMBIGUOS (el request pudo llegar) → no.
        (await Evaluar(opciones.ShouldHandle, HttpStatusCode.InternalServerError)).Should().BeFalse();
        (await Evaluar(opciones.ShouldHandle, new TimeoutRejectedException("colgado"))).Should().BeFalse();
    }

    /// <summary>
    /// Invoca <c>ShouldHandle</c> con un Outcome construido desde el código HTTP
    /// o la excepción indicada (la firma de Polly es RetryPredicateArguments:
    /// contexto + outcome + nº de intento).
    /// </summary>
    private static ValueTask<bool> Evaluar(
        Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> shouldHandle,
        HttpStatusCode codigo)
        => shouldHandle(new RetryPredicateArguments<HttpResponseMessage>(
            ResilienceContextPool.Shared.Get(),
            Outcome.FromResult(new HttpResponseMessage(codigo)),
            0));

    /// <inheritdoc cref="Evaluar"/>
    private static ValueTask<bool> Evaluar(
        Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> shouldHandle,
        Exception excepcion)
        => shouldHandle(new RetryPredicateArguments<HttpResponseMessage>(
            ResilienceContextPool.Shared.Get(),
            Outcome.FromException<HttpResponseMessage>(excepcion),
            0));

    // ────────────────────────── comportamiento real ────────────────────────

    [Test]
    public async Task Lectura_Retrae500_DosReintentos_AlTerceroPasa()
    {
        var pipeline = RefitResilience.ConstruirLectura(delay: TimeSpan.Zero);
        var intentos = 0;

        var respuesta = await pipeline.ExecuteAsync(_ =>
        {
            intentos++;
            return new ValueTask<HttpResponseMessage>(intentos < 3
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK));
        }, CancellationToken.None);

        intentos.Should().Be(3, "1 intento original + 2 reintentos");
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Lectura_NoToca404_UnSoloIntento()
    {
        var pipeline = RefitResilience.ConstruirLectura(delay: TimeSpan.Zero);
        var intentos = 0;

        var respuesta = await pipeline.ExecuteAsync(_ =>
        {
            intentos++;
            return new ValueTask<HttpResponseMessage>(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }, CancellationToken.None);

        intentos.Should().Be(1, "el 404 lo decide el dominio, sin reintentos");
        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Escritura_NoRetrae500_UnSoloIntento()
    {
        var pipeline = RefitResilience.ConstruirEscritura(delay: TimeSpan.Zero);
        var intentos = 0;

        var respuesta = await pipeline.ExecuteAsync(_ =>
        {
            intentos++;
            return new ValueTask<HttpResponseMessage>(
                new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }, CancellationToken.None);

        intentos.Should().Be(1, "reintentar un 500 en escritura podría duplicar el efecto");
        respuesta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Test]
    public async Task Escritura_RetraeCuandoLaConexionNuncaSeEstablecio()
    {
        var pipeline = RefitResilience.ConstruirEscritura(delay: TimeSpan.Zero);
        var intentos = 0;
        var rechazada = new HttpRequestException(
            "refused", new SocketException((int)SocketError.ConnectionRefused));

        var respuesta = await pipeline.ExecuteAsync(_ =>
        {
            intentos++;
            if (intentos < 3) throw rechazada;
            return new ValueTask<HttpResponseMessage>(new HttpResponseMessage(HttpStatusCode.OK));
        }, CancellationToken.None);

        intentos.Should().Be(3, "la petición nunca salió: reintentar es seguro");
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Escritura_NOReintentaTrasUnTimeout()
    {
        // Timeout corto para el test: el callback cuelga 1 s, el timeout corta a 50 ms.
        var pipeline = RefitResilience.ConstruirEscritura(
            delay: TimeSpan.Zero, timeout: TimeSpan.FromMilliseconds(50));
        var intentos = 0;

        var act = async () => await pipeline.ExecuteAsync(async ct =>
        {
            intentos++;
            // El callback DEBE observar el token (como hace HttpClient.SendAsync):
            // solo así el timeout puede cancelarlo y convertirse en TimeoutRejectedException.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutRejectedException>();
        intentos.Should().Be(1, "un timeout en escritura es ambiguo: sin reintento");
    }

    // ────────────────────────── cortacircuitos ─────────────────────────────

    [Test]
    public async Task Circuito_SaltaTrasCincoFallos_YDejaDeInvocarElCallback()
    {
        var pipeline = RefitResilience.ConstruirEscritura(delay: TimeSpan.Zero);
        var intentos = 0;

        Func<Task> llamar = async () => await pipeline.ExecuteAsync(_ =>
        {
            intentos++;
            return new ValueTask<HttpResponseMessage>(
                new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }, CancellationToken.None);

        // 2 llamadas × hasta 3 intentos bastan para acumular los 5 fallos.
        for (var i = 0; i < 10 && intentos < RefitResilience.FallosCircuito + 1; i++)
        {
            try { await llamar(); } catch (BrokenCircuitException) { break; }
        }

        intentos.Should().Be(RefitResilience.FallosCircuito,
            "el circuito abre exactamente en el umbral de fallos");

        var intentosAlAbrir = intentos;
        var act = llamar;
        await act.Should().ThrowAsync<BrokenCircuitException>(
            "con el circuito abierto ni se intenta llamar");
        intentos.Should().Be(intentosAlAbrir, "el callback NO se invoca con el circuito abierto");
    }

    // ────────────────────── selección por verbo en el handler ──────────────

    [Test]
    public async Task Handler_GET_UsaElPipelineDeLectura_YReintenta()
    {
        var fake = new FakeHandler(HttpStatusCode.InternalServerError);
        var handler = new ResilienceDelegatingHandler(
            new ResiliencePipelineProvider(NullLoggerFactory.Instance, TimeSpan.Zero))
        {
            InnerHandler = fake,
        };
        using var cliente = new HttpClient(handler);

        var respuesta = await cliente.GetAsync("http://catalogo-prueba/api/productos");

        respuesta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        fake.Chamadas.Should().Be(3, "GET ante 5xx: 1 intento + 2 reintentos");
    }

    [Test]
    public async Task Handler_POST_NoUsaReintentoDeLectura()
    {
        var fake = new FakeHandler(HttpStatusCode.InternalServerError);
        var handler = new ResilienceDelegatingHandler(
            new ResiliencePipelineProvider(NullLoggerFactory.Instance, TimeSpan.Zero))
        {
            InnerHandler = fake,
        };
        using var cliente = new HttpClient(handler);

        var respuesta = await cliente.PostAsync(
            "http://catalogo-prueba/api/pedidos", new StringContent("{}"));

        respuesta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        fake.Chamadas.Should().Be(1, "POST ante 5xx: sin reintentos (no idempotente)");
    }

    // ────────────────────────── proveedor por autoridad ────────────────────

    [Test]
    public void Proveedor_CacheaPorAutoridad_CircuitosAislados()
    {
        var proveedor = new ResiliencePipelineProvider(NullLoggerFactory.Instance);

        var catalogoA = proveedor.Obtener(new Uri("http://catalog:5105/api/x"));
        var catalogoB = proveedor.Obtener(new Uri("http://catalog:5105/api/y"));
        var identity = proveedor.Obtener(new Uri("http://identity:5101/api/z"));

        catalogoA.Should().BeSameAs(catalogoB, "misma autoridad → mismo circuito (cacheado)");
        identity.Should().NotBeSameAs(catalogoA, "autoridades distintas → circuitos aislados");
    }

    // ────────────────────────────── utilidades ─────────────────────────────

    /// <summary>Dev siempre 5xx y cuenta las llamadas que llegan.</summary>
    private sealed class FakeHandler(HttpStatusCode codigo) : HttpMessageHandler
    {
        public int Chamadas { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas++;
            return Task.FromResult(new HttpResponseMessage(codigo));
        }
    }
}
