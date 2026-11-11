using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TiendaApi.Tests.Integration;

/// <summary>
///     Integración del contrato POST /internal/emails con la cola REAL:
///     WebApplicationFactory arranca el servicio completo (Development →
///     MemoryEmailService + EmailBackgroundService sobre el Channel). Se verifica
///     que el endpoint responde rápido (202) y que el mensaje acaba PROCESADO por
///     la cola — la prueba de que el ciclo endpoint → cola → envío funciona.
/// </summary>
public class InternalEmailsControllerIntegrationTests {
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private RegistroLog _registro = null!;

    [SetUp]
    public void Setup() {
        _registro = new RegistroLog();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            // Capta los logs de MemoryEmailService (su "EMAIL SENT" es la
            // señal observable de que la cola procesó el mensaje).
            builder.ConfigureServices(services =>
                services.AddSingleton<ILoggerProvider>(_registro));
        });
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown() {
        _client.Dispose();
        _factory.Dispose();
        _registro.Dispose();
    }

    [Test]
    public async Task Post_MensajeValido_Devuelve202Accepted() {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/internal/emails", new {
            to = "destinatario@test.com",
            subject = "Bienvenida",
            body = "<p>Hola</p>",
            isHtml = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Post_SinTo_Devuelve400BadRequest() {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/internal/emails", new {
            to = "",
            subject = "Asunto",
            body = "Cuerpo",
            isHtml = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Post_MensajeValido_LoProcesaLaCola_LoLlegaMemoryEmailService() {
        // Destinatario EXCLUSIVO de este test para no cruzar logs entre tests.
        string destinatario = $"cola_{Guid.NewGuid():N}@test.com";

        HttpResponseMessage response = await _client.PostAsJsonAsync("/internal/emails", new {
            to = destinatario,
            subject = "Prueba de cola",
            body = "<p>Procesado por BackgroundService</p>",
            isHtml = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // La cola es en segundo plano: se espera (máx 5s) a que el
        // BackgroundService consuma el mensaje y MemoryEmailService lo loguee.
        DateTime limite = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < limite &&
               !_registro.Lineas.Any(l => l.Contains(destinatario) && l.Contains("Para:"))) {
            await Task.Delay(50);
        }

        _registro.Lineas.Should().Contain(l => l.Contains("EMAIL SENT"),
            "MemoryEmailService debe registrar el envío tras pasar por la cola");
        _registro.Lineas.Should().Contain(l => l.Contains(destinatario),
            "el mensaje con nuestro destinatario debe llegar a través de la cola");
    }

    /// <summary>ILoggerProvider que retiene las líneas ya formateadas (test-friendly).</summary>
    private sealed class RegistroLog : ILoggerProvider {
        private readonly ConcurrentQueue<string> _lineas = new();

        public IReadOnlyCollection<string> Lineas => _lineas.ToArray();

        public ILogger CreateLogger(string categoryName) {
            return new LoggerRegistro(_lineas);
        }

        public void Dispose() { }

        private sealed class LoggerRegistro(ConcurrentQueue<string> lineas) : ILogger {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) {
                lineas.Enqueue(formatter(state, exception));
            }
        }
    }
}
