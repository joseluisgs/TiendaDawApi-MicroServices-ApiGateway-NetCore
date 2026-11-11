using System.Text;
using BuildingBlocks.Jwt;
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

try {
    Log.Information("arrancando graphql-service...");

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

    // ?? R6 — doble validación JWT: el MISMO código que los demás servicios.
    // Define también la policy "AdminOnly" que usan queries y mutations.
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // Clients Refit a los dueños de los datos (R4: GraphQL es un BFF sin BD).
    builder.Services.AddServices(builder.Configuration);

    // HotChocolate: mismas queries, mutations, types y suscripciones que el
    // monolito — solo cambia quién resuelve los datos (Refit en vez de MediatR).
    builder.Services.AddGraphQL(builder.Environment);

    // Swagger del endpoint GraphQL (solo en desarrollo).
    builder.Services.AddSwagger();

    var app = builder.Build();

    // Swagger solo en desarrollo (el servicio es privado: solo detrás del gateway).
    if (app.Environment.IsDevelopment()) {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // Una línea por petición (método, ruta, estado, ms).
    app.UseSerilogRequestLogging();

    // /graphql + /graphql/ws (suscripciones).
    app.MapGraphQLEndpoints();

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "graphql" }));

    Log.Information("=================================================================");
    Log.Information("graphql-service · BFF GraphQL (queries/mutations por Refit)");
    Log.Information("=================================================================");
    Log.Information("URL:      {Urls}", builder.Configuration["Urls"]);
    Log.Information("PUBLICO:  /graphql (queries anónimas; mutations ADMIN)");
    Log.Information("BD:       ninguna — consulta a catalog y orders por Refit");
    Log.Information("REFIT:    catalog (productos/categorías) · orders (pedidos)");
    Log.Information("=================================================================");

    app.Run();
} catch (Exception ex) {
    Log.Fatal(ex, "graphql-service terminó inesperadamente");
} finally {
    Log.CloseAndFlush();
}
