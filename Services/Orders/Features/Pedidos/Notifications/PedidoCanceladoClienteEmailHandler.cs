using MediatR;
using Serilog;
using TiendaApi.Api.Services.Email;
namespace TiendaApi.Api.Features.Pedidos.Notifications;

/// <summary>
/// Handler que envía al CLIENTE el aviso de cancelación de su pedido.
/// 
/// 📧 Matriz de emails: el usuario recibe todo lo que pasa a su pedido; el
/// admin recibe además su aviso de gestión (PedidoCanceladoEmailHandler).
/// </summary>
public class PedidoCanceladoClienteEmailHandler(IEmailApi emailService)
    : INotificationHandler<PedidoCanceladoNotification> {
    /// <inheritdoc/>
    public async Task Handle(PedidoCanceladoNotification notification, CancellationToken cancellationToken) {
        var destinatario = notification.Destinatario;
        if (string.IsNullOrWhiteSpace(destinatario?.Email)) return;

        var content = EmailTemplates.PedidoCanceladoCliente(
            notification.PedidoId,
            notification.Total,
            destinatario.NombreCompleto);

        try {
            await emailService.EnqueueAsync(new EmailRequest(destinatario.Email, $"🚫 Tu pedido #{notification.PedidoId} ha sido cancelado", EmailTemplates.CreateBase($"Pedido #{notification.PedidoId} cancelado", content), IsHtml: true));
            Log.Information("📧 [EMAIL] Cancelación encolada para el cliente: {PedidoId} -> {Email}",
                notification.PedidoId, destinatario.Email);
        } catch (Exception ex) {
            // 📧 Mejor esfuerzo: email caído NO rompe la operación del pedido.
            Log.Warning(ex, "PedidoCanceladoClienteEmailHandler: email-service no disponible, no se encoló el aviso");
        }
    }
}
