using TiendaApi.Api.Data.Seed.Cassandra;
using TiendaApi.Api.Infrastructures;
using TiendaApi.Api.Repositories.Historial;
using TiendaApi.Api.Services.Historial;

var builder = WebApplication.CreateBuilder(args);

// Microservicio historical: dueño del histórico de precios y el feed (Cassandra).
// API interna (/internal/*) consumida por identity y catalog con Refit.
builder.Services.AddCassandra(builder.Configuration);
builder.Services.AddScoped<CassandraSeeder>();
builder.Services.AddScoped<IHistorialRepository, CassandraHistorialRepository>();
builder.Services.AddScoped<IHistorialService, HistorialService>();
builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    // IMPORTANTE: isDevelopment = false → CREATE IF NOT EXISTS, sin DROP:
    // este servicio comparte el keyspace con el monolito durante el porte y un
    // DROP aquí borraría la serie seed que esperan los E2E.
    await app.InitializeCassandraAsync(isDevelopment: false);

    // Semilla idempotente (omite si el historial ya existe).
    var seeder = scope.ServiceProvider.GetService<CassandraSeeder>();
    if (seeder is not null)
        await seeder.SeedAsync();
}

app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "historical" }));
app.MapControllers();

app.Run();
