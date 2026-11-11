using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;
using Serilog;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Productos;
using TiendaApi.Api.Features.Productos.Notifications;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Productos;
using TiendaApi.Api.Services.Cache;
using TiendaApi.Api.Services.Productos;
using TiendaApi.Api.Services.Storage;

namespace TiendaApi.Api.Features.Productos.Commands;

/// <summary>
///     Comando para eliminar un producto.
/// </summary>
public record DeleteProductoCommand(long Id)
    : IRequest<UnitResult<DomainError>>;

/// <summary>
///     Handler del comando DeleteProductoCommand.
/// </summary>
public class DeleteProductoCommandHandler(
    IProductoRepository repository,
    IStorageApi storageApi,
    IMediator mediator,
    ICacheService cacheService,
    IOutputCacheStore outputCacheStore)
    : IRequestHandler<DeleteProductoCommand, UnitResult<DomainError>> {
    /// <inheritdoc />
    public async Task<UnitResult<DomainError>> Handle(
        DeleteProductoCommand request, CancellationToken cancellationToken) {
        Producto? producto = await repository.FindByIdAsync(request.Id);
        if (producto is null) {
            return UnitResult.Failure<DomainError>(ProductoError.NotFound(request.Id));
        }

        long categoriaId = producto.CategoriaId;

        if (producto.IsLocalImage() && !string.IsNullOrWhiteSpace(producto.Imagen)) {
            // 🎓 El fichero vive en storage (dueño): se le pide borrarlo por Refit.
            // Si no responde, el producto se elimina igual (mejor esfuerzo).
            try {
                await storageApi.DeleteAsync(producto.Imagen);
            } catch (Exception ex) {
                Log.Warning(ex, "storage no disponible: no se borró la imagen {Imagen} del producto {Id}",
                    producto.Imagen, request.Id);
            }
        }

        await repository.DeleteAsync(request.Id);

        // 🎓 Orden correcto: PRIMERO replicar (Publish → sync a MongoDB), DESPUÉS invalidar.
        await mediator.Publish(new ProductoEliminadoNotification(request.Id), cancellationToken);

        await cacheService.RemoveAsync($"productos:{request.Id}");
        await cacheService.RemoveAsync($"productos:categoria:{categoriaId}");
        await outputCacheStore.EvictByTagAsync("productos", cancellationToken);
        ProductoService.InvalidatePagedCache();

        return UnitResult.Success<DomainError>();
    }
}
