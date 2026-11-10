# 19. GraphQL con HotChocolate

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/19-graphql.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/19-graphql.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** GraphQL es ahora un **microservicio
> BFF sin BD** (`Services/GraphQL`) que consume por Refit a los dueños. El
> esquema (queries, mutations, suscripciones) es **idéntico** al aquí
> documentado. Ver [35 §35.3](35-arquitectura-microservicios.md).
