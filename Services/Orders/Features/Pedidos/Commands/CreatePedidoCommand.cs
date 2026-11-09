using CSharpFunctionalExtensions;
using FluentValidation;
using MediatR;
using Refit;
using Serilog;
using System.Net;
using TiendaApi.Api.Dtos.Pedidos;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Pedidos;
using TiendaApi.Api.Features.Pedidos.Notifications;
using TiendaApi.Api.Mappers;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Pedidos;
using TiendaApi.Api.Services.Cache;
using TiendaApi.Api.Services.Catalog;
using TiendaApi.Api.Services.Identity;
using Microsoft.AspNetCore.OutputCaching;

namespace TiendaApi.Api.Features.Pedidos.Commands;

/// <summary>
/// Comando para crear un nuevo pedido.
/// </summary>
public record CreatePedidoCommand(long UserId, PedidoRequestDto Dto)
    : IRequest<Result<PedidoDto, DomainError>>;

/// <summary>
/// Handler del comando CreatePedidoCommand.
///
/// 🎓 Transacción Serializable del monolito → <b>validación + decremento atómico
/// en catalog</b>: orders no tiene PostgreSQL (R5), así que lee el snapshot del
/// producto (nombre/precio) por Refit y descuenta el stock en el dueño, que es
/// quien lo protege con su propia transacción (204 · 404 · 422).
/// </summary>
public class CreatePedidoCommandHandler(
    IPedidosRepository pedidosRepository,
    ICatalogApi catalogApi,
    IUsersApi usersApi,
    IValidator<PedidoRequestDto> pedidoValidator,
    IValidator<PedidoItemRequestDto> pedidoItemValidator,
    IMediator mediator,
    ICacheService cacheService,
    IOutputCacheStore outputCacheStore)
    : IRequestHandler<CreatePedidoCommand, Result<PedidoDto, DomainError>>
{
    /// <inheritdoc/>
    public async Task<Result<PedidoDto, DomainError>> Handle(
        CreatePedidoCommand request, CancellationToken cancellationToken)
    {
        // Equivalente al FindByIdAsync del monolito: el dueño de los usuarios
        // responde 404 si no existe o está borrado.
        if (!await ExisteUsuarioAsync(request.UserId))
            return Result.Failure<PedidoDto, DomainError>(
                NotFoundError.FromId(request.UserId, "Usuario"));

        var validationResult = await pedidoValidator.ValidateAsync(request.Dto, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return Result.Failure<PedidoDto, DomainError>(PedidoError.ValidacionConCampos(errors));
        }

        return await CrearConItemsAsync(request, cancellationToken);
    }

    /// <summary>
    /// Valida cada item, congela su snapshot y descuenta stock en catalog.
    /// </summary>
    private async Task<Result<PedidoDto, DomainError>> CrearConItemsAsync(
        CreatePedidoCommand request, CancellationToken cancellationToken)
    {
        var pedidoItems = new List<PedidoItem>();
        decimal total = 0;

        foreach (var itemDto in request.Dto.Items)
        {
            var itemValidation = await pedidoItemValidator.ValidateAsync(itemDto, cancellationToken);
            if (!itemValidation.IsValid)
            {
                var itemErrors = itemValidation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Result.Failure<PedidoDto, DomainError>(PedidoError.ValidacionConCampos(itemErrors));
            }

            // 1) Snapshot del producto (nombre y precio al momento de la compra).
            var producto = await LeerProductoAsync(itemDto.ProductoId);
            if (producto is null)
                return Result.Failure<PedidoDto, DomainError>(
                    PedidoError.ProductoNoEncontrado(itemDto.ProductoId));

            // 2) Descuento atómico en catalog: él valida el stock con su propia
            //    transacción (equivale al producto.Stock -= cantidad del monolito).
            var decremento = await DescontarStockAsync(itemDto.ProductoId, itemDto.Cantidad, producto);
            if (decremento.IsFailure)
                return Result.Failure<PedidoDto, DomainError>(decremento.Error);

            var item = new PedidoItem
            {
                ProductoId = itemDto.ProductoId,
                NombreProducto = producto.Nombre,
                Cantidad = itemDto.Cantidad,
                Precio = producto.Precio,
                Subtotal = producto.Precio * itemDto.Cantidad
            };
            pedidoItems.Add(item);
            total += item.Subtotal;
        }

        var pedido = new Pedido
        {
            UserId = request.UserId,
            Destinatario = request.Dto.Destinatario?.ToEntity(),
            Items = pedidoItems,
            Total = total,
            Estado = PedidoEstado.PENDIENTE,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var pedidoGuardado = await pedidosRepository.SaveAsync(pedido);

        var dto = pedidoGuardado.ToDto();
        await mediator.Publish(new PedidoCreadoNotification(dto), cancellationToken);

        await cacheService.RemoveAsync($"pedidos:{pedidoGuardado.Id}");
        await outputCacheStore.EvictByTagAsync("pedidos", cancellationToken);

        return Result.Success<PedidoDto, DomainError>(dto);
    }

    /// <summary>¿Existe el usuario en identity? (404 → false, cualquier otro fallo → true).</summary>
    private async Task<bool> ExisteUsuarioAsync(long userId)
    {
        try
        {
            await usersApi.GetUsuarioAsync(userId);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Lee el snapshot del producto; null si catalog responde 404.</summary>
    private async Task<ProductoSnapshotDto?> LeerProductoAsync(long productoId)
    {
        try
        {
            return await catalogApi.GetProductoAsync(productoId);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Descuenta stock en catalog y traduce sus códigos a errores de dominio
    /// (204 ok · 404 producto · 422 stock insuficiente).
    /// </summary>
    private async Task<Result<Unit, DomainError>> DescontarStockAsync(
        long productoId, int cantidad, ProductoSnapshotDto producto)
    {
        try
        {
            await catalogApi.DecrementarStockAsync(productoId, new DecrementoStock(cantidad));
            return Result.Success<Unit, DomainError>(Unit.Value);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Failure<Unit, DomainError>(PedidoError.ProductoNoEncontrado(productoId));
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // catalog ya monta el MISMO texto que el monolito
            // ("Stock insuficiente para el producto 'X'. Disponible: N, Solicitado: M"):
            // se reenvía tal cual para que el E2E y los clientes no noten el cambio.
            var mensaje = MensajeDe(ex) ??
                PedidoError.StockInsuficiente(producto.Nombre, producto.Stock, cantidad).Message;
            Log.Warning("Stock insuficiente para el producto {Id}: {Mensaje}", productoId, mensaje);
            return Result.Failure<Unit, DomainError>(new BusinessRuleError(mensaje));
        }
    }

    /// <summary>Extrae {message} del cuerpo JSON de una ApiException de ASP.NET.</summary>
    private static string? MensajeDe(ApiException ex)
    {
        if (string.IsNullOrWhiteSpace(ex.Content)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            ex.Content, "\"message\"\\s*:\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }
}
