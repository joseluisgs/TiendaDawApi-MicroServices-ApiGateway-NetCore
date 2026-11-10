# 23. Documentación y Versionado de APIs

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/23-documentacion.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/23-documentacion.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** cada servicio expone **su**
> `swagger.json` y el gateway los **fusiona en un único documento** en
> `/swagger` (con los `paths` por prefijo). Ver [36 §36.6](36-api-gateway-yarp.md).
