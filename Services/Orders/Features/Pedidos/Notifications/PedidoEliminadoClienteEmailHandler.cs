using MediatR;
using Serilog;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Api.Features.Pedidos.Notifications;

/// <summary>
///     Handler que envía al CLIENTE el aviso de que su pedido ha sido eliminado
///     por un administrador.
///     📧 Matriz de emails: el usuario recibe todo lo que pasa a su pedido; el
///     admin no se notifica su propia acción (lo ve en el panel y en tiempo real).
/// </summary>
public class PedidoEliminadoClienteEmailHandler(IEmailApi emailService)
    : INotificationHandler<PedidoEliminadoNotification> {
    /// <inheritdoc />
    public async Task Handle(PedidoEliminadoNotification notification, CancellationToken cancellationToken) {
        DestinatarioDto? destinatario = notification.Destinatario;
        if (string.IsNullOrWhiteSpace(destinatario?.Email)) {
            return;
        }

        string content = EmailTemplates.PedidoCanceladoCliente(
            notification.PedidoId,
            notification.Total,
            destinatario.NombreCompleto);

        try {
            await emailService.EnqueueAsync(new EmailRequest(destinatario.Email,
                $"🚫 Tu pedido #{notification.PedidoId} ha sido cancelado",
                EmailTemplates.CreateBase($"Pedido #{notification.PedidoId} cancelado", content), true));
            Log.Information("📧 [EMAIL] Aviso de eliminación encolado para el cliente: {PedidoId} -> {Email}",
                notification.PedidoId, destinatario.Email);
        } catch (Exception ex) {
            // 📧 Mejor esfuerzo: email caído NO rompe la operación del pedido.
            Log.Warning(ex, "PedidoEliminadoClienteEmailHandler: email-service no disponible, no se encoló el aviso");
        }
    }
}
