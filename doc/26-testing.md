# 24. Testing con NUnit

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/24-testing.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/24-testing.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** 11 proyectos de test (unit +
> integración por servicio con Testcontainers) y **4 suites E2E** contra el
> gateway en `:5000` (Automation, Newman, Bruno local y Bruno Docker). El
> detalle de comandos está en el README.
