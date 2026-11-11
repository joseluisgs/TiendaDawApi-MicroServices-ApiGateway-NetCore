using System.Text;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Hosting;
using Serilog.Sinks.SystemConsole.Themes;
using TiendaApi.Api.Infrastructures;

// 📺 Salida por pantalla igual que el monolito (patrón SerilogConfig): consola
// con template legible y filtros para que el mensaje del servicio no se pierda
// en el ruido de hosting.
Console.OutputEncoding = Encoding.UTF8;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        theme: AnsiConsoleTheme.Code)
    .CreateLogger();

try {
    Log.Information("arrancando email-service...");

    var builder = WebApplication.CreateBuilder(args);

    // Salida por Serilog (patrón del monolito: builder.Logging.AddSerilog) SIN
    // UseSerilog(): esa vía reemplaza la fábrica MEL y los ILoggerProvider del
    // DI —los que usan los tests con WebApplicationFactory (aquí Email.Tests
    // capta los logs de la cola)— dejarían de recibir los mensajes con su
    // formato nativo. ClearProviders: fuera los providers MEL por defecto
    // (console/debug) para que no dupliquen la consola.
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger);

    // DiagnosticContext: lo necesita UseSerilogRequestLogging (en el monolito lo
    // registra UseSerilog; aquí, a mano, para no tocar la fábrica MEL).
    builder.Services.AddSingleton(new DiagnosticContext(Log.Logger));
    builder.Services.AddSingleton<IDiagnosticContext>(sp => sp.GetRequiredService<DiagnosticContext>());

    // Microservicio email: cola de envío SMTP (mejor esfuerzo). API interna
    // (/internal/emails) consumida por identity, orders y catalog con Refit: el
    // request responde rápido y el BackgroundService mueve la cola con Polly.
    builder.Services.AddEmail(builder.Environment);
    builder.Services.AddControllers();

    var app = builder.Build();

    app.UseAuthorization();

    // 📺 Una línea por petición (método, ruta, estado, ms): lo que se ejecuta.
    app.UseSerilogRequestLogging();

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "email" }));
    app.MapControllers();

    Log.Information("=================================================================");
    Log.Information("email-service · Cola de envío SMTP (mejor esfuerzo)");
    Log.Information("=================================================================");
    Log.Information("URL:         {Urls}", builder.Configuration["Urls"]);
    Log.Information("INTERNO:     POST /internal/emails → 202 y encola; el envío va en BackgroundService");
    Log.Information("CONSUMIDORES: identity (bienvenida) · orders (5 avisos) · catalog (producto, reporte)");
    Log.Information("=================================================================");

    app.Run();
} catch (Exception ex) {
    Log.Fatal(ex, "email-service terminó inesperadamente");
} finally {
    Log.CloseAndFlush();
}

/// <summary>
/// Exposición del tipo <c>Program</c> para que WebApplicationFactory&lt;Program&gt;
/// pueda arrancar el servicio en los tests de integración.
/// </summary>
public partial class Program { }
