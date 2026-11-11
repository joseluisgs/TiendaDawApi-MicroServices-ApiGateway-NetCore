using Refit;
using TiendaApi.Api.Dtos.Backup;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Dtos.Proveedores;

namespace TiendaApi.Api.Services.Catalog;

/// <summary>
/// Contrato Refit del microservicio <b>catalog</b> (R7) para el backup.
///
/// 🎓 Backup no tiene BD: el <b>export</b> LEE por las rutas públicas y el
/// <b>import</b> ESCRIBE en los endpoints internos de catalog — el upsert por
/// clave, la validación de FKs y la re-sincronización de secuencias los hace el
/// dueño, no este servicio.
///
/// Los métodos de import devuelven <see cref="HttpResponseMessage"/> para poder
/// leer el código: 201 (creado) · 200 (actualizado) · 204 (sin cambios) son la
/// información que necesita el informe.
/// </summary>
public interface ICatalogApi {
    // ── Export (rutas públicas, paginadas) ──────────────────────────────

    /// <summary>Página de categorías.</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño (máx. 100).</param>
    [Get("/api/v1/categorias")]
    Task<PagedResult<CategoriaDto>> GetCategoriasAsync([Query] int page, [Query] int size);

    /// <summary>Página de categorías borradas (para incluirBorrados).</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    /// <param name="isDeleted">true para pedir solo las borradas.</param>
    [Get("/api/v1/categorias")]
    Task<PagedResult<CategoriaDto>> GetCategoriasBorradasAsync([Query] int page, [Query] int size, [Query] bool isDeleted);

    /// <summary>Página de proveedores.</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    [Get("/api/v1/proveedores")]
    Task<PagedResult<ProveedorDto>> GetProveedoresAsync([Query] int page, [Query] int size);

    /// <summary>Página de proveedores borrados (para incluirBorrados).</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    /// <param name="isDeleted">true para pedir solo los borrados.</param>
    [Get("/api/v1/proveedores")]
    Task<PagedResult<ProveedorDto>> GetProveedoresBorradosAsync([Query] int page, [Query] int size, [Query] bool isDeleted);

    /// <summary>Página de productos.</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    [Get("/api/v1/productos")]
    Task<PagedResult<ProductoDto>> GetProductosAsync([Query] int page, [Query] int size);

    /// <summary>Página de productos borrados (para incluirBorrados).</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    /// <param name="isDeleted">true para pedir solo los borrados.</param>
    [Get("/api/v1/productos")]
    Task<PagedResult<ProductoDto>> GetProductosBorradosAsync([Query] int page, [Query] int size, [Query] bool isDeleted);

    // ── Import (rutas internas, R7: no pasan por el gateway) ────────────

    /// <summary>Importa una categoría (upsert por id). 201 · 200 · 204 · 400.</summary>
    /// <param name="fila">Fila del backup.</param>
    [Post("/api/catalog/internal/categorias/import")]
    Task<HttpResponseMessage> ImportCategoriaAsync([Body] ImportCategoriaDto fila);

    /// <summary>Importa un proveedor (upsert por código). 201 · 200 · 204 · 400.</summary>
    /// <param name="fila">Fila del backup.</param>
    [Post("/api/catalog/internal/proveedores/import")]
    Task<HttpResponseMessage> ImportProveedorAsync([Body] ImportProveedorDto fila);

    /// <summary>Importa un producto (upsert por id). 201 · 200 · 204 · 400.</summary>
    /// <param name="fila">Fila del backup.</param>
    [Post("/api/catalog/internal/productos/import")]
    Task<HttpResponseMessage> ImportProductoAsync([Body] ImportProductoDto fila);
}
