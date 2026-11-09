using System.Diagnostics;

namespace TiendaApi.Api.Infrastructures;

/// <summary>
/// Health checks del <b>gateway</b>: sondea los servicios detrás de él.
///
/// 🎓 T12: hasta aquí estas rutas se proyectaban al monolito (su propio health,
/// que solo miraba sus BDs). Con el monolito retirado, el gateway —que es la
/// única puerta pública— pasa a ser quien responde por TODOS.
///
/// Contrato (idéntico al del monolito, lo exigen las suites):
/// <list type="bullet">
/// <item><c>/health</c> → 200 + JSON con <c>status</c> que contiene "OK"</item>
/// <item><c>/health/live</c> → 200 siempre (solo el proceso)</item>
/// <item><c>/health/ready</c> → 200 si los backends responden, 503 si alguno cae</item>
/// </list>
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Backends que el gateway sondea (host, puerto y nombre para el informe).</summary>
    private static readonly (string Nombre, int Puerto)[] Backends =
    [
        ("identity", 5101),
        ("storage", 5102),
        ("historical", 5103),
        ("email", 5104),
        ("catalog", 5105),
        ("orders", 5106),
        ("statistics", 5107),
        ("backup", 5108),
        ("graphql", 5109),
    ];

    /// <summary>
    /// Mapea <c>/health</c>, <c>/health/live</c> y <c>/health/ready</c>.
    /// </summary>
    /// <param name="app">Aplicación en marcha.</param>
    /// <returns>Application builder.</returns>
    public static WebApplication MapHealthEndpoint(this WebApplication app)
    {
        // Liveness: solo el proceso. No sondea nada (para orquestadores).
        app.MapGet("/health/live", () => Results.Ok(new { status = "OK" }));

        // Readiness + health agregado: sondea los backends (en paralelo).
        app.MapGet("/health/ready", async () =>
        {
            var informe = await SondearAsync();
            return informe.TodosOk
                ? Results.Ok(informe.Payload)
                : Results.Json(informe.Payload, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        app.MapGet("/health", async () =>
        {
            var informe = await SondearAsync();
            // El contrato pide 200 con "OK" mientras el gateway esté vivo;
            // el detalle de qué backend cae está en el body y en /health/ready.
            return Results.Ok(informe.Payload);
        });

        return app;
    }

    /// <summary>
    /// Sondea todos los backends en PARALELO con un timeout corto: el health del
    /// gateway no puede tardar más que lo que tarda el backend más lento.
    /// </summary>
    private static async Task<(bool TodosOk, object Payload)> SondearAsync()
    {
        var reloj = Stopwatch.StartNew();
        // Timeout corto: el health del gateway no puede tardar más que el
        // polling que hacen los orquestadores (2s en reiniciar-api.ps1).
        using var cliente = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1200) };

        var tareas = Backends.Select(async b =>
        {
            try
            {
                // Los servicios exponen `/health` (no todos `/health/live`).
                var respuesta = await cliente.GetAsync($"http://localhost:{b.Puerto}/health");
                return (b.Nombre, Ok: respuesta.IsSuccessStatusCode, Error: (string?)null);
            }
            catch (Exception ex)
            {
                return (b.Nombre, Ok: false, Error: ex.GetType().Name);
            }
        });

        var resultados = await Task.WhenAll(tareas);
        reloj.Stop();

        var ok = resultados.Count(r => r.Ok);
        var payload = new
        {
            // "OK" literal: las suites (Postman/Bruno) buscan esa cadena.
            status = ok == resultados.Length ? "OK" : "DEGRADED",
            totalDuration = reloj.Elapsed.ToString(),
            checks = resultados.Select(r => new
            {
                name = r.Nombre,
                status = r.Ok ? "OK" : "ERROR",
                description = r.Ok ? "respondiendo" : (r.Error ?? "sin respuesta"),
            }).ToArray(),
            summary = $"{ok}/{resultados.Length} servicios respondiendo",
        };

        return (ok == resultados.Length, payload);
    }
}
