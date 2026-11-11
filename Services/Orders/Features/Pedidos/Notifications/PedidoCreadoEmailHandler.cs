using MediatR;
using Serilog;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Api.Features.Pedidos.Notifications;

/// <summary>
///     Handler que envía el email de confirmación del pedido creado.
///     🎓 OPEN/CLOSED: cambiar el canal de notificación no obliga a tocar el comando.
/// </summary>
public class PedidoCreadoEmailHandler(IEmailApi emailService, IConfiguration configuration)
    : INotificationHandler<PedidoCreadoNotification> {
    /// <inheritdoc />
    public async Task Handle(PedidoCreadoNotification notification, CancellationToken cancellationToken) {
        string? adminEmail = configuration["Smtp:AdminEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail)) {
            return;
        }

        string content = EmailTemplates.PedidoCreado(notification.Pedido.Id, notification.Pedido.Total,
            notification.Pedido.Items?.Count ?? 0, notification.Pedido.UserId);
        try {
            await emailService.EnqueueAsync(new EmailRequest(adminEmail, $"🛒 Nuevo Pedido #{notification.Pedido.Id}",
                EmailTemplates.CreateBase("Nuevo Pedido Recibido", content), true));
        } catch (Exception ex) {
            // 📧 Mejor esfuerzo: email caído NO rompe la operación del pedido.
            Log.Warning(ex, "PedidoCreadoEmailHandler: email-service no disponible, no se encoló el aviso");
        }
    }
}
