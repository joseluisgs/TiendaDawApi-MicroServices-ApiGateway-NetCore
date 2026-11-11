using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Services.Storage;

namespace TiendaApi.Api.Controllers;

/// <summary>
///     API interna de <c>storage</c> (rutas <c>/internal/*</c>, R7): la consume
///     <b>catalog</b> por Refit para subir y borrar las imágenes de productos.
///     🎓 R5 · dueño del dato = quien escribe: los ficheros los guarda (y sirve)
///     storage, así que el upload pasa por aquí y no directamente por catalog.
///     El cliente NUNCA llega a estas rutas (no hay ruta en el gateway) y solo es
///     accesible por la red interna (R12).
///     Respuesta: la ruta relativa pública (<c>/images/uploads/…</c>), que es la
///     que catalog guarda en la columna <c>imagen</c> y la que el gateway sirve.
/// </summary>
[ApiController]
[Route("internal/storage")]
[Produces("application/json")]
public class StorageInternalController(IStorageService storageService, ILogger<StorageInternalController> logger)
    : ControllerBase {
    /// <summary>
    ///     Guarda un fichero subido en la carpeta indicada y devuelve su ruta pública.
    /// </summary>
    /// <param name="file">Fichero (multipart/form-data, campo <c>file</c>).</param>
    /// <param name="folder">Carpeta destino (por defecto <c>productos</c>).</param>
    /// <returns>201 con <c>{ url }</c>, o 400 si no pasa las validaciones.</returns>
    [HttpPost("files")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(typeof(StorageFileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StorageFileDto>> Upload(
        IFormFile file,
        [FromForm] string folder = "productos") {
        if (file is null or { Length: 0 }) {
            return BadRequest(new { message = "Debe proporcionar un archivo" });
        }

        Result<string, DomainError> resultado = await storageService.SaveFileAsync(file, folder);
        if (resultado.IsFailure) {
            return BadRequest(new { message = resultado.Error.Message });
        }

        logger.LogInformation("Fichero guardado: {Url}", resultado.Value);
        return Created(resultado.Value, new StorageFileDto(resultado.Value));
    }

    /// <summary>
    ///     Elimina un fichero por su ruta pública. Idempotente: si no existe, 204 igual.
    /// </summary>
    /// <param name="url">Ruta pública del fichero (<c>/images/uploads/…</c>).</param>
    /// <returns>204 No Content.</returns>
    [HttpDelete("files")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete([FromQuery] string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return BadRequest(new { message = "Debe indicar la ruta del archivo" });
        }

        await storageService.DeleteFileAsync(url);
        return NoContent();
    }
}

/// <summary>Ruta pública del fichero guardado.</summary>
public sealed record StorageFileDto(string Url);
