using Refit;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Productos;

// ⚠️ HotChocolate trae un atributo Query homónimo: aquí solo se usa el de Refit.
using Query = Refit.QueryAttribute;

namespace TiendaApi.Api.Services.Catalog;

/// <summary>
/// Contrato Refit del microservicio <b>catalog</b> (R7) para el BFF GraphQL.
///
/// 🎓 GraphQL no tiene dominio propio: traduce cada query/mutation a la llamada
/// REST equivalente. Los errores de negocio se reenvían tal cual para que
/// aparezcan en el array <c>errors</c> de la respuesta GraphQL (mismo contrato
/// que el monolito, donde el mutation lanzaba excepción).
/// </summary>
public interface ICatalogApi {
    // ── Queries ────────────────────────────────────────────────────────

    /// <summary>Página de productos (base 0).</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño (máx. 100).</param>
    [Get("/api/v1/productos")]
    Task<PagedResult<ProductoDto>> GetProductosAsync([Query] int page, [Query] int size);

    /// <summary>Producto por ID (404 → null en GraphQL).</summary>
    /// <param name="id">ID del producto.</param>
    [Get("/api/v1/productos/{id}")]
    Task<ProductoDto> GetProductoAsync(long id);

    /// <summary>Página de categorías (base 0).</summary>
    /// <param name="page">Página base 0.</param>
    /// <param name="size">Tamaño.</param>
    [Get("/api/v1/categorias")]
    Task<PagedResult<CategoriaDto>> GetCategoriasAsync([Query] int page, [Query] int size);

    /// <summary>Categoría por ID (404 → null en GraphQL).</summary>
    /// <param name="id">ID de la categoría.</param>
    [Get("/api/v1/categorias/{id}")]
    Task<CategoriaDto> GetCategoriaAsync(long id);

    // ── Mutations (solo ADMIN, el token viaja en la cabecera) ──────────

    /// <summary>Crea un producto. 201 con el DTO creado.</summary>
    /// <param name="dto">Datos del producto.</param>
    [Post("/api/v1/productos")]
    Task<ProductoDto> CreateProductoAsync([Body] ProductoRequestDto dto);

    /// <summary>Actualización parcial (merge de campos null), igual que el PATCH REST.</summary>
    /// <param name="id">ID del producto.</param>
    /// <param name="dto">Campos a cambiar.</param>
    [Patch("/api/v1/productos/{id}")]
    Task<ProductoDto> UpdateProductoAsync(long id, [Body] ProductoPatchDto dto);

    /// <summary>Baja lógica (soft delete).</summary>
    /// <param name="id">ID del producto.</param>
    [Delete("/api/v1/productos/{id}")]
    Task DeleteProductoAsync(long id);
}
