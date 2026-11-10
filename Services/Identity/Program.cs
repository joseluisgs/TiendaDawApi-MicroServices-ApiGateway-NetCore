using System.Text;
using BuildingBlocks.Http;
using BuildingBlocks.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Refit;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Hosting;
using Serilog.Sinks.SystemConsole.Themes;
using TiendaApi.Api.Data;
using TiendaApi.Api.Data.Seed;
using TiendaApi.Api.Infrastructures;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Usuarios;
using TiendaApi.Api.Services.Auth;
using TiendaApi.Api.Services.Email;
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

try
{
    Log.Information("arrancando identity-service...");

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

    // Infraestructura copiada del monolito (mismas extensiones, mismo orden).
    builder.Services.AddMvcControllers();
    builder.Services.AddApiVersioningPolicy();
    builder.Services.AddSwagger();
    builder.Services.AddFluentValidationServices();
    builder.Services.AddMediatRHandlers();
    builder.Services.AddCache(builder.Environment, builder.Configuration);

    // 🎓 JWT compartido (BuildingBlocks): validación + esquema Bearer + políticas.
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // Base de datos propia: identity_db (usuarios, roles y logins externos).
    builder.Services.AddDbContext<TiendaDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    // Solo Identity (sin Auth:Mode, sin cookies): misma política de contraseñas
    // que el monolito (mínimo 8, dígito, mayúscula, minúscula; sin símbolos).
    builder.Services.AddIdentityCore<User>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddRoles<IdentityRole<long>>()
        .AddSignInManager()
        .AddEntityFrameworkStores<TiendaDbContext>()
        .AddDefaultTokenProviders();

    // Emisión del JWT y servicios del dominio.
    builder.Services.AddScoped<IAuthService, IdentityAuthService>();
    builder.Services.AddScoped<IJwtService, JwtService>();
    builder.Services.AddScoped<IJwtTokenExtractor, JwtTokenExtractor>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();

    // 🎓 Conexiones con otros servicios (REST con Refit): direcciones por
    // configuración (Services:Email / Services:Historical) — en Docker, el
    // nombre del contenedor; en desarrollo, localhost.
    builder.Services.AddRefitClient<IEmailApi>()
        .ConfigureHttpClient(c => c.BaseAddress =
            new Uri(builder.Configuration["Services:Email"] ?? "http://localhost:5104"))
        .AddTiendaResilience();
    builder.Services.AddRefitClient<IHistorialApi>()
        .ConfigureHttpClient(c => c.BaseAddress =
            new Uri(builder.Configuration["Services:Historical"] ?? "http://localhost:5103"))
        .AddTiendaResilience();
    builder.Services.AddScoped<IHistorialService, HistorialRefitService>();

    // Puertas de login externo (Google, GitHub, Microsoft) — misma config Auth.
    builder.Services.AddOAuthDoors(builder.Configuration);

    var app = builder.Build();

    // Swagger solo en desarrollo (el servicio es privado: solo se ve detrás del gateway).
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // 📺 Una línea por petición (método, ruta, estado, ms): lo que se ejecuta.
    app.UseSerilogRequestLogging();

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" }));
    app.MapControllers();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<TiendaDbContext>();

        if (app.Environment.IsDevelopment())
        {
            // Como el monolito (y como catalog): en desarrollo se recrea la BD en
            // cada arranque, para que "reiniciar la API entre corridas" vuelva al
            // estado semilla - sin esto los usuarios de las suites (p. ej. newuser)
            // se acumulan y el siguiente POST da 500 por clave duplicada.
            await db.Database.EnsureDeletedAsync();
        }

        // ?? T12.5: SIEMPRE crea la BD si falta (patrón catalog). Sin esto, en
        // producción (Docker) identity_db nunca se crearía: EnsureDeleted solo
        // ocurre en desarrollo, y antes EnsureCreated vivía dentro del mismo if.
        await db.Database.EnsureCreatedAsync();

        // Semilla idempotente (roles + usuarios): en producción crea los
        // usuarios de prueba si la BD está recién creada; si ya existen, los omite.
        await IdentitySeed.InitializeAsync(scope.ServiceProvider);
    }

    Log.Information("=================================================================");
    Log.Information("identity-service · Autenticación y usuarios (dueño de la BD)");
    Log.Information("=================================================================");
    Log.Information("URL:         {Urls}", builder.Configuration["Urls"]);
    Log.Information("PUBLICO:     /api/v1/auth/* · /api/v1/users/* (vía gateway)");
    Log.Information("INTERNO:     /internal/users/{id} · /internal/users/activos");
    Log.Information("JWT:         ÚNICO emisor (HS256) + valida con BuildingBlocks");
    Log.Information("BD:          identity_db (PostgreSQL) · Refit → email, historical");
    Log.Information("=================================================================");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "identity-service terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}
