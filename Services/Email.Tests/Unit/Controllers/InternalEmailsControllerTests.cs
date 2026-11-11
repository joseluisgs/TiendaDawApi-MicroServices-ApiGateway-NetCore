using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using TiendaApi.Api.Controllers;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Tests.Unit.Controllers;

/// <summary>
/// Tests del contrato interno POST /internal/emails: valida la carga útil y
/// escribe en el Channel (202 Accepted) — allí espera el EmailBackgroundService.
/// La validación es la única frontera antes de la cola.
/// </summary>
public class InternalEmailsControllerTests {
    private Channel<EmailMessage> _emailChannel = null!;
    private InternalEmailsController _controller = null!;

    [SetUp]
    public void Setup() {
        _emailChannel = Channel.CreateUnbounded<EmailMessage>();
        _controller = new InternalEmailsController(_emailChannel);
    }

    [Test]
    public async Task Enqueue_MensajeValido_Devuelve202_YEncolaElMensaje() {
        var result = await _controller.Enqueue(
            new EmailRequest("destinatario@test.com", "Bienvenida", "<p>Hola</p>", IsHtml: true));

        result.Should().BeOfType<AcceptedResult>();

        // El mensaje queda en el canal con todos sus campos, listo para el
        // consumidor en segundo plano.
        _emailChannel.Reader.TryRead(out var mensaje).Should().BeTrue();
        mensaje!.To.Should().Be("destinatario@test.com");
        mensaje.Subject.Should().Be("Bienvenida");
        mensaje.Body.Should().Be("<p>Hola</p>");
        mensaje.IsHtml.Should().BeTrue();
    }

    [Test]
    public async Task Enqueue_SinTo_Devuelve400_NoEncola() {
        var result = await _controller.Enqueue(
            new EmailRequest("", "Asunto", "Cuerpo", IsHtml: true));

        result.Should().BeOfType<BadRequestObjectResult>();
        _emailChannel.Reader.TryRead(out _).Should().BeFalse("un mensaje inválido nunca llega a la cola");
    }

    [Test]
    public async Task Enqueue_SinSubject_Devuelve400_NoEncola() {
        var result = await _controller.Enqueue(
            new EmailRequest("destinatario@test.com", " ", "Cuerpo", IsHtml: true));

        result.Should().BeOfType<BadRequestObjectResult>();
        _emailChannel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task Enqueue_SinBody_Devuelve400_NoEncola() {
        var result = await _controller.Enqueue(
            new EmailRequest("destinatario@test.com", "Asunto", null!, IsHtml: false));

        result.Should().BeOfType<BadRequestObjectResult>();
        _emailChannel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task Enqueue_TextoPlano_MantieneElFlagIsHtml() {
        var result = await _controller.Enqueue(
            new EmailRequest("destinatario@test.com", "Asunto", "Cuerpo sin tags", IsHtml: false));

        result.Should().BeOfType<AcceptedResult>();

        _emailChannel.Reader.TryRead(out var mensaje).Should().BeTrue();
        mensaje!.IsHtml.Should().BeFalse();
    }

    [Test]
    public async Task Enqueue_MultiplesMensajes_LosEncolaEnOrden() {
        await _controller.Enqueue(new EmailRequest("a@test.com", "Uno", "1", IsHtml: true));
        await _controller.Enqueue(new EmailRequest("b@test.com", "Dos", "2", IsHtml: true));

        _emailChannel.Reader.TryRead(out var primero).Should().BeTrue();
        _emailChannel.Reader.TryRead(out var segundo).Should().BeTrue();
        primero!.To.Should().Be("a@test.com", "el canal FIFO conserva el orden de llegada");
        segundo!.To.Should().Be("b@test.com");
    }
}
