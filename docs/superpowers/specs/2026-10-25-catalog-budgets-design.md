# Diseño: catálogo y presupuestos

Diseño para la siguiente fase de implementación. Las opciones y defaults indicados como **propuesta del constructor** son decisiones reversibles de diseño, no acuerdos nuevos del usuario. En la base `e974f85e8becd28a54c2154d9adca247eeda8649`, `FinanceOperation` vive en `Data/AppDbContext.cs`; `CardPurchase`, `CardInstallment` y `StatementAdjustment` viven en `Finance/FinanceEntities.cs`. El contexto y los permisos se resuelven en `FinanceWorkspaceFilter` y `WorkspacePermissions`.

## Intención y límites

Entregar una clasificación estructurada de operaciones históricas/nuevas y presupuestos mensuales por categoría y moneda. La clasificación explica operaciones; no cambia su efecto en caja. El presupuesto muestra consumo/compromiso una vez y explica cada fila.

Incluye: categorías de gasto/ingreso, comercios frecuentes y fuentes de ingreso separados; asignación estructurada con snapshot histórico; presupuesto por workspace, categoría, período y ARS/USD; consulta explicable por operación/cuota. Excluye: motor de reglas, árbol de categorías, rollover, alertas automáticas, reparto de caja común, migración de planillas, clasificación inferida y nuevas políticas comerciales.

## Enfoques

1. **Dejar las etiquetas como texto y normalizar al consultar.** Es barato al principio, pero no valida el uso, mezcla categorías con fuentes/comercios y deja asociaciones ambiguas al renombrar.
2. **Catálogos estructurados locales al workspace, conservando el texto histórico como snapshot.** Agrega una migración y FKs, pero aprovecha el ledger, evita relaciones entre espacios y conserva las consultas existentes. **Propuesta del constructor: elegir este enfoque** por ajuste al host único y necesidad de trazabilidad.
3. **Motor configurable de clasificación y presupuesto.** Puede cubrir más escenarios, pero introduce fórmulas editables sin beneficio necesario para la primera entrega.

## Modelo propuesto

- `Category`: workspace, `Kind=Expense|Income`, nombre, archivado y versión. Sin jerarquía ni catálogo global.
- `Merchant`: workspace, nombre, versión y archivo. No se adivina desde `Description`.
- `IncomeSource`: workspace, nombre, tipo `Salary|Extra|AnnualBonus|IndependentWork|Other`, archivo y versión. No sustituye la categoría ni crea componentes bruto/neto.
- Los nombres se comparan exactamente, sensibles a mayúsculas y espacios; la unicidad es por workspace y, para categoría/fuente, por tipo.
- Inicio: por cada workspace se crean únicamente categorías `Sin clasificar` de tipo `Expense` e `Income`, además de buckets para cada etiqueta histórica exacta; no se sugieren categorías de negocio. La creación de un nuevo espacio agrega esas dos categorías en la misma transacción, sin escribir desde las consultas de catálogo. Comercios y fuentes de ingreso quedan vacíos hasta que una persona los cree explícitamente. Las personas pueden agregar valores después.
- Las categorías históricas de operación, compra o ajuste se backfillean por workspace, tipo y cadena `Category` exacta, preservando cada snapshot original. Un campo vacío o compuesto solo por blancos recibe “Sin clasificar”, conservando el snapshot original. El tipo de operación se conoce por el campo estructurado; no se infiere de la etiqueta. No se crean comerciantes ni fuentes desde texto. Todos los IDs nuevos tienen FK compuesta con `WorkspaceId`.
- `FinanceOperation.Category`, `CardPurchase.Category` y `StatementAdjustment.Category` siguen como snapshots compatibles. Se agregan IDs nulos para transición. Las escrituras nuevas validan workspace y tipo; al renombrar categoría o comercio no se altera historia. Un caller legado que envía categoría textual se resuelve por coincidencia exacta dentro del workspace, sin búsqueda difusa.
- `MonthlyBudget`: workspace, `YYYY-MM`, categoría Expense, ARS/USD, límite no negativo y versión; índice único por workspace/período/categoría/moneda. Sin total editable mensual ni conversión de moneda.
- En el corte autónomo de Catálogo/Presupuesto, la consulta suma `FinanceOperation.Amount` no anulados por período, categoría y moneda, y cada cuota real por su período. Todavía no hay liquidaciones no monetarias. Cuando se integre Recurring/Healthcare, esa consulta reemplaza su aporte de operación por `OperationSettlementCalculator.Calculate(...).OperationMonthBudgetContribution`; este contrato se define en aquel plan, no implica que la calculadora ya exista. En el resultado integrado, el caso 1.000 bruto, 300 nómina y 700 a tarjeta cuenta 300 en octubre y 700 en noviembre; con 700 de banco en vez de tarjeta, octubre conserva 1.000 de consumo y caja solo refleja 700. Se excluyen total de compra, total de resumen y pagos de tarjeta como gastos; ajustes de resumen confirmados cuentan una vez.
- `FinanceOverview.budget.operationExpensesArs/Usd` presenta ese mismo aporte del mes. Los campos existentes `cashExpensesArs/Usd` se conservan como aliases de compatibilidad con exactamente el mismo valor, calculado una sola vez. La interfaz usa “Consumo registrado” para el presupuesto y “Caja” solo para entradas/salidas reales de cuentas; no los confunde. Si se anula una operación, su aporte queda en cero; una `CardPurchase` real y sus cuotas siguen hasta un crédito del emisor. `FinancePayment` siempre pertenece a una cuenta real y la liquidación no monetaria nunca crea `LedgerEntry`.

## Permisos, seguridad y experiencia

Decisiones del controlador: las dos categorías iniciales se identifican con `IsDefault` y conservan nombre/tipo; no se renombran ni archivan, porque sostienen la clasificación de registros heredados sin coincidencia. Las demás categorías, comercios y fuentes admiten renombre y archivo, manteniendo sus snapshots. Los nombres nuevos se recortan en los extremos, aceptan hasta 80 caracteres y no incluyen caracteres de control; la historia almacenada no se recorta ni normaliza.

Cada mutación de catálogo persiste un `CatalogCommand` por workspace/RequestId, con tipo de comando, recurso, hash del payload original, resultado JSON, actor de servidor y hora UTC. Comando, cambio y auditoría comparten transacción; un replay exige permiso vigente y devuelve el resultado guardado sin repetir escrituras.

API y consultas derivan el workspace del contexto resuelto por `FinanceWorkspaceFilter`. `read` consulta; `manageResources` administra catálogo/presupuesto; `recordFinance` conserva su uso para registrar movimientos. PersonalReadGrant sigue siendo read-only. Mutaciones requieren CSRF, GUID idempotente, comparar payload al reintentar (igual devuelve el mismo resultado, distinto devuelve 409), y versión en edición. Montos son cadenas decimales en API y `numeric(18,2)` en PostgreSQL. Copy en español. La UI debe distinguir gasto registrado y compromiso de tarjeta y enlazar sus orígenes.

## Criterios y decisiones abiertas

- Historial conserva texto, ID de operación, montos y fechas; ninguna fuente/comercio se deduce automáticamente.
- Catálogos y presupuestos se aíslan entre espacios; archivar no borra histórico ni permite nuevas selecciones.
- ARS/USD, presupuesto y consumo se ven separados; las cuotas mensuales aparecen una vez, pago y resumen no añaden gasto.
- **Propuesta del constructor:** presupuesto hogar pertenece al workspace Hogar; sin rollover, alertas o topes por integrante. Monto cero significa sin margen asignado, pero no bloquea gastos.
- No se definen jerarquías, métricas ni alertas futuras. No son requisitos de esta entrega.

## Restricciones de proyecto

Angular 22, OptimusUI 2 compatible, .NET 10, PostgreSQL y un solo host ASP.NET. No usar Ionic. Aplicar los límites de RAM de `AGENTS.md` antes de builds o pruebas. No copiar datos, nombres, enlaces ni identificadores de las planillas privadas al repositorio o a fixtures.

