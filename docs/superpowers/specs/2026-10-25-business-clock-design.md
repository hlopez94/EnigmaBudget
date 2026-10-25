# Fecha financiera argentina

Parte del objetivo completo, posterior al rediseño móvil. La base usa fechas de negocio sin hora, pero deriva algunos cortes y validaciones desde UTC. Entre las 00:00 y 03:00 UTC, el día argentino todavía es el anterior. El usuario ya pidió ARS/USD y circuitos locales; el contrato operativo establece hoy en America/Argentina/Buenos_Aires.

Se adopta un reloj de negocio inyectable basado en TimeProvider y TimeZoneInfo con esa zona. Se mantienen instantes técnicos en UTC (eventos, expiraciones, actualización de proveedor) y fechas de negocio en Argentina. No hay un cambio de zona configurable por usuario en este bloque.

El reloj se usa en validaciones de pagos/cobros/transferencias/ajustes confirmados, cortes actuales de cuentas/tarjetas y período por defecto de reglas. Fechas futuras de obligaciones pendientes siguen permitidas. Las cuotas futuras de una compra confirmada siguen integrando deuda comprometida; no se eliminan por pertenecer a otro mes.

El saldo actual de cuenta solo incluye una apertura ya vigente y asientos con fecha efectiva hasta hoy. Esto también protege la lectura de datos legados aceptados en la ventana UTC anterior. Deuda de apertura de tarjeta futura queda fuera del saldo actual hasta su fecha argentina; sus campos originales se conservan para consulta. No se borra ni refecha historia.

Se descarta fijar el proceso completo a una zona del sistema: depende del despliegue y no permite pruebas aisladas. Tampoco se corrige solo el cliente, que ya usa la zona argentina; los permisos y efectos reales se verifican en servidor.

Pruebas reales con reloj fijo: 01/11/2026 01:30Z equivale a 31/10 argentino; operaciones confirmadas del 01/11 se rechazan y pendientes se aceptan sin banco. A las 03:30Z ya es 01/11 argentino y se habilitan sus aperturas/pagos válidos. Los dos endpoints de saldos coinciden. No debe haber un reloj estático mutable que contamine pruebas paralelas.
