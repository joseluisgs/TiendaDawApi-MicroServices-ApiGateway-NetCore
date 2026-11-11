using System.Text;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Hosting;
using Serilog.Sinks.SystemConsole.Themes;
using TiendaApi.Api.Data.Seed.Cassandra;
using TiendaApi.Api.Infrastructures;
using TiendaApi.Api.Repositories.Historial;
using TiendaApi.Api.Services.Historial;

// 📺 Salida por pantalla igual que el monolito (patrón SerilogConfig): consola
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
    Log.Information("arrancando historical-service...");

    var builder = WebApplication.CreateBuilder(args);

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

    // Microservicio historical: dueño del histórico de precios y el feed (Cassandra).
    // API interna (/internal/*) consumida por identity y catalog con Refit.
    builder.Services.AddCassandra(builder.Configuration);
    builder.Services.AddScoped<CassandraSeeder>();
    builder.Services.AddScoped<IHistorialRepository, CassandraHistorialRepository>();
    builder.Services.AddScoped<IHistorialService, HistorialService>();
    builder.Services.AddControllers();

    var app = builder.Build();

    // SIEMPRE inicializa el keyspace (patrón catalog/identity): sin esto, en
    // producción el keyspace `tienda` no llegaría
    // a crearse. Con isDevelopment: false es CREATE IF NOT EXISTS + tablas
    // IF NOT EXISTS: nunca hace DROP, así que en desarrollo el seed del E2E se
    // conserva igual que hasta ahora.
    using (var scope = app.Services.CreateScope()) {
        await app.InitializeCassandraAsync(isDevelopment: false);

        // Semilla idempotente (omite si el historial ya existe).
        var seeder = scope.ServiceProvider.GetService<CassandraSeeder>();
        if (seeder is not null)
            await seeder.SeedAsync();
    }

    app.UseAuthorization();

    // 📺 Una línea por petición (método, ruta, estado, ms): lo que se ejecuta.
    app.UseSerilogRequestLogging();

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "historical" }));
    app.MapControllers();

    Log.Information("=================================================================");
    Log.Information("historical-service · Histórico de precios y feed (Cassandra)");
    Log.Information("=================================================================");
    Log.Information("URL:         {Urls}", builder.Configuration["Urls"]);
    Log.Information("INTERNO:     /internal/historial/feed · /productos/{{id}} · /vistas · /precios");
    Log.Information("CONSUMIDORES: identity (feed) · catalog (serie, vistas, precios)");
    Log.Information("CASSANDRA:   keyspace 'tienda'");
    Log.Information("=================================================================");

    app.Run();
} catch (Exception ex) {
    Log.Fatal(ex, "historical-service terminó inesperadamente");
} finally {
    Log.CloseAndFlush();
}
