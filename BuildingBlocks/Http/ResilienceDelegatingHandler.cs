using Polly;

namespace BuildingBlocks.Http;

/// <summary>
/// DelegatingHandler que aplica la política de <see cref="RefitResilience"/> a cada
/// petición <b>según su verbo</b>.
///
/// 🎓 Por qué un handler propio y no <c>AddResilienceHandler</c>: esta última
/// construye UN pipeline por client y su <c>ShouldHandle</c> solo ve el
/// <c>Outcome&lt;HttpResponseMessage&gt;</c> (respuesta o excepción) — no puede
/// saber si la petición era un GET o un POST cuando falla con excepción, que es
/// justo la distinción de idempotencia. Aquí el verbo está a la vista en
/// <c>request.Method</c> y los pipelines se cachean por autoridad en
/// <see cref="ResiliencePipelineProvider"/> (patrón DelegatingHandler de los docs
/// de Polly, igual que el ejemplo de hedging).
///
/// Orden en la cadena: este handler va tras <c>AuthorizationForwardingHandler</c>;
/// si reintenta, la cabecera ya está en el request (y ese handler la guarda con
/// <c>Contains</c>), así que no se duplica en ningún orden.
/// </summary>
public sealed class ResilienceDelegatingHandler(ResiliencePipelineProvider provider)
    : DelegatingHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // El pipeline decide por verbo: lectura (GET/HEAD) reintenta todo lo
        // transitorio; escritura solo si la conexión nunca se estableció.
        var pipelines = provider.Obtener(request.RequestUri);
        var pipeline = RefitResilience.EsLectura(request.Method)
            ? pipelines.Lectura
            : pipelines.Escritura;

        return pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token),
            cancellationToken).AsTask();
    }
}
