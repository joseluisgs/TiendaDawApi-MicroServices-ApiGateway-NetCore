using System.Text;
using BuildingBlocks.Jwt;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Hosting;
using Serilog.Sinks.SystemConsole.Themes;
using TiendaApi.Api.Infrastructures;

// 📺 Salida por pantalla igual que el monolito (patrón SerilogConfig): consola
// con template legible y filtros para que el mensaje del servicio no se pierda
// en el ruido de hosting/EF.
Console.OutputEncoding = Encoding.UTF8;

// registrados en el DI (compatibilidad con tests de integración).

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        theme: AnsiConsoleTheme.Code)
    .CreateLogger();

try {
    Log.Information("arrancando catalog-service...");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Salida por Serilog (patrón del monolito: builder.Logging.AddSerilog) SIN
    // UseSerilog(): esa vía reemplaza la fábrica MEL y los ILoggerProvider del
    // DI —los que usan los tests con WebApplicationFactory— dejarían de recibir
    // los mensajes con su formato nativo. ClearProviders: fuera los providers
    // MEL por defecto (console/debug) para que no dupliquen la consola.
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger);

    // DiagnosticContext: lo necesita UseSerilogRequestLogging (en el monolito lo
    // registra UseSerilog; aquí, a mano, para no tocar la fábrica MEL).
    builder.Services.AddSingleton(new DiagnosticContext(Log.Logger));
    builder.Services.AddSingleton<IDiagnosticContext>(sp => sp.GetRequiredService<DiagnosticContext>());

    // Infraestructura copiada del monolito (mismas extensiones, mismo orden).
    builder.Services.AddMvcControllers();
    builder.Services.AddApiVersioningPolicy();
    builder.Services.AddSwagger();
    builder.Services.AddFluentValidationServices();
    builder.Services.AddMediatRHandlers();
    builder.Services.AddCache(builder.Environment, builder.Configuration);

    // 🔐 R6 · doble validación JWT: el MISMO código que los demás servicios
    // (BuildingBlocks/Jwt → sección "Jwt" idéntica a la de identity).
    // La firma solo la hace identity; aquí solo se valida.
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // Datos: catalog_db (PostgreSQL, fuente de verdad) + productos_read (MongoDB).
    builder.Services.AddDatabases(builder.Configuration, builder.Environment);
    builder.Services.AddRepositories(builder.Configuration);

    // Fachadas internas + clients Refit a email, historical, identity y storage.
    builder.Services.AddServices(builder.Configuration);

    // Caché HTTP (OutputCache + Age) - solo endpoints con [OutputCache].
    builder.Services.AddOutputCacheConfig();

    // Background: reporte semanal (Refit a identity + email) y job reparador
    // de la réplica Mongo.
    builder.Services.AddBackgroundJobs();

    // SignalR (hub /hubs/productos). Los WebSockets puros /ws/* no se
    // replican en esta arquitectura (decisión del proyecto: solo SignalR).
    builder.Services.AddRealtimeSignalR();

    // Health checks (sondeo de PostgreSQL, MongoDB y Redis en /health).
    builder.Services.AddHealthChecks(builder.Environment);

    WebApplication app = builder.Build();

    // Swagger solo en desarrollo (el servicio es privado: solo se ve detrás del gateway).
    if (app.Environment.IsDevelopment()) {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // Caché HTTP: antes de MapControllers; solo afecta a los endpoints con [OutputCache].
    app.UseOutputCacheConfig();

    // 📺 Una línea por petición (método, ruta, estado, ms): lo que se ejecuta.
    app.UseSerilogRequestLogging();

    app.UseWebSockets();

    // SignalR: hubs hacia el navegador (el proxy lo hace el gateway).
    app.MapSignalRHubs();

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "catalog" }));
    app.MapControllers();

    // Semilla + read model (idempotente).
    await app.InitializeDatabaseAsync(app.Environment.IsDevelopment());

    Log.Information("=================================================================");
    Log.Information("catalog-service · Categorías, proveedores y productos (CQRS)");
    Log.Information("=================================================================");
    Log.Information("URL:         {Urls}", builder.Configuration["Urls"]);
    Log.Information("PUBLICO:     /api/v1/categorias · /api/v1/proveedores · /api/v1/productos");
    Log.Information("INTERNO:     /api/catalog/internal/*/import · /internal/productos/{id}/stock/decremento");
    Log.Information("TIEMPO REAL: /hubs/productos (SignalR)");
    Log.Information("BD:          catalog_db (PG, fuente de verdad) + catalog_db.productos_read (Mongo)");
    Log.Information("REFIT:       historical · identity · email · storage");
    Log.Information("=================================================================");

    app.Run();
} catch (Exception ex) {
    Log.Fatal(ex, "catalog-service terminó inesperadamente");
} finally {
    Log.CloseAndFlush();
}
