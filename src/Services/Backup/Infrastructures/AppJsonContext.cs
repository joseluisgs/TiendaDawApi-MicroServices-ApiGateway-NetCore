using System.Text.Json.Serialization;
using TiendaApi.Api.Dtos.Backup;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Dtos.Proveedores;

namespace TiendaApi.Api;

/// <summary>
/// Contexto source-gen de JSON del microservicio <b>backup</b>.
///
/// 🎓 Es el equivalente reducido al del monolito: los tipos que este servicio
/// serializa (el propio backup, el informe de importación y los DTOs públicos de
/// catalog que consume). Se comparte con <c>BackupService.OpcionesJson</c> para
/// que el round-trip export→import lea exactamente lo que escribió (camelCase).
/// </summary>
[JsonSerializable(typeof(BackupDto))]
[JsonSerializable(typeof(ManifestBackupDto))]
[JsonSerializable(typeof(BackupCategoriaDto))]
[JsonSerializable(typeof(BackupProveedorDto))]
[JsonSerializable(typeof(BackupProductoDto))]
[JsonSerializable(typeof(List<BackupCategoriaDto>))]
[JsonSerializable(typeof(List<BackupProveedorDto>))]
[JsonSerializable(typeof(List<BackupProductoDto>))]
[JsonSerializable(typeof(ImportReportDto))]
[JsonSerializable(typeof(EntidadImportReportDto))]
[JsonSerializable(typeof(ErrorFilaDto))]
[JsonSerializable(typeof(CatalogoFormatosDto))]
[JsonSerializable(typeof(FormatoDto))]
[JsonSerializable(typeof(EntidadCatalogoDto))]
[JsonSerializable(typeof(LimitesBackupDto))]
[JsonSerializable(typeof(ReglasBackupDto))]
[JsonSerializable(typeof(CategoriaDto))]
[JsonSerializable(typeof(ProveedorDto))]
[JsonSerializable(typeof(ProductoDto))]
[JsonSerializable(typeof(PagedResult<CategoriaDto>))]
[JsonSerializable(typeof(PagedResult<ProveedorDto>))]
[JsonSerializable(typeof(PagedResult<ProductoDto>))]
public partial class AppJsonContext : JsonSerializerContext;
