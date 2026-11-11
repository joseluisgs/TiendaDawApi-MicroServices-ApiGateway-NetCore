using Microsoft.AspNetCore.SignalR;
using Moq;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Features.Pedidos.Notifications;
using TiendaApi.Api.Realtime.Pedidos;

namespace TiendaApi.Tests.Unit.Features.Pedidos;

/// <summary>
///     🎓 Tests de paridad de canales de los handlers de pedidos (CQRS).
///     Cada evento de tiempo real debe salir por SignalR a los grupos del dueño
///     (user-{id}) y de administradores - nunca a Clients.All - sin canal WebSocket (solo SignalR) y
///     dueño + admins. Regresión del bug de broadcast (cualquier usuario autenticado
///     veía todos los pedidos) y de los eventos que solo tenían un canal.
/// </summary>
public class PedidosNotificationHandlersTests {
    private const long UserId = 1;
    private Mock<IClientProxy> _adminsProxy = null!;
    private Mock<IHubClients> _clients = null!;

    private Mock<IHubContext<PedidosHub>> _hubContext = null!;
    private Mock<IClientProxy> _userProxy = null!;

    [SetUp]
    public void Setup() {
        _userProxy = new Mock<IClientProxy>();
        _adminsProxy = new Mock<IClientProxy>();
        _clients = new Mock<IHubClients>();
        _clients.Setup(c => c.Group($"user-{UserId}")).Returns(_userProxy.Object);
        _clients.Setup(c => c.Group("admins")).Returns(_adminsProxy.Object);
        _hubContext = new Mock<IHubContext<PedidosHub>>();
        _hubContext.Setup(h => h.Clients).Returns(_clients.Object);
    }

    private static PedidoDto UnPedido(string estado = "Pendiente") {
        return new PedidoDto(
            "PED-2024-0001",
            UserId,
            null!,
            new List<PedidoItemDto>(),
            99.5m,
            estado,
            null,
            DateTime.UtcNow);
    }

    /// <summary>Auxiliar: verifica que el envío fue a los dos grupos y jamás a Clients.All.</summary>
    private void VerificarGruposSelectivos(string metodo) {
        _userProxy.Verify(p => p.SendCoreAsync(metodo, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _adminsProxy.Verify(p => p.SendCoreAsync(metodo, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clients.Verify(c => c.All, Times.Never);
    }

    #region SignalR: grupos selectivos (nunca Clients.All)

    [Test]
    public async Task PedidoCreadoSignalRHandler_EnvíaAlDueñoYAdmins_NoABroadcast() {
        var handler = new PedidoCreadoSignalRHandler(_hubContext.Object);

        await handler.Handle(new PedidoCreadoNotification(UnPedido()), CancellationToken.None);

        VerificarGruposSelectivos("PedidoCreado");
    }

    [Test]
    public async Task EstadoPedidoActualizadoSignalRHandler_EnvíaAlDueñoYAdmins_NoABroadcast() {
        var handler = new EstadoPedidoActualizadoSignalRHandler(_hubContext.Object);

        await handler.Handle(new EstadoPedidoActualizadoNotification(UnPedido(), "Enviado"), CancellationToken.None);

        VerificarGruposSelectivos("PedidoActualizado");
    }

    [Test]
    public async Task PedidoActualizadoSignalRHandler_EnvíaAlDueñoYAdmins_NoABroadcast() {
        var handler = new PedidoActualizadoSignalRHandler(_hubContext.Object);

        await handler.Handle(new PedidoActualizadoNotification(UnPedido()), CancellationToken.None);

        VerificarGruposSelectivos("PedidoActualizado");
    }

    [Test]
    public async Task PedidoEliminadoSignalRHandler_EnvíaAlDueñoYAdmins_NoABroadcast() {
        var handler = new PedidoEliminadoSignalRHandler(_hubContext.Object);

        await handler.Handle(new PedidoEliminadoNotification("PED-2024-0001", UserId, "Pendiente", 99.5m, null),
            CancellationToken.None);

        VerificarGruposSelectivos("PedidoEliminado");
    }

    [Test]
    public async Task PedidoCanceladoSignalRHandler_EnvíaAlDueñoYAdmins_NoABroadcast() {
        var handler = new PedidoCanceladoSignalRHandler(_hubContext.Object);

        await handler.Handle(new PedidoCanceladoNotification("PED-2024-0001", UserId, 99.5m, null),
            CancellationToken.None);

        VerificarGruposSelectivos("PedidoEliminado");
    }

    [Test]
    public async Task PedidoModificadoPorAdminSignalRHandler_EnvíaAlDueñoYAdmins_NoABroadcast() {
        var handler = new PedidoModificadoPorAdminSignalRHandler(_hubContext.Object);

        await handler.Handle(new PedidoModificadoPorAdminNotification(UnPedido()), CancellationToken.None);

        VerificarGruposSelectivos("PedidoActualizado");
    }

    #endregion
}
