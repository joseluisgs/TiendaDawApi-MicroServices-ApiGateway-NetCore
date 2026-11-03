using MediatR;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Api.Features.Users.Notifications;

/// <summary>
/// Handler que envía el email de bienvenida cuando se publica UsuarioRegistradoNotification.
///
/// 🎓 OPEN/CLOSED: el comando de registro no conoce el email. El envío real lo
/// hace el microservicio <c>email</c> (cola + SMTP + Polly) vía Refit: aquí solo
/// se compone el mensaje y se pide el envío; si email-service no responde, el
/// registro no falla (mejor esfuerzo, como el monolito con SMTP caído).
/// </summary>
public class UsuarioRegistradoBienvenidaEmailHandler(IEmailApi email)
    : INotificationHandler<UsuarioRegistradoNotification>
{
    /// <inheritdoc/>
    public async Task Handle(UsuarioRegistradoNotification notification, CancellationToken cancellationToken)
    {
        var content = $"<p>Hola <strong>{notification.Usuario.Username}</strong>, te damos la bienvenida a Tienda DAW.</p>";
        var body = EmailTemplates.CreateBase("Bienvenido a Tienda DAW", content);

        try
        {
            await email.EnqueueAsync(new EmailRequest(
                notification.Usuario.Email,
                "¡ Bienvenido a Tienda DAW",
                body,
                IsHtml: true));
        }
        catch (Exception)
        {
            // Mejor esfuerzo: como el monolito, un email que no se puede pedir
            // no debe romper el alta del usuario.
        }
    }
}
