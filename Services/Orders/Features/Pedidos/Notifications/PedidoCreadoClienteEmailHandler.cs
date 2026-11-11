using MediatR;
using Serilog;
using TiendaApi.Api.Services.Email;
namespace TiendaApi.Api.Features.Pedidos.Notifications;

/// <summary>
/// Handler que envía el email de CONFIRMACIÓN del pedido creado AL CLIENTE
/// que lo realizó (dirección del destinatario del pedido).
/// 
/// 🎓 Paridad con la API normal: antes solo existía
/// <c>PedidoCreadoEmailHandler</c>, que avisaba únicamente a
/// <c>Smtp:AdminEmail</c>, y el cliente no recibía nada. Ahora salen los dos
/// emails. OPEN/CLOSED: el comando no cambia, se añade un handler.
/// </summary>
public class PedidoCreadoClienteEmailHandler(IEmailApi emailService)
    : INotificationHandler<PedidoCreadoNotification> {
    /// <inheritdoc/>
    public async Task Handle(PedidoCreadoNotification notification, CancellationToken cancellationToken) {
        var destinatario = notification.Pedido.Destinatario;
        if (string.IsNullOrWhiteSpace(destinatario?.Email)) return;

        var content = EmailTemplates.PedidoConfirmacionCliente(
            notification.Pedido.Id,
            notification.Pedido.Total,
            notification.Pedido.Items?.Count ?? 0,
            destinatario.NombreCompleto);

        try {
            await emailService.EnqueueAsync(new EmailRequest(destinatario.Email, $"✅ Pedido #{notification.Pedido.Id} confirmado", EmailTemplates.CreateBase($"Pedido #{notification.Pedido.Id} confirmado", content), IsHtml: true));
            Log.Information("📧 [EMAIL] Confirmación de pedido encolada para el cliente: {PedidoId} -> {Email}",
                notification.Pedido.Id, destinatario.Email);
        } catch (Exception ex) {
            // 📧 Mejor esfuerzo: email caído NO rompe la operación del pedido.
            Log.Warning(ex, "PedidoCreadoClienteEmailHandler: email-service no disponible, no se encoló el aviso");
        }
    }
}
