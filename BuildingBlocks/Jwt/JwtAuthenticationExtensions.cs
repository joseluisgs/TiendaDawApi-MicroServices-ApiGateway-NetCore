using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Jwt;

/// <summary>
///     Validación JWT compartida: la MISMA instalación en el gateway y en cada
///     microservicio (defense in depth — nadie confía solo en el borde).
///     🎓 Es exactamente los mismos parámetros con los que validaba el monolito:
///     clave simétrica, issuer/audience y <c>ClockSkew = Zero</c>. La firma solo
///     la hace <c>identity</c>; aquí solo se valida.
/// </summary>
public static class JwtAuthenticationExtensions {
    /// <summary>
    ///     Añade autenticación JWT Bearer con la sección <c>Jwt</c> compartida.
    ///     Lanza si faltan la clave o la sección (mejor fallar al arrancar que
    ///     aceptar tokens inválidos).
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services,
        IConfiguration configuration) {
        JwtSettings jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
                          ?? throw new InvalidOperationException(
                              $"Falta la sección de configuración '{JwtSettings.SectionName}'");

        if (string.IsNullOrWhiteSpace(jwt.Key)) {
            throw new InvalidOperationException("Falta 'Jwt:Key' (clave compartida de firma/validación)");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => {
                options.TokenValidationParameters = new TokenValidationParameters {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        // Defensa en profundidad: fijamos explícitamente el esquema Bearer como
        // default (authenticate/challenge/forbid) para que ningún registro futuro
        // (cookies de Identity, otra librería) cambie el comportamiento.
        services.AddAuthentication(options => {
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        // Políticas idénticas a las del monolito. Todas declaran el esquema
        // Bearer explícitamente (autosuficiencia ante cambios de default scheme).
        // Los literales coinciden con los roles de la aplicación (ADMIN/USER).
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy("RequireAdminRole", new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireRole("ADMIN")
                .Build())
            .AddPolicy("RequireUserRole", new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireRole("USER", "ADMIN")
                .Build())
            .AddPolicy("AdminOnly", new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireRole("ADMIN")
                .Build());

        return services;
    }
}
