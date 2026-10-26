# Experiencia móvil operativa

El objetivo global sigue siendo el producto completo de `docs/producto/funcionalidades-y-circuitos.md`. Este diseño resuelve primero la experiencia de teléfono solicitada expresamente; no redefine la finalización del producto.

## Evidencia y dirección

La app actual se inspeccionó a 390×844: el encabezado, saludo y período consumen cerca de media pantalla; la navegación requiere desplazamiento horizontal; movimientos/cuentas/grupos muestran formularios extensos antes de su historial. Encajar el ancho de página no proporciona una experiencia móvil útil.

Se conserva Angular/Optimus y la identidad visual verde. Se adopta una estructura pensada para teléfono: encabezado compacto, navegación inferior fija y listados antes de formularios. Es una decisión de implementación dentro de la solicitud de optimizar y avanzar; la definición funcional y la ejecución con Luna ya fueron autorizadas. No se introduce una ronda de aprobación que frene la implementación.

Se consideró limitar el cambio a tamaños CSS; no resuelve el acceso a tareas ni la prioridad de información. Se consideró una app Ionic distinta; contradice la plataforma elegida y duplica mantenimiento. La solución es una única UI Angular adaptativa, reutilizable en Capacitor.

## Navegación y jerarquía

- En menos de 768 px: barra inferior con Inicio, Movimientos, Tarjetas, Compartidos y Más; etiquetas e iconos SVG simples, estado activo y objetivos táctiles de al menos 44 px. Más ofrece Cuentas y la configuración inicial cuando corresponda; otros módulos solo se agregan al estar operativos.
- Barra inferior respeta `safe-area-inset-bottom`, no tapa contenido ni envíos. En escritorio permanece navegación completa legible.
- Encabezado móvil contextual de unos 22 px, sin saludo gigante ni texto repetido. Marca compacta, período y actualización accesibles sin dominar la pantalla.
- Inicio conserva saldos y deuda ARS/USD separados. Cuotas del período, importes actuales y crédito no se confunden. Las acciones principales aparecen antes de listados largos y estados vacíos explican su acción.
- Navegar conserva formularios no enviados; cambiar contexto no descarta ediciones de forma silenciosa.

## Formularios y detalles

Movimientos muestra primero historial, filtros y acciones de ingreso/gasto/transferencia. Cuentas y tarjetas muestran sus recursos y acciones; Compartidos muestra grupos/cobros y permite elegir edición/alta. Los formularios de alta/ajuste/pago se abren al elegir una acción, con título, cierre/cancelación y envío identificables. Se mantienen las operaciones actuales completas.

Usar un componente de diálogo/panel reutilizable con foco inicial, cierre Escape/volver/cancelar, foco devuelto al disparador, contenido desplazable y campos/acciones visibles con teclado. Preferir OptimusUI cuando su API instalada cubra esa interacción. No implementar cuatro trampas de foco distintas. Durante guardado se evita doble envío y cierre accidental; en error se mantienen valores y UUID de reintento. No cerrar un formulario ante error.

En móvil, campos de al menos 16 px y controles de 44–48 px, etiquetas persistentes, `inputmode` decimal, fechas nativas y sin depender de hover. No esconder importes con puntos suspensivos; los montos grandes se ajustan o saltan línea. Los tipos de operación y estados se traducen al español.

## Verificación

Recorrido PostgreSQL/host real a 390 px: configuración vacía, cuentas, movimiento, compra, reglas mensuales, cobros y resumen/pagos. Mantener borrador/recarga y las invariantes exactas de dinero. A 360 px y escritorio verificar navegación/diálogos y que no haya desborde ni acciones cubiertas; cerrar con teclado devuelve foco. No basta una captura vacía ni un test de ancho.

La vista local se actualiza con recursos estáticos aditivos, sin borrar hashes antiguos, reiniciar la base o forzar recarga de una pestaña con datos del usuario.
