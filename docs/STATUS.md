# Estado del proyecto

Fecha: 2026-01-27

## Resumen
- Repo creado y publicado en GitHub: novarabackend/MedipielControlPrecios.
- Estructura inicial con carpetas `backend/` y `frontend/`.
- Frontend inicializado con plantilla CoreUI Angular Admin Template.
- Backend inicializado (.NET 8) con API base, modelos y scheduler stub.

## Estructura
- `frontend/`: plantilla CoreUI (Angular) ya clonada.
- `backend/`: API .NET 8 con controladores base, modelos y DbContext.
- `README.md`: descripcion general.

## Pendientes inmediatos
1. Crear migraciones EF Core y aplicar esquema SQL Server.
2. Implementar carga real de catalogo (Excel) + validacion.
3. Implementar reportes Excel con filtros.
4. Configurar autenticacion con Keycloak (OTP por correo).

## Notas
- La propuesta funcional y estimacion se encuentran en `wireframes/PROPUESTA.html` (fuera del repo).
