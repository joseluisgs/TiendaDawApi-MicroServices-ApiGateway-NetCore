using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.Features.Users.Queries;

namespace TiendaApi.Api.Controllers;

/// <summary>
/// API interna de <c>identity</c> (rutas <c>/internal/*</c>, R7): la consumen
/// <b>orders</b> (validar el usuario al crear un pedido) y <b>catalog</b>
/// (usuarios activos para el reporte semanal) por Refit. El cliente NUNCA
/// llega aquí (no hay ruta en el gateway) y solo es accesible por la red
/// interna (R12).
/// </summary>
[ApiController]
[Route("internal/users")]
[Produces("application/json")]
public class UsersInternalController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Existe y no está borrado → 200 con el usuario; si no, 404.
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetUsuario([FromRoute] long id)
    {
        var resultado = await mediator.Send(new GetUsuarioInternoQuery(id));
        if (resultado.IsFailure)
            return NotFound(new { message = resultado.Error.Message });

        return Ok(resultado.Value);
    }

    /// <summary>
    /// Usuarios activos (<c>!IsDeleted</c>, ordenados por email) → 200 con la lista.
    /// </summary>
    [HttpGet("activos")]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetActivos()
        => Ok(await mediator.Send(new GetUsuariosActivosQuery()));
}
