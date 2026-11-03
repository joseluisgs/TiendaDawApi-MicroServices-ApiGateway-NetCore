using Microsoft.AspNetCore.Mvc;
using TiendaApi.Api.Dtos.Historial;
using TiendaApi.Api.Services.Historial;

namespace TiendaApi.Api.Controllers;

/// <summary>
/// API interna del microservicio <c>historical</c> (rutas <c>/internal/*</c>, R7):
/// la consumen identity (feed) y catalog (serie y vistas) por Refit.
///
/// 🎓 Este servicio no valida la existencia del producto: esa certificación la
/// hace el dueño (catalog) antes de llamar. Aquí solo se sirven las series.
/// </summary>
[ApiController]
[Route("internal/historial")]
public class HistoricalInternalController(IHistorialService service) : ControllerBase
{
    /// <summary>Feed de actividad (últimos productos vistos) de un usuario.</summary>
    [HttpGet("feed")]
    [ProducesResponseType(typeof(IReadOnlyList<VistaFeedDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<VistaFeedDto>>> GetFeed(
        [FromQuery] long usuarioId,
        [FromQuery] int limit = 20)
        => Ok(await service.GetFeedVistasAsync(usuarioId, limit));

    /// <summary>Serie histórica de precios de un producto.</summary>
    [HttpGet("productos/{productoId:long}")]
    [ProducesResponseType(typeof(IReadOnlyList<HistorialPrecioDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HistorialPrecioDto>>> GetHistorial(
        [FromRoute] long productoId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] int limit = 50)
    {
        var resultado = await service.GetHistorialPreciosAsync(productoId, desde, hasta, limit);
        if (resultado.IsFailure)
            return BadRequest(new { message = resultado.Error.Message });

        return Ok(resultado.Value);
    }

    /// <summary>Registra la vista de un usuario sobre un producto (el nombre lo aporta el dueño).</summary>
    [HttpPost("vistas")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RegistrarVista([FromBody] RegistrarVistaRequest body)
    {
        var resultado = await service.RegistrarVistaAsync(body.UsuarioId, body.ProductoId, body.NombreProducto);
        if (resultado.IsFailure)
            return BadRequest(new { message = resultado.Error.Message });

        return NoContent();
    }
}

/// <summary>Cuerpo de escritura de una vista (nombre aportado por el dueño del producto).</summary>
public sealed record RegistrarVistaRequest(long UsuarioId, long ProductoId, string NombreProducto);
