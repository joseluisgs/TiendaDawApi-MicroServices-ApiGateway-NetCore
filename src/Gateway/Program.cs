var builder = WebApplication.CreateBuilder(args);

// 🎓 API Gateway con YARP: no tiene lógica de negocio. Enruta TODA la API del
// monolito (que sigue detrás como backend temporal) para que el cliente —y la
// suite E2E— no note el reparto. En las fases siguientes, cada ruta irá
// cambiando de destino hacia su microservicio.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// El mapa completo de rutas vive en appsettings.json (ReverseProxy:Routes):
// auth, users, categorias, proveedores, productos, pedidos, estadisticas,
// backup, storage, graphql, hubs, health, version y swagger.
app.MapReverseProxy();

app.Run();
