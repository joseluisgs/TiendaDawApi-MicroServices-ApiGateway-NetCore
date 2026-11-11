using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using TiendaApi.Api.Controllers;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Tests.Unit.Controllers;

/// <summary>
///     Tests del contrato interno POST /internal/emails: valida la carga útil y
///     escribe en el Channel (202 Accepted) — allí espera el EmailBackgroundService.
///     La validación es la única frontera antes de la cola.
/// </summary>
public class InternalEmailsControllerTests {
    private InternalEmailsController _controller = null!;
    private Channel<EmailMessage> _emailChannel = null!;

    [SetUp]
    public void Setup() {
        _emailChannel = Channel.CreateUnbounded<EmailMessage>();
        _controller = new InternalEmailsController(_emailChannel);
    }

    [Test]
    public async Task Enqueue_MensajeValido_Devuelve202_YEncolaElMensaje() {
        IActionResult result = await _controller.Enqueue(
            new EmailRequest("destinatario@test.com", "Bienvenida", "<p>Hola</p>", true));

        result.Should().BeOfType<AcceptedResult>();

        // El mensaje queda en el canal con todos sus campos, listo para el
        // consumidor en segundo plano.
        _emailChannel.Reader.TryRead(out EmailMessage? mensaje).Should().BeTrue();
        mensaje!.To.Should().Be("destinatario@test.com");
        mensaje.Subject.Should().Be("Bienvenida");
        mensaje.Body.Should().Be("<p>Hola</p>");
        mensaje.IsHtml.Should().BeTrue();
    }

    [Test]
    public async Task Enqueue_SinTo_Devuelve400_NoEncola() {
        IActionResult result = await _controller.Enqueue(
            new EmailRequest("", "Asunto", "Cuerpo", true));

        result.Should().BeOfType<BadRequestObjectResult>();
        _emailChannel.Reader.TryRead(out _).Should().BeFalse("un mensaje inválido nunca llega a la cola");
    }

    [Test]
    public async Task Enqueue_SinSubject_Devuelve400_NoEncola() {
        IActionResult result = await _controller.Enqueue(
            new EmailRequest("destinatario@test.com", " ", "Cuerpo", true));

        result.Should().BeOfType<BadRequestObjectResult>();
        _emailChannel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task Enqueue_SinBody_Devuelve400_NoEncola() {
        IActionResult result = await _controller.Enqueue(
            new EmailRequest("destinatario@test.com", "Asunto", null!, false));

        result.Should().BeOfType<BadRequestObjectResult>();
        _emailChannel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task Enqueue_TextoPlano_MantieneElFlagIsHtml() {
        IActionResult result = await _controller.Enqueue(
            new EmailRequest("destinatario@test.com", "Asunto", "Cuerpo sin tags", false));

        result.Should().BeOfType<AcceptedResult>();

        _emailChannel.Reader.TryRead(out EmailMessage? mensaje).Should().BeTrue();
        mensaje!.IsHtml.Should().BeFalse();
    }

    [Test]
    public async Task Enqueue_MultiplesMensajes_LosEncolaEnOrden() {
        await _controller.Enqueue(new EmailRequest("a@test.com", "Uno", "1", true));
        await _controller.Enqueue(new EmailRequest("b@test.com", "Dos", "2", true));

        _emailChannel.Reader.TryRead(out EmailMessage? primero).Should().BeTrue();
        _emailChannel.Reader.TryRead(out EmailMessage? segundo).Should().BeTrue();
        primero!.To.Should().Be("a@test.com", "el canal FIFO conserva el orden de llegada");
        segundo!.To.Should().Be("b@test.com");
    }
}
