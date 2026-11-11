using System.Security.Claims;
using HotChocolate;
using HotChocolate.Types;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Common;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Orders;

// ⚠️ Refit y HotChocolate traen atributos homónimos (Authorize, Query): aquí
// solo se usan los de HotChocolate, así que se desambigüan con alias.
using Authorize = HotChocolate.Authorization.AuthorizeAttribute;

namespace TiendaApi.Api.GraphQL.Queries;

/// <summary>
/// Consultas GraphQL de la tienda.
///
/// 🎓 BFF (R4): GraphQL no tiene BD ni dominio propio — cada query es la
/// llamada REST equivalente hecha por Refit al dueño del dato (catalog u
/// orders). El contrato de salida es el MISMO DTO que el monolito, así que el
/// esquema no cambia.
///
/// 🎓 Seguridad: GraphQL solo expone DTOs, nunca entidades de escritura. Los
/// endpoints a los que llama exigen ADMIN y aquí viaja el JWT del llamante
/// (<c>AuthorizationForwardingHandler</c>), de modo que la autorización se
/// resuelve en el dueño — no se confía en el filtro local.
/// </summary>
public class TiendaQuery {
    // ── productos ──────────────────────────────────────────────────────

    /// <summary>Obtiene todos los productos (primera página del listado público).</summary>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Lista de productos (DTOs).</returns>
    public async Task<IReadOnlyList<ProductoDto>> GetProductos(
        [Service] ICatalogApi catalogApi,
        CancellationToken ct = default) {
        var pagina = await catalogApi.GetProductosAsync(page: 0, size: 100);
        return (pagina.Items ?? []).ToList();
    }

    /// <summary>Obtiene un producto por ID (null si catalog responde 404).</summary>
    /// <param name="id">ID del producto.</param>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Producto encontrado (DTO) o null.</returns>
    public async Task<ProductoDto?> GetProducto(
        long id,
        [Service] ICatalogApi catalogApi,
        CancellationToken ct = default) {
        try {
            return await catalogApi.GetProductoAsync(id);
        } catch (Refit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
            // GraphQL devuelve null (no error) cuando el recurso no existe.
            return null;
        }
    }

    /// <summary>Obtiene productos paginados.</summary>
    /// <param name="page">Número de página (base 1, contrato GraphQL).</param>
    /// <param name="size">Elementos por página.</param>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resultado paginado de productos.</returns>
    public async Task<PagedResult<ProductoDto>> GetProductosPaged(
        [Service] ICatalogApi catalogApi,
        int page = 1,
        int size = 10,
        CancellationToken ct = default) {
        // GraphQL expone paginación base 1; la API REST trabaja con base 0.
        var pagina = await catalogApi.GetProductosAsync(Math.Max(page - 1, 0), size);
        return pagina with { Page = page };
    }

    // ── categorías ─────────────────────────────────────────────────────

    /// <summary>Obtiene todas las categorías.</summary>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Lista de categorías (DTOs).</returns>
    public async Task<IReadOnlyList<CategoriaDto>> GetCategorias(
        [Service] ICatalogApi catalogApi,
        CancellationToken ct = default) {
        var pagina = await catalogApi.GetCategoriasAsync(page: 0, size: 100);
        return (pagina.Items ?? []).ToList();
    }

    /// <summary>Obtiene una categoría por ID (null si catalog responde 404).</summary>
    /// <param name="id">ID de la categoría.</param>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Categoría encontrada (DTO) o null.</returns>
    public async Task<CategoriaDto?> GetCategoria(
        long id,
        [Service] ICatalogApi catalogApi,
        CancellationToken ct = default) {
        try {
            return await catalogApi.GetCategoriaAsync(id);
        } catch (Refit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
            return null;
        }
    }

    /// <summary>Obtiene categorías paginadas.</summary>
    /// <param name="page">Número de página (base 1, contrato GraphQL).</param>
    /// <param name="size">Elementos por página.</param>
    /// <param name="catalogApi">Cliente Refit de catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resultado paginado de categorías.</returns>
    public async Task<PagedResult<CategoriaDto>> GetCategoriasPaged(
        [Service] ICatalogApi catalogApi,
        int page = 1,
        int size = 10,
        CancellationToken ct = default) {
        var pagina = await catalogApi.GetCategoriasAsync(Math.Max(page - 1, 0), size);
        return pagina with { Page = page };
    }

    // ── pedidos ────────────────────────────────────────────────────────

    /// <summary>
    /// Obtiene TODOS los pedidos. Solo administradores.
    /// </summary>
    /// <param name="ordersApi">Cliente Refit de orders.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Lista de pedidos (DTOs, no entidades).</returns>
    [Authorize(policy: "AdminOnly")]
    public async Task<IReadOnlyList<PedidoDto>> GetPedidos(
        [Service] IOrdersApi ordersApi,
        CancellationToken ct = default) {
        return await ordersApi.GetPedidosAsync();
    }

    /// <summary>
    /// Obtiene MIS pedidos: los del usuario autenticado.
    ///
    /// 🎓 El filtro lo aplica <b>orders</b> en <c>/me</c> con el JWT que llega
    /// en la cabecera: aquí no se decide nada, así que es imposible devolver
    /// pedidos de otro usuario.
    /// </summary>
    /// <param name="user">Usuario autenticado (claims del JWT).</param>
    /// <param name="ordersApi">Cliente Refit de orders.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Lista de pedidos del usuario autenticado.</returns>
    [Authorize]
    public async Task<IReadOnlyList<PedidoDto>> GetMisPedidos(
        ClaimsPrincipal user,
        [Service] IOrdersApi ordersApi,
        CancellationToken ct = default) {
        // Comprobación local mínima: si no hay identidad, no se llama (evita
        // que un token sin claims pida /me y reciba un 401 opaco).
        var claim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !long.TryParse(claim, out _))
            throw new GraphQLException("No se pudo identificar al usuario autenticado (falta el claim NameIdentifier).");

        return await ordersApi.GetMisPedidosAsync();
    }
}
