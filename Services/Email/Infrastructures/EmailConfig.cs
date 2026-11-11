using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Extensiones de configuración de servicios de email del microservicio.
///
/// 🎓 A diferencia del monolito (donde en desarrollo no había consumidor), aquí
/// la cola (Channel + <see cref="EmailBackgroundService"/>) está SIEMPRE activa:
/// el endpoint <c>POST /internal/emails</c> la alimenta y el consumidor envía.
/// Desarrollo: MemoryEmailService (solo loguea, no envía nada real).
/// Producción: MailKitEmailService (SMTP real con pipeline Polly).
/// </summary>
public static class EmailConfig {
    /// <summary>
    /// Configura el servicio de email: canal + consumidor en segundo plano y
    /// la implementación de envío según el entorno.
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IWebHostEnvironment environment) {
        // Cola thread-safe compartida: endpoint (productor) → consumidor.
        services.AddSingleton(Channel.CreateUnbounded<EmailMessage>());

        // Consumidor SIEMPRE registrado: procesa la cola en segundo plano.
        services.AddHostedService<EmailBackgroundService>();

        if (environment.IsDevelopment()) {
            services.TryAddScoped<IEmailService, MemoryEmailService>();
        } else {
            // Pipeline de resiliencia (Polly) compartido por todos los
            // MailKitEmailService: Retry 3 + CircuitBreaker(3, 30s) + Timeout 10s.
            services.AddSingleton(static sp =>
                PollyConfig.BuildEmailPipeline(
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger("Polly.Email")));
            services.TryAddScoped<IEmailService, MailKitEmailService>();
        }

        return services;
    }
}
