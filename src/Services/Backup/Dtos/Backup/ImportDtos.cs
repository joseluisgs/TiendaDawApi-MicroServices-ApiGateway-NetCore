namespace TiendaApi.Api.Dtos.Backup;

/// <summary>
/// Una categoría tal como se recibe en un import (ID explícito).
///
/// 🎓 Mismos campos que <c>BackupCategoriaDto</c> del monolito: el backup
/// exporta e importa con el MISMO formato, así que el round-trip no cambia
/// datos. El upsert es por clave <c>id</c>.
/// </summary>
public record ImportCategoriaDto(
    long Id,
    string Nombre,
    string? Descripcion,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Un proveedor tal como se recibe en un import. El código es su clave.</summary>
public record ImportProveedorDto(
    string Codigo,
    string Nombre,
    string? Descripcion,
    string? Contacto,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Un producto tal como se recibe en un import. El upsert es por clave <c>id</c>.</summary>
public record ImportProductoDto(
    long Id,
    string Nombre,
    string Descripcion,
    decimal Precio,
    int Stock,
    string? Imagen,
    long CategoriaId,
    string? ProveedorId,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>Cuerpo del descuento atómico de stock (lo consume orders en T8).</summary>
public record DecrementoStockDto(int Cantidad);
