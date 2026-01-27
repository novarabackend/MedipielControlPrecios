# Infraestructura (definida)

## Entorno
- On‑premise.
- Entornos: **dev** y **prod**.

## Contenedores
- **frontend**: Angular build servido por Nginx.
- **backend**: .NET 8 (modular monolith) con API + jobs.

> Postgres va **fuera** de contenedores (servidor on‑prem existente) o como servicio aparte si lo prefieren.

## Base de datos
- SQL Server.
- Conexión desde backend vía `appsettings.json`.

## Scheduler (simple)
- **HostedService** en .NET (timer interno) para correr scraping diario + ejecuciones al cargar archivo.
- Sin servicios externos.

## Archivos Excel
- No se almacenan de forma permanente.
- Se parsean al cargar y se eliminan (opcional: guardar para auditoría si lo piden).

## Logs
- Solo logs (stdout/stderr del backend y Nginx).
- Rotación con `logrotate` en el host.

## CI/CD
- No se usa.

## Dominio
- Por definir.

## Puertos sugeridos
- Frontend: 8080 (Nginx)
- Backend: 5000 (Kestrel)
- SQL Server: 1433

## Pendientes por confirmar
- ¿SQL Server en servidor dedicado o container separado?
- Sistema operativo del host on‑prem.
- Ruta de logs y política de retención.
- Hora exacta de corrida diaria.
