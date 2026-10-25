# Seguimiento del producto completo

Objetivo activo: optimizar la experiencia móvil e implementar la aplicación completa solicitada, según `funcionalidades-y-circuitos.md` y las decisiones de la conversación. Una entrega parcial no completa este objetivo. Este archivo conserva los pendientes entre sesiones.

Estado inicial auditado el 25/10/2026: rama `codex/rebuild-net10-angular22`, base `a514d15`. La base operativa cuenta con evidencia previa de 38 pruebas API, 26 web y dos recorridos reales; esa evidencia no demuestra que los módulos pendientes estén implementados.

| Área | Estado observado | Evidencia necesaria para completar |
|---|---|---|
| Experiencia móvil | Primera entrega verificada en Chromium contra ASP.NET y PostgreSQL reales: flujo financiero a 390×844; resumen y acción principal sobre las listas; navegación inferior; borrador, Escape/foco, rechazo real de guardado y controles de diálogos; distribución a 360×800; escritorio a 1280×900. Evidencia permanente: `web/e2e/finance-workflow.spec.ts`, commits `c5c3d27`/`1ba024d`, 31 pruebas web y tres recorridos reales; capturas locales `work/task2-*.png`. | Continuar la revisión móvil de los módulos existentes al avanzar el alcance completo; mantener las comprobaciones de 360/390 px y escritorio en cada circuito relevante |
| Acceso Google | Código configurado, sin credenciales reales verificadas | Inicio/cierre real, revocación y errores seguros, web y Android |
| Cuentas, aperturas, conciliación | Base operativa existente | Mantener ARS/USD, tipos/alias, mantenimiento, conciliación y trazabilidad completos |
| Movimientos y pagos | Base manual existente | Fuentes/categorías/comercios, filtros completos, devoluciones y vínculos, histórico y exportación |
| Tarjetas/cuotas/resúmenes | Circuito manual existente | Cuotas anteriores/restantes, deuda de arranque desglosada sin duplicar, ajustes de calendario y correcciones históricas |
| Cobros mensuales compartidos | Grupos manuales con compras de tarjeta existentes | Conservar regla por mes, ampliar a efectivo/terceros/múltiples pagadores, invitados y liquidación |
| Fechas financieras | Implementado: BusinessClock Argentina, cortes actuales y validaciones reales coherentes | 41 pruebas API aprobadas, incluidas tres regresiones de medianoche, aperturas/asientos futuros y cuotas comprometidas; publicación local actualizada |
| Categorías y presupuestos | Pendiente | CRUD, límites por período, ejecución y explicación por origen |
| Servicios/recurrencias | Pendiente | Generación idempotente por período, aumentos con vigencia, pausa, cuentas/tarjetas y desglose de obra social |
| Devoluciones/reclamos | Pendiente | Créditos esperados/acreditados y reversión vinculada, sin doble consumo ni cancelación implícita de cuotas |
| Préstamos | Pendiente | Capital/interés separados, calendario, pagos parciales y efecto de caja correcto |
| Chanchitos/ahorros | Pendiente | Reservas respaldadas por cuentas, aportes/retiros, metas/fecha estimada, sin doble reserva ni gasto ficticio |
| Proyección/disponibilidad | Pendiente | Calendario único de obligaciones/cobros, escenarios, reservas, saldo mínimo y explicación sin sumar deuda+resumen |
| DolarAPI | Pendiente | Consulta real, tipo/compra/venta/fecha visibles, caché y errores; sin cambiar importes históricos ni mezclar monedas |
| Tenants y RBAC | API y UI local implementadas: Personal/Hogar, miembros, roles, selección autorizada, invitaciones y límite de cuatro plazas. Chromium con PostgreSQL verificó aceptación entre Alice/Bob/Reader, bloqueo de capacidad, salida, archivo y que una identidad revocada pierde su contexto. Picker de perfiles sintéticos disponible solo con Development y `Authentication:DevelopmentLoginEnabled`; no representa acceso Google. El corte completo Task 4 pasó 69 API Release, 51 web y cuatro recorridos reales; la actualización local de 5082 quedó saludable. | Google real, eventual publicación externa y terminar los módulos financieros que operan sobre los espacios |
| Privacidad personal | Interfaz y API local verificadas: consentimiento opt-in, lectura sin escritura ni grupos independientes, revocación directa aun cuando hay integrantes sin consentimiento, pérdida de lectura, y salida/reingreso sin restaurar permisos. Chromium confirmó 200/403/404 contra el host y PostgreSQL reales; no usa éxitos API simulados. | Revisar cualquier nuevo circuito financiero compartido contra estos permisos y completar una prueba posterior con Google real cuando exista proyecto/dominio |
| Hogar/caja común | El espacio Hogar, sus integrantes y el registro de cuentas/movimientos comunes funcionan con alcance autorizado; la aceptación real creó un gasto compartido y dejó intacto el saldo Personal. | Aportes, adelantos, presupuesto/reparto mensual, cierre/arrastre y trazabilidad entre cuentas |
| Grupos independientes | Base manual de tarjeta; colaboración pendiente | Independientes del hogar, beneficiarios y pagadores, deudas por moneda, liquidación y reclamo de historial con confirmación |
| Archivos S3/R2 | Configuración local, sin operaciones privadas | Carga/descarga autorizadas, límites/validación, proveedor S3 compatible y prueba de aislamiento |
| Importación Excel/CSV | Análisis de fuentes y diseño existentes; sin importador | Mapeo/vista previa, errores/fórmulas/plantillas, deduplicación, corte histórico y lote idempotente |
| Último resumen | Mejora solicitada pendiente | Formatos explícitos soportados, propuesta revisable, cuotas y conciliación sin duplicación |
| Planes y Mercado Pago | Pendiente | Personal/Hogar, catálogo/precios configurables, checkout, webhooks verificados, conciliación, cobertura y cancelación |
| Administración | Pendiente | Backpanel con permisos, suscriptores/pagos/planes, auditoría y operaciones trazables |
| Web instalable/Android | Web existente; empaquetado pendiente | PWA, Capacitor mínimo sin Ionic, Google/archivos/navegación Android y compilación verificable |
| Operación VPS/Cloudflare | Host único y empaquetado existentes; arranque local de contenedor bloqueado | Arranque reproducible, proxy/TLS, R2 privado, copias/restauración, registros y comprobaciones |

## Orden de ejecución

1. Rediseño móvil de todos los módulos ya operativos y publicación visual local.
2. Fecha argentina común y migración de tenants/membresías/RBAC conservando identidades, IDs e historial personal. Antes de cerrar la base de identidad, comprobar un corte temprano de acceso y retorno de enlaces Android; el empaquetado completo sigue en la etapa final, pero esa compatibilidad no se posterga.
3. Ahorros/reservas, proyección y DolarAPI; categorías, presupuestos, recurrencias/servicios, devoluciones y préstamos; completar detalles históricos de tarjetas.
4. Hogar/caja común/privacidad y colaboración de grupos independientes sobre la autorización común.
5. Archivos privados, exportaciones e importaciones con revisión.
6. Planes, Mercado Pago y administración.
7. PWA/Android, integración de acceso/archivos y operación final.

Cada bloque requiere código, pruebas apropiadas y flujo visible real. Una integración que necesite credenciales externas no se declara probada por tener configuración o mocks. Se continuará con trabajo independiente mientras falte configuración; solo se solicitará información que no pueda obtenerse de las fuentes conectadas.

## Restricciones permanentes

Angular 22/OptimusUI 2, .NET 10, PostgreSQL, un solo host. Sin Ionic. S3 compatible para archivos y R2 al desplegar. Importes exactos ARS/USD, permisos en servidor, sin datos privados publicados. Agentes Luna. Tareas pesadas secuenciales, 4 GiB por árbol de procesos/contenedor, pausa con menos de 6 GiB disponibles. Conservar la base de vista previa y los datos que cargue el usuario.

## Decisiones confirmadas durante la ejecución

- 25/10/2026: el usuario confirma que una suscripción Hogar incluye las funciones personales de sus cuatro integrantes y el espacio familiar compartido. Esto no comparte sus datos personales: el consentimiento de lectura sigue siendo opcional y revocable. El máximo de cuatro incluye al propietario.
- 25/10/2026: Task 4 agregó la gestión visible de espacios y privacidad, perfil sintético local opt-in, pruebas reales de consentimiento/revocación/capacidad y el primer despliegue local coordinado. Se verificaron 69 pruebas API Release, 51 web y cuatro pruebas Chromium con PostgreSQL. Google real sigue sin credenciales/proyecto/dominio; caja común, grupos ampliados, comercialización y Android siguen pendientes.
- Los cobros de compras compartidas se calculan cuota por cuota según cada mes, como ya fue confirmado. La deuda completa frente al emisor sigue siendo del titular; esta decisión no está abierta.
- Se anticipa la autorización común antes de nuevos módulos compartidos para evitar repetir resolutores de propietario y migrar fondos/historial dos veces. Es organización de implementación; no elimina módulos ni cambia el alcance.

## Hallazgo temprano Android

La revisión técnica del 25/10/2026 identificó que la app Angular empaquetada tiene un origen distinto de la API y no hereda la sesión del navegador del sistema. Capacitor mínimo y el host único siguen siendo las restricciones. El siguiente corte deberá probar Google y una operación autenticada en Android, sin usar la opción de recarga remota como solución de producción. Aún no existe código Android, ni prueba real de Google o dispositivo; no se declara ese requisito completado. Referencias primarias: [Google/Credential Manager](https://developer.android.com/identity/sign-in/credential-manager-siwg-implementation), [configuración de Capacitor](https://capacitorjs.com/docs/configuration).

- 25/10/2026: el usuario pidió Product Design, delegó elegir entre alternativas y confirmó ejecución autónoma sin intervención. Se generaron tres alternativas; el constructor eligió «Panorama sereno». Rediseño en curso sobre Angular/OptimusUI reales, sin prototipo separado. No modifica el alcance de aplicación completa ni presenta diseños como funciones ya operativas.
