# TiendaDaw API · Microservicios + API Gateway

Reescritura de la **API de tienda en microservicios** con un API Gateway (**YARP**), partiendo del monolito [`TiendaDawApi-Cqrs-MediatR-NetCore`](https://github.com/joseluisgs/TiendaDawApi-Cqrs-MediatR-NetCore) al que se **porta toda su funcionalidad**: mismo comportamiento y mismos endpoints, de modo que la suite E2E del monolito es válida también contra el gateway.

## Funcionalidad básica

- **Catálogo**: productos, categorías y proveedores (con lecturas desnormalizadas en MongoDB, patrón CQRS).
- **Pedidos**: carrito/estados con documentos embebidos en MongoDB.
- **Usuarios**: autenticación con **Identity + JWT + OAuth 2.0** (Google, GitHub, Microsoft).
- **Extras del monolito**: estadísticas, backup/restore, feed e histórico de precios (Cassandra), imágenes, GraphQL y notificaciones en tiempo real (SignalR).

## Arquitectura (en construcción)

Cada microservicio es una API pequeña con **su propia base de datos** y se comunican entre sí por **REST**; SignalR se usa solo hacia el cliente. El gateway es la única puerta de entrada.

| Servicio | Base de datos |
| --- | --- |
| `gateway` (YARP) | — (solo enruta) |
| `identity` (auth + users) | PostgreSQL `identity_db` |
| `categories` | PostgreSQL `categories_db` |
| `providers` | PostgreSQL `providers_db` |
| `products` | PostgreSQL `products_db` (escritura) + MongoDB `products_read` (lectura, CQRS) |
| `orders` | MongoDB `orders_db` |
| `historical` | Cassandra (histórico de precios y feed) |
| `statistics`, `backup`, `graphql` | sin base de datos (agregan por REST) |
| `storage` | ficheros (imágenes) |

| Pieza | Tecnología |
| --- | --- |
| Gateway | YARP |
| Servicios | ASP.NET Core (.NET 10) |
| Almacenamiento | PostgreSQL · MongoDB · Cassandra · Redis (caché) |

> **Estado**: primer commit. El proyecto está en fase de porte desde el monolito.
