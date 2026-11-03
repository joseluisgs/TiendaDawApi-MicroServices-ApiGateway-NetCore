using BuildingBlocks.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Refit;
using Serilog;
using TiendaApi.Api.Data;
using TiendaApi.Api.Data.Seed;
using TiendaApi.Api.Infrastructures;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Usuarios;
using TiendaApi.Api.Services.Auth;
using TiendaApi.Api.Services.Email;
using TiendaApi.Api.Services.Historial;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("arrancando identity-service...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

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
            new Uri(builder.Configuration["Services:Email"] ?? "http://localhost:5104"));
    builder.Services.AddRefitClient<IHistorialApi>()
        .ConfigureHttpClient(c => c.BaseAddress =
            new Uri(builder.Configuration["Services:Historical"] ?? "http://localhost:5103"));
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

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" }));
    app.MapControllers();

    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        // Como el monolito: crear el esquema si falta y sembrar (idempotente).
        var db = scope.ServiceProvider.GetRequiredService<TiendaDbContext>();
        await db.Database.EnsureCreatedAsync();
        await IdentitySeed.InitializeAsync(scope.ServiceProvider);
    }

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
