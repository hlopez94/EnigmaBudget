# Contrato operativo: movimientos, tarjetas y reparto mensual

Contrato compartido entre backend, UI y pruebas del plan2026-10-24-operations. Nombres JSON camelCase. UUIDs y dinero/fechas son strings. No modificar nombres sin coordinar con el controlador.

## Convenciones

- Todas las rutas /api requieren identidad autenticada salvo las existentes de sesión. Mutaciones X-CSRF-TOKEN. Scope obtenido de la identidad; no se recibe workspaceId del cliente.
- Dinero decimal invariante, máximo2decimales, rango numeric(18,2); importes positivos salvo apertura/ajuste/crédito explícito. ARS/USD. Fechas YYYY-MM-DD, períodos YYYY-MM. Hoy según zona America/Argentina/Buenos_Aires, sin desplazar fechas por UTC en el cliente.
- Mutaciones POST de efectos monetarios llevan id UUID del cliente: reintento idéntico devuelve el registro sin duplicar; mismoid con datosdistintos409. Validación400, ajeno/inexistente404, staleversion/mescerrado409, noauth401.
- Actualizaciones de configuración llevan version; devuelve nueva versión. Usar transacciones y concurrencia en operaciones con saldo pendiente, tarjeta y reglas para no cobrar/pagar dos veces. Identificadores duplicados se validan también en arrays.
- Las entidades confirmadas no se eliminan físicamente. Archivar recurso no borra historia, saldo ni deuda.

## Panorama y consultas

GET /api/finance?period=YYYY-MM devuelve FinanceOverview:

```
{period, accounts:Account[], cards:Card[], operations:Operation[],
 groups:Group[], installments:Installment[], receivables:Receivable[],
 budget:{cashExpensesArs,cashExpensesUsd,cardInstallmentsArs,cardInstallmentsUsd,
         cardChargesArs,cardChargesUsd}, totals:{balanceArs,balanceUsd,
         cardDebtArs,cardDebtUsd,cardCreditArs,cardCreditUsd}}
```

Saldos y deuda son actuales; operaciones/cuotas/cargos son del período seleccionado. Nunca sumar saldos ARS y USD. GET /api/operations?period=...&accountId=... filtra movimientos propios; GET /api/cards/{cardId}/purchases devuelve compras y GET /api/purchases/{id} devuelve compra con calendario completo. Fechas antes de apertura no se vuelven a sumar al saldo.

## Cuentas y tarjetas después del asistente

Mantener GET /api/accounts y /api/cards compatibles con campos originales. Extender:

```
Account {id,name,bankName,currency,openingBalance,openingDate,currentBalance,isArchived,version}
Card {id,name,bankName,closingDay,dueDay,statementDebtArs,statementDebtUsd,asOfDate,
      debtArs,debtUsd,creditArs,creditUsd,isArchived,version}
```

POST /api/accounts: {id,name,bankName,currency,openingBalance,openingDate}. Crear apertura, no ingreso. PUT /api/accounts/{id}: {version,name,bankName,isArchived}. Apertura no se sobrescribe con movimientos existentes; mantenimiento cambia etiquetas/archivo, conservando historial. POST /api/accounts/{id}/adjustments: {id,date,observedBalance,reason}; calcular diferencia con saldo registrado a esa fecha y registrar ajuste identificado, no ingreso/gasto.

POST /api/cards: {id,name,bankName,closingDay,dueDay,statementDebtArs,statementDebtUsd,asOfDate}. Deuda inicial por moneda no es compra nueva. PUT /api/cards/{id}: {version,name,bankName,closingDay,dueDay,isArchived}. Una tarjeta archivada no acepta compras nuevas, pero conserva pagos, consultas y deuda pendiente. Cuenta archivada no admite nuevas operaciones; correcciones históricas siguen trazables.

Nombres/banco obligatorios<=120, días nulos o1..31, aperturas con fecha válida, deudas iniciales>=0. Clientes iniciales/onboarding existentes siguen funcionando.

## Ingresos, gastos pendientes y transferencias

POST /api/operations:
```
{id,kind:'Income'|'Expense',accountId,date,description,category,amount,paid:boolean}
```

Moneda derivada de cuenta, descripción1..240, categoría<=80. Con paid=true crear el pago y asiento bancario en la misma transacción. Con paid=false registrar obligación confirmada sin movimiento de dinero. Un ingreso/gasto puede pagarse/cobrarse parcialmente mediante:

POST /api/operations/{id}/payments: {id,accountId,date,amount}. Cuenta misma moneda, importe<=pendiente; ingreso produce entrada y gasto salida. Rechazar pagos confirmados futuros y anteriores a apertura. La fecha de la obligación puede ser futura si queda pendiente. No considerar cobrar deuda como sueldo cuando su origen es compartido/tarjeta.

Operation {id,kind,accountId,date,description,category,amount,currency,paidAmount,pendingAmount,status:'Pending'|'Partial'|'Settled'|'Voided',version,payments:[{id,accountId,date,amount}]}.

POST /api/operations/{id}/void: {id,version,reason}; reversar pagos con asientos vinculados y marcar anulada, sin borrar. Repetir reversión no duplica efectos. No seguir pagando operación anulada. Gastos confirmados se imputan al período del gasto aunque el dinero salga después; saldos usan solo pagos reales.

POST /api/transfers: {id,fromAccountId,toAccountId,date,amount,destinationAmount}. Misma moneda exige importes iguales; diferente moneda exige ambos importes reales, sin conversión automática. Crear dos asientos vinculados, nunca ingreso/gasto de consumo. No transferir a la misma cuenta ni cuentas ajenas/archivadas.

Regla bancaria única: openingBalance + suma de LedgerEntry firmados de esa cuenta. LedgerEntry incluye fuente/originId, fecha y clase para distinguir pagos, cobros compartidos, transferencias y ajustes de consumo. No persistir saldos mutables susceptibles a lostupdates.

## Grupos manuales de compras compartidas

En esta entrega los grupos organizan compras de las tarjetas propias con participantes manuales, incluidos quienes no tienen cuenta. No simular invitaciones o acceso autenticado de otros usuarios. El propio usuario es un participante (isSelf=true) y los demás son externos registrados; hay exactamente un self. Cada grupo pertenece al workspace autenticado y no concede lectura a nadie.

POST /api/groups: {id,name,participants:[{id,name,isSelf}]}; entre2y50 participantes, nombres1..120, IDsúnicos. GET /api/groups devuelve Group{id,name,version,participants,rule:MonthlyRule|null} del período seleccionado (queryperiod). No ofrecer edición de miembros tras crear compras mientras no exista un circuito de cambio histórico de beneficiarios; sí reglas por mes editables.

PUT /api/groups/{groupId}/rules/{period}:
```
{version,mode:'Equal'|'Percentage'|'Income',basisCurrency:'ARS'|'USD',
 weights:[{participantId,value}]}
```
MonthlyRule {period,version,mode,basisCurrency,weights,confirmed}. Equal reparte iguales; Percentage suma100.00; Income suma positiva, permite ceros individuales y bases de una misma moneda. No acceder a movimientos privados de otros para calcular; los valores son bases declaradas. Todos los participantes deben aparecer una vez. Grupo conserva versión agregada para serializar regla/confirmación/cobro.

POST /api/groups/{groupId}/rules/{period}/confirm: {version}; congelar regla del mes. Intento de modificar regla confirmada devuelve409; meses futuros sin confirmar pueden cambiar. Crear regla de un mes sin regla usa version0.

Para cada cuota compartida usar regla EXACTA de su período. Si falta: última regla anterior solamente como proyección isEstimated=true; sin anterior,amount=null y estado MissingRule. No cobrar una estimación. Primer cobro con regla definida confirma automáticamente ese mes. Al cambiar mes futuro, recalcular dinámicamente previsiones de sus cuotas; cuotas de meses confirmados/cobrados no cambian. Nuevas compras en mes ya confirmado usan esa misma regla confirmada.

Reparto exacto en centavos usando cocientes enteros y mayor resto, desempate estable por participanteId. Sumatoria de partes=cuota. Evitar overflow de multiplicar dos decimalesmáximos (usar centavos y BigInteger o equivalente exacto).

Receivable {groupId,sourceId:installmentId,purchaseId,period,description,currency,
 participantId,participantName,isSelf,amount:string|null,collectedAmount,pendingAmount:string|null,
 isEstimated,ruleVersion,monthConfirmed,status:'MissingRule'|'Estimated'|'Pending'|'Partial'|'Collected'|'OwnShare'}.

El self muestra su parte sin cobro a sí mismo. Los demás generan cobros al titular de la tarjeta:

POST /api/groups/{groupId}/collections:
```
{id,installmentId,participantId,accountId,date,amount,appliedAmount,ruleVersion}
```
amount es entrada bancaria en moneda de cuenta; appliedAmount cancela deuda del participante en moneda de cuota. Misma moneda exige igualdad; FX explícito admite dos importes positivos. No superar pendiente, no cobrar self, no cobrar mes sin regla propia. Congelar regla y registrar cobro+asiento juntos. Cobrar NO modifica CardPayment ni deuda bancaria. CollectedAmount suma appliedAmount, no importe FX de banco. Idempotencia y concurrencia evitan cobros duplicados/superiores a la parte.

## Compras y cuotas

POST /api/purchases:
```
{id,cardId,date,description,category,currency,totalAmount,installmentCount,firstPeriod,groupId:null|uuid}
```
installmentCount1..120; total positivo y suficiente para cuotas positivas; primera cuota en/o después del mes de apertura, compra no anterior al corte de tarjeta para evitar duplicar apertura. No crear/alterar cuotas en un resumen confirmado de esa tarjeta/moneda/período; informar409. Tarjeta/grupo propios, tarjeta activa.

Purchase{id,cardId,date,description,category,currency,totalAmount,installmentCount,firstPeriod,groupId,installments:Installment[]}.
Installment{id,purchaseId,cardId,description,currency,number,count,period,amount,groupId}.

La compra crea todas las obligaciones previstas y deuda comprometida, no asientos bancarios. División exacta en centavos (residuo determinista); total cuotas=totalCompra. Compartida: la parte de cada persona se calcula por MES, no porcentaje fijo almacenado en compra. Presupuesto mensual muestra cuota; no suma totalCompra otra vez. Las compras históricas/importaciones/edición de cuotas previas se mantienen en el siguiente plan, no se inventan desde aperturas.

## Resúmenes y pagos

GET /api/cards/{cardId}/statement?period=YYYY-MM&currency=ARS|USD devuelve:
```
Statement {id:null|uuid,cardId,period,currency,version,state:'Estimated'|'Draft'|'Confirmed',
 dueDate:null|string,actualTotal:null|string,expectedAmount,carryAmount,pendingAmount,
 creditBalance,installments:Installment[],adjustments:[{id,description,category,amount}],
 payments:[CardPayment]}
```

La deuda inicial se considera obligación de apertura del mes de asOfDate. Deuda actual=apertura + cuotas/compras reconocidas + ajustes confirmados − pagos aplicados. No sumar resúmenes como nueva deuda. Si neto<0:debt0 y credit positivo por moneda.

Importe pendiente para un período usa obligaciones acumuladas hasta ese período y pagos/créditos aplicados, cada origen una vez. Cuotas futuras no aparecen como pendientes del resumen actual. Saldo anterior no se copia como una segunda obligación. Lista current installments y carryAmount explican el importe. Pagos superiores al saldo de un mes se conservan para próximos cargos; mostrar crédito separado.

PUT /api/cards/{cardId}/statement:
```
{id,period,currency,version,dueDate,actualTotal,
 adjustments:[{id,description,category,amount}]}
```
Guarda revisión borrador, importes firmados en ajustes (positivo cargo/interés, negativo crédito/devolución), total real>=0. No convertir ajustes borrador en deuda real. No editar confirmado. dueDate se puede completar manualmente; calendario solo da estimación si card closing/dueDay informados, sin inventar fecha bancaria segura.

POST /api/cards/{cardId}/statement/confirm: {id,version}. Confirmar solo si total real coincide con pendiente estimado incluyendo ajustes ingresados y pagos reales actuales. Si no coincide400 con diferencia; usuario debe explicar el ajuste. Confirmar cargos una sola vez; después de confirmar snapshot inmutable y pendientes de pago dinámicos. Refund negativo afecta deuda solo al confirmar crédito real; no cancela cuotas futuras automáticamente. Si ya confirmado, reintento devuelveestadoactual sin duplicar ajustes.

POST /api/cards/{cardId}/payments:
```
{id,accountId,date,amount,currency,appliedAmount,statementId:null|uuid}
```
amount es salida bancaria; currency es moneda de obligación ARS/USD; appliedAmount es cancelación de deuda de tarjeta. Si cuenta misma moneda igualdad; si distinta dos importes reales explícitos. Fecha real<=hoy y>=aperturas. Pago puede ser parcial/completo y admite excedente identificado como crédito. No genera gasto presupuestario nuevo. No se pagan cargos inexistentes por moneda sin una obligación/anticipo claramente mostrado. Si statementId se informa, debe ser de esa tarjeta/moneda y estar confirmado.

CardPayment {id,cardId,accountId,date,amount,accountCurrency,currency,appliedAmount,statementId}.

La conciliación del resumen es independiente de la confirmación del reparto del grupo: no bloquear pago al banco porque aún no se definió cuánto debe un participante.

## Casos verificables

1. CuentaARS100000: ingreso1000 cobrado ->101000; gasto100 pagado ->100900. Transferencia100 a otraARS ->100800 y100. Total100900, gasto100. Idsrepetidos no cambian otra vez.
2. Gasto pendiente100: saldo sin cambio; pago40 ->baja40, pendiente60; pago60 ->pendiente0 ybaja60, consumo100 una sola vez. Pago excedente rechazado; anulación revierte pagos sin borrarhistoria.
3. Compra120000/3 desdeOct ->3cuotas40000; bancos sin cambio, deuda120000, presupuestoOct40000. ResumenOct40000 confirmado; pago20000 ->deuda100000, pendiente20000; segundo pago20000 ->deuda80000, pendiente0. Noviembre pendiente40000, no80000.
4. Grupo selfA+externoB. Octubre60/40 ->B16000. Noviembre30/70 ->B28000. CambiarNov a50/50 ->B20000; Octconfirmado sigue16000. Diciembre sinregla soloestimación, no cobro permitido.
5. Cobrar8000B ->banco+8000, pendienteB8000, tarjeta sin cambios. ReintentoID igual no suma otra vez. Otro cobro8000 ->pendiente0; otroimporte>pendiente400. ModificarOctcon cobro409.
6. Partición0.01 conpesos60/40 ->partes0.01/0.00 exactas; no inventar centavos. BasesIngreso0/0 rechazadas; mezcla de participantes ajenos rechazada.
7. Resumen con cargo100 no concilia si total real40000; con real40100 confirma y deuda+100una vez. Reintento no duplica. Crédito confirmado reduce deuda o genera saldo a favor; pagar no agrega consumo.
8. Todo recurso ajeno404sinwrites; sinCSRF400; importesconsubcentavos/overflow y fechas inválidas400. Dos peticiones concurrentes no pierden versiones ni permiten sobrecobrar.

## Límites de esta entrega operativa

Gestión manual completa de cuentas, dinero y ciclo de tarjeta, con reparto/cobro mensual de compras compartidas. Invitaciones autenticadas, caja común multiusuario, gastos grupales pagados por terceros, recurrencias automáticas, lectura de resúmenes/importación, objetivos/DolarAPI/Android/facturación siguen definidos en el documento de producto para los siguientes planes. No ofrecer sus botones como si funcionaran. Estos límites no impiden cargar y continuar finanzas reales con las funciones de este contrato.
