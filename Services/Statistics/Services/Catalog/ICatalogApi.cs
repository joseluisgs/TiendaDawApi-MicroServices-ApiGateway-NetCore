using Refit;
using TiendaApi.Api.Dtos.Common;

namespace TiendaApi.Api.Services.Catalog;

/// <summary>
/// Contrato Refit del microservicio <b>catalog</b> (rutas públicas, R7).
///
/// 🎓 Statistics no tiene BD: pide los catálogos completos paginando (los
/// endpoints son paginados; se recorre hasta tenerlos todos) y los convierte en
/// los modelos de dominio que consume <c>EstadisticasCalculator</c> — así el
/// cálculo es BYTE IDÉNTICO al del monolito.
/// </summary>
public interface ICatalogApi {
    /// <summary>Página de productos (el read model de Mongo).</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño (máx. 100 por la validación).</param>
    [Get("/api/v1/productos")]
    Task<PagedResult<ProductoSnapshot>> GetProductosAsync([Query] int page, [Query] int size);

    /// <summary>
    /// Total de productos borrados (el listado público los excluye, así que se
    /// piden aparte por su contador — equivalente al <c>FindAllPagedAsync(IsDeleted: true)</c>.
    /// </summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño (1 basta: solo se usa TotalCount).</param>
    /// <param name="isDeleted">true para pedir solo los borrados.</param>
    [Get("/api/v1/productos")]
    Task<PagedResult<ProductoSnapshot>> GetProductosBorradosAsync([Query] int page, [Query] int size, [Query] bool isDeleted);

    /// <summary>Página de categorías.</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    [Get("/api/v1/categorias")]
    Task<PagedResult<CategoriaSnapshot>> GetCategoriasAsync([Query] int page, [Query] int size);

    /// <summary>Página de proveedores.</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    [Get("/api/v1/proveedores")]
    Task<PagedResult<ProveedorSnapshot>> GetProveedoresAsync([Query] int page, [Query] int size);
}

/// <summary>
/// Campos de producto que usa el cálculo de estadísticas.
///
/// 🎓 Ojo con la forma del JSON de catalog: la categoría y el proveedor vienen
/// <b>anidados</b> (<c>categoria:{id,nombre}</c>), no como IDs planos. Si se
/// mapean como planos quedan en 0/null y TODOS los cruces fallan en silencio.
/// </summary>
public sealed record ProductoSnapshot(
    long Id, string Nombre, decimal Precio, int Stock,
    CategoriaRef? Categoria, ProveedorRef? Proveedor, DateTime CreatedAt);

/// <summary>Referencia anidada a una categoría dentro del producto.</summary>
public sealed record CategoriaRef(long Id);

/// <summary>Referencia anidada a un proveedor dentro del producto.</summary>
public sealed record ProveedorRef(string Codigo);

/// <summary>Campos de categoría que usa el cálculo.</summary>
public sealed record CategoriaSnapshot(long Id, string Nombre);

/// <summary>Campos de proveedor que usa el cálculo.</summary>
public sealed record ProveedorSnapshot(string Codigo, string Nombre);
