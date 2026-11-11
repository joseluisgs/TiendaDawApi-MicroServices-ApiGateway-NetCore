using HotChocolate;
using System.Net;
using System.Text.Json;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.GraphQL.Inputs;
using TiendaApi.Api.Services.Catalog;

// âš ï¸ Refit y HotChocolate traen atributos homÃ³nimos (Authorize, Query). AquÃ­
// solo se usan los de HotChocolate, asÃ­ que NO se importa `using Refit;`:
// Refit.ApiException se referencia por su namespace completo.
using Authorize = HotChocolate.Authorization.AuthorizeAttribute;

namespace TiendaApi.Api.GraphQL.Mutations;

/// <summary>
/// Mutations de GraphQL para productos (requiere rol ADMIN).
///
/// ðŸŽ“ GraphQL: en vez de devolver null silencioso, lanzamos excepciÃ³n para que
/// el cliente vea el error en el array <c>errors</c> de la respuesta â€” mismo
/// contrato que el monolito.
///
/// ðŸŽ“ BFF (R4): el mutation es una capa de traducciÃ³n â€” mapea el input y llama
/// por Refit al endpoint REST de catalog. La validaciÃ³n de negocio (FK de
/// categorÃ­a, precio, stockâ€¦) la hace <b>catalog</b>: aquÃ­ solo se reenvÃ­a su
/// mensaje para que salga en <c>errors[]</c>.
///
/// ðŸŽ“ InyecciÃ³n: las dependencias scoped (los clients Refit) se inyectan por
/// PARÃMETRO <c>[Service]</c>, nunca por constructor â€” HotChocolate resuelve el
/// tipo de mutaciÃ³n con lifetime singleton.
/// </summary>
public class ProductoMutation {
    /// <summary>Crea un nuevo producto.</summary>
    /// <param name="input">Datos del producto.</param>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Producto creado (DTO).</returns>
    [Authorize(policy: "AdminOnly")]
    public async Task<ProductoDto> CreateProducto(
        CreateProductoInput input,
        [Service] ICatalogApi catalogApi,
        CancellationToken ct = default) {
        var dto = new ProductoRequestDto {
            Nombre = input.Nombre,
            Descripcion = input.Descripcion ?? string.Empty,
            Precio = input.Precio,
            Stock = input.Stock,
            Imagen = input.Imagen,
            CategoriaId = input.CategoriaId,
            ProveedorCodigo = input.ProveedorCodigo
        };

        try {
            return await catalogApi.CreateProductoAsync(dto);
        } catch (Refit.ApiException ex) {
            throw new GraphQLException(MensajeDeCatalogo(ex));
        }
    }

    /// <summary>
    /// Actualiza un producto existente (merge de campos: solo se envÃ­an los que cambian).
    ///
    /// ðŸŽ“ Equivale al <c>PATCH /api/v1/productos/{id}</c> de catalog: la lectura
    /// previa y el merge de campos null viven allÃ­, no aquÃ­.
    /// </summary>
    /// <param name="id">ID del producto.</param>
    /// <param name="input">Campos a cambiar.</param>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Producto actualizado (DTO).</returns>
    [Authorize(policy: "AdminOnly")]
    public async Task<ProductoDto> UpdateProducto(
        long id,
        UpdateProductoInput input,
        [Service] ICatalogApi catalogApi,
        CancellationToken ct = default) {
        var patch = new ProductoPatchDto {
            Nombre = input.Nombre,
            Descripcion = input.Descripcion,
            Precio = input.Precio,
            Stock = input.Stock,
            Imagen = input.Imagen,
            CategoriaId = input.CategoriaId,
            ProveedorCodigo = input.ProveedorCodigo
        };

        try {
            return await catalogApi.UpdateProductoAsync(id, patch);
        } catch (Refit.ApiException ex) {
            throw new GraphQLException(MensajeDeCatalogo(ex));
        }
    }

    /// <summary>Elimina un producto (soft delete).</summary>
    /// <param name="id">ID del producto.</param>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>true si se eliminÃ³.</returns>
    [Authorize(policy: "AdminOnly")]
    public async Task<bool> DeleteProducto(
        long id,
        [Service] ICatalogApi catalogApi,
        CancellationToken ct = default) {
        try {
            await catalogApi.DeleteProductoAsync(id);
            return true;
        } catch (Refit.ApiException ex) {
            throw new GraphQLException(MensajeDeCatalogo(ex));
        }
    }

    /// <summary>
    /// Extrae el <c>message</c> que catalog devolviÃ³ para que aparezca en el
    /// <c>errors[]</c> de GraphQL con el mismo texto que darÃ­a REST
    /// (p. ej. "Errores de validaciÃ³n" â†’ el E2E busca "validaci").
    /// </summary>
    private static string MensajeDeCatalogo(Refit.ApiException ex) {
        if (string.IsNullOrWhiteSpace(ex.Content))
            return ex.StatusCode switch {
                HttpStatusCode.NotFound => "Recurso no encontrado",
                HttpStatusCode.UnprocessableEntity => "Regla de negocio incumplida",
                _ => $"Error devuelto por catalog ({(int)ex.StatusCode})"
            };

        try {
            using var doc = JsonDocument.Parse(ex.Content);
            if (doc.RootElement.TryGetProperty("message", out var m))
                return m.GetString() ?? ex.Content;
        } catch {
            // no era JSON: se reenvÃ­a tal cual
        }

        return ex.Content;
    }
}
