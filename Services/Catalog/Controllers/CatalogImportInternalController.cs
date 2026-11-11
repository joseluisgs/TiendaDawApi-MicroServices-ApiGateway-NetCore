using CSharpFunctionalExtensions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TiendaApi.Api.Dtos.Backup;
using TiendaApi.Api.Dtos.Categorias;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.Dtos.Proveedores;
using TiendaApi.Api.Mappers;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Categorias;
using TiendaApi.Api.Repositories.Productos;
using TiendaApi.Api.Repositories.Proveedores;

namespace TiendaApi.Api.Controllers;

/// <summary>
/// API interna de <c>catalog</c> para el <b>import de backups</b> (rutas
/// <c>/api/catalog/internal/*</c>, R7): la consume <b>backup</b> por Refit.
///
/// 🎓 El cliente NUNCA llega aquí (no hay ruta en el gateway) y solo es
/// accesible por la red interna (R12). El upsert es por clave — <c>id</c> en
/// categorías y productos, <c>codigo</c> en proveedores — con el MISMO
/// comportamiento que el <c>BackupService</c> del monolito: validación de
/// formato, comprobación de FKs y altas con ID explícito
/// (<c>OVERRIDING SYSTEM VALUE</c> + re-sincronización de secuencia).
///
/// <b>Códigos de respuesta</b> (los usa backup para armar su informe):
/// <list type="bullet">
/// <item><b>201</b> → creado (no existía esa clave)</item>
/// <item><b>200</b> → actualizado (existía y había cambios)</item>
/// <item><b>204</b> → sin cambios (round-trip: mismos datos)</item>
/// <item><b>400</b> → validación o FK inexistente (fila con error)</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/catalog/internal")]
[Produces("application/json")]
public class CatalogImportInternalController(
    ICategoriaRepository categoriaRepository,
    IProveedorRepository proveedorRepository,
    IProductoRepository productoRepository,
    IValidator<CategoriaRequestDto> categoriaValidator,
    IValidator<ProveedorRequestDto> proveedorValidator,
    IValidator<ProductoRequestDto> productoValidator,
    ILogger<CatalogImportInternalController> logger) : ControllerBase {
    /// <summary>Upsert de una categoría por ID explícito.</summary>
    [HttpPost("categorias/import")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportCategoria([FromBody] ImportCategoriaDto fila) {
        var validacion = await categoriaValidator.ValidateAsync(new CategoriaRequestDto {
            Nombre = fila.Nombre,
            Descripcion = fila.Descripcion,
        });
        if (!validacion.IsValid)
            return BadRequest(new { message = Mensaje(validacion) });

        var existente = await categoriaRepository.FindByIdAsync(fila.Id);
        if (existente is null) {
            await categoriaRepository.CrearConIdExplicitoAsync(new Categoria {
                Id = fila.Id,
                Nombre = fila.Nombre,
                Descripcion = fila.Descripcion,
                IsDeleted = fila.IsDeleted,
                CreatedAt = Utc(fila.CreatedAt),
                UpdatedAt = Utc(fila.UpdatedAt),
            });
            await categoriaRepository.ResincronizarSecuenciaAsync();
            logger.LogInformation("Import: categoría {Id} creada", fila.Id);
            return CreatedAtAction(null, new { id = fila.Id });
        }

        // Misma comparación que el monolito: si no cambió nada, es un no-op.
        if (existente.Nombre == fila.Nombre && existente.Descripcion == fila.Descripcion
            && existente.IsDeleted == fila.IsDeleted)
            return NoContent();

        existente.Nombre = fila.Nombre;
        existente.Descripcion = fila.Descripcion;
        existente.IsDeleted = fila.IsDeleted;
        await categoriaRepository.UpdateAsync(existente);
        logger.LogInformation("Import: categoría {Id} actualizada", fila.Id);
        return Ok();
    }

    /// <summary>Upsert de un proveedor por código (clave de negocio, sin identidad).</summary>
    [HttpPost("proveedores/import")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportProveedor([FromBody] ImportProveedorDto fila) {
        var validacion = await proveedorValidator.ValidateAsync(new ProveedorRequestDto {
            Codigo = fila.Codigo,
            Nombre = fila.Nombre,
            Descripcion = fila.Descripcion,
            Contacto = fila.Contacto,
        });
        if (!validacion.IsValid)
            return BadRequest(new { message = Mensaje(validacion) });

        var existente = await proveedorRepository.FindByIdAsync(fila.Codigo);
        if (existente is null) {
            await proveedorRepository.CreateAsync(new Proveedor {
                Codigo = fila.Codigo,
                Nombre = fila.Nombre,
                Descripcion = fila.Descripcion,
                Contacto = fila.Contacto,
                IsDeleted = fila.IsDeleted,
                CreatedAt = Utc(fila.CreatedAt),
                UpdatedAt = Utc(fila.UpdatedAt),
            });
            logger.LogInformation("Import: proveedor {Codigo} creado", fila.Codigo);
            return CreatedAtAction(null, new { codigo = fila.Codigo });
        }

        if (existente.Nombre == fila.Nombre && existente.Descripcion == fila.Descripcion
            && existente.Contacto == fila.Contacto && existente.IsDeleted == fila.IsDeleted)
            return NoContent();

        existente.Nombre = fila.Nombre;
        existente.Descripcion = fila.Descripcion;
        existente.Contacto = fila.Contacto;
        existente.IsDeleted = fila.IsDeleted;
        await proveedorRepository.UpdateAsync(existente);
        logger.LogInformation("Import: proveedor {Codigo} actualizado", fila.Codigo);
        return Ok();
    }

    /// <summary>Upsert de un producto por ID explícito (valida FKs de categoría y proveedor).</summary>
    [HttpPost("productos/import")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportProducto([FromBody] ImportProductoDto fila) {
        var validacion = await productoValidator.ValidateAsync(new ProductoRequestDto {
            Nombre = fila.Nombre,
            Descripcion = fila.Descripcion,
            Precio = fila.Precio,
            Stock = fila.Stock,
            Imagen = fila.Imagen,
            CategoriaId = fila.CategoriaId,
            ProveedorCodigo = fila.ProveedorId,
        });
        if (!validacion.IsValid)
            return BadRequest(new { message = Mensaje(validacion) });

        if (!await categoriaRepository.ExistsByIdAsync(fila.CategoriaId))
            return BadRequest(new { message = $"La categoría con ID {fila.CategoriaId} no existe", field = "categoriaId" });

        if (fila.ProveedorId is not null && !await proveedorRepository.ExistsByIdAsync(fila.ProveedorId))
            return BadRequest(new { message = $"El proveedor con código {fila.ProveedorId} no existe", field = "proveedorId" });

        var existente = await productoRepository.FindByIdAsync(fila.Id);
        if (existente is null) {
            await productoRepository.CrearConIdExplicitoAsync(new Producto {
                Id = fila.Id,
                Nombre = fila.Nombre,
                Descripcion = fila.Descripcion,
                Precio = fila.Precio,
                Stock = fila.Stock,
                Imagen = fila.Imagen,
                CategoriaId = fila.CategoriaId,
                ProveedorId = fila.ProveedorId,
                IsDeleted = fila.IsDeleted,
                CreatedAt = Utc(fila.CreatedAt),
                UpdatedAt = Utc(fila.UpdatedAt),
                RowVersion = new byte[8],
            });
            await productoRepository.ResincronizarSecuenciaAsync();
            logger.LogInformation("Import: producto {Id} creado", fila.Id);
            return CreatedAtAction(null, new { id = fila.Id });
        }

        // Misma comparación que el monolito: si no cambió nada, es un no-op
        // (así el round-trip export→import del E2E reporta "sin cambios").
        if (existente.Nombre == fila.Nombre && existente.Descripcion == fila.Descripcion
            && existente.Precio == fila.Precio && existente.Stock == fila.Stock
            && existente.CategoriaId == fila.CategoriaId && existente.ProveedorId == fila.ProveedorId
            && existente.IsDeleted == fila.IsDeleted)
            return NoContent();

        existente.Nombre = fila.Nombre;
        existente.Descripcion = fila.Descripcion;
        existente.Precio = fila.Precio;
        existente.Stock = fila.Stock;
        existente.Imagen = fila.Imagen;
        existente.CategoriaId = fila.CategoriaId;
        existente.ProveedorId = fila.ProveedorId;
        existente.IsDeleted = fila.IsDeleted;
        await productoRepository.UpdateAsync(existente);
        logger.LogInformation("Import: producto {Id} actualizado", fila.Id);
        return Ok();
    }

    /// <summary>Npgsql exige Kind=Utc para timestamp with time zone.</summary>
    private static DateTime Utc(DateTime valor) =>
        valor.Kind == DateTimeKind.Utc ? valor : DateTime.SpecifyKind(valor, DateTimeKind.Utc);

    /// <summary>Mensaje legible de una validación fallida.</summary>
    private static string Mensaje(FluentValidation.Results.ValidationResult validacion) =>
        string.Join("; ", validacion.Errors.Select(e => e.ErrorMessage));
}
