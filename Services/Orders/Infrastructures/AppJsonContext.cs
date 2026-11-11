using System.Text.Json.Serialization;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Models;

namespace TiendaApi.Api;

/// <summary>
///     Contexto source-gen de JSON del microservicio <b>orders</b>.
///     🎓 Es el equivalente reducido al del monolito: solo los tipos que este
///     servicio serializa (cachea o devuelve). El RedisCacheService combina este
///     contexto con reflexión, así que lo que no aparezca aquí igual se serializa.
/// </summary>
[JsonSerializable(typeof(PedidoDto))]
[JsonSerializable(typeof(PedidoItemDto))]
[JsonSerializable(typeof(PagedResult<PedidoDto>))]
[JsonSerializable(typeof(List<PedidoDto>))]
[JsonSerializable(typeof(PedidoRequestDto))]
[JsonSerializable(typeof(PedidoItemRequestDto))]
[JsonSerializable(typeof(UpdatePedidoDto))]
[JsonSerializable(typeof(UpdateEstadoDto))]
[JsonSerializable(typeof(PedidoFilterDto))]
[JsonSerializable(typeof(MyPedidoFilterDto))]
[JsonSerializable(typeof(DestinatarioDto))]
[JsonSerializable(typeof(DireccionDto))]
[JsonSerializable(typeof(Pedido))]
[JsonSerializable(typeof(PedidoItem))]
[JsonSerializable(typeof(Destinatario))]
[JsonSerializable(typeof(Direccion))]
public partial class AppJsonContext : JsonSerializerContext;
