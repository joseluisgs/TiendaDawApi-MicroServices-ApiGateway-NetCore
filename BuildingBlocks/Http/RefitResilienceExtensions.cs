using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Http;

/// <summary>
/// Extensión <c>AddTiendaResilience()</c> para los clients Refit entre
/// microservicios.
/// </summary>
public static class RefitResilienceExtensions {
    /// <summary>
    /// Añade la política de resiliencia al client: timeout por intento (10 s
    /// lecturas / 5 s escrituras), reintento por verbo y cortacircuitos por
    /// backend (ver <see cref="RefitResilience"/>).
    ///
    /// Se registra DESPUÉS de <c>AuthorizationForwardingHandler</c> cuando existe:
    /// el reenvío de JWT ocurre una sola vez por petición lógica y el reintento
    /// reutiliza el mismo request (la cabecera ya está puesta, no se duplica).
    /// </summary>
    public static IHttpClientBuilder AddTiendaResilience(this IHttpClientBuilder builder) {
        builder.Services.TryAddSingleton(static sp =>
            new ResiliencePipelineProvider(sp.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddTransient<ResilienceDelegatingHandler>();

        return builder.AddHttpMessageHandler<ResilienceDelegatingHandler>();
    }
}
