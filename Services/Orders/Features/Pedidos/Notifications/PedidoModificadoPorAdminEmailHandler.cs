using MediatR;
using Serilog;
using TiendaApi.Api.Services.Email;
namespace TiendaApi.Api.Features.Pedidos.Notifications;

/// <summary>
/// Handler que avisa al CLIENTE cuando un administrador modifica los datos
/// de su pedido (sin cambio de estado).
/// </summary>
public class PedidoModificadoPorAdminEmailHandler(IEmailApi emailService)
    : INotificationHandler<PedidoModificadoPorAdminNotification>
{
    /// <inheritdoc/>
    public async Task Handle(PedidoModificadoPorAdminNotification notification, CancellationToken cancellationToken)
    {
        var destinatario = notification.Pedido.Destinatario;
        if (string.IsNullOrWhiteSpace(destinatario?.Email)) return;

        var content = EmailTemplates.PedidoModificadoCliente(
            notification.Pedido.Id,
            destinatario.NombreCompleto);

        try
        {
            await emailService.EnqueueAsync(new EmailRequest(destinatario.Email, $"✏️ Tu pedido #{notification.Pedido.Id} ha sido modificado", EmailTemplates.CreateBase($"Pedido #{notification.Pedido.Id} modificado", content), IsHtml: true));
            Log.Information("📧 [EMAIL] Aviso de modificación encolado para el cliente: {PedidoId} -> {Email}",
                notification.Pedido.Id, destinatario.Email);
        }
        catch (Exception ex)
        {
            // 📧 Mejor esfuerzo: email caído NO rompe la operación del pedido.
            Log.Warning(ex, "PedidoModificadoPorAdminEmailHandler: email-service no disponible, no se encoló el aviso");
        }
    }
}
