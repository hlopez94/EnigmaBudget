# Validación local de autenticación nativa para Android

Estado: **APK debug y aceptación local aprobados**. Esta evidencia cubre el contrato del servidor y el empaquetado local; no valida Google, un teléfono ni la asociación real de App Links.

## Resultados locales

| Área | Resultado | Evidencia |
|---|---|---|
| Aceptación nativa/API | PASS, 1 prueba | El inicio de desarrollo optativo emitió una sesión nativa; `/api/me`, lista de espacios, cuentas y finanzas respondieron bajo esa sesión; se creó una cuenta y un gasto; un segundo usuario no pudo consultar el espacio ajeno; refresh rotó, replay revocó la familia y logout revocó acceso y refresh. |
| Base de datos | PASS | PostgreSQL local de prueba en el puerto 55432. `ApiFactory` creó y eliminó esquemas únicos por prueba. No se usó la base de la aplicación ni se insertaron datos financieros reales. |
| TLS de la aceptación | Alcance limitado | ASP.NET `WebApplicationFactory` atendió solicitudes con URI HTTPS en `TestServer` en memoria. Comprueba middleware, autorización y la exigencia HTTPS del contrato; no prueba un socket TLS, certificado de dispositivo ni conexión remota. |
| Host App Links | PASS, regla empaquetada | La prueba Gradle usa la misma función de validación que `app/build.gradle`. Aceptó un nombre DNS válido, rechazó esquemas, comodines, puertos, rutas, query, fragmentos, mayúsculas y espacios; comprobó que el manifiesto queda limitado a HTTPS, host exacto y `/invitar`. El host vacío omite el filtro. |
| Android JVM | PASS, 2 pruebas | `AppLinksPolicyTest`: host DNS válido y host vacío; rechazos para entradas inválidas. |
| Guardas de recursos | PASS | La RAM insuficiente y la ausencia del ejecutor cgroup rechazaron antes de iniciar un hijo. Un hijo verificó límites reales de cgroup v2 de 4 GiB RAM y 512 MiB swap; el wrapper propagó su código de salida. |
| APK debug | PASS | APK firmado con el certificado debug, manifiesto/icono verificados, sin `server.url`, secreto de cliente ni JWT en recursos públicos. No se creó APK release ni keystore. |

El build Angular conservó la advertencia existente de tamaño: **775,05 kB** iniciales frente al presupuesto de **760 kB**, **15,05 kB** por encima. No se cambió el presupuesto. Gradle emitió avisos no bloqueantes sobre la versión 8.14.3 y el SDK XML; la compilación terminó correctamente.

## Artefacto

- Archivo generado, ignorado por Git: `web/android/app/build/outputs/apk/debug/app-debug.apk`
- Tamaño: **10 647 530 bytes**
- SHA-256: `aff5759b304cc8552804ea1bdf56d452fba8c74ff9320924712ee3b63a2a611d`
- Paquete: `com.enigmabudget.dev`
- Firma: Android Debug; SHA-256 del certificado de depuración: `0e27dfaec619b71a3d9afaba529c9484f53dd5eac103ef5a629a63a79b485c2b`
- Marca: vector reutilizando el monograma “e” blanco en cursiva y el verde `#376C4B` del sitio.
- `server.url` no está configurado. El host API quedó sin configurar en esta compilación; no se agregó un host de muestra.
- Como no hay dominio configurado, el APK no contiene el filtro App Links. La prueba de manifiesto ejercitó una configuración de ejemplo solo dentro del test y no acredita asociación de dominio.

## Versiones seleccionadas

- Node **24.22.0**, npm **11.19.1**
- .NET SDK **10.0.402**
- JDK **21.0.13**
- Capacitor **8.5.3** (Core, CLI y Android); Capacitor App **8.1.2**
- Android Gradle Plugin **8.13.0**, Gradle Wrapper **8.14.3**, Kotlin Gradle Plugin **2.4.21**
- Android `compileSdk` / `targetSdk` **36**, `minSdk` **24**, Build Tools **35.0.0**

## Comandos ejecutados

Los comandos usaron las instalaciones locales de desarrollo, pero los scripts públicos aceptan `DOTNET_COMMAND`, `JAVA_HOME` y `ANDROID_HOME` del operador y no dependen de esas rutas locales.

```bash
DOTNET_COMMAND="$(pwd)/work/tooling/dotnet/dotnet" scripts/accept-native-local.sh
JAVA_HOME="$(pwd)/work/tooling/jdk21" \
  ANDROID_HOME="$(pwd)/work/tooling/android-sdk" \
  ANDROID_SDK_ROOT="$(pwd)/work/tooling/android-sdk" \
  scripts/build-android-debug.sh --self-test-guards
JAVA_HOME="$(pwd)/work/tooling/jdk21" \
  ANDROID_HOME="$(pwd)/work/tooling/android-sdk" \
  ANDROID_SDK_ROOT="$(pwd)/work/tooling/android-sdk" \
  scripts/build-android-debug.sh
```

La aceptación requiere `work/workspace-test-connection.txt` local, ignorado y con permisos privados; el script no imprime la cadena de conexión. Solo acepta PostgreSQL local en el puerto 55432 y delega al test harness que crea esquemas efímeros. El build corre Angular y Gradle en secuencia, con heap Node de 2 GiB, dos workers Angular, un worker Gradle, Kotlin en proceso y cgroup v2 de usuario limitado y comprobado. Se cancela si quedan menos de 6 GiB disponibles.

## Prerrequisitos externos pendientes

- **Google:** no hay proyecto Google Cloud ni clientes OAuth Android/Web proporcionados. La ruta de desarrollo no simula Credential Manager ni cuenta como inicio Google.
- **API pública:** falta un dominio HTTPS estable para configurar y validar el host remoto del API.
- **App Links real:** falta dominio bajo control del usuario y publicar `/.well-known/assetlinks.json` con el package y la huella SHA-256 del certificado que se use para firmar. La huella del APK debug no es una publicación ni una prueba del dominio.
- **Dispositivo:** SDK Android, plataforma 36, Build Tools 35.0.0, `adb` y JDK 21 están disponibles localmente. Falta un teléfono Android físico para validar Credential Manager, Keystore, restore, logout y apertura real de invitaciones.
- **Pruebas de dispositivo:** pendientes; no se creó ni inició un emulador.

La aceptación local usa identidades sintéticas y conserva la autenticación web con cookies/CSRF. No se publicaron configuraciones, credenciales, asociaciones de dominio ni datos de producción.
