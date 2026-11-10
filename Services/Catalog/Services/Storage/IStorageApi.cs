using Refit;

namespace TiendaApi.Api.Services.Storage;

/// <summary>
/// Contrato Refit del microservicio <b>storage</b> (rutas internas, R7).
///
/// 🎓 Decisión de imagen : <b>storage es el
/// dueño de los ficheros</b> — catalog le pasa el archivo por Refit y guarda la
/// ruta pública que él devuelve. El gateway sirve <c>/images/uploads/*</c> desde
/// storage, que es donde el fichero vive.
/// </summary>
public interface IStorageApi
{
    /// <summary>Sube un fichero y devuelve su ruta pública.</summary>
    /// <param name="file">Archivo (multipart, campo <c>file</c>).</param>
    /// <param name="folder">Carpeta destino (por defecto productos).</param>
    [Multipart]
    [Post("/internal/storage/files")]
    Task<StorageFileDto> UploadAsync(StreamPart file, [Query] string folder = "productos");

    /// <summary>Elimina un fichero por su ruta pública (idempotente).</summary>
    /// <param name="url">Ruta pública (<c>/images/uploads/…</c>).</param>
    [Delete("/internal/storage/files")]
    Task DeleteAsync([Query] string url);
}

/// <summary>Ruta pública del fichero guardado.</summary>
public sealed record StorageFileDto(string Url);
