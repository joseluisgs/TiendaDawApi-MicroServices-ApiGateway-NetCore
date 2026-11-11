using MediatR;
using Serilog;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Api.MediatR.Productos.Notifications;

/// <summary>
///     Handler que envía un email al administrador cuando se crea un producto.
///     🎓 En el monolito encolaba el email en el propio proceso; aquí lo manda por
///     Refit a <b>email</b> (dueño de la cola SMTP). Si el servicio no responde, la
///     creación del producto NO falla: mejor esfuerzo, como en el monolito.
/// </summary>
public class ProductoCreadoEmailHandler(
    IEmailApi emailApi,
    IConfiguration configuration)
    : INotificationHandler<ProductoCreadoNotification> {
    /// <summary>
    ///     Procesa el evento ProductoCreado y envía el email de notificación al administrador.
    /// </summary>
    public async Task Handle(ProductoCreadoNotification notification, CancellationToken cancellationToken) {
        string? adminEmail = configuration["Smtp:AdminEmail"];
        if (string.IsNullOrEmpty(adminEmail)) {
            Log.Warning("Email de admin no configurado, saltando notificación");
            return;
        }

        ProductoDto producto = notification.Producto;
        string content = EmailTemplates.ProductoCreado(producto.Nombre, producto.Precio, producto.Stock, producto.Id);
        string body = EmailTemplates.CreateBase("Nuevo Producto Creado", content);

        try {
            await emailApi.EnqueueAsync(new EmailRequest(
                adminEmail,
                "📧 Nuevo Producto en Tienda DAW",
                body,
                true));
            Log.Information("📧 [EMAIL] Notificación enviada: Producto creado ID={ProductoId}", producto.Id);
        } catch (Exception ex) {
            // Mejor esfuerzo: email-service no disponible → se registra y sigue.
            Log.Warning(ex, "email-service no disponible: no se pudo encolar el aviso del producto {ProductoId}",
                producto.Id);
        }
    }
}
