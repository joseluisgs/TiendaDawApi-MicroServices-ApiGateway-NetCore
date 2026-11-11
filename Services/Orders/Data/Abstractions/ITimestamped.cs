namespace TiendaApi.Api.Data.Abstractions;

/// <summary>
///     Interfaz de auditoría para entidades.
///     Implementada por: Pedido (aquí) · Categoria, Producto, User (en sus servicios).
///     Propiedades: CreatedAt (readonly), UpdatedAt.
/// </summary>
public interface ITimestamped {
    /// <summary>Fecha de creación (se asigna en INSERT).</summary>
    DateTime CreatedAt { get; }

    /// <summary>Fecha de última modificación (se actualiza en INSERT/UPDATE).</summary>
    DateTime UpdatedAt { get; }
}
