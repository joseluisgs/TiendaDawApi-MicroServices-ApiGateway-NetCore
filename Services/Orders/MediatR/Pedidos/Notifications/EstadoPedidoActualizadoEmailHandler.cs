using MediatR;
using Serilog;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Api.MediatR.Pedidos.Notifications;

/// <summary>
///     Handler que envía el email de cambio de estado al CLIENTE del pedido.
///     📧 Matriz de emails (ver doc/21/15): el usuario es quien debe enterarse
///     de los cambios de estado de SU pedido. El admin no recibe este email
///     porque los cambios de estado los provoca él desde el panel.
/// </summary>
public class EstadoPedidoActualizadoEmailHandler(IEmailApi emailService)
    : INotificationHandler<EstadoPedidoActualizadoNotification> {
    /// <inheritdoc />
    public async Task Handle(EstadoPedidoActualizadoNotification notification, CancellationToken cancellationToken) {
        DestinatarioDto? destinatario = notification.Pedido.Destinatario;
        if (string.IsNullOrWhiteSpace(destinatario?.Email)) {
            return;
        }

        string content = EmailTemplates.PedidoEstadoActualizado(
            notification.Pedido.Id,
            notification.Pedido.Estado,
            notification.NuevoEstado,
            notification.Pedido.Total);

        try {
            await emailService.EnqueueAsync(new EmailRequest(destinatario.Email,
                $"📦 Pedido #{notification.Pedido.Id} - {notification.NuevoEstado}",
                EmailTemplates.CreateBase("Cambio de Estado de Pedido", content), true));
            Log.Information("📧 [EMAIL] Cambio de estado encolado para el cliente: {PedidoId} -> {Email} ({Estado})",
                notification.Pedido.Id, destinatario.Email, notification.NuevoEstado);
        } catch (Exception ex) {
            // 📧 Mejor esfuerzo: email caído NO rompe la operación del pedido.
            Log.Warning(ex, "EstadoPedidoActualizadoEmailHandler: email-service no disponible, no se encoló el aviso");
        }
    }
}
