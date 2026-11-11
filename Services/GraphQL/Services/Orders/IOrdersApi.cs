using Refit;
using TiendaApi.Api.Dtos.Pedidos;

namespace TiendaApi.Api.Services.Orders;

/// <summary>
/// Contrato Refit del microservicio <b>orders</b> (R7) para el BFF GraphQL.
///
/// 🎓 <c>misPedidos</c> reutiliza el endpoint <c>/me</c>: orders es quien decide
/// qué pedidos son del usuario autenticado (le llega su JWT), así GraphQL no
/// filtra a mano y no puede filtrar mal.
/// </summary>
public interface IOrdersApi {
    /// <summary>Todos los pedidos (solo ADMIN, el token viaja en la cabecera).</summary>
    [Get("/api/v1/pedidos")]
    Task<List<PedidoDto>> GetPedidosAsync();

    /// <summary>
    /// Mis pedidos: los del usuario del token recibido (orders aplica la
    /// pertenencia; GraphQL solo propaga la cabecera Authorization).
    /// </summary>
    [Get("/api/v1/pedidos/me")]
    Task<List<PedidoDto>> GetMisPedidosAsync();
}
