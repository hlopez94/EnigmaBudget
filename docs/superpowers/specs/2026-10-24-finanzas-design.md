# Aplicación de finanzas personales, hogar y grupos

Diseño para revisión · 24 de octubre de 2026

Este documento reúne requisitos confirmados y propuestas para construir una primera versión. Las propuestas están identificadas y quedan sujetas a aprobación. Implementación autorizada por el usuario el 24 de octubre de 2026. Las planillas y ramas previas se conservan; la nueva implementación vive en una rama separada.

## 1. Objetivo

Reemplazar las planillas por una aplicación que permita continuar registrando ingresos y gastos, entender el dinero disponible, anticipar compromisos y organizar gastos compartidos y objetivos de ahorro. Debe responder cuánto tengo hoy, cuánto ya comprometí, cuánto me deben y cuándo puedo alcanzar una meta, separando pesos y dólares.

El producto se comercializa mediante planes Personal y Hogar. La experiencia de uso cotidiano debe permitir registrar un movimiento rápidamente desde el teléfono y explicar cómo se calculó cada saldo o proyección.

## 2. Requisitos confirmados

- Angular 22 y OptimusUI para Angular 22.
- Una sola aplicación backend .NET 10 y PostgreSQL; sin separar servicios.
- Web y Android con Capacitor reducido a lo necesario; evitar Ionic siempre que sea posible.
- Usuarios, tenants, Google para iniciar sesión y RBAC.
- Espacios personales, hogar y grupos de gastos independientes del hogar.
- Datos personales compartibles opcionalmente; los gastos compartidos tienen su propio acceso.
- Plan Personal y plan Hogar de hasta cuatro integrantes.
- Grupos también con plan Personal y participantes sin cuenta, invitables posteriormente.
- Cuentas bancarias, tarjetas y movimientos en ARS y USD.
- Compras en cuotas, resúmenes, pagos, cobros, previsiones y disponibilidad.
- Chanchitos personales y compartidos con aportes y estimación de cumplimiento.
- Cotizaciones desde DolarAPI.
- El presupuesto mensual considera por defecto las cuotas correspondientes a ese mes; el total de la compra se conserva en una vista diferenciada.
- Asistente inicial para cargar cuentas bancarias, saldos iniciales y tarjetas. Lectura del último resumen para detectar cuotas como mejora futura.
- VPS detrás de Cloudflare, administración de suscriptores y cobros con Mercado Pago.
- Almacenamiento compatible con S3: servicio local como MinIO o equivalente en desarrollo; Cloudflare R2 en producción.
- Los repositorios existentes se usan como antecedentes funcionales, sin reutilizarlos para dirigir el código nuevo.

La carga sin conexión se incorpora como propuesta basada en la respuesta afirmativa durante la conversación; no se confirmó todavía su alcance exacto.

## 3. Hallazgos de referencia

Las fuentes privadas aportadas por el usuario muestran gastos personales y comunes, repartos iguales o proporcionales, ingresos, reintegros, servicios recurrentes, cuotas y presupuestos. Los archivos originales, enlaces privados y valores de personas no forman parte de este repositorio público.

## 4. Enfoque elegido

Se consideraron una aplicación central y servicios separados. El usuario eligió explícitamente una sola aplicación .NET, Angular y PostgreSQL. La organización por carpetas o módulos internos no implica procesos separados.

Para Android se consideraron una interfaz independiente y una base Angular compartida con Capacitor. Se eligió compartir Angular, usar OptimusUI y minimizar Capacitor, evitando Ionic.

Propuesta de despliegue: el mismo backend sirve los archivos compilados de Angular, la API y las tareas programadas dentro de un solo proceso .NET. PostgreSQL es la base transaccional; R2 es almacenamiento de objetos, no una segunda base de movimientos. El paquete Android contiene el frontend y consume esa misma API.

## 5. Espacios, tenants y privacidad

Propuesta: un espacio personal, un hogar y un grupo son espacios de datos distintos, cada uno con su tenant y sus membresías. La identidad de usuario es global y puede participar en varios espacios; la suscripción es una entidad comercial distinta del tenant.

- Al registrarse, la persona obtiene su espacio personal.
- El hogar agrupa hasta cuatro identidades, incluido el titular, sin fusionar sus espacios personales.
- Un grupo tiene miembros propios y no consume lugares del hogar.
- Los participantes sin cuenta son registros locales del grupo, no identidades autenticadas.
- Una invitación permite reclamar ese participante con confirmación y auditoría; nunca se vincula historial solo por coincidencia de nombre.

Privacidad propuesta: datos personales privados por defecto; el propietario puede conceder lectura general de sus finanzas al hogar y revocarla. Esta concesión no permite editar ni se extiende a los grupos. Al revocarla desaparece el acceso futuro; no se puede retirar una copia que otro miembro ya haya exportado o visto.

Los gastos compartidos, pagos y deudas visibles para sus participantes se conservan aunque alguien deje de compartir sus datos privados o salga del grupo. Para repartir por ingresos, cada integrante puede declarar la base de cálculo del período sin conceder lectura de sus movimientos personales.

No se acepta un TenantId del cliente como prueba de autorización: cada operación valida identidad, membresía, recurso y permiso. La concesión de lectura personal es una relación explícita entre espacios, no un acceso global entre tenants.

## 6. Roles y funciones de los planes

Propuesta de roles: propietario, administrador, miembro y lector dentro de un espacio. En un hogar, titular de facturación y administrador son responsabilidades separadas. En plataforma: administrador comercial, soporte y operador financiero con permisos mínimos.

Los permisos dicen qué puede hacer una persona; los beneficios del plan dicen qué funciones y límites tiene disponibles. Ambos se verifican en el servidor, incluidos exportaciones, archivos y sincronización.

| Plan | Alcance confirmado | Propuesta para completar |
|---|---|---|
| Personal | Una persona y acceso a grupos | Finanzas personales completas, tarjetas, ahorros y proyección |
| Hogar | Hasta cuatro personas | Incluye funciones personales para integrantes y economía compartida del hogar |

Las invitaciones pendientes reservan un lugar con vencimiento para evitar sobrecupo; la aceptación también comprueba el límite de forma atómica. Un grupo no hereda datos ni permisos de los hogares de sus miembros.

Precios, prueba gratuita, cantidad de grupos, límites de almacenamiento y pertenencia simultánea a hogares quedan como políticas comerciales configurables. Propuesta inicial: un hogar activo por persona; grupos múltiples. Un miembro de grupo necesita cobertura Personal o Hogar para operar autenticado; el organizador puede registrar participantes sin cuenta. No se presupone un plan gratuito.

## 7. Núcleo de movimientos y cuentas

Se distinguen ingreso, gasto, transferencia entre cuentas, aporte a caja común, pago de deuda, reintegro, devolución y ajuste. Un saldo surge de movimientos confirmados y un saldo inicial identificado; una previsión no modifica el saldo real.

Cada movimiento conserva importe, moneda, fecha efectiva, período, cuenta, descripción, categoría, autor y origen. Un gasto además puede tener comercio, participantes y comprobantes. Se separan fecha de compra, imputación, vencimiento y pago.

Propuesta: un registro interno de asientos equilibrados, con importes decimales exactos, respalda saldos y trazabilidad sin exigir conocimientos contables al usuario. Correcciones de operaciones confirmadas generan reversión o ajuste vinculado. Los borradores son editables; no se borra silenciosamente historia financiera compartida.

Transferir entre cuentas propias o aportar a un chanchito no representa un nuevo gasto. Los importes negativos importados se clasifican por significado; no se asume automáticamente que son ingresos.

La información común se registra una sola vez y se refleja en vistas personales mediante vínculos. Un aporte del miembro, el gasto desde caja común y un reembolso no se suman como tres gastos de consumo.

## 8. Hogar y grupos

Hogar: servicios, presupuesto de comida, cuentas comunes, aportes esperados y realizados, repartos y metas compartidas. Los repartos permiten partes iguales, porcentajes, montos fijos o proporción de ingresos con vigencia por período. Una regla nueva no recalcula acuerdos anteriores ya confirmados.

Grupo: viajes, salidas y compras puntuales, con uno o varios pagadores y beneficiarios seleccionados por gasto. Se calculan saldos por participante y moneda y se proponen transferencias para saldar. Se permiten pagos parciales y cierre cuando no existen saldos pendientes, o cierre con deuda pendiente expresamente reconocida.

Propuesta: no compensar ARS y USD automáticamente. Una liquidación entre monedas requiere registrar los importes y tipo de cambio acordados. El algoritmo reparte el residuo de redondeo de forma determinista, para que las partes sumen exactamente el gasto.

Si alguien sale, queda inactivo para gastos nuevos y mantiene su historial y obligaciones. Los miembros pueden proponer correcciones; quienes tengan permiso confirman cambios auditados. Un participante sin cuenta no puede confirmar por sí mismo pagos que otro cargó en su nombre: se muestra quién lo registró.

## 9. Tarjetas, cuotas y resúmenes

- Banco, tarjeta, titular, moneda de consumos, calendario de cierre y vencimiento y cuenta habitual de pago.
- Compra con costo total, cuotas, intereses y primera cuota; importación de compras que ya están a mitad de su calendario.
- Calendario estimado de cuotas por resumen, ajustable al resumen efectivamente emitido.
- Criterio confirmado: el presupuesto mensual imputa las cuotas correspondientes al mes. Una vista alternativa muestra el valor total de las compras adquiridas, sin sumarlo nuevamente a las cuotas. Los pagos del resumen siguen siendo cancelaciones de deuda, no gastos adicionales.
- Consumos y obligaciones separados en ARS y USD, con impuestos, comisiones, devoluciones e intereses identificados.
- Resumen estimado, emitido, parcialmente pagado, pagado o vencido; pagos parciales y múltiples cuentas de origen.
- El pago reduce la deuda de tarjeta y el saldo bancario sin volver a contar el consumo.
- El importe real liquidado por el emisor prevalece sobre una estimación de tipo de cambio o impuesto.
- Si se paga menos que el total, registrar saldo financiado e intereses confirmados; no prometer simular exactamente cada financiación bancaria en la primera versión.
- Una compra compartida en cuotas conserva el acuerdo de reparto y el calendario de cobro a los demás participantes.

Propuesta inicial: la obligación de cada participante se distribuye por cuota y su cobro se registra por separado. El banco sigue teniendo al titular como deudor por todo el resumen; un reembolso esperado no reduce la obligación bancaria.

## 10. ARS, USD y DolarAPI

Cada cuenta tiene moneda propia; un banco puede contener cuentas ARS y USD. Las tarjetas tienen obligaciones por moneda y pagos con asignación explícita a cada saldo.

El backend consulta DolarAPI y guarda tipo de dólar, compra, venta, fecha publicada y fecha de consulta. La moneda original no se reemplaza por una equivalencia. Para consolidar se elige referencia y lado compra/venta, mostrando fecha y criterio; para una operación se conserva el cambio real aplicado y su procedencia, con corrección manual.

Una conversión usa dos importes y comisiones explícitos; las tasas actuales no reescriben historia. El histórico propio comienza con las consultas guardadas, sin asumir que DolarAPI proporciona todas las fechas pasadas.

Si el proveedor falla, conservar último dato con indicación de antigüedad; si no hay tasa, mostrar saldos separados y pedir un valor cuando sea indispensable convertir. Las tasas usadas en proyecciones son supuestos editables, no predicciones garantizadas.

## 11. Proyección y disponibilidad

Vista por fecha y moneda: saldo actual confirmado + cobros pendientes incluidos en el escenario − pagos futuros − reservas elegidas. Cada obligación ya pagada deja de proyectarse; se evita descontar dos veces cuota y resumen, o aporte al hogar y consumo común.

Mostrar por separado saldo bancario, deuda total, dinero reservado, cobros esperados y disponibilidad proyectada. La disponibilidad hoy no incluye cobros futuros. Escenarios propuestos: compromisos confirmados y escenario con ingresos y gastos estimados; supuestos de aumentos y cambios visibles y editables.

Las recurrencias permiten periodicidad, próxima fecha y cambios con vigencia; se generan ocurrencias identificadas para no duplicar al ejecutar tareas nuevamente. Avisar sobre faltantes antes de vencimientos y explicar qué operaciones producen el faltante. El límite de tarjeta es crédito, no efectivo disponible.

## 12. Chanchitos

Meta, moneda, monto objetivo, fecha deseada, aportes, retiros y participantes. Diferenciar reserva virtual respaldada por cuentas de una cuenta bancaria separada. El mismo dinero no puede asignarse dos veces ni contarse como reserva y saldo adicional.

Calcular faltante, porcentaje, aporte periódico necesario y fecha estimada con el aporte elegido. Si no hay aporte positivo o existen supuestos insuficientes, indicar que no se puede estimar. Rendimientos e inflación serían supuestos opcionales, nunca resultados garantizados. Fondos de emergencia y objetivos de compra usan el mismo modelo básico.

## 13. Importación y continuidad de carga

La aceptación del usuario se interpreta como avanzar con la propuesta de recuperar todo el historial aprovechable de las planillas, dejando inconsistencias para revisión. Esta importación es opcional para nuevos usuarios: también pueden empezar sin historial mediante el asistente inicial.

### Asistente inicial confirmado

Permite configurar progresivamente cuentas bancarias y tarjetas, guardar el avance y completar lo faltante después. Flujo propuesto:

1. Elegir el espacio donde se crearán los recursos: personal u hogar, según permisos.
2. Agregar bancos y cuentas, identificadas por nombre y moneda ARS o USD; incluir billeteras y efectivo como opciones del mismo flujo.
3. Informar el saldo de cada cuenta y la fecha a la que corresponde. La aplicación registra una apertura identificada; no lo cuenta como ingreso del mes.
4. Agregar tarjetas, banco, titular y configuración de cierre y vencimiento; indicar deuda inicial por moneda, pagos ya efectuados y cuenta habitual de pago. No solicitar número completo ni código de seguridad.
5. Cargar manualmente compras con cuotas pendientes, permitiendo indicar cuota actual, cantidad restante y primer vencimiento pendiente. Mostrar por separado deuda del resumen y compromisos de cuotas futuras para no sumarlos dos veces.
6. Revisar el resumen de configuración por moneda y confirmar. Se pueden omitir pasos y completarlos desde la configuración posteriormente.

La fecha de saldo inicial establece un corte para calcular saldos. Si se importa historia anterior al corte, esa historia sirve para consultas y reportes, pero no vuelve a modificar el saldo de apertura. Los movimientos posteriores sí lo modifican; si el usuario cambia la fecha o importa movimientos alrededor del corte, debe revisar la conciliación. El mismo criterio aplica a deuda inicial y pagos de tarjetas.

Una cuota pendiente cargada al configurar una tarjeta representa un compromiso existente, no una nueva compra realizada hoy. Las cuotas futuras aún no incluidas en un resumen no se presentan como saldo exigible de ese resumen. Cuando no se conoce el detalle, se permite deuda inicial sin desglose, marcando que la proyección de cuotas está incompleta hasta completarlo.

### Mejora futura: cargar el último resumen

El usuario podrá adjuntar el último resumen y revisar una propuesta con tarjeta, período, cierres, vencimientos, saldos ARS/USD, consumos y cuotas detectadas, por ejemplo cuota 3 de 12. La extracción debe mostrar campos dudosos, fuente y totales a conciliar; confirmar antes de crear registros. El archivo permanece privado en almacenamiento S3/R2.

El último resumen no necesariamente contiene todos los compromisos pendientes ni todos los datos para reconstruir una compra. No inventar costo original, cuotas variables, fechas o intereses faltantes. Distinguir obligaciones del resumen actual y cuotas de períodos futuros, y permitir completar información manualmente. Antes de incorporar, comparar con compras, cuotas, deuda inicial e importaciones existentes para proponer vínculos y evitar duplicados. Los formatos y bancos soportados se definirán en esa etapa; no se promete compatibilidad universal ni lectura automática como parte del lanzamiento inicial.

### Migración de las planillas

Importar los archivos proporcionados mediante un perfil específico; una importación universal de cualquier Excel queda fuera de la primera versión. Propuesta de entrada: XLSX/CSV exportado por el usuario y cargado a la app; la conexión permanente a Drive no es requisito confirmado.

La importación pasa por preparación, mapeo de personas/cuentas/categorías, vista previa, advertencias y confirmación. Conserva archivo, hoja, celda o fila, identificador del lote y relación con cada registro creado.

Detectar filas de plantilla, fórmulas con errores, montos faltantes, fechas ambiguas y posibles duplicados. No inventar días exactos donde solo hay un mes; guardar el período y la precisión temporal. No tratar ceros de plantilla como evidencia de que un servicio es gratis o fue pagado.

Los bloques personales y comunes pueden describir el mismo hecho: proponer vínculos y revisar coincidencias antes de sumar. Reimportar el mismo archivo no duplica operaciones; cambios en una fuente se presentan como posibles actualizaciones, sin sobreescribir automáticamente movimientos conciliados. Verificar totales por período, cuenta y moneda antes de terminar. Un lote confirmado se puede revertir con trazabilidad y revisión de registros ya utilizados.

La aplicación se convierte en el lugar de carga cotidiana. Exportar movimientos y saldos conserva la posibilidad de revisar o migrar datos.

## 14. Sin conexión

Propuesta limitada: crear borradores de ingresos y gastos simples y consultar información previamente sincronizada. Invitaciones, permisos, facturación, liquidaciones, pagos de resumen y modificaciones de repartos requieren conexión en la primera versión.

Cada borrador tiene identificador generado en el dispositivo; los reintentos no crean duplicados. Al sincronizar se vuelven a verificar permisos y estado de la cuenta. Un cambio concurrente no se sobrescribe silenciosamente: se conserva el borrador para resolverlo. Un borrador pendiente no aparece como confirmado en saldos compartidos.

Cachear solo espacios autorizados y limpiar datos al cerrar sesión o al recibir revocación. Un dispositivo desconectado no puede enterarse inmediatamente de una revocación; limitar la duración del acceso offline, y no cachear por defecto datos personales ajenos compartidos. Credenciales sensibles se guardan mediante mecanismos apropiados de cada plataforma.

## 15. Una aplicación y almacenamiento de objetos

Un repositorio nuevo, un frontend Angular y un host .NET con áreas internas de identidad, espacios, finanzas, tarjetas, proyección, ahorros, importación, archivos y facturación. No se añaden microservicios, colas externas ni Redis como requisitos iniciales. Tareas persistentes con estado en PostgreSQL se ejecutan dentro del host, tolerando reinicios y reintentos.

PostgreSQL contiene datos estructurados, relaciones, asientos, permisos, cotizaciones, suscripciones, auditoría y metadatos de archivos. S3/R2 contiene comprobantes, adjuntos y archivos de importación/exportación. No guardar credenciales bancarias ni datos completos de tarjetas.

Proveedor local S3 compatible por seleccionar durante la implementación; MinIO es una opción, no una dependencia obligatoria. En producción se configura R2. Una interfaz interna de archivos usa únicamente operaciones compatibles necesarias: cargar, consultar, descargar y eliminar. Validar contra R2 real antes de publicar; compatibilidad S3 no significa equivalencia de todas las funciones de AWS.

Objetos privados, claves opacas y metadatos con tenant, propietario, estado, tamaño y tipo. Cada descarga verifica permisos en .NET y entrega URL firmada breve o transmite el archivo. Nunca entregar credenciales del bucket al cliente. Validar tamaño y formato y finalizar cargas comprobando que existe el objeto esperado. Resolver cargas fallidas y objetos huérfanos mediante tareas recuperables.

Las URLs firmadas de R2 usan el dominio de la API S3; no se presupone que funcionen en un dominio personalizado. La aplicación conserva identificadores de objeto, no URLs temporales. Los adjuntos personales siguen las mismas reglas de privacidad que sus movimientos.

## 16. Identidad y operación

Inicio de sesión con Google. Propuesta: identidad local vinculada por proveedor e identificador estable, sin usar el correo como única clave. Web y Android tendrán flujos compatibles con su plataforma, con retorno validado a la app y renovación/revocación de sesión. La librería concreta de autenticación queda para el plan técnico; no se ha elegido Auth0 ni otro proveedor adicional.

El servidor controla autorización, validación, pertenencia y límites. El backpanel no concede acceso automático a las finanzas privadas de los clientes; soporte excepcional debe ser explícito, acotado y auditado. Proteger administradores con un nivel de autenticación adecuado y registrar cambios de roles, cobros y concesiones de acceso.

Producción: VPS, proxy HTTPS detrás de Cloudflare, aplicación .NET, PostgreSQL sin exposición pública y R2. Copias de PostgreSQL y objetos fuera del VPS, restauración probada, secretos fuera del repositorio, logs sin datos financieros sensibles y alertas de fallos de sincronización, importación, tareas y pagos. No cachear respuestas privadas de API en Cloudflare. Configurar límites de carga coherentes entre proxy, app y almacenamiento.

## 17. Suscripciones y backpanel

Planes versionados, suscripción, titular, cobertura de integrantes, historial de cargos, pagos, devoluciones y eventos del proveedor. Separar la suscripción del SaaS de los gastos recurrentes que el usuario registra para su vida personal.

Mercado Pago se integra para cobrar en web. El servidor procesa notificaciones autenticadas, consulta el recurso del proveedor y aplica transiciones idempotentes. No activar beneficios basándose en una redirección del navegador. Manejar eventos repetidos y desordenados, reintentos y conciliación periódica.

Backpanel: buscar clientes y hogares, revisar plan, período cubierto, pago pendiente/aprobado/rechazado, próximos cobros, cancelaciones, devoluciones, incidencias y conciliación. Registrar pagos manuales con referencia, autor y motivo, distinguiéndolos de los verificados por Mercado Pago.

Propuesta inicial de política: mensual en ARS; cancelación al fin del período pagado; después de vencimiento se conserva lectura/exportación y se limita crear datos nuevos. Nunca borrar finanzas por impago. No aplicar cambios de plan ni prorrateos silenciosos. Precios, días de gracia, devoluciones y tratamiento exacto de cambios Personal/Hogar deben aprobarse antes de implementar facturación.

Android: la distribución por APK o por Google Play no está definida. Si se publica en Play, la venta de funciones digitales puede requerir su sistema de facturación según país y programa; no asumir que el checkout de Mercado Pago dentro de la app está permitido. Propuesta para la primera versión: compras de suscripción en web y Android con acceso a cobertura existente, sin flujo de compra externo dentro de la app hasta validar el canal. No implica una nueva aplicación backend.

## 18. Secuencia propuesta de entrega

1. Base: aplicación única, Google, espacios, permisos, ARS/USD, asistente inicial de cuentas/saldos/tarjetas, movimientos, archivos S3 local, importador de las planillas y exportación. Probar primero con los datos proporcionados y validar diferencias. El asistente debe permitir empezar sin importar historia y continuar su configuración más tarde.
2. Finanzas completas: cuotas, resúmenes, pagos, recurrencias, reintegros, proyección, cotizaciones y chanchitos.
3. Colaboración: hogar, reglas históricas de reparto, privacidad opcional, grupos independientes, participantes sin cuenta, cobros y liquidaciones. El modelo de espacios de la etapa 1 ya contempla estas relaciones.
4. Android y operación comercial: Capacitor mínimo, borradores offline, experiencia móvil, R2, backpanel, Mercado Pago y políticas de suscripción aprobadas. La web se adapta a teléfonos desde la primera etapa.

Estas son entregas de un mismo producto; no proyectos o servicios separados. El piloto puede validar las primeras etapas antes de abrir ventas. La prueba de autenticación Android y retorno de enlaces debe hacerse temprano, antes de cerrar la etapa 1, para no descubrir incompatibilidades al final.

## 19. Criterios de aceptación del diseño

- Pagar un resumen no duplica el gasto; devolver o anular una compra ajusta deuda y calendario de forma trazable.
- Transferencias y reservas no inflan gastos ni patrimonio.
- Las partes de un gasto suman exactamente su total y conservan el acuerdo histórico.
- Se verifica aislamiento entre espacios y privacidad al listar, exportar, descargar adjuntos y sincronizar.
- Se conservan ARS y USD separados y cada conversión explica tasa, fecha e importes reales.
- Reintentos de importación, sincronización, recurrencias y webhooks no duplican efectos.
- Un hogar no supera cuatro integrantes y los grupos no consumen esos lugares.
- Los participantes invitados recuperan su historial solo tras vinculación verificada.
- La proyección explica supuestos y evita descontar la misma obligación varias veces.
- Las planillas se importan con informe de errores, diferencias y registros sin resolver.
- El asistente puede retomarse y confirma cuentas y tarjetas por moneda y fecha; importar historia anterior no duplica saldos de apertura, deuda inicial ni cuotas pendientes.
- Se comprueban cargas y descargas privadas tanto en almacenamiento local como R2 y se ensaya restauración de datos y adjuntos.

## 20. Fuera de la primera versión

Sincronización bancaria automática, custodia o transferencia real de dinero entre usuarios, inversión desde la app, lectura automática universal de tickets, motor fiscal y simulador exacto de refinanciación de todos los bancos. La extracción de cuotas desde el último resumen está prevista como mejora futura, empezando por formatos seleccionados y siempre con revisión. Tampoco iOS ni integración permanente con Drive están confirmados. Se registra el hecho de un pago; la aplicación no lo ejecuta contra el banco.

## 21. Decisiones abiertas para revisión

Las propuestas de este documento no son acuerdos nuevos. Especialmente: alcance offline, cobertura personal de los integrantes de Hogar, un hogar por persona, permisos de lectura personal sin edición, importación inicial por archivos exportados, reparto de cuotas compartidas, proveedor S3 local, canal Android y reglas de facturación. El constructor puede elegir organización de proyectos, librerías y esquema físico siempre que respete una sola aplicación y los contratos descritos. No elegir precios ni políticas comerciales irreversibles por cuenta propia.

Revisión independiente realizada sobre este borrador. Quedan por resolver las siguientes decisiones de producto antes de sus respectivas etapas:

1. Resuelto con el usuario: el presupuesto mensual usa las cuotas correspondientes al mes como lectura predeterminada. Se conserva la vista de consumo adquirido por el total de la compra; ambas lecturas permanecen diferenciadas y no duplican operaciones.
2. Historial: se toma la aceptación del usuario como avanzar con todo el historial aprovechable, junto a un asistente para cuentas y saldos iniciales confirmado explícitamente. Conservar el corte de apertura para evitar duplicación y no presumir saldos que las fuentes no permiten comprobar. El uso de archivos exportados como entrada sigue siendo una propuesta técnica; una conexión permanente con Drive no fue solicitada.
3. Antes de consolidar suscripciones: confirmar cobertura personal de integrantes de Hogar, límite de hogares por persona y condiciones para que un invitado con cuenta, pero sin plan propio, opere en un grupo. La propuesta de exigir cobertura no es un requisito del usuario.
4. Antes de tarjetas compartidas: confirmar si la deuda entre participantes nace completa o por cuota. Antes de Android y facturación: confirmar alcance offline, canal de distribución y políticas comerciales pendientes.

La separación entre espacios y privacidad opcional refleja la conversación; el detalle de concesión de solo lectura es una propuesta para aprobación del diseño. La selección del proveedor S3 local y de librerías no requiere una decisión comercial del usuario.

## 22. Fuentes

- Antecedentes públicos: https://github.com/hlopez94/EnigmaBudget y https://github.com/hlopez94/enigma-budget.
- [OptimusUI: compatibilidad](https://optimus.openng.org/installation).
- [Capacitor](https://capacitorjs.com/docs).
- [DolarAPI: cotizaciones actuales](https://dolarapi.com/docs/argentina/operations/get-dolares).
- [R2: compatibilidad S3](https://developers.cloudflare.com/r2/api/s3/api/) y [URLs firmadas](https://developers.cloudflare.com/r2/api/s3/presigned-urls/).
- [Mercado Pago: notificaciones de suscripciones](https://www.mercadopago.com.ar/developers/es/docs/links-and-debts/additional-content/your-integrations/notifications).
- [Google Play: política de pagos](https://support.google.com/googleplay/android-developer/answer/10281818?hl=en).
