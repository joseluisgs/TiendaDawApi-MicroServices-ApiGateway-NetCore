using TiendaApi.Api.Infrastructures;

var builder = WebApplication.CreateBuilder(args);

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
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "storage" }));
app.MapControllers();

app.Run();
