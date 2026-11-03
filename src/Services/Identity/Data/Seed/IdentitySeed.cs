using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TiendaApi.Api.Models;

namespace TiendaApi.Api.Data.Seed;

/// <summary>
/// Semilla del microservicio identity: roles y usuarios de prueba.
///
/// 🎓 Es el modo Identity del <c>SqlSeeder</c> del monolito, reducido a lo que
/// este servicio es dueño: crea los roles ADMIN y USER (si faltan) y los dos
/// usuarios de <see cref="SeedCredentials"/> con UserManager (hash PBKDF2) y su
/// rol en AspNetUserRoles. Idempotente: un usuario ya creado no se toca.
/// </summary>
public static class IdentitySeed
{
    /// <summary>Sembran roles y usuarios si faltan.</summary>
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(IdentitySeed));

        using var scope = serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<long>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // Roles primero: AddToRoleAsync falla si el rol no existe.
        foreach (var rol in new[] { UserRoles.ADMIN, UserRoles.USER })
        {
            if (!await roleManager.RoleExistsAsync(rol))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<long>(rol));
                if (result.Succeeded)
                    logger.LogInformation("Rol {Role} creado", rol);
                else
                    logger.LogError("Error al crear rol {Role}: {Errors}",
                        rol, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        // Credenciales unificadas (SeedCredentials): las mismas que en el monolito.
        var usuarios = new List<(User Usuario, string Password)>
        {
            (new() { UserName = SeedCredentials.AdminUsername, Email = "admin@tienda.com", Role = UserRoles.ADMIN },
                SeedCredentials.AdminPassword),
            (new() { UserName = SeedCredentials.UserUsername, Email = "userdaw@tienda.com", Role = UserRoles.USER },
                SeedCredentials.UserPassword),
        };

        foreach (var (usuario, password) in usuarios)
        {
            if (await userManager.FindByEmailAsync(usuario.Email!) is not null)
                continue;

            var result = await userManager.CreateAsync(usuario, password);
            if (!result.Succeeded)
            {
                logger.LogWarning("Error creando usuario {Email}: {Errors}",
                    usuario.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
                continue;
            }

            if (usuario.Role == UserRoles.ADMIN)
                await userManager.AddToRoleAsync(usuario, UserRoles.ADMIN);

            logger.LogInformation("Usuario creado: {Email}", usuario.Email);
        }
    }
}
