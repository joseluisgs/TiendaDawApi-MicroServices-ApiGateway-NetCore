# 32. Backup y Restore

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/32-backup-restore.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/32-backup-restore.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** el backup es ahora un **microservicio
> sin BD** (`Services/Backup`): el export lee por las rutas públicas de catalog y
> el import escribe en sus endpoints internos. Mismos formatos, `dryRun` y reglas
> que aquí. Ver [35 §35.3](35-arquitectura-microservicios.md).
