using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Tests.Unit.Services.Email;

/// <summary>
/// Tests de EmailBackgroundService: constructor y, sobre todo, el ciclo real
/// de vida de la cola — consume mensajes del Channel, los envía con el
/// IEmailService del scope y NO se corta si un envío revienta (el siguiente
/// mensaje de la cola se procesa igualmente: mejor esfuerzo, como el monolito).
/// </summary>
public class EmailBackgroundServiceTests
{
    private Channel<EmailMessage> _emailChannel = null!;

    [SetUp]
    public void Setup()
    {
        _emailChannel = Channel.CreateUnbounded<EmailMessage>();
    }

    [Test]
    public void Constructor_ParametrosValidos_NoLanzaExcepcion()
    {
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<EmailBackgroundService>>();

        var act = () => new EmailBackgroundService(
            _emailChannel,
            mockServiceProvider.Object,
            mockLogger.Object);

        act.Should().NotThrow();
    }

    [Test]
    public async Task ExecuteAsync_MensajeEnLaCola_LoProcesaConElEmailService()
    {
        var emailService = new Mock<IEmailService>();
        var recibido = new TaskCompletionSource<EmailMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        emailService
            .Setup(e => e.SendEmailAsync(It.IsAny<EmailMessage>()))
            .Callback<EmailMessage>(m => recibido.TrySetResult(m))
            .Returns(Task.CompletedTask);

        var servicio = new EmailBackgroundService(
            _emailChannel, CrearProveedor(emailService), Mock.Of<ILogger<EmailBackgroundService>>());

        await servicio.StartAsync(CancellationToken.None);
        await _emailChannel.Writer.WriteAsync(new EmailMessage
        {
            To = "destinatario@test.com",
            Subject = "Asunto",
            Body = "Cuerpo"
        });

        // El consumidor debe tomar el mensaje y pasárselo al servicio de envío.
        var mensaje = await recibido.Task.WaitAsync(TimeSpan.FromSeconds(5));
        mensaje.To.Should().Be("destinatario@test.com");
        mensaje.Subject.Should().Be("Asunto");

        await servicio.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ExecuteAsync_SiUnEnvioFalla_SigueConElSiguienteMensaje()
    {
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(e => e.SendEmailAsync(It.Is<EmailMessage>(m => m.To == "roto@test.com")))
            .ThrowsAsync(new InvalidOperationException("SMTP caído"));

        var segundoRecibido = new TaskCompletionSource<EmailMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        emailService
            .Setup(e => e.SendEmailAsync(It.Is<EmailMessage>(m => m.To == "sano@test.com")))
            .Callback<EmailMessage>(m => segundoRecibido.TrySetResult(m))
            .Returns(Task.CompletedTask);

        var servicio = new EmailBackgroundService(
            _emailChannel, CrearProveedor(emailService), Mock.Of<ILogger<EmailBackgroundService>>());

        await servicio.StartAsync(CancellationToken.None);
        await _emailChannel.Writer.WriteAsync(new EmailMessage { To = "roto@test.com", Subject = "x", Body = "x" });
        await _emailChannel.Writer.WriteAsync(new EmailMessage { To = "sano@test.com", Subject = "x", Body = "x" });

        // Un fallo no detiene el bucle: el segundo mensaje DEBE procesarse.
        var mensaje = await segundoRecibido.Task.WaitAsync(TimeSpan.FromSeconds(5));
        mensaje.To.Should().Be("sano@test.com");

        await servicio.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// IServiceProvider falso con el mínimo de la cadena real del servicio:
    /// CreateScope() → scope.ServiceProvider → GetRequiredService&lt;IEmailService&gt;().
    /// </summary>
    private static IServiceProvider CrearProveedor(Mock<IEmailService> emailService)
    {
        var scopeProvider = new Mock<IServiceProvider>();
        scopeProvider.Setup(p => p.GetService(typeof(IEmailService))).Returns(emailService.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopeProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);

        return provider.Object;
    }
}
