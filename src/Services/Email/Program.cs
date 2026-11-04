using TiendaApi.Api.Infrastructures;

var builder = WebApplication.CreateBuilder(args);

// Microservicio email: cola de envío SMTP (mejor esfuerzo). API interna
// (/internal/emails) consumida por identity, orders y catalog con Refit: el
// request responde rápido y el BackgroundService mueve la cola con Polly.
builder.Services.AddEmail(builder.Environment);
builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "email" }));
app.MapControllers();

app.Run();

/// <summary>
/// Exposición del tipo <c>Program</c> para que WebApplicationFactory&lt;Program&gt;
/// pueda arrancar el servicio en los tests de integración.
/// </summary>
public partial class Program { }
