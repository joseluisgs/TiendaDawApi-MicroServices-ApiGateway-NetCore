using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using TiendaApi.Api.Dtos.Productos;
using TiendaApi.Api.MediatR.Productos.Notifications;
using TiendaApi.Api.Services.Email;

namespace TiendaApi.Tests.Unit.MediatR.Productos;

/// <summary>
///     Tests del handler que avisa al admin de un producto nuevo.
///     🎓 En el monolito encolaba el email en el mismo proceso; aquí lo manda a
///     <b>email</b> por Refit y es <b>mejor esfuerzo</b>: si el servicio no está,
///     la creación del producto no puede fallar.
/// </summary>
public class ProductoCreadoEmailHandlerTests {
    // Se recrean en [SetUp]: cada test parte de mocks limpios, sin arrastrar
    // invocaciones de los tests anteriores (NUnit reutiliza la instancia).
    private Mock<IEmailApi> _email = null!;
    private ProductoCreadoEmailHandler _handler = null!;

    [SetUp]
    public void SetUp() {
        _email = new Mock<IEmailApi>();
        _handler = new ProductoCreadoEmailHandler(_email.Object, Configuracion("admin@tienda.com"));
    }

    private static IConfiguration Configuracion(string? admin) {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Smtp:AdminEmail"] = admin
            })
            .Build();
    }

    private static ProductoCreadoNotification Notificacion() {
        return new ProductoCreadoNotification(new ProductoDto(1, "Laptop", "desc", 1299.99m, 10, null,
            new CategoriaResumenDto(1, "Electrónica"), null, DateTime.UtcNow, DateTime.UtcNow));
    }

    [Test]
    public async Task Handle_ConAdminConfigurado_EncolaElAviso() {
        await _handler.Handle(Notificacion(), CancellationToken.None);

        _email.Verify(e => e.EnqueueAsync(It.Is<EmailRequest>(m =>
            m.To == "admin@tienda.com" &&
            m.IsHtml &&
            m.Body.Contains("Laptop"))), Times.Once);
    }

    [Test]
    public async Task Handle_SinAdminConfigurado_NoEnviaNada() {
        var sinAdmin = new ProductoCreadoEmailHandler(_email.Object, Configuracion(null));

        await sinAdmin.Handle(Notificacion(), CancellationToken.None);

        _email.Verify(e => e.EnqueueAsync(It.IsAny<EmailRequest>()), Times.Never);
    }

    [Test]
    public async Task Handle_ServicioEmailCaido_NoPropagaLaExcepcion() {
        _email.Setup(e => e.EnqueueAsync(It.IsAny<EmailRequest>()))
            .ThrowsAsync(new InvalidOperationException("email-service caído"));

        Func<Task> act = async () => await _handler.Handle(Notificacion(), CancellationToken.None);

        await act.Should().NotThrowAsync("el aviso es mejor esfuerzo: no rompe la creación del producto");
    }
}
