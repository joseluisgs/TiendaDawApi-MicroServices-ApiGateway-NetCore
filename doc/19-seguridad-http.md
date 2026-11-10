# 17. Seguridad HTTP: HSTS, HTTPS y Security Headers

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/17-seguridad-http.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/17-seguridad-http.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** CORS, rate limit y security headers
> se resuelven en el **gateway** (la única puerta pública); cada servicio aplica
> además los suyos en su propio pipeline. Ver [36 §36.1](36-api-gateway-yarp.md).
