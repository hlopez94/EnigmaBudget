# Espacios, Hogar y privacidad

Bloque del objetivo de aplicación completa. Reutiliza el núcleo financiero y la UI móvil; no crea otro backend ni reemplaza Google.

## Decisiones confirmadas

Personal, Hogar hasta cuatro personas y grupos independientes. El usuario confirmó que una suscripción Hogar cubre las funciones personales de sus cuatro integrantes y el espacio familiar. La cobertura comercial nunca implica acceso a los datos personales: compartir lectura general es opcional, privado por defecto y revocable. Los cobros compartidos de tarjetas siguen el reparto de cada cuota/mes; el titular mantiene la deuda bancaria completa.

## Migración conservadora

Mantener cada Workspace.Id actual como espacio Personal permanente, con sus cuentas, asientos, compras, cuotas, reglas y pagos intactos. Agregar ApplicationUser por subject autenticado estable, Workspace.Kind/Name/OwnerUserId/Version y WorkspaceMembership. Backfill idempotente: cada OwnerSubject existente obtiene su usuario y membresía Owner sin cambiar IDs financieros ni mover dinero.

OwnerSubject se conserva para compatibilidad del Personal; espacios no personales no reutilizan ese valor. Su índice único admite null para espacios compartidos. El subject identifica al usuario; nombre o correo no fusionan identidades ni historiales.

No copiar las compras privadas a un grupo para implementar colaboración: eso expondría deuda/datos del emisor y duplicaría obligaciones. Los grupos colaborativos posteriores conservan vínculos y vistas compartidas limitadas sobre las cuotas pertinentes.

## Autorización común

La API resuelve usuario, espacio seleccionado y permisos en servidor. Una cabecera X-Workspace-Id es solo selección: el servidor comprueba membresía activa o concesión de lectura, nunca la toma como autorización. Sin cabecera se usa el Personal propio, manteniendo clientes anteriores. Una selección inválida/ajena responde404 sin volver silenciosamente al Personal.

Los endpoints financieros usan un único contexto de espacio mediante filtro/resolutor común. Sesión y onboarding siguen refiriéndose al Personal propio; gestión de espacios/invitaciones valida sus recursos y permisos específicos. Sustituir ambos aprovisionadores duplicados actuales por un servicio compartido, conservando la recuperación de colisión de propietario.

Roles: Owner y Admin gestionan recursos/configuración; Member registra actividad financiera compartida y propone correcciones históricas; Reader solo consulta. Correcciones confirmadas y gestión de miembros se validan con permisos explícitos. La responsabilidad de facturación se conserva separada del rol de administrador. El propietario no puede salir o eliminar su última titularidad sin un circuito de transferencia/cierre válido. No borrar historial al revocar una membresía.

Una concesión PersonalReadGrant vincula el Personal propietario con un Hogar concreto. El lector necesita además membresía activa en ese Hogar; su rol efectivo en el Personal ajeno es de lectura, aunque sea administrador familiar. No crear membresías de escritura en el Personal por compartir. Todas las lecturas, exportaciones y futuros adjuntos reutilizan esa comprobación; revocar corta solicitudes posteriores. La UI explica que no puede retirar copias que el lector ya haya visto/exportado.

## Hogar e invitaciones

El límite de cuatro incluye al propietario. Invitaciones pendientes no vencidas reservan lugar; aceptación comprueba el límite nuevamente de forma atómica. Crear un Hogar no convierte a sus integrantes en miembros de todos los grupos de los demás.

Invitar requiere permiso, correo destinatario y rol permitido. Token aleatorio criptográfico de256bits, solo hash en base, vencimiento de siete días, revocación y uso único. Devolver el enlace una sola vez para compartir; nunca registrar token o URL completa en auditoría. No mandar mensajes reales a terceros durante pruebas de desarrollo.

Aceptar requiere token válido, sesión y correo verificado por Google coincidente. El correo verificado permite aceptar esa invitación, no fusionar usuarios por nombre/correo. Si una cookie anterior carece de la verificación, pedir reingreso. Se verifica el campo oficial del perfil Google antes de emitir esa afirmación; no confiar en un campo enviado por el cliente.

Las pruebas con múltiples identidades de navegador pueden usar una lista explícita de identidades sintéticas configurada únicamente en Development y con login de desarrollo habilitado. La ruta nunca existe en Production y rechaza identidades no configuradas. No agregar un selector de identidad en producción ni un permiso administrativo al primer usuario.

## UI y evidencia

Selector de espacio visible con nombre, tipo y rol. Al cambiar, no mostrar temporalmente datos del espacio anterior, ni enviar un borrador a otra cuenta/espacio. Si hay edición pendiente, confirmar descarte/cancelar antes de recrear el contexto; solicitudes atrasadas no actualizan el nuevo espacio. Las opciones de lectura no ofrecen crear/pagar/editar; el servidor las rechaza igualmente si se manipula la UI.

Gestión de Hogar: integrantes, invitaciones pendientes/expiradas y revocación, roles según permiso. Configuración personal: consentimiento de lectura familiar claramente separado de pertenencia y suscripción. Las funciones personales cubiertas por Hogar se calculan por membresía/cobertura activa; no se activan por un parámetro del cliente.

Comprobar con dos o más identidades: datos anteriores conservados; no-miembro404; Reader no escribe; Admin familiar no accede al Personal sin consentimiento; concesión permite lectura y nunca escritura; revocación y salida cortan acceso; dos aceptaciones no superan cuatro plazas; enlaces expirados/reusados/para otro correo se rechazan; grupos permanecen independientes.

## Límites de este bloque, sin reducir el objetivo

El motor de permisos y membresías se implementa antes de cajas comunes/importaciones/archivos. Las operaciones de aportes, adelantos, cierres y grupos con varios pagadores vienen en su bloque financiero sobre estos mismos espacios. Catálogo, precios, cobertura pagada, Mercado Pago y backpanel tienen su bloque posterior: no se presentan como facturación operativa por crear un Hogar. No se eligen precios ni políticas comerciales irreversibles sin el usuario.
