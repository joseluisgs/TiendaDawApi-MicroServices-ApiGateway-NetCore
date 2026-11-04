using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Api.Controllers;

/// <summary>
/// API interna del microservicio <c>email</c> (rutas <c>/internal/*</c>, R7):
/// la consumen identity (bienvenida), orders (avisos de pedido) y catalog
/// (producto creado y reporte) por Refit.
///
/// 🎓 El endpoint solo VALIDA y ENCOLA (responde rápido, 202): escribe en el
/// Channel que consume el <see cref="EmailBackgroundService"/> en segundo plano,
/// y ese consumidor llama al <see cref="IEmailService"/> (SMTP + Polly en
/// producción). Mejor esfuerzo: el consumidor jamás ve fallar su operación
/// principal por un email.
/// </summary>
[ApiController]
[Route("internal/emails")]
public class InternalEmailsController(Channel<EmailMessage> emailChannel) : ControllerBase
{
    /// <summary>Encola un email para su envío en segundo plano.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Enqueue([FromBody] EmailRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.To) ||
            string.IsNullOrWhiteSpace(body.Subject) ||
            string.IsNullOrWhiteSpace(body.Body))
            return BadRequest(new { message = "To, Subject y Body son obligatorios" });

        // Escritura no bloqueante en el canal (unbounded): el request responde
        // ya y el BackgroundService se encarga del envío.
        await emailChannel.Writer.WriteAsync(new EmailMessage
        {
            To = body.To,
            Subject = body.Subject,
            Body = body.Body,
            IsHtml = body.IsHtml
        });

        return Accepted();
    }
}

/// <summary>Email ya compuesto por el servicio emisor (contrato Refit compartido).</summary>
public sealed record EmailRequest(string To, string Subject, string Body, bool IsHtml);
