# 2. Arquitectura Global y Pipeline HTTP de ASP.NET Core

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/02-arquitectura-pipeline-http.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/02-arquitectura-pipeline-http.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** el pipeline HTTP se ejecuta **en
> cada servicio** (su propio `Program.cs`, con los mismos middlewares aquí
> descritos) y el gateway añade **el suyo** delante: validación JWT de borde,
> CORS, rate limit y proxy de hubs. Ver [36 · API Gateway](36-api-gateway-yarp.md).
