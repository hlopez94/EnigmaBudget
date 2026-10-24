# EnigmaBudget

Fundación ejecutable para configurar cuentas, saldos iniciales y deuda del resumen actual. Un único host ASP.NET Core sirve la API y los archivos compilados de Angular; PostgreSQL guarda los datos estructurados. La configuración de S3/R2 queda preparada para una etapa posterior, sin endpoints de archivos todavía.

La aplicación aún no reemplaza toda la planilla ni representa el SaaS terminado. La primera entrega cubre el inicio de sesión disponible, el borrador de configuración inicial y la confirmación idempotente de cuentas y tarjetas.

## Requisitos

- .NET SDK 10.
- Node.js 24 y npm 11.
- PostgreSQL 17 para pruebas y desarrollo, o Docker Compose con Podman compatible.
- Playwright Chromium para la prueba de navegador: desde `web/`, ejecutá `npx playwright install chromium`.

## Desarrollo local con contenedores

`scripts/local-env.sh` genera credenciales aleatorias locales en `.env` y una configuración privada de Garage en `work/garage.toml`. Ambos archivos están ignorados por Git. No publiques sus valores. `.env.example` solo documenta las variables esperadas; sus textos de reemplazo no son credenciales utilizables. El script deja `ENIGMABUDGET_E2E_CONNECTION` vacío para impedir que Playwright use accidentalmente la base habitual.

```sh
./scripts/local-env.sh
docker compose up --build
```

La aplicación queda en `http://127.0.0.1:5080`. En este Compose local, el inicio de desarrollo está habilitado explícitamente. PostgreSQL queda publicado solo en `127.0.0.1:55432`; Garage S3 queda en `127.0.0.1:3900`. El puerto de administración de Garage y sus puertos internos de clúster no se publican. En la primera inicialización, PostgreSQL crea una base separada `enigmabudget_test` para pruebas.

Garage corre aquí como un nodo local de desarrollo sin redundancia; no lo uses como almacenamiento de producción.

Para detener el stack y conservar los datos:

```sh
docker compose down
```

Eliminá los volúmenes solo si también querés borrar los datos locales de PostgreSQL y Garage.

## Compilar y verificar

`scripts/verify.sh` corre las pruebas .NET con PostgreSQL real, las pruebas unitarias web y el build Angular. Si `ENIGMABUDGET_E2E_CONNECTION` está definido, también levanta el host .NET real y ejecuta Playwright contra esa base. La base de navegador debe ser nueva, aislada y descartable: el acceso de desarrollo usa una identidad fija de prueba.

Con el Compose local activo, creá una base vacía `enigmabudget_e2e` con el usuario local:

```sh
docker compose exec -T postgres createdb -U enigmabudget enigmabudget_e2e
```

Si el volumen de PostgreSQL ya existía antes de agregar `enigmabudget_test`, creá también esa base de pruebas una vez con `docker compose exec -T postgres createdb -U enigmabudget enigmabudget_test`. Configurá las cadenas de forma que integración y navegador usen bases independientes:

```sh
export ENIGMABUDGET_TEST_CONNECTION='Host=127.0.0.1;Port=55432;Database=enigmabudget_test;Username=enigmabudget;Password=VALOR_DE_TU_ENV'
export ENIGMABUDGET_E2E_CONNECTION='Host=127.0.0.1;Port=55432;Database=enigmabudget_e2e;Username=enigmabudget;Password=VALOR_DE_TU_ENV'
./scripts/verify.sh
```

También se puede correr el navegador por separado con `ENIGMABUDGET_E2E_CONNECTION='cadena-a-base-vacía' npm --prefix web run test:e2e`. Playwright exige esa variable y no recurre a la conexión de las pruebas unitarias. La configuración compila y arranca el host de un solo proceso en `127.0.0.1:5080`, aplica las migraciones al iniciar y usa una identidad local fija. Para repetir el flujo, usá otra base vacía. Las capturas de la prueba se guardan en `work/task3-dashboard.png`.

Para crear y publicar la aplicación en `artifacts/publish`:

```sh
./scripts/build.sh
```

El build de contenedor es multi-etapa y compila Angular antes de publicar .NET:

```sh
docker build -t enigmabudget:local .
```

## Inicio de sesión y cookies

El botón de desarrollo aparece solo si `ASPNETCORE_ENVIRONMENT=Development` y `Authentication:DevelopmentLoginEnabled=true`. Esa identidad fija es para desarrollo local y pruebas; el host no la registra en Production. En Development opt-in, las cookies siguen el esquema HTTP/HTTPS solicitado para permitir loopback HTTP. En los demás entornos, las cookies de autenticación y antifalsificación conservan `SecurePolicy=Always`.

Google está deshabilitado de forma segura si falta cualquiera de `Authentication__Google__ClientId` o `Authentication__Google__ClientSecret`; el endpoint devuelve 503. Configure ambos secretos fuera del repositorio para probar el flujo real. No se probaron credenciales reales ni una autorización de Google.

## VPS y proxy inverso

En producción, terminá TLS en el proxy y configurá el proxy inmediato como confiable con `ReverseProxy__KnownProxies__0`, `ReverseProxy__KnownProxies__1`, etc., usando direcciones IP exactas. La aplicación procesa `X-Forwarded-Proto` y `X-Forwarded-For` solo desde proxies conocidos; conserva los valores predeterminados confiables de loopback. No uses una lista vacía de restricciones ni reenvíes encabezados de clientes sin que el proxy los elimine y escriba de nuevo. Para Nginx local en el mismo VPS, por ejemplo, confiá solo en `127.0.0.1` o `::1` según dónde escuche el proceso. Para un proxy en otro host, agregá la IP privada fija de ese proxy.

El proxy debe reenviar el esquema TLS original (`X-Forwarded-Proto: https`) después de sanitizar los encabezados entrantes. Esto permite que los callbacks de Google y la validación antifalsificación vean HTTPS. La aplicación no confía en `X-Forwarded-For` enviado directamente por navegadores arbitrarios. No habilites el inicio de desarrollo en VPS ni Production.

Las respuestas `/api` envían `Cache-Control: no-store` y `Pragma: no-cache`. Configurá también Cloudflare para omitir caché en `/api/*`; el frontend estático puede usar su propia política de caché.

## Almacenamiento S3 y R2

La sección `Storage:S3` acepta `Endpoint`, `Bucket`, `Region` y `ForcePathStyle` y valida el endpoint HTTP(S) cuando se configura. Compose conecta el seam a Garage, compatible con S3. El bucket permanece privado. Esta versión no implementa carga, descarga, URLs firmadas, políticas de archivos ni procesamiento de adjuntos; las credenciales S3 no se consumen aún por el host.

Para Cloudflare R2, configurá el endpoint regional de la API S3, el bucket privado, región `auto` y `ForcePathStyle` según la futura integración. Guardá las claves de R2 en el almacén de secretos del entorno, nunca en Git. La futura implementación del SDK AWS deberá aplicar los ajustes de firma/checksum que exige R2 para `PutObject`; no se declara verificado ningún flujo R2.

## Estado del producto

### Disponible en esta base

- Inicio con Google cuando hay credenciales configuradas y acceso local opt-in en Development.
- Espacio personal y borrador de configuración aislado por identidad autenticada.
- Cuentas ARS/USD, saldos iniciales y deuda del resumen actual en monedas separadas.
- Guardado versionado y confirmación idempotente dentro de una transacción PostgreSQL.
- Host único, migraciones al inicio, frontend compilado y servido desde el mismo proceso.

### Pendiente

- Libro de ingresos, gastos, transferencias, pagos, reintegros y conciliación.
- Movimientos, categorías, presupuestos, límites y estados de resúmenes.
- Calendario, cuotas futuras, pagos de tarjeta y proyecciones de deuda y disponibilidad.
- Hogares, grupos, participantes, invitaciones, permisos y reparto de gastos.
- Metas de ahorro, pronósticos y cotizaciones de DolarAPI.
- Migración de historial Excel/CSV y revisión de duplicados.
- Carga, lectura y descarga privada de adjuntos en S3/R2.
- Sincronización offline y experiencia Android/Capacitor.
- Planes, suscripciones, Mercado Pago y administración.
- Auditoría ampliada, gestión operativa, copias y restauración.

No hay pantallas ficticias para esas funciones. Los saldos actuales no incorporan movimientos ni cuotas futuras.
