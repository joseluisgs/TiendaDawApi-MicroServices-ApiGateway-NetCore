using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using TiendaApi.Api.Models;
using TiendaApi.Api.Repositories.Pedidos;

namespace TiendaApi.Tests.Integration.TestContainers.Pedidos;

/// <summary>
/// Tests de integración del repositorio de pedidos sobre <b>MongoDB real</b>.
///
/// 🎓 Recorren el camino de persistencia de orders: documentos con ObjectId,
/// items desnormalizados, paginación por ventana y pertenencia por usuario
/// (lo que usan los endpoints <c>/me</c> para no filtrar pedidos ajenos).
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PedidosRepositoryIntegrationTests
{
    private const string DatabaseName = "it_orders_repo";
    private IMongoDatabase _database = null!;
    private PedidosNativeRepository _repo = null!;

    private static Pedido Pedido(long userId, string nombre, decimal total, DateTime? creado = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        UserId = userId,
        Items = [new PedidoItem { ProductoId = 1, NombreProducto = nombre, Cantidad = 1, Precio = total, Subtotal = total }],
        Total = total,
        Estado = PedidoEstado.PENDIENTE,
        CreatedAt = creado ?? DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [SetUp]
    public void Setup()
    {
        // GetMongoDatabase hace drop: cada test arranca de colección vacía.
        _database = AssemblyContainerFixture.GetMongoDatabase(DatabaseName);
        _repo = new PedidosNativeRepository(_database, NullLogger<PedidosNativeRepository>.Instance);
    }

    [Test]
    public async Task Save_LuegoFindById_DevuelveElPedidoConSusItems()
    {
        var guardado = await _repo.SaveAsync(Pedido(1, "Laptop Dell XPS 15", 1299.99m));

        guardado.Id.Should().NotBe(ObjectId.Empty, "MongoDB asigna el ObjectId");

        var leido = await _repo.FindByIdAsync(guardado.Id.ToString());

        leido.Should().NotBeNull();
        leido!.UserId.Should().Be(1);
        leido.Total.Should().Be(1299.99m);
        leido.Estado.Should().Be(PedidoEstado.PENDIENTE);
        leido.Items.Should().ContainSingle()
            .Which.NombreProducto.Should().Be("Laptop Dell XPS 15",
                "los items van desnormalizados en el mismo documento");
    }

    [Test]
    public async Task FindById_Inexistente_DevuelveNull()
    {
        var leido = await _repo.FindByIdAsync(ObjectId.GenerateNewId().ToString());

        leido.Should().BeNull("un miss no es un error: es un 404 en la capa superior");
    }

    [Test]
    public async Task FindAll_PorFecha_Descendente()
    {
        await _repo.SaveAsync(Pedido(1, "Viejo", 10m, DateTime.UtcNow.AddDays(-2)));
        await _repo.SaveAsync(Pedido(1, "Nuevo", 20m, DateTime.UtcNow));

        var todos = (await _repo.FindAllAsync()).ToList();

        todos.Should().HaveCount(2);
        todos[0].Total.Should().Be(20m, "el más reciente primero");
        todos[1].Total.Should().Be(10m);
    }

    [Test]
    public async Task FindByUserId_SoloDevuelveLosDelUsuario()
    {
        await _repo.SaveAsync(Pedido(1, "Del uno", 10m));
        await _repo.SaveAsync(Pedido(2, "Del dos", 20m));

        var delUno = (await _repo.FindByUserIdAsync(1)).ToList();

        delUno.Should().ContainSingle().Which.Total.Should().Be(10m,
            "el filtrado por usuario es lo que sostiene /pedidos/me");
    }

    [Test]
    public async Task FindByUserIdPaged_DevuelvePaginaYTotal()
    {
        for (var i = 1; i <= 5; i++)
            await _repo.SaveAsync(Pedido(1, "P" + i, i, DateTime.UtcNow.AddMinutes(i)));

        var (items, total) = await _repo.FindByUserIdPagedAsync(1, page: 0, size: 2);

        total.Should().Be(5, "el total es del filtro, no de la página");
        items.Should().HaveCount(2);
        items.First().Total.Should().Be(5m, "ordenado por fecha descendente");
    }

    [Test]
    public async Task FindByRangoFechas_IncluyeElInicio_ExcluyeElFinal()
    {
        var desde = DateTime.UtcNow.AddDays(-1);
        var hasta = DateTime.UtcNow;

        await _repo.SaveAsync(Pedido(1, "Antes", 1m, desde.AddDays(-1)));
        await _repo.SaveAsync(Pedido(1, "Dentro", 2m, desde.AddHours(1)));
        await _repo.SaveAsync(Pedido(1, "Después", 3m, hasta.AddMinutes(1)));

        var ventana = (await _repo.FindByRangoFechasAsync(desde, hasta)).ToList();

        ventana.Should().ContainSingle(p => p.Total == 2m,
            "ventana semiabierta [desde, hasta): la usa statistics");
    }

    [Test]
    public async Task Update_CambiaElEstado_SinDuplicar()
    {
        var pedido = await _repo.SaveAsync(Pedido(1, "P", 10m));

        pedido.Estado = PedidoEstado.ENVIADO;
        await _repo.UpdateAsync(pedido);

        var todos = (await _repo.FindAllAsync()).ToList();

        todos.Should().ContainSingle("el update sobrescribe por _id, no inserta otro");
        todos.Single().Estado.Should().Be(PedidoEstado.ENVIADO);
    }
}
