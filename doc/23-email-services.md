# 21. Email Services: Envío de Correos Electrónicos

> **Este documento vive en el proyecto original.**
>
> La versión canónica de este tema está en
> [doc/21-email-services.md](https://github.com/joseluisgs/TiendaDawApi-NetCore/blob/develop/doc/21-email-services.md)
> del repositorio [TiendaDawApi-NetCore](https://github.com/joseluisgs/TiendaDawApi-NetCore).
>
> Esta variante (microservicios + API Gateway) no mantiene una copia propia del
> contenido general: los endpoints, los datos y el comportamiento son los mismos,
> así que se documentan una sola vez. Aquí solo se conservan los documentos
> específicos de esta arquitectura (ver [35 · Arquitectura](35-arquitectura-microservicios.md)).

> **En la arquitectura de microservicios:** el envío es ahora un **microservicio
> con cola SMTP** (`Services/Email`): los demás le mandan `POST /internal/emails`
> y reciben `202` al instante. SMTP, plantillas y destinatarios son los mismos
> que aquí. Ver [38 §38.6](38-comunicacion-microservicios.md).
