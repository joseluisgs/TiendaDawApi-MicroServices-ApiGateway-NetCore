using System.Text.Json.Serialization;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Dtos.Proveedores;
using TiendaApi.Api.Models;

namespace TiendaApi.Api;

/// <summary>
/// Contexto source-gen de JSON del microservicio catalog.
///
/// 🎓 Es el equivalente reducido al del monolito: solo los tipos que este
/// servicio serializa (cachea o devuelve). El RedisCacheService combina este
/// contexto con reflexión, así que lo que no aparezca aquí igual se serializa.
/// </summary>
[JsonSerializable(typeof(CategoriaDto))]
[JsonSerializable(typeof(CategoriaRequestDto))]
[JsonSerializable(typeof(CategoriaFilterDto))]
[JsonSerializable(typeof(PagedResult<CategoriaDto>))]
[JsonSerializable(typeof(List<CategoriaDto>))]
[JsonSerializable(typeof(ProveedorDto))]
[JsonSerializable(typeof(ProveedorRequestDto))]
[JsonSerializable(typeof(ProveedorFilterDto))]
[JsonSerializable(typeof(PagedResult<ProveedorDto>))]
[JsonSerializable(typeof(List<ProveedorDto>))]
[JsonSerializable(typeof(ProductoDto))]
[JsonSerializable(typeof(ProductoRequestDto))]
[JsonSerializable(typeof(ProductoPatchDto))]
[JsonSerializable(typeof(ProductoFilterDto))]
[JsonSerializable(typeof(PagedResult<ProductoDto>))]
[JsonSerializable(typeof(List<ProductoDto>))]
[JsonSerializable(typeof(CategoriaResumenDto))]
[JsonSerializable(typeof(ProveedorResumenDto))]
[JsonSerializable(typeof(HistorialPrecioDto))]
[JsonSerializable(typeof(List<HistorialPrecioDto>))]
[JsonSerializable(typeof(Categoria))]
[JsonSerializable(typeof(Producto))]
[JsonSerializable(typeof(Proveedor))]
public partial class AppJsonContext : JsonSerializerContext;
