using System.Net;
using System.Text.Json;
using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;
using Refit;
using Serilog;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Productos;
using TiendaApi.Api.Features.Productos.Notifications;
using TiendaApi.Api.Mappers;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Productos;
using TiendaApi.Api.Services.Cache;
using TiendaApi.Api.Services.Productos;
using TiendaApi.Api.Services.Storage;

namespace TiendaApi.Api.Features.Productos.Commands;

/// <summary>
///     Comando para actualizar la imagen de un producto.
/// </summary>
public record UpdateProductoImageCommand(long Id, IFormFile Image)
    : IRequest<Result<ProductoDto, DomainError>>;

/// <summary>
///     Handler del comando UpdateProductoImageCommand.
///     🎓 Decisión de imagen : el fichero lo guarda <b>storage</b> (dueño de los
///     ficheros) por Refit; aquí solo se guarda la ruta pública que devuelve en la
///     columna <c>imagen</c>. El monolito hacía lo mismo pero con el servicio
///     local de ficheros del propio proceso.
/// </summary>
public class UpdateProductoImageCommandHandler(
    IProductoRepository repository,
    IStorageApi storageApi,
    IMediator mediator,
    ICacheService cacheService,
    IOutputCacheStore outputCacheStore)
    : IRequestHandler<UpdateProductoImageCommand, Result<ProductoDto, DomainError>> {
    /// <inheritdoc />
    public async Task<Result<ProductoDto, DomainError>> Handle(
        UpdateProductoImageCommand request, CancellationToken cancellationToken) {
        Producto? producto = await repository.FindByIdAsync(request.Id);
        if (producto is null) {
            return Result.Failure<ProductoDto, DomainError>(ProductoError.NotFound(request.Id));
        }

        string rutaPublica;
        try {
            await using Stream stream = request.Image.OpenReadStream();
            var parte = new StreamPart(
                stream,
                request.Image.FileName,
                request.Image.ContentType,
                "file");
            StorageFileDto subida = await storageApi.UploadAsync(parte);
            rutaPublica = subida.Url;
        } catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest) {
            // storage valida tamaño/extensión/tipo de contenido y devuelve 400
            // con { message }: se reemite como el error de validación del monolito.
            return Result.Failure<ProductoDto, DomainError>(
                ValidationError.Create(MensajeDeError(ex)));
        }

        if (producto.IsLocalImage() && !string.IsNullOrWhiteSpace(producto.Imagen)) {
            try {
                await storageApi.DeleteAsync(producto.Imagen);
            } catch (Exception ex) {
                Log.Warning(ex, "storage no disponible: no se borró la imagen anterior {Imagen}", producto.Imagen);
            }
        }

        producto.Imagen = rutaPublica;
        Producto updated = await repository.UpdateAsync(producto);
        ProductoDto dto = updated.ToDto();

        // ✅ Orden correcto: PRIMERO replicar (Publish → sync a MongoDB), DESPUÉS invalidar.
        // El precio NO cambia al cambiar solo la imagen: precioAnterior = precio
        // actual → el handler del historial no registrará punto (correcto).
        await mediator.Publish(new ProductoActualizadoNotification(dto, dto.Precio), cancellationToken);

        await cacheService.RemoveAsync($"productos:{request.Id}");
        await outputCacheStore.EvictByTagAsync("productos", cancellationToken);
        ProductoService.InvalidatePagedCache();

        Log.Information("Notificación publicada y caché invalidada para producto imagen actualizada ID: {ProductoId}",
            dto.Id);

        return Result.Success<ProductoDto, DomainError>(dto);
    }

    /// <summary>Extrae el <c>message</c> del 400 de storage (o uno genérico).</summary>
    private static string MensajeDeError(ApiException ex) {
        try {
            if (string.IsNullOrEmpty(ex.Content)) {
                return "Archivo no válido";
            }

            using var doc = JsonDocument.Parse(ex.Content);
            if (doc.RootElement.TryGetProperty("message", out JsonElement mensaje)) {
                return mensaje.GetString() ?? "Archivo no válido";
            }
        } catch (JsonException) {
            // cuerpo no JSON: se responde con el mensaje genérico
        }

        return "Archivo no válido";
    }
}
