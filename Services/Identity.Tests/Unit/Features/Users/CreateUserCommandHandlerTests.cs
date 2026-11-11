using Microsoft.AspNetCore.Identity;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Configuration;
using Moq;
using TiendaApi.Api.Dtos.Usuarios;
using TiendaApi.Api.Errors;
using TiendaApi.Api.Errors.Usuarios;
using TiendaApi.Api.Features.Users.Commands;
using TiendaApi.Api.Features.Users.Notifications;
using TiendaApi.Api.Mappers;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Usuarios;
using TiendaApi.Api.Services.Cache;

namespace TiendaApi.Tests.Unit.Features.Users;

public class CreateUserCommandHandlerTests {
    [Test]
    public async Task Handle_ComandoValido_DevuelveSuccessYPublicaNotification() {
        var repository = new Mock<IUserRepository>();
        var validator = new Mock<IValidator<RegisterDto>>();
        var mediator = new Mock<IMediator>();
        var cacheService = new Mock<ICacheService>();
        var userManager = MockuserManagerExito();
        var dto = new RegisterDto { Username = "juan", Email = "juan@test.com", Password = "password123" };
        validator.Setup(v => v.ValidateAsync(dto, It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
        repository.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync((User?)null);
        repository.Setup(r => r.FindByEmailAsync(dto.Email)).ReturnsAsync((User?)null);
        repository.Setup(r => r.SaveAsync(It.IsAny<User>())).ReturnsAsync(new User { Id = 1, UserName = "juan" });
        var handler = new CreateUserCommandHandler(userManager.Object, repository.Object, validator.Object, mediator.Object, cacheService.Object);

        var result = await handler.Handle(new CreateUserCommand(dto), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        mediator.Verify(m => m.Publish(It.IsAny<UsuarioRegistradoNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_ValidacionFalla_DevuelveValidationError() {
        var repository = new Mock<IUserRepository>();
        var validator = new Mock<IValidator<RegisterDto>>();
        var mediator = new Mock<IMediator>();
        var cacheService = new Mock<ICacheService>();
        var userManager = MockuserManagerExito();
        var dto = new RegisterDto { Username = "", Email = "juan@test.com", Password = "password123" };
        validator.Setup(v => v.ValidateAsync(dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("Username", "obligatorio")]));
        var handler = new CreateUserCommandHandler(userManager.Object, repository.Object, validator.Object, mediator.Object, cacheService.Object);

        var result = await handler.Handle(new CreateUserCommand(dto), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Handle_UsernameExistente_DevuelveError() {
        var repository = new Mock<IUserRepository>();
        var validator = new Mock<IValidator<RegisterDto>>();
        var mediator = new Mock<IMediator>();
        var cacheService = new Mock<ICacheService>();
        var userManager = MockuserManagerExito();
        var dto = new RegisterDto { Username = "juan", Email = "juan@test.com", Password = "password123" };
        validator.Setup(v => v.ValidateAsync(dto, It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
        repository.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync(new User { Id = 1, UserName = "juan" });
        var handler = new CreateUserCommandHandler(userManager.Object, repository.Object, validator.Object, mediator.Object, cacheService.Object);

        var result = await handler.Handle(new CreateUserCommand(dto), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Handle_EmailExistente_DevuelveError() {
        var repository = new Mock<IUserRepository>();
        var validator = new Mock<IValidator<RegisterDto>>();
        var mediator = new Mock<IMediator>();
        var cacheService = new Mock<ICacheService>();
        var userManager = MockuserManagerExito();
        var dto = new RegisterDto { Username = "juan", Email = "juan@test.com", Password = "password123" };
        validator.Setup(v => v.ValidateAsync(dto, It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
        repository.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync((User?)null);
        repository.Setup(r => r.FindByEmailAsync(dto.Email)).ReturnsAsync(new User { Id = 1, Email = "juan@test.com" });
        var handler = new CreateUserCommandHandler(userManager.Object, repository.Object, validator.Object, mediator.Object, cacheService.Object);

        var result = await handler.Handle(new CreateUserCommand(dto), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    /// <summary>Mock de UserManager que aprueba crear, anadir rol y cambiar password.</summary>
    private static Mock<UserManager<User>> MockuserManagerExito() {
        var store = new Mock<IUserStore<User>>();
        var mock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(u => u.CreateAsync(It.IsAny<User>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        mock.Setup(u => u.AddToRoleAsync(It.IsAny<User>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        mock.Setup(u => u.RemovePasswordAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        mock.Setup(u => u.AddPasswordAsync(It.IsAny<User>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        return mock;
    }
}
