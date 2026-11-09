using System.Text.Json.Serialization;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Dtos.Proveedores;

namespace TiendaApi.Api;

/// <summary>
/// Contexto source-gen de JSON del microservicio <b>graphql</b>.
///
/// ðŸŽ“ Solo los DTOs que este servicio serializa en sus respuestas REST
/// (la propia GraphQL usa HotChocolate y sus propios serializadores).
/// </summary>
[JsonSerializable(typeof(ProductoDto))]
[JsonSerializable(typeof(ProductoRequestDto))]
[JsonSerializable(typeof(ProductoPatchDto))]
[JsonSerializable(typeof(CategoriaResumenDto))]
[JsonSerializable(typeof(ProveedorResumenDto))]
[JsonSerializable(typeof(List<ProductoDto>))]
[JsonSerializable(typeof(PagedResult<ProductoDto>))]
[JsonSerializable(typeof(CategoriaDto))]
[JsonSerializable(typeof(CategoriaRequestDto))]
[JsonSerializable(typeof(List<CategoriaDto>))]
[JsonSerializable(typeof(PagedResult<CategoriaDto>))]
[JsonSerializable(typeof(ProveedorDto))]
[JsonSerializable(typeof(ProveedorRequestDto))]
[JsonSerializable(typeof(PedidoDto))]
[JsonSerializable(typeof(PedidoItemDto))]
[JsonSerializable(typeof(DestinatarioDto))]
[JsonSerializable(typeof(DireccionDto))]
[JsonSerializable(typeof(List<PedidoDto>))]
public partial class AppJsonContext : JsonSerializerContext;
