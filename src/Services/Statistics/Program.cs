using System.Text;
using BuildingBlocks.Jwt;
using MediatR;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Hosting;
using Serilog.Sinks.SystemConsole.Themes;
using TiendaApi.Api.Infrastructures;

// ?? Salida por pantalla igual que el monolito (patrón SerilogConfig): consola
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

try
{
    Log.Information("arrancando statistics-service...");

    var builder = WebApplication.CreateBuilder(args);

    // Salida por Serilog (patrón del monolito: builder.Logging.AddSerilog) SIN
    // UseSerilog(): esa vía reemplaza la fábrica MEL y los ILoggerProvider del
    // DI —los que usan los tests con WebApplicationFactory— dejarían de recibir
    // los mensajes con su formato nativo.
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger);

    // DiagnosticContext: lo necesita UseSerilogRequestLogging.
    builder.Services.AddSingleton(new DiagnosticContext(Log.Logger));
    builder.Services.AddSingleton<IDiagnosticContext>(sp => sp.GetRequiredService<DiagnosticContext>());

    // Infraestructura (mismas extensiones y orden que el resto de servicios).
    builder.Services.AddMvcControllers();
    builder.Services.AddApiVersioningPolicy();
    builder.Services.AddSwagger();
    builder.Services.AddFluentValidationServices();
    builder.Services.AddMediatRHandlers();

    // ?? R6 — doble validación JWT: el MISMO código que los demás servicios
    // (BuildingBlocks/Jwt → sección "Jwt" idéntica a la de identity).
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // ?? SIN BASE DE DATOS (R5): statistics solo agrega lo que pide por Refit
    // a catalog, identity, orders e historical.
    builder.Services.AddServices(builder.Configuration);

    // Caché HTTP (OutputCache + Age) — solo endpoints con [OutputCache].
    builder.Services.AddOutputCacheConfig();

    // Health checks (statistics no sondea dependencias: cada servicio expone el suyo).
    builder.Services.AddHealthChecks(builder.Environment);

    var app = builder.Build();

    // Swagger solo en desarrollo (el servicio es privado: solo detrás del gateway).
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // Caché HTTP: antes de MapControllers; solo afecta a endpoints con [OutputCache].
    app.UseOutputCacheConfig();

    // Una línea por petición (método, ruta, estado, ms).
    app.UseSerilogRequestLogging();

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "statistics" }));
    app.MapHealthEndpoint();
    app.MapControllers();

    Log.Information("=================================================================");
    Log.Information("statistics-service · 9 vistas agregadas (sin BD)");
    Log.Information("=================================================================");
    Log.Information("URL:      {Urls}", builder.Configuration["Urls"]);
    Log.Information("PUBLICO:  /api/v1/estadisticas (ADMIN)");
    Log.Information("BD:       ninguna — agrega al vuelo por Refit");
    Log.Information("REFIT:    catalog · identity · orders · historical (Task.WhenAll)");
    Log.Information("=================================================================");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "statistics-service terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}
