# Rediseño visual con Product Design

El usuario pidió Product Design, alternativas visuales y que el constructor elija e implemente sin intervención. No reemplaza el objetivo de aplicación completa. Se generaron tres propuestas independientes basadas en capturas reales del producto y datos sintéticos; se eligió la tercera imagen mostrada, «Panorama sereno». El original de trabajo se conserva en `work/product-design/selected-panorama.png` (artefacto local ignorado).

## Alternativas y elección del constructor

- «Hoy claro»: gran superficie verde para saldos; acción visible, pero mayor peso visual y menos espacio para compromisos.
- «Mes en foco»: mayor énfasis en cuotas; más paneles y riesgo de restar protagonismo al saldo real.
- «Panorama sereno», elegida por el constructor: saldo tipográfico sin caja, acción principal clara, filas ligeras para cuotas, deuda total y cuentas. Mejora lectura y densidad en teléfono conservando la marca verde.

## Alcance de esta entrega

Aplicar el lenguaje visual a la aplicación Angular existente con OptimusUI2, sin un prototipo separado ni sustituir el runtime. Inicio es la referencia principal; movimientos, cuentas, tarjetas/resúmenes, compartidos, espacios/privacidad, acceso y asistente heredan tipografía, controles, encabezados, espaciado, estados y superficies coherentes. Conservar sus flujos funcionales y permisos, incluyendo selección y borradores. Los nuevos módulos financieros usarán estos mismos patrones.

Inicio muestra espacio y período legibles, «Saldo en cuentas» con ARS principal y USD separado, «Registrar movimiento» como acción dominante permitida, «Este mes» con cuotas por moneda, deuda total de tarjetas distinguida, y cuentas como filas. Mantener compra de tarjeta y cobros accesibles mediante acciones secundarias pertinentes. No llamar «disponible» al saldo menos deuda total, inventar vencimientos, unir monedas, ocultar deuda USD o mezclar cuotas con consumo total. Créditos a favor y avisos conservan su significado. Las cifras son del API; las de la imagen son ejemplos sintéticos.

## Sistema visual y adaptación

Canvas marfil claro, tinta verde casi negra, verde de marca y tintes suaves semánticos. Tipografía sans existente DM Sans/Manrope, cuerpo14–16px, entradas16px, etiquetas legibles, importes con numerales alineados y tamaños escalables. Menos cajas anidadas y sombras; agrupar por espacio/títulos/divisores. Mantener contraste WCAG AA, foco visible, controles44px, reducción de movimiento y estados disabled/busy/error/read-only claros.

Usar una biblioteca de iconos coherente, compatible y ligera verificada en fuentes oficiales, evitando glifos Unicode como iconos nuevos. Conservar la identidad existente; no añadir imágenes decorativas, gráficos ficticios ni branding nuevo. Los iconos no reemplazan etiquetas accesibles.

En360/390×844, saldo y acción primaria deben aparecer antes del pliegue incluso con configuración incompleta; esa ayuda pasa a una presentación compacta accesible. Monedas y valores largos deben envolver sin colisionar. Navegación inferior segura para área táctil, nunca tapa contenido final; escritorio1280×900 aprovecha dos columnas con la misma jerarquía. Texto de espacio largo y títulos Espacios/privacidad no se pisan con selectores.

## Autorización, estado y comprobación

No cambia contabilidad, contratos ni migraciones financieras. PersonalReadGrant continúa solo lectura sin grupos; archivo/permisos se validan en servidor; no se reintroducen formularios a Reader. No borrar datos ni sustituir API con respuestas simuladas de éxito. Corregir además el texto menor de invitación a miembro ya activo para no prometer cambio de rol que la aceptación conserva.

Comparar la referencia elegida y capturas reales normalizadas en una misma imagen de comparación; documentar densidad, viewport y diferencias esperadas por datos, accesibilidad y elementos funcionales no dibujados. Verificar tipografía, ritmo, tokens, iconos/marca, contenido, foco y estados. Product Design QA debe terminar passed sin P0/P1/P2 antes de entregar. Capturas con datos sintéticos aislados, nunca datos financieros del usuario en repositorio público. Conservar todas las pruebas financieras y multiusuario existentes, además de geometría móvil e interacciones visibles. Vista local5082 se actualiza con activos aditivos e index al final después de los gates; conservar backend/cookies y PG55433.

Tareas pesadas secuenciales, >=6GiB disponibles, límite4GiB y512MiBswap; Node2GiB, Angular/Vitest2workers, Playwright1. El usuario delegó decisiones visuales y de implementación; no detenerse por menús de aprobación de skills.
