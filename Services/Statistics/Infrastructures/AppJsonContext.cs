using System.Text.Json.Serialization;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Estadisticas;

namespace TiendaApi.Api;

/// <summary>
/// Contexto source-gen de JSON del microservicio <b>statistics</b>.
///
/// 🎓 Es el equivalente reducido al del monolito: solo los tipos que este
/// servicio devuelve (todas las vistas agregadas y su paginación).
/// </summary>
[JsonSerializable(typeof(ResumenEstadisticasDto))]
[JsonSerializable(typeof(ResumenVentasDto))]
[JsonSerializable(typeof(ResumenProductosDto))]
[JsonSerializable(typeof(ResumenCategoriasDto))]
[JsonSerializable(typeof(ResumenUsuariosDto))]
[JsonSerializable(typeof(ComparativaDto))]
[JsonSerializable(typeof(VentasSerieDto))]
[JsonSerializable(typeof(PuntoSerieDto))]
[JsonSerializable(typeof(TotalesDto))]
[JsonSerializable(typeof(PeriodoDto))]
[JsonSerializable(typeof(ProductosMasVendidosDto))]
[JsonSerializable(typeof(FilaProductoVendidoDto))]
[JsonSerializable(typeof(CategoriasEstadisticasDto))]
[JsonSerializable(typeof(FilaCategoriaDto))]
[JsonSerializable(typeof(ProveedoresEstadisticasDto))]
[JsonSerializable(typeof(FilaProveedorDto))]
[JsonSerializable(typeof(EstadosEstadisticasDto))]
[JsonSerializable(typeof(FilaEstadoDto))]
[JsonSerializable(typeof(CanceladosDto))]
[JsonSerializable(typeof(ClientesEstadisticasDto))]
[JsonSerializable(typeof(FilaClienteDto))]
[JsonSerializable(typeof(InventarioDto))]
[JsonSerializable(typeof(InventarioResumenDto))]
[JsonSerializable(typeof(FilaInventarioCategoriaDto))]
[JsonSerializable(typeof(FilaAgotadoDto))]
[JsonSerializable(typeof(FilaValorDto))]
[JsonSerializable(typeof(PreciosEstadisticasDto))]
[JsonSerializable(typeof(PuntoPrecioDto))]
[JsonSerializable(typeof(PagedResult<ResumenEstadisticasDto>))]
public partial class AppJsonContext : JsonSerializerContext;
