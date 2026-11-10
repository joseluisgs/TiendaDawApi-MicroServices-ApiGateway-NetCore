using System.Text;
using BuildingBlocks.Jwt;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Hosting;
using Serilog.Sinks.SystemConsole.Themes;
using TiendaApi.Api.Infrastructures;
using TiendaApi.Api.Middleware;

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
    Log.Information("arrancando gateway...");

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

    // ?? API Gateway con YARP: no tiene lógica de negocio. Enruta la API a los
    // microservicios (R12 — única puerta pública): el cliente y la suite E2E no
    // notan el reparto porque las rutas son exactamente las del monolito.
    builder.Services.AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

    // 🔐 R6 · doble validación JWT: el gateway valida con el MISMO código que los
    // servicios (BuildingBlocks/Jwt → sección "Jwt" idéntica a la de identity).
    // Quien firma es solo identity; aquí solo se valida.
    builder.Services.AddJwtAuthentication(builder.Configuration);

          // Rate limit en el borde (ENDPOINTS §11.1): vive en el gateway,
      // que es la única puerta pública.
      builder.Services.AddRateLimitingPolicy();

      var app = builder.Build();
        // Seguridad en el borde (ENDPOINTS §11.1): el gateway es la
        // única puerta pública, así que los headers de seguridad viven AQUÍ.
        // Ojo: las páginas UI (Swagger, GraphQL,
        // índice) reciben una CSP relajada; sin ella se sirven en blanco.
        app.UseSecurityHeaders();
        app.UseRateLimiting();

    // 🔐 Si el cliente MANDA un token, el gateway lo valida (firma, issuer,
    // audience, caducidad). Uno inválido o caducado corta AQUÍ con 401 — no llega
    // ni al monolito ni al servicio. Sin token = petición anónima: la autorización
    // por endpoint (roles ADMIN/USER) la sigue haciendo el servicio destino, igual
    // que los [Authorize] del monolito (los endpoints públicos —health, signin,
    // listados— siguen sin token).
    app.UseAuthentication();
    app.Use(async (context, next) =>
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Token inválido o caducado" });
            return;
        }

        await next();
    });
    app.UseAuthorization();

    // 📺 Una línea por petición (método, ruta, estado, ms): lo que se ejecuta.
    app.UseSerilogRequestLogging();

    // El mapa completo de rutas vive en appsettings.json (ReverseProxy:Routes):
    // auth, users, categorias, proveedores, productos, pedidos, estadisticas,
    // backup, storage, graphql, hubs, health, version y swagger.
            // El gateway sirve SUS PROPIOS endpoints de infraestructura
        // (health, version, swagger) con el mismo shape que el monolito.
        app.MapHealthEndpoint();
        app.MapInfoEndpoints();
        app.MapSwaggerEndpoints();
app.MapReverseProxy();

    Log.Information("=================================================================");
    Log.Information("api-gateway · Única puerta pública (YARP, R12)");
    Log.Information("=================================================================");
    Log.Information("URL:         {Urls}", builder.Configuration["Urls"]);
    Log.Information("RUTAS:       proxy YARP + /health* + /version + / + /swagger");
    Log.Information("JWT:         validación en el borde (Bearer inválido → 401 aquí)");
    Log.Information("DESTINOS:    10 microservicios (identity :5101 … graphql :5109)");
    Log.Information("=================================================================");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "gateway terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}
