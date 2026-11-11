using System.Text;
using BuildingBlocks.Jwt;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Hosting;
using Serilog.Sinks.SystemConsole.Themes;
using TiendaApi.Api.Infrastructures;
using TiendaApi.Api.Services.Auth;

// ?? Salida por pantalla igual que el monolito (patrón SerilogConfig): consola
// con template legible y filtros para que el mensaje del servicio no se pierda
// en el ruido de hosting/EF.
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
    Log.Information("arrancando orders-service...");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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
    builder.Services.AddCache(builder.Environment, builder.Configuration);

    // ?? R6 — doble validación JWT: el MISMO código que los demás servicios
    // (BuildingBlocks/Jwt → sección "Jwt" idéntica a la de identity).
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // Datos: orders_db (MongoDB) — los productos viven en catalog y los
    // usuarios en identity, ambos por Refit.
    builder.Services.AddDatabases(builder.Configuration, builder.Environment);
    builder.Services.AddRepositories(builder.Configuration);

    // Clients Refit a catalog, identity y email.
    builder.Services.AddServices(builder.Configuration);

    builder.Services.AddSingleton<IJwtService,
        JwtService>();
    builder.Services.AddSingleton<IJwtTokenExtractor,
        JwtTokenExtractor>();

    // Caché HTTP (OutputCache + Age) — solo endpoints con [OutputCache].
    builder.Services.AddOutputCacheConfig();

    builder.Services.AddRealtimeSignalR();

    // Health checks (sondeo de MongoDB y Redis en /health).
    builder.Services.AddHealthChecks(builder.Environment);

    WebApplication app = builder.Build();

    // Swagger solo en desarrollo (el servicio es privado: solo detrás del gateway).
    if (app.Environment.IsDevelopment()) {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // Caché HTTP: antes de MapControllers; solo afecta a endpoints con [OutputCache].
    app.UseOutputCacheConfig();

    // Una línea por petición (método, ruta, estado, ms).
    app.UseSerilogRequestLogging();

    app.UseWebSockets();

    // SignalR: hub hacia el navegador (el proxy lo hace el gateway).
    app.MapSignalRHubs();

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "orders" }));
    app.MapControllers();

    // Semilla de pedidos (idempotente): sin ella el E2E de paginación no puede
    // comprobar el header Link (exige más de una página).
    await app.InitializeDatabaseAsync();

    Log.Information("=================================================================");
    Log.Information("orders-service · Pedidos (MongoDB + Refit a catalog/identity/email)");
    Log.Information("=================================================================");
    Log.Information("URL:         {Urls}", builder.Configuration["Urls"]);
    Log.Information("PUBLICO:     /api/v1/pedidos (ADMIN y me)");
    Log.Information("TIEMPO REAL: /hubs/pedidos (SignalR)");
    Log.Information("BD:          orders_db (MongoDB, colección pedidos con items desnormalizados)");
    Log.Information("REFIT:       catalog (stock + snapshot) · identity (usuario) · email (avisos)");
    Log.Information("=================================================================");

    app.Run();
} catch (Exception ex) {
    Log.Fatal(ex, "orders-service terminó inesperadamente");
} finally {
    Log.CloseAndFlush();
}
