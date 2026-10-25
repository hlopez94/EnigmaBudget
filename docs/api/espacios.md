# Contrato de espacios y privacidad

Contrato del próximo bloque; no declara estos endpoints ya disponibles. JSON camelCase, UUID/fecha/dinero conservan las convenciones operativas. Todas las mutaciones requieren sesión y X-CSRF-TOKEN. Sujeto/rol/autor salen del servidor.

## Contexto y compatibilidad

X-Workspace-Id selecciona un espacio y nunca autoriza por sí mismo. Sin cabecera se usa el Personal propio; inválida/ajena404, permiso insuficiente403, versión obsoleta409. No se vuelve al Personal silenciosamente. Onboarding permanece siempre en el Personal propio, independientemente de esa cabecera.

WorkspaceDto: `{id,name,ownerDisplayName:null|string,membershipVersion:null|int,kind:'Personal'|'Household',role:'Owner'|'Admin'|'Member'|'Reader',isSharedPersonalRead,isArchived,version,permissions:{read,readGroups,recordFinance,manageResources,correctHistory,manageMembers,managePrivacy}}`.

GET /api/me: `{id,displayName,email:null|string,emailVerified,personalWorkspaceId}`. El subject interno no se toma del cliente ni se fusiona por email.

ownerDisplayName identifica al titular autorizado para distinguir varios Personales compartidos con el mismo nombre; no expone subject ni correo. Se devuelve solo junto a un espacio que el solicitante puede consultar. membershipVersion es la versión de la membresía activa del solicitante, distinta de version del espacio; vale null en una concesión personal sin membresía. Permite que Member/Reader salgan del Hogar sin consultar la lista administrativa de integrantes.

GET /api/workspaces devuelve Personal propio, Hogares activos/accesibles y Personales con concesión válida de lectura. POST /api/workspaces: `{id,kind:'Household',name}` crea Hogar y Owner de forma atómica; no compra una suscripción ni crea nuevos Personales arbitrarios. Reintento idéntico del id devuelve el mismo recurso; datos distintos409.

GET /api/workspaces/{id} devuelve WorkspaceDto. PUT: `{version,name,isArchived}` por Owner/Admin. Archivar conserva historia y vuelve la actividad financiera de ese espacio de solo lectura.

## Permisos financieros

| Operación | Permiso requerido |
|---|---|
| Consultas financieras generales | read |
| Detalles de grupos y sus reglas/participantes/cobros | readGroups; una concesión PersonalReadGrant no lo otorga |
| Registrar ingreso/gasto, pagos/cobros, transferencia, compra, borrador/confirmación/pago de resumen | recordFinance |
| Alta/edición/archivo de cuentas y tarjetas, crear grupo y cambiar/confirmar sus reglas | manageResources |
| Ajuste de saldo y anulación de operación confirmada | correctHistory |
| Gestión de integrantes/invitaciones | manageMembers |
| Consentimiento de lectura personal | managePrivacy, siempre del propietario del Personal |

Owner/Admin tienen lectura, registro, recursos, corrección y miembros en el espacio. Member lee y registra actividad; Reader solo lee. El Owner del Personal tiene su privacidad; un Admin familiar nunca la obtiene por su rol. La titularidad de facturación se implementará separada de estos permisos.

Una concesión PersonalReadGrant permite ver las finanzas del titular, pero no los detalles de sus grupos independientes. GETgroups/receivables exige readGroups y el panorama servido mediante la concesión omite arrays de grupos/participantes/cobros identificables. Una referencia groupId en una compra no concede acceso al grupo. Los miembros del Hogar tampoco heredan membresía de grupos externos.

El contexto financiero común reemplaza las consultas por OwnerSubject en cada handler. Aprobar una cabecera y luego seguir consultando el Personal sería incorrecto. Rechazos no crean efectos financieros ni modifican datos ajenos.

## Integrantes e historia

GET /api/workspaces/{id}/members: miembros con `{userId,displayName,role,active,version}`. PUT /members/{userId}: `{version,role}` para Owner/Admin; no cambia Owner. DELETE /members/{userId} con `{version}` desactiva sin borrar historia, con permisos y protección del Owner. POST /leave `{version}` usa membershipVersion de la membresía propia y permite salir a no propietarios; no usa la versión del espacio. POST /owner-transfer `{version,userId}` solo Owner de Hogar a integrante activo; Personal no transfiere titularidad. No cambia responsable comercial por implicación.

Hasta cuatro plazas de Hogar, contando Owner y pendientes no vencidas. Cambios de plazas/roles/propiedad se serializan sobre el Hogar. Participar en un Hogar no incorpora a sus grupos ni habilita lectura personal automáticamente.

GET /api/workspaces/{id}/audit por Owner/Admin muestra actor, acción, recurso, fecha UTC y cambios seguros, sin tokens/credenciales. Efectos auditados se guardan en la transacción de sus modificaciones; una lectura o un reintento monetario idéntico no duplica efectos.

## Invitaciones

POST /api/workspaces/{id}/invitations: `{id,email,role:'Admin'|'Member'|'Reader'}`. Requiere permiso y plaza, token criptográfico256bits con hash almacenado, siete días de vigencia, uso único. Respuesta primera emisión: `{id,email,role,status,expiresAt,version,shareUrl}`. Solo la primera respuesta devuelve shareUrl; no se guarda token legible. Reintento idéntico devuelve metadata/shareUrl:null sin reservar otra plaza. La UI explica cómo revocar y emitir otro enlace si se perdió; no simula recuperar un secreto que no existe.

GET /invitations en el Hogar lista metadata a sus administradores. DELETE /invitations/{invitationId} `{version}` revoca. El token viaja en fragmento de enlace, no como query registrada en el servidor; no hay envío real de email implícito en la creación del enlace.

POST /api/invitations/preview: `{token}` autenticado; POST /api/invitations/accept: `{token}`. Verifican hash, vigencia, revocación, correo Google verificado coincidente, capacidad y uso único. Devuelven metadata mínima y, al aceptar, WorkspaceDto/membresía. El mismo usuario puede reintentar una aceptación exitosa sin crear otra membresía; un token usado no reactiva una membresía luego revocada. Otro correo/no verificado no acepta. No se enlazan identidades por nombre/correo.

Google conserva su handler .NET. La afirmación de correo verificado se incorpora desde el perfil validado del proveedor (email_verified o el campo oficial equivalente del endpoint configurado), nunca de un request de cliente. [Referencia Google](https://developers.google.com/identity/openid-connect/openid-connect). La autorización de recursos se comprueba en servidor; [referencia ASP.NET](https://learn.microsoft.com/en-us/aspnet/core/mvc/security/authorization/resource-based?view=aspnetcore-10.0).

## Consentimiento personal

GET /api/personal-sharing devuelve los Hogares del propietario con `{householdId,householdName,enabled,version,sharedWithUserIds,needsAudienceRenewal}`. PUT /api/personal-sharing/{householdId}: `{version,enabled}`. Solo el propietario del Personal y estando activo en ese Hogar. Default deshabilitado. Activar captura a los integrantes actuales y la generación vigente de sus membresías; un integrante nuevo o que reingresa necesita que el propietario renueve el consentimiento. Reenviar enabled:true con versión vigente renueva ese alcance; needsAudienceRenewal indica integrantes actuales aún no cubiertos. sharedWithUserIds identifica solo lectores actualmente cubiertos. La UI explica «Compartir con los integrantes actuales», sin habilitar lectura automática a futuros invitados.

Acceso requiere propietario y lector aún activos en el Hogar, concesión vigente y la generación de membresía que recibió el consentimiento. Una generación cambia al reactivar una membresía, no por un simple cambio de rol. El identificador/generación lo decide el servidor, sin depender de timestamps iguales durante pruebas ni de una versión incrementada por cambios de rol. Permiso efectivo Reader sobre el Personal ajeno, nunca escritura. Revocar/salir/quitar al propietario invalida concesiones correspondientes; reingresar no vuelve a compartir sin consentimiento. No se pueden retirar copias ya vistas/exportadas; se explica en la UI. El chequeo se reutiliza después en archivos/exportación/sincronización.

## Desarrollo y evidencia

Para pruebas de navegador, Development puede configurar identidades sintéticas adicionales con email verificado simulado. Solo con login de desarrollo opt-in; rechaza claves no configuradas, ruta inexistente en Production. El usuario local actual sigue funcionando sin body nuevo. Nunca aceptar una identidad arbitraria en producción ni dar administración al primer usuario.

Precios, cobertura comercial verificada, Mercado Pago y administración de plataforma continúan en su bloque comercial. Este contrato implementa límites/permisos de espacios; no cobra ni afirma suscripciones activas.
