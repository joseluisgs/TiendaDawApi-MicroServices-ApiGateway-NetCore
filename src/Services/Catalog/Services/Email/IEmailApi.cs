using Refit;

namespace TiendaApi.Api.Services.Email;

/// <summary>
/// Contrato Refit del microservicio <c>email</c> (rutas internas, R7).
///
/// 🎓 El envío SMTP (y su cola con Polly) vive en email-service: este servicio
/// solo <b>enciende</b> la petición. Si email-service no responde, el aviso se
/// registra y no rompe la operación del usuario — mejor esfuerzo, como en el
/// monolito.
/// </summary>
public interface IEmailApi
{
    /// <summary>Encola un email para su envío.</summary>
    [Post("/internal/emails")]
    Task EnqueueAsync([Body] EmailRequest body);
}

/// <summary>Email ya compuesto por el servicio emisor.</summary>
public sealed record EmailRequest(string To, string Subject, string Body, bool IsHtml);
