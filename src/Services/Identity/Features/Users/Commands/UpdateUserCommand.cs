using Microsoft.AspNetCore.Identity;
using TiendaApi.Api.Models;
using CSharpFunctionalExtensions;
using FluentValidation;
using MediatR;
using Serilog;
using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Usuarios;
using TiendaApi.Api.Mappers;
using TiendaApi.Api.Repositories.Usuarios;
using TiendaApi.Api.Services.Cache;

namespace TiendaApi.Api.Features.Users.Commands;

/// <summary>
/// Comando para actualizar un usuario.
/// </summary>
public record UpdateUserCommand(long Id, UserUpdateDto Dto)
    : IRequest<Result<UserDto, DomainError>>;

/// <summary>
/// Handler del comando UpdateUserCommand. Si viene contraseña nueva, la gestiona
/// <c>UserManager</c> (solo Identity: hash PBKDF2 — no BCrypt).
/// </summary>
public class UpdateUserCommandHandler(
    UserManager<User> userManager,
    IUserRepository repository,
    IValidator<UserUpdateDto> validator,
    ICacheService cacheService)
    : IRequestHandler<UpdateUserCommand, Result<UserDto, DomainError>>
{
    /// <inheritdoc/>
    public async Task<Result<UserDto, DomainError>> Handle(
        UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await repository.FindByIdAsync(request.Id);
        if (user is null or { IsDeleted: true })
            return Result.Failure<UserDto, DomainError>(UsuarioError.NotFound(request.Id));

        var validationResult = await validator.ValidateAsync(request.Dto, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return Result.Failure<UserDto, DomainError>(UsuarioError.ValidacionConCampos(errors));
        }

        if (!string.IsNullOrWhiteSpace(request.Dto.Email) && request.Dto.Email != user.Email)
        {
            var existing = await repository.FindByEmailAsync(request.Dto.Email);
            if (existing is not null && existing.Id != request.Id)
                return Result.Failure<UserDto, DomainError>(UsuarioError.EmailExistente(request.Dto.Email));
            user.Email = request.Dto.Email;
        }

        if (!string.IsNullOrWhiteSpace(request.Dto.Password))
        {
            // Solo Identity: el cambio de contraseña pasa por UserManager (PBKDF2).
            var reset = await userManager.RemovePasswordAsync(user);
            if (!reset.Succeeded)
            {
                var errors = new Dictionary<string, string[]>
                {
                    ["password"] = reset.Errors.Select(e => e.Description).ToArray()
                };
                return Result.Failure<UserDto, DomainError>(UsuarioError.ValidacionConCampos(errors));
            }

            var add = await userManager.AddPasswordAsync(user, request.Dto.Password);
            if (!add.Succeeded)
            {
                var errors = new Dictionary<string, string[]>
                {
                    ["password"] = add.Errors.Select(e => e.Description).ToArray()
                };
                return Result.Failure<UserDto, DomainError>(UsuarioError.ValidacionConCampos(errors));
            }
        }

        var updated = await repository.UpdateAsync(user);
        var dto = updated.ToDto();

        await cacheService.RemoveAsync($"usuarios:{request.Id}");
            

        return Result.Success<UserDto, DomainError>(dto);
    }
}
