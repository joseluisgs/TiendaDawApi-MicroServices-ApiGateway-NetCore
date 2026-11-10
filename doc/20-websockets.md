# 18. WebSockets y Comunicación en Tiempo Real

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/18-websockets.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/18-websockets.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** esta variante **no replica** los
> WebSockets puros `/ws/*` — usa **solo SignalR** (`/hubs/productos`,
> `/hubs/pedidos`) con el hub en el servicio dueño y proxy en el gateway.
> Ver [36 §36.7](36-api-gateway-yarp.md).
