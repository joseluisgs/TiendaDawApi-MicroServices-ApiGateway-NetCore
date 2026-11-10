# 31. Estadísticas: Cálculo al Vuelo

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/31-estadisticas.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/31-estadisticas.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** las estadísticas son ahora un
> **microservicio sin BD** (`Services/Statistics`) que agrega por Refit a
> catalog, identity, orders y historical (reenviando el JWT del llamante). Los
> cálculos aquí documentados son **idénticos**. Ver [35 §35.3](35-arquitectura-microservicios.md).
