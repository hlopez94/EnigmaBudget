# EnigmaBudget: funcionalidades y circuitos completos

Definición funcional para revisión · 24 de octubre de 2026

## 1. Qué debe resolver el producto

Reemplazar el trabajo de las planillas: registrar, corregir, consultar y continuar la economía personal y compartida, desde una compra hasta su pago, reparto, devolución y efecto futuro. La persona debe poder responder: cuánto tengo, cuánto debo, cuánto vence, cuánto me deben, cuánto puedo usar y cuánto puedo ahorrar.

Esta es la definición del producto solicitado. El alcance reducido de la base implementada no sustituye estas funcionalidades. El asistente inicial es una entrada opcional: terminarlo nunca debe cerrar la posibilidad de agregar o modificar cuentas, tarjetas y operaciones.

**Confirmado**: requisitos expresados por el usuario. **Propuesta**: reglas operativas necesarias para completar los circuitos, aún revisables. Los ejemplos son inventados; no contienen información de las personas de las planillas.

### Requisitos ya confirmados

- Personal, Hogar de hasta cuatro integrantes y grupos independientes del hogar.
- Compartir la visualización de las finanzas personales es opcional y controlado por su propietario.
- Grupos también desde el plan Personal; participantes sin cuenta, invitables después.
- ARS y USD en cuentas, tarjetas, movimientos, deudas y ahorros; cotizaciones desde DolarAPI.
- Presupuesto mensual por las cuotas del mes; valor total de la compra en una vista diferenciada.
- Seguimiento de compras, cuotas, resúmenes, pagos, cobros y disponibilidad futura.
- Chanchitos con aportes y estimación de cuándo se alcanza la meta.
- Asistente de cuentas y montos iniciales; lectura del último resumen como mejora futura.
- Web y Android; una aplicación .NET 10 con PostgreSQL, Angular 22 y OptimusUI, Capacitor mínimo, evitando Ionic.
- Archivos privados con almacenamiento S3 compatible en desarrollo y R2 al desplegar; planes, RBAC, administración y Mercado Pago para la comercialización.

## 2. Qué aportan los Excel al alcance

Se usaron las ocho extracciones ya revisadas. Se analizó su estructura y contenido legible; no se supone que cada fórmula sea correcta ni que cada casilla vacía represente un impago. Las referencias originales y los datos particulares permanecen privados.

| Fuente | Estructura observada | Función que necesita la aplicación |
|---|---|---|
| Gastos compartidos 2025 | Detalle, importe, quién pagó, reintegros, servicios por integrante, aportes, extras anteriores y disponible común | Gastos compartidos, caja común, adelantos personales, aportes y cierre mensual con arrastre |
| Gastos personales 2026 | Ingresos, créditos, servicios, movimientos diarios y compartidos; medio y estado de ingreso/pago | Carga cotidiana unificada, cuentas, pendientes, pagos y vistas mensuales sin duplicación |
| Créditos | Costo, medio de compra y pago, cuotas, compra/proceso/inicio/fin/próximo pago y calendario | Compras financiadas y compromisos por período; separar tarjeta, préstamo, recurrencia y fondo |
| Servicios | Modo 50/50 o proporcional, rubros, importes mensuales y cálculos por persona | Presupuesto del hogar, recurrencias, aumentos y reglas de reparto con vigencia |
| Dos archivos de ingresos | Fuentes de sueldo, extras, aguinaldo, totales y neto | Ingresos esperados/cobrados, fuentes y base configurable para repartir gastos |
| Proporcionalidades | Ingresos y partes por integrante y período | Explicar y conservar las bases y porcentajes de cada reparto |
| Obra social | Valor real, aportes, bonificaciones y total por persona | Desgloses de cargos/descuentos y distinción entre descuento de sueldo, aporte y reintegro |

**Hallazgos que cambian el modelo:**

1. “Quién” mezcla personas y cuenta común: hay que separar quién registra, quién paga, desde dónde y a quién corresponde el gasto.
2. “Créditos” mezcla conceptos: un nombre de bloque no convierte automáticamente una fila en compra con tarjeta.
3. Los negativos representan devoluciones, reintegros, transferencias y ajustes. No todos son ingresos.
4. Los ingresos y la obra social incluyen plantillas precargadas; buena parte de Créditos es calendario vacío. No importar ceros como operaciones reales.
5. Hay errores de referencias, divisiones y sueldos no encontrados. Deben revisarse, no convertirse en cero.
6. “Disponible”, “comida puesta” y “a poner” expresan caja, adelantos y compromisos diferentes. Deben poder explicarse por movimientos vinculados.

## 3. Conceptos que deben mantenerse separados

| Concepto | Qué representa | Efecto principal |
|---|---|---|
| Gasto/compra | Qué se consumió o adquirió y a quién corresponde | Consumo o compromiso del presupuesto |
| Pago | Cuándo y desde dónde se entregó dinero | Saldo real y cancelación de una obligación |
| Transferencia | Dinero entre cuentas propias o aporte a una caja | Cambia dónde está el dinero; no crea consumo |
| Compra con tarjeta | Obligación frente al emisor por una compra | Deuda de tarjeta; todavía no salida bancaria |
| Cuota | Parte programada de una compra | Importe que corresponde al presupuesto y resumen de un período |
| Resumen | Documento que agrupa cargos, créditos y saldos | Determina cuánto informa el banco para ese período; no crea la compra otra vez |
| Cobro entre participantes | Recuperación de un importe adelantado | Aumenta caja y cancela lo que otra persona debía; no es sueldo |
| Reintegro/devolución | Recuperación vinculada a una compra, banco o proveedor | Corrige el neto del gasto y/o acredita dinero o tarjeta según el caso |
| Reserva/chanchito | Dinero propio asignado a un objetivo | Reduce disponibilidad libre, no patrimonio ni consumo |
| Saldo inicial | Situación a una fecha de comienzo | Punto de partida, no ingreso ni gasto del mes |

Cada operación conserva moneda, importe, fecha, período, estado y origen. Según corresponda: cuenta, tarjeta, comercio, categoría, participantes, reparto, vencimiento, comprobante y relaciones con otros movimientos. Una descripción por sí sola no sustituye esos datos.

## 4. Funciones accesibles todos los días

El producto debe permitir, aun con el asistente finalizado:

- Crear, consultar, editar datos y archivar cuentas y tarjetas; agregar nuevas en cualquier momento.
- Cargar ingreso, gasto, compra con tarjeta, transferencia, pago, cobro, devolución y ajuste desde acciones identificables.
- Consultar movimientos por período, cuenta, tarjeta, categoría, persona, espacio, moneda y estado.
- Abrir el detalle y recorrer sus vínculos: compra → cuotas → resumen → pagos; gasto común → reparto → cobros.
- Corregir errores con trazabilidad; cancelar borradores y anular operaciones sin dejar saldos incoherentes.
- Ver pendientes y vencimientos, registrar pagos parciales y marcar cobros efectivamente recibidos.
- Gestionar categorías, comercios frecuentes, cuentas predeterminadas y reglas recurrentes.
- Exportar datos propios y compartidos según permisos; conservar adjuntos privados.

**Regla de experiencia:** un listado vacío ofrece la acción necesaria para llenarlo. “Podés agregar una cuenta” debe tener un acceso real para hacerlo. Una función todavía no implementada se identifica como pendiente y no se presenta como acción operativa.

## 5. Cuentas y comienzo del uso

### Alta inicial y mantenimiento

1. Elegir espacio personal u hogar y tipo: banco, billetera o efectivo. Una entidad puede tener varias cuentas y monedas.
2. Cargar nombre, moneda, saldo a una fecha y, opcionalmente, un alias para identificarla. No pedir claves bancarias.
3. Revisar aperturas por moneda; confirmar o guardar para seguir después.
4. Poder agregar otra cuenta, cambiar nombre, consultar movimientos y archivar sin borrar el historial.

Cambiar el nombre no modifica movimientos anteriores. Corregir una apertura requiere revisar el efecto sobre el saldo; no reescribir silenciosamente una cuenta ya utilizada. Una cuenta archivada conserva consultas y pagos históricos, pero deja de aparecer como opción habitual de carga.

### Conciliación de cuenta

Comparar el saldo registrado con el observado en banco o billetera a una fecha. Localizar operaciones faltantes, duplicadas o con fecha equivocada. Si queda una diferencia sin detalle, registrar un ajuste explícito con motivo, autor y fecha. No reemplazar el saldo ocultando la diferencia.

## 6. Circuito de ingresos y cobros

1. Elegir fuente: sueldo, extra, aguinaldo, trabajo independiente u otra categoría.
2. Informar período al que corresponde, importe neto, moneda, cuenta prevista y fecha esperada de cobro.
3. Guardar como esperado o registrar como efectivamente cobrado.
4. Al cobrar, indicar cuenta, fecha e importe real. Permitir cobros parciales; el resto sigue pendiente.
5. Una recurrencia genera la previsión siguiente; no inventa un depósito.

Estados propuestos: previsto, pendiente, parcialmente cobrado, cobrado y cancelado. El vencimiento atrasado se muestra como condición, no convierte la previsión en dinero real.

Las retenciones o descuentos pueden explicar la diferencia entre bruto y neto. Si un aporte de obra social ya fue retenido del sueldo, no descontarlo otra vez de la cuenta bancaria. La base del reparto proporcional puede usar una declaración de ingreso elegible sin compartir el detalle de todos los movimientos.

**No confundir:** un préstamo recibido, una transferencia propia o un reembolso de un gasto adelantado no son ingresos de consumo. Se ven como entradas de dinero en el flujo de caja, con su clasificación correspondiente.

## 7. Circuito de gasto contado, débito o pendiente

### Pago inmediato

1. Elegir gasto personal o compartido; descripción, categoría, fecha, moneda e importe.
2. Elegir quién paga y la cuenta o billetera de origen.
3. Si corresponde, indicar participantes y reparto; adjuntar comprobante opcional.
4. Confirmar: registrar el gasto y una única salida de la cuenta.
5. Consultarlo en el listado y en el saldo actualizado; poder vincular una devolución posterior.

### Gasto ya realizado pero todavía no pagado

**Propuesta:** distinguir una previsión de una obligación confirmada. Una factura pendiente conserva proveedor, período, total, vencimiento y saldo por pagar. Confirmar el consumo no simula el pago.

Pagar luego aplica uno o varios pagos a esa misma obligación. Un parcial reduce su pendiente y la cuenta por el importe pagado. No crea un segundo gasto. Permitir varias cuentas de origen cuando sea necesario, conservando cada importe.

Estados del pago: pendiente, parcial o pagado; vencido se deriva de fecha y pendiente. Un borrador todavía no confirmado no altera saldos. Un gasto confirmado que se anula genera una corrección vinculada y revierte sus efectos; no desaparece dejando pagos huérfanos.

## 8. Transferencias y cambios de moneda

- Entre cuentas de la misma moneda: cuenta origen, destino, importe y fecha; una operación con dos lados vinculados. El total propio no aumenta ni disminuye salvo comisiones.
- Entre ARS y USD: registrar cuánto salió y cuánto entró, cambio real y comisiones. No suponer que ambos importes se derivan siempre de una cotización de referencia.
- A caja común: registrar salida personal y aporte recibido por el hogar, con permisos de cada lado. En el hogar se ve el aporte, no el resto de las finanzas privadas del miembro.
- A un fondo o cuenta de ahorro: distinguir traslado real de dinero de asignación virtual al objetivo.

DolarAPI sirve para referencias y equivalencias. La vista informa tipo de dólar, compra/venta y fecha usados; la operación conserva el cambio efectivamente aplicado. Sin tasa válida se mantienen saldos separados; no se suman ARS y USD como si fueran la misma unidad.

## 9. Tarjetas: alta, calendario y deuda existente

Registrar banco, nombre, titular, fechas habituales de cierre/vencimiento, cuentas usuales de pago y monedas. Datos opcionales: últimos cuatro dígitos y límites informados. Nunca código de seguridad ni número completo obligatorio.

El calendario mensual admite fechas reales distintas de la regla habitual. La fecha de compra y la de procesamiento pueden caer en resúmenes diferentes; se conserva la asignación real del emisor, con ajuste manual si la estimación no coincide.

### Arranque con una tarjeta ya utilizada

Separar tres componentes por moneda y fecha de apertura:

1. Saldo pendiente del resumen actual.
2. Compras procesadas todavía no incluidas en ese resumen.
3. Cuotas de compras anteriores que vencerán en períodos futuros.

Si solo se conoce un total sin detalle, permitir registrarlo como deuda inicial sin desglosar, avisando que la proyección está incompleta. Cuando se completa el desglose, distribuir ese mismo total; no agregarlo nuevamente. Identificar qué cuotas ya están incluidas en el resumen inicial para no duplicarlas.

Una compra anterior se puede cargar con cuota actual, cantidad restante y primer período pendiente. La historia anterior a la apertura sirve para consultas, pero no vuelve a modificar un saldo que ya la contiene. Los pagos posteriores a la apertura sí modifican cuentas y deuda.

## 10. Compra con tarjeta: una cuota o varias

1. Elegir tarjeta y espacio, descripción/categoría, moneda, fecha y total financiado informado.
2. Cargar cantidad de cuotas, primera cuota o resumen esperado y, si se conoce, fecha de procesamiento. Una cuota es el mismo circuito con un único vencimiento.
3. Revisar calendario estimado, costo total, importes de cada cuota, cargos conocidos y participantes.
4. Confirmar: aumenta la deuda comprometida con la tarjeta; no baja la cuenta bancaria.
5. Cuando aparece el resumen, vincular el cargo real con la cuota prevista y ajustar el calendario si corresponde.

El presupuesto mensual utiliza las cuotas del mes, como ya se acordó. La vista de compras adquiridas muestra el costo total. No sumar ambas vistas para presentar un supuesto gasto total.

**Propuesta:** en una división exacta repartir centavos de forma determinista para que la suma coincida; si el banco informa cuotas diferentes, conservarlas. Separar precio de contado, total financiado e intereses cuando sean conocidos; no inventar una financiación faltante. Una cuota futura anulada solo se elimina del compromiso cuando la anulación está confirmada.

## 11. Circuito de cierre y revisión del resumen

1. **Estimación:** agrupar las cuotas, compras y cargos previstos para el próximo período, separados ARS/USD.
2. **Recepción:** cargar manualmente el período, cierre, vencimiento y totales reales por moneda. La lectura automática del archivo es una mejora posterior.
3. **Revisión:** comparar cada línea con lo ya registrado. Vincular coincidencias, registrar compras faltantes, devoluciones, impuestos, intereses, comisiones y otros cargos.
4. **Resolución de diferencias:** corregir la previsión o registrar una línea sin clasificar/ajuste explícito con motivo. No crear otra compra para igualar totales.
5. **Confirmación:** conservar el resumen real y el detalle revisado. El importe informado por el emisor prevalece sobre la estimación.
6. **Seguimiento:** mostrar pagado, pendiente y vencimiento; registrar pagos y créditos posteriores vinculados.

Se separan dos dimensiones: estado del documento —estimado, en revisión, confirmado o rectificado— y estado de pago —pendiente, parcial o pagado—. El indicador de vencido depende de la fecha y del saldo pendiente. El crédito a favor no es un estado de pago alternativo: es un saldo separado por moneda, que puede coexistir con obligaciones pendientes. Solo disminuye una obligación cuando el emisor lo aplica a ella; no compensa por sí solo otras monedas ni resúmenes.

Un resumen es un agrupador, no otra compra ni otra deuda. Si el banco traslada saldo impago al resumen siguiente, se vincula la misma obligación: no se suman el saldo anterior y su arrastre como dos deudas. Los intereses nuevos sí son cargos nuevos.

**Propuesta:** se puede registrar un pago basado en el total real aun si falta clasificar el detalle; no se presenta como conciliado hasta resolver las diferencias. Rectificar un resumen conserva la versión anterior y explica qué cambió.

## 12. Circuito de pago de tarjeta

1. Seleccionar tarjeta/resumen y ver pendiente ARS y USD, créditos a favor y pagos ya aplicados.
2. Elegir pago total o parcial, fecha real y cuenta/s de origen.
3. Indicar cuánto dinero sale y a qué obligación se aplica. Si se cruzan monedas, guardar ambos importes y el cambio/cargos reales del emisor.
4. Confirmar: disminuye la cuenta y la deuda aplicada. No vuelve a sumar gasto al presupuesto.
5. Si queda pendiente, conservarlo y mostrar sus próximos vencimientos. Registrar intereses cuando estén informados; no dar por pagado el total por haber hecho un pago parcial.

Permitir varios pagos para el mismo resumen. Un exceso genera crédito a favor identificado, no un ingreso ficticio. Mostrar crédito acreditado, aplicado y todavía sin aplicar, independientemente del estado del pago. La distribución entre saldos se muestra y puede corregirse según lo que efectivamente aplicó el emisor; no se compensan automáticamente monedas.

**Ejemplo sintético:** resumen USD 200; pago USD 75 desde cuenta USD: la cuenta baja USD 75 y quedan USD 125 pendientes. Si se cancelan esos USD 75 usando ARS, se registra la salida real en ARS y la cancelación USD 75 por separado. DolarAPI no sustituye el cambio realmente liquidado.

## 13. Devoluciones, reintegros, reclamos y correcciones

### Devolución del comercio o promoción bancaria

Vincular la devolución a la compra, registrar total/parcial, moneda, lugar de acreditación y estado esperado o acreditado. Solo al acreditarse cambia dinero o crédito de tarjeta. Puede llegar a una cuenta distinta de la usada al comprar.

En tarjeta, un crédito puede reducir deuda o crear saldo a favor. No asumir que una devolución cancela cuotas futuras: reflejar lo que informe el emisor. La compra y el calendario mantienen sus relaciones y correcciones.

### Cobro a otra persona

Un reembolso de un participante cancela su deuda y aumenta el dinero recibido. **Nunca reduce por sí mismo lo adeudado al banco, ni siquiera después de cobrarlo.** La deuda bancaria baja cuando se aplica un pago o crédito del emisor.

### Reclamo o impugnación

Marcar el cargo como reclamado sin darlo por devuelto. Si el emisor acredita provisionalmente, identificar ese crédito; si lo revierte, registrar la reversión. Mantener visible la obligación que el banco sigue informando.

### Corrección histórica

**Propuesta:** períodos abiertos permiten corrección vinculada; después de un cierre, mostrar ajuste o rectificación explícita. No reescribir reportes cerrados sin dejar evidencia. Toda corrección compartida conserva quién la hizo y cómo cambia lo que corresponde a los demás.

## 14. Servicios y gastos recurrentes

Crear una regla con concepto, frecuencia, período inicial/final, vencimiento, importe fijo o estimado, moneda, categoría y medio habitual. Pausar, finalizar o cambiar desde un período elegido.

Cada período produce una obligación distinta. Se revisa el importe real y se registra su pago, sin duplicar el gasto previsto. Un aumento modifica las próximas previsiones; no cambia automáticamente meses ya pagados.

Permitir pagar un servicio desde banco, billetera o tarjeta. Al pagarlo con tarjeta se cancela la obligación con el proveedor y queda deuda de tarjeta; el posterior pago del resumen cancela esa deuda, sin un segundo gasto.

Obra social: desglosar valor, bonificación y aportes, indicando su naturaleza. Un descuento ya aplicado al sueldo no es otra salida bancaria. Un reintegro esperado requiere un cobro posterior. Los ceros de una plantilla no significan “servicio pagado” ni “servicio gratis”.

## 15. Hogar, aportes y caja común

### Preparar el mes

1. Definir presupuesto por rubro y servicios/obligaciones del mes.
2. Elegir reparto: igualitario, porcentajes, montos fijos o proporcional a ingresos elegibles.
3. Revisar la base de ingreso declarada por cada integrante y sus porcentajes.
4. Conservar el acuerdo del período; mostrar aportes esperados, ya realizados y faltantes.

Si falta la base o el total es cero, el cálculo proporcional queda pendiente. No sustituir por 50/50 sin elección explícita. Si un solo integrante declara ingreso positivo, mostrar el resultado propuesto y confirmarlo. Los cambios de ingreso no reescriben repartos históricos confirmados.

### Durante el mes

- **Aporte a caja:** transferencia real a la cuenta común. Aumenta caja, no consumo.
- **Gasto desde caja:** una salida común con reparto; no crea una deuda ficticia al usuario que lo cargó.
- **Adelanto personal:** un integrante paga desde su cuenta o tarjeta; el gasto común identifica lo que se le debe o el crédito que se aplica a su aporte.
- **Reintegro:** se vincula al gasto y a quien lo recibió; si debe ingresar a caja, queda ese traslado pendiente hasta registrarlo.

**Propuesta:** un adelanto puede descontarse del aporte pendiente o devolverse al integrante, mediante elección visible; nunca ambas cosas por el mismo importe. Haber pagado personalmente no implica que ese dinero haya entrado físicamente a la caja común.

### Cierre mensual

Mostrar por separado presupuesto, consumo neto, aportes recibidos, adelantos, reintegros, saldo de caja y pendientes por integrante. **Propuesta:** conservar sobrante como apertura siguiente y déficit como compromiso pendiente; no transformarlos en ingresos/gastos ficticios. El cierre congela el acuerdo del período y permite ajustes posteriores trazables.

## 16. Grupos independientes y compras compartidas

Crear grupo para viaje, salida o compra; miembros con cuenta o participantes externos por nombre. El grupo no ocupa lugares del Hogar ni concede acceso a finanzas personales.

Un gasto admite uno o varios pagadores y solo los beneficiarios que participaron. Repartir igual, por porcentaje o importe, con partes que sumen exactamente el total. Mostrar saldo por persona y moneda y propuesta de liquidación; registrar pagos parciales, cobros, correcciones y cierre.

### Compra compartida con tarjeta

El titular conserva toda la obligación frente al emisor. La participación de cada persona se vincula a la misma compra y a su calendario.

**Regla confirmada por el usuario:** programar el cobro mes a mes, calculando cada cuota con la regla de reparto de costos de su propio mes. No congelar un porcentaje de la compra para todos los meses. Un cambio de reparto futuro modifica las previsiones de esos meses; los repartos mensuales ya confirmados o con cobros se conservan. Mostrar separado lo adeudado confirmado y lo estimado para meses todavía sin confirmar. Cobrar no equivale a pagar la tarjeta.

Si falta una regla futura, la última regla conocida puede mostrarse solo como estimación identificada; para cobrar se debe definir o confirmar el reparto de ese mes. Confirmar el primer cobro confirma también su regla mensual. Para corregir un mes ya confirmado se requiere un ajuste explícito; no recalcular silenciosamente cobros anteriores.

**Ejemplo sintético:** A compra ARS 120.000 en tres cuotas de ARS 40.000. En octubre, A 60% y B 40%: a B le corresponde ARS 16.000. En noviembre, A 50% y B 50%: a B le corresponde ARS 20.000. Diciembre se calcula con su regla propia; no se presenta una deuda total fija de B como si el reparto no pudiera cambiar. El banco conserva el compromiso total de ARS 120.000 a cargo de A. El gasto consolidado de cada cuota es ARS 40.000; las vistas personales son su distribución, no consumo adicional.

Si B paga ARS 16.000 a A, baja la cuenta por cobrar y sube la cuenta de A. La deuda bancaria no cambia hasta pagar al emisor. Si B todavía no pagó, A debe prever el importe completo de la tarjeta, sin considerar ese cobro como dinero disponible hoy.

Cuando un participante externo crea cuenta, reclama su historial con confirmación; no vincular personas solo por nombre. Salir del grupo no borra deudas ni información compartida anterior.

## 17. Préstamos y otras deudas

Funcionalidad propuesta por la mezcla de medios y compromisos observada en las planillas: distinguir préstamo bancario o personal de compra con tarjeta y de gasto recurrente.

Registrar contraparte, moneda, dinero recibido/entregado, saldo inicial, fechas y calendario conocido. Separar capital de intereses y cargos informados. Recibir capital genera entrada y obligación, no sueldo; devolver capital cancela obligación, no vuelve a ser consumo. Intereses y comisiones sí se clasifican como gastos.

En el flujo de caja y compromisos mensuales se muestra el pago completo; en consumo se distinguen sus componentes. No se exige un simulador de financiación de todos los bancos ni se inventan tasas o cargos desconocidos.

## 18. Disponibilidad y proyección

Valores separados por moneda:

- **Saldo real:** apertura + entradas confirmadas − salidas confirmadas desde el corte.
- **Deuda total de tarjeta:** obligaciones reconocidas menos pagos/créditos aplicados; incluye cuotas futuras, identificadas como tales.
- **Pendiente del resumen actual:** lo informado para ese resumen menos aplicaciones; no incluye otra vez los saldos trasladados.
- **Por cobrar:** lo que deben participantes o terceros, separado de caja.
- **Reservado:** dinero propio asignado a objetivos.
- **Saldo libre de reservas hoy:** dinero utilizable menos reservas; no incluye sueldos o reembolsos todavía no recibidos ni el límite de crédito. No equivale al margen que se puede gastar sin comprometer pagos próximos.
- **Proyección a una fecha:** saldo de partida + entradas previstas incluidas en el escenario − salidas pendientes de ese escenario. La vista de disponibilidad libre descuenta reservas una sola vez.
- **Margen para gastar hasta una fecha:** propuesta de mostrar el menor saldo libre proyectado entre hoy y esa fecha, según el escenario elegido. Si aparece un faltante antes del próximo cobro, se informa aunque el saldo de fin de mes sea positivo. No se presenta una estimación como garantía.

**Deuda total y pendiente de resumen son vistas superpuestas, no importes sumables.** El pendiente actual ya forma parte de la deuda total. El desglose conciliable distingue saldo actual, consumos procesados aún no resumidos, cuotas futuras y otros saldos, asignando cada obligación a un solo componente; un saldo trasladado se vincula sin duplicarlo. Los créditos a favor todavía no aplicados se muestran aparte.

La proyección se alimenta del calendario de pagos y cobros pendientes, no de sumar los titulares “deuda total” y “resumen pendiente”. Cada salida futura tiene un único origen, incluso cuando aparece en varias vistas.

Mostrar escenario con compromisos confirmados y escenario con estimaciones. Cada resultado permite abrir los movimientos que lo explican. Permitir variar ingresos, aumentos de servicios y cotizaciones; identificar supuestos y montos desconocidos.

Una compra en cuotas y el resumen que la agrupa no son dos salidas futuras. Un pago ya registrado no se descuenta nuevamente como vencimiento. Cobros entre cuentas propias, aportes al hogar y consumos comunes se consolidan según sus vínculos.

## 19. Chanchitos y ahorros

Crear meta personal o compartida con nombre, moneda, objetivo y fecha opcional. Elegir cuentas que respaldan el dinero, registrar asignaciones, retiros y aportes de participantes; ver faltante, progreso, aporte periódico necesario y fecha estimada.

Distinguir dinero reservado de aporte prometido y de transferencia bancaria real. Una reserva no es gasto ni saldo adicional. La misma suma no puede respaldar dos reservas completas a la vez. Al usar el dinero para comprar, registrar el gasto y liberar/aplicar la reserva vinculada sin descontar dos veces.

**Propuesta:** estados activa, pausada, alcanzada y cerrada; saldo superior al objetivo permitido e identificado. Sin aporte periódico positivo no se promete una fecha. Para meta USD respaldada por ARS, la equivalencia depende de una cotización visible: no se afirma que ya se poseen esos dólares.

Fondos de emergencia, mudanza o retiro se pueden representar como metas/fondos. Si el dinero está invertido o no se puede retirar aún, declarar esa condición y fecha de disponibilidad; no presentarlo automáticamente como efectivo utilizable.

## 20. Historial, importación y último resumen

### Excel inicial

1. Elegir archivo/períodos y mapear cuentas, personas, monedas y conceptos.
2. Separar actividad, fórmulas auxiliares, plantillas, previsiones y errores.
3. Identificar filas repetidas entre vistas personales y comunes; vincularlas.
4. Revisar signos, fechas ambiguas, estados de pago y correspondencia de cuotas.
5. Mostrar vista previa, totales por moneda y diferencias con las fuentes.
6. Confirmar el lote y conservar su origen. Reimportar no duplica operaciones.

La fecha de apertura manda sobre el efecto en saldos: historia previa ya comprendida en una apertura no vuelve a afectar caja o deuda. Datos sin día exacto conservan su período y precisión; no se inventa una fecha de pago. Un error de fórmula queda pendiente de revisión.

### Último resumen, mejora futura confirmada

Adjuntar archivo y proponer tarjeta, período, totales, compras, cuotas actuales y próximas. Cada detección se revisa antes de incorporarse. Comparar contra deuda inicial y operaciones existentes para evitar duplicados. El último resumen puede no contener todas las cuotas futuras; los datos faltantes se completan manualmente. Definir bancos/formatos soportados al implementar, sin prometer lectura universal.

## 21. Acceso, planes y administración

- Personal y Hogar hasta cuatro; titular de facturación separado del administrador de gastos.
- Roles propuestos por espacio: propietario, administrador, miembro y lector; permisos comprobados también al descargar o exportar.
- Privacidad personal desactivada para terceros por defecto; compartir lectura con el hogar es opcional y revocable. Administrar el hogar no abre datos privados.
- Propuesta comercial: un invitado puede operar solo en el grupo al que fue invitado, sin que eso le conceda funciones personales pagas; no exigirle un plan propio sin definir esa política. Este punto sigue abierto.
- Administración: suscriptores, planes, cobertura, invitaciones, cargos, pagos aprobados/rechazados/pendientes, período cubierto, cancelación y conciliación con Mercado Pago.
- Un pago de suscripción del SaaS es distinto de los pagos personales registrados por sus clientes. Los beneficios se actualizan por estado verificado del proveedor, no por una pantalla de “pago exitoso”.

Precios, gracia, devoluciones comerciales, límites de grupos/archivos y pertenencia simultánea a hogares requieren decisión comercial. La distribución Android y su mecanismo de cobro también deben definirse antes de publicar ventas dentro de esa app.

## 22. Navegación funcional mínima

Se propone acceso permanente a Inicio, Movimientos, Cuentas, Tarjetas/Resúmenes, Compartidos, Proyección y Ahorros. Configuración y administración se muestran según permisos.

Inicio resume dinero, deudas, vencimientos, cobros y próximos pasos. Desde cada dato se accede a su explicación y acción: registrar gasto/ingreso, pagar, cobrar, revisar resumen o completar información. La configuración inicial nunca es la única vía para mantener los recursos.

Después del asistente vacío, deben existir “Agregar cuenta” y “Agregar tarjeta”. Después de cargar una cuenta, deben existir “Registrar ingreso”, “Registrar gasto” y “Ver movimientos”. Después de una compra en tarjeta, deben existir “Ver cuotas”, “Ver resumen” y “Registrar pago”.

## 23. Cuándo considerar útil la primera versión operativa

No basta con finalizar la configuración o mostrar un tablero. Deben completarse estos recorridos con persistencia y acciones accesibles:

| Caso | Resultado verificable |
|---|---|
| Cuenta nueva después del asistente | Se agrega sin reiniciar ni borrar lo anterior |
| Ingreso y gasto en débito | Cambia el saldo correcto y aparece el detalle en movimientos |
| Gasto pendiente con dos pagos | Queda un gasto, dos pagos y pendiente correcto |
| Compra de ARS 120.000 en tres cuotas | Compromiso total 120.000, presupuesto por 40.000 y calendario visible; caja sin cambio hasta pagar |
| Resumen con cuota y cargo adicional | Coincide el total bancario; no se duplica la cuota estimada |
| Pago parcial y total de tarjeta | Baja caja/deuda, quedan aplicaciones visibles y el gasto no aumenta otra vez |
| Devolución después del cierre | Crédito vinculado, efecto real y corrección visible; sin borrar cuotas arbitrariamente |
| Compra compartida y cobro parcial | Saldo por participante correcto, con deuda bancaria del titular independiente |
| Caja común con adelanto y aporte | Se explican consumo, aporte pendiente y saldo físico sin duplicarlos |
| Aporte a chanchito | Baja libre disponible, no el patrimonio ni el consumo |
| Saldo inicial más historia importada | No se vuelve a sumar lo anterior al corte; los errores quedan señalados |
| Dos pestañas o un reintento de carga | No duplica compras/pagos ni pierde cambios sin advertencia |

Estos casos son criterios de aceptación del producto, no afirmaciones sobre lo que ya está implementado.

## 24. Qué está definido y qué debe resolverse

La separación de conceptos y los circuitos anteriores son la propuesta funcional completa. El stack, monedas, planes Personal/Hogar, grupos independientes, privacidad opcional y presupuesto por cuotas ya están confirmados.

Las decisiones que todavía afectan el comportamiento son:

1. Qué ingresos se consideran para el reparto proporcional: neto esperado, neto cobrado, extras incluidos/excluidos o base declarada. Propuesta: base declarada por período, con fuentes seleccionadas y carácter provisional hasta confirmar.
2. Compras compartidas en cuotas: confirmado el cobro mensual según el reparto de costos de cada mes, conservando meses confirmados y distinguiendo previsiones futuras.
3. Adelantos a caja común: propuesta de elegir descontar del aporte o devolver, sin aplicar ambas compensaciones.
4. Cierre mensual: propuesta de arrastrar caja real y obligaciones pendientes separadamente; ajustes históricos trazables.
5. Comercialización: acceso de invitados sin plan propio, pertenencia a hogares, límites, precios y reglas de cambios/cancelación.

El sentido de etiquetas heredadas ambiguas —por ejemplo “aportes” de obra social o ciertos negativos— se revisará al importar cada caso. No hace falta resolver todas las filas de una planilla para definir el circuito; sí antes de convertirlas automáticamente en movimientos.

## 25. Relación con la implementación actual

La base actual dispone de autenticación/configuración, aperturas y consulta de cuentas/tarjetas. Todavía no implementa los circuitos cotidianos definidos aquí. La revisión del navegador detectó un tablero sin acciones para cargar operaciones o agregar recursos una vez finalizado el asistente.

Los próximos planes deben tomar este documento como alcance funcional y entregar recorridos completos. La prioridad propuesta es: cuentas/movimientos y pendientes; tarjeta/cuotas/resumen/pagos; colaboración/caja común; importación y proyección/ahorros; comercialización y Android. Esta secuencia no elimina funciones del producto ni convierte el asistente en una versión operativa suficiente.
