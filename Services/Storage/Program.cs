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

try
{
    Log.Information("arrancando storage-service...");

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

    // Microservicio storage: dueño de los ficheros subidos (imágenes de productos).
    // Sirve /storage/{path} y las imágenes estáticas (/images/uploads/...).
    builder.Services.AddControllers();
    builder.Services.AddStorage();

    var app = builder.Build();

    // Prepara el directorio de subidas (patrón del monolito).
    app.InitializeStorage(app.Environment.IsDevelopment());

    // Imágenes estáticas bajo wwwroot (la URL devuelta por los uploads es /images/...).
    app.UseStaticFiles();

    app.UseAuthorization();

    // 📺 Una línea por petición (método, ruta, estado, ms): lo que se ejecuta.
    app.UseSerilogRequestLogging();

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "storage" }));
    app.MapControllers();

    Log.Information("=================================================================");
    Log.Information("storage-service · Ficheros e imágenes subidas");
    Log.Information("=================================================================");
    Log.Information("URL:         {Urls}", builder.Configuration["Urls"]);
    Log.Information("SERVE:       GET /storage/{**path} · /images/... (wwwroot)");
    Log.Information("UPLOAD:      imagenes via Refit a storage (el dueño de los ficheros)");
    Log.Information("=================================================================");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "storage-service terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}
