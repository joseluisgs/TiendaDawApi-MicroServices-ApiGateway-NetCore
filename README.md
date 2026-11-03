# TiendaDaw API · Microservicios + API Gateway

Reescritura de la **API de tienda en microservicios** con un API Gateway (**YARP**), partiendo del monolito [`TiendaDawApi-Cqrs-MediatR-NetCore`](https://github.com/joseluisgs/TiendaDawApi-Cqrs-MediatR-NetCore) al que se **porta toda su funcionalidad**: mismo comportamiento y mismos endpoints, de modo que la suite E2E del monolito es válida también contra el gateway.

## Funcionalidad básica

- **Catálogo**: productos, categorías y proveedores (con lecturas desnormalizadas en MongoDB, patrón CQRS).
- **Pedidos**: carrito/estados con documentos embebidos en MongoDB.
- **Usuarios**: autenticación con **Identity + JWT + OAuth 2.0** (Google, GitHub, Microsoft).
- **Extras del monolito**: estadísticas, backup/restore, feed e histórico de precios (Cassandra), imágenes, GraphQL y notificaciones en tiempo real (SignalR).

## Arquitectura (en construcción)

Cada microservicio es una API pequeña con **su propia base de datos** y se comunican entre sí por **REST (Refit)**; SignalR se usa solo hacia el cliente. El gateway es la única puerta de entrada.

| Servicio | Contenido | Base de datos |
| --- | --- | --- |
| `gateway` (YARP) | Enrutado, auth en el borde, proxy SignalR | — (solo enruta) |
| `identity` | Auth + users (Identity + JWT + OAuth 2.0) | PostgreSQL `identity_db` |
| `catalog` | Categorías, proveedores y productos (CQRS: escritura en PostgreSQL, lectura desnormalizada en MongoDB) | PostgreSQL `catalog_db` + MongoDB `products_read` |
| `orders` | Pedidos (documento embebido) + notificaciones | MongoDB `orders_db` |
| `historical` | Histórico de precios y feed (TTL 180 días); lo consumen `catalog` e `identity` | Cassandra |
| `email` | Cola de envío de emails (bienvenida, avisos de pedidos, reportes) | sin BD (SMTP) |
| `statistics` | Las 9 consultas de estadísticas | sin BD (agrega por Refit) |
| `backup` | Export/import de datos del catálogo | sin BD (escribe vía `catalog`) |
| `graphql` | Fachada GraphQL (queries y mutations) | sin BD (agrega por Refit) |
| `storage` | Imágenes subidas | Ficheros |

| Pieza | Tecnología |
| --- | --- |
| Gateway | YARP |
| Servicios | ASP.NET Core (.NET 10) |
| Almacenamiento | PostgreSQL · MongoDB · Cassandra · Redis (caché) |

> **Estado**: primer commit. El proyecto está en fase de porte desde el monolito.
