#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
source "$SCRIPT_DIR/lib/bounded-process.sh"

if [[ "${1:-}" == "--self-test-guards" ]]; then
  self_test_guards
  python3 "$SCRIPT_DIR/native-packaging-guard.py" --source
  if [[ -n "${JAVA_HOME:-}" && -x "$JAVA_HOME/bin/java" ]]; then
    run_bounded /bin/bash -c '"$1" --no-daemon --max-workers=1 -p "$2" testAppLinksPolicy' \
      bounded-app-links-test "$ROOT/web/android/gradlew" "$SCRIPT_DIR"
    if [[ -n "${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}" ]]; then
      run_bounded /bin/bash -c 'cd "$1" && ./gradlew --no-daemon --max-workers=1 -Pkotlin.compiler.execution.strategy=in-process :app:testDebugUnitTest --tests com.enigmabudget.dev.AppLinksPolicyTest' \
        bounded-app-links-unit "$ROOT/web/android"
    else
      printf 'SKIP: definir ANDROID_HOME para ejecutar AppLinksPolicyTest en la JVM Android.\n'
    fi
  else
    printf 'SKIP: definir JAVA_HOME con JDK 21 para ejecutar las pruebas Gradle de App Links.\n'
  fi
  exit 0
fi
if [[ "${1:-}" == "--" ]]; then shift; fi
if [[ "$#" -ne 0 ]]; then
  printf 'Uso: scripts/build-android-debug.sh [--self-test-guards]\n' >&2
  exit 2
fi

require_ram
if [[ -z "${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}" ]]; then
  printf 'Falta Android SDK. Instalá platform 36 y Build Tools compatibles, y definí ANDROID_HOME.\n' >&2
  exit 2
fi
SDK_PATH="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [[ ! -d "$SDK_PATH/platforms/android-36" ]] || ! find "$SDK_PATH/build-tools" -maxdepth 2 -name apksigner -type f -executable -print -quit 2>/dev/null | grep -q . || ! find "$SDK_PATH/build-tools" -maxdepth 2 -name aapt -type f -executable -print -quit 2>/dev/null | grep -q .; then
  printf 'Android SDK incompleto. Instalá la plataforma 36 y Build Tools con aapt/apksigner, y volvé a intentarlo.\n' >&2
  exit 2
fi
if [[ -z "${JAVA_HOME:-}" || ! -x "$JAVA_HOME/bin/java" || ! -x "$JAVA_HOME/bin/javac" ]]; then
  printf 'Falta JDK 21. Definí JAVA_HOME con una instalación que incluya java y javac 21.\n' >&2
  exit 2
fi
if ! "$JAVA_HOME/bin/java" -version 2>&1 | head -n 1 | grep -Eq 'version "21\.'; then
  printf 'El build requiere JDK 21 por el wrapper Gradle actual. Revisá JAVA_HOME.\n' >&2
  exit 2
fi
if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
  printf 'Falta Node.js/npm. Instalá Node 24 y npm 11, y volvé a intentarlo.\n' >&2
  exit 2
fi
if [[ "$(node --version)" != v24.* || "$(npm --version | cut -d. -f1)" != 11 ]]; then
  printf 'El build requiere Node 24 y npm 11 según las versiones fijadas del proyecto.\n' >&2
  exit 2
fi
if [[ ! -d "$ROOT/web/node_modules" ]]; then
  printf 'Faltan dependencias locales de Angular/Capacitor. Ejecutá npm ci en web con la RAM y el límite requeridos.\n' >&2
  exit 2
fi
python3 "$SCRIPT_DIR/native-packaging-guard.py" --source

export ANDROID_HOME="${ANDROID_HOME:-$ANDROID_SDK_ROOT}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export NODE_OPTIONS="--max-old-space-size=2048"
export NG_BUILD_MAX_WORKERS=2
export GRADLE_OPTS='-Xmx2g -Dorg.gradle.workers.max=1'
export ENIGMABUDGET_API_BASE_URL="${ENIGMABUDGET_API_BASE_URL:-}"
export ENIGMABUDGET_APP_LINKS_HOST="${ENIGMABUDGET_APP_LINKS_HOST:-}"
export ENIGMABUDGET_GOOGLE_SERVER_CLIENT_ID="${ENIGMABUDGET_GOOGLE_SERVER_CLIENT_ID:-}"

run_bounded /bin/bash -c '
  set -euo pipefail
  cd "$1/web"
  npm run build -- --configuration production --progress=false
  ./node_modules/.bin/cap sync android
  cd android
  ./gradlew --no-daemon --max-workers=1 -Pkotlin.compiler.execution.strategy=in-process assembleDebug
  apk="$1/web/android/app/build/outputs/apk/debug/app-debug.apk"
  test -f "$apk"
  python3 "$1/scripts/native-packaging-guard.py" --bundle "$apk"
  sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
  apksigner="$(find "$sdk/build-tools" -maxdepth 2 -name apksigner -type f -executable | sort -V | tail -n 1)"
  aapt="$(find "$sdk/build-tools" -maxdepth 2 -name aapt -type f -executable | sort -V | tail -n 1)"
  if [[ -z "$apksigner" || -z "$aapt" ]]; then
    printf "Faltan apksigner/aapt de Android Build Tools para verificar el APK.\\n" >&2
    exit 2
  fi
  signer="$("$apksigner" verify --print-certs "$apk" 2>&1)" || { printf "La firma de depuración del APK no pasó la verificación.\\n" >&2; exit 1; }
  if ! grep -q "Android Debug" <<<"$signer"; then
    printf "El APK no está firmado con el certificado de depuración esperado.\\n" >&2
    exit 1
  fi
  manifest="$("$aapt" dump xmltree "$apk" AndroidManifest.xml)"
  if [[ -n "${ENIGMABUDGET_APP_LINKS_HOST:-}" ]]; then
    if ! grep -q "android:path.*invitar" <<<"$manifest" || ! grep -Fq "${ENIGMABUDGET_APP_LINKS_HOST}" <<<"$manifest"; then
      printf "El filtro App Links del APK no coincide con el host y la ruta configurados.\\n" >&2
      exit 1
    fi
  elif grep -q "android:path.*invitar" <<<"$manifest" || grep -q "android:host" <<<"$manifest"; then
    printf "El APK contiene App Links aunque no hay un dominio configurado.\\n" >&2
    exit 1
  fi
  resources="$("$aapt" dump resources "$apk")"
  icon_id="$(sed -n "s/.*android:icon([^)]*)=@\\(0x[[:xdigit:]]*\\).*/\\1/p" <<<"$manifest" | head -n 1)"
  round_icon_id="$(sed -n "s/.*android:roundIcon([^)]*)=@\\(0x[[:xdigit:]]*\\).*/\\1/p" <<<"$manifest" | head -n 1)"
  if [[ -z "$icon_id" || "$icon_id" != "$round_icon_id" ]] || ! grep -Fq "resource $icon_id com.enigmabudget.dev:drawable/ic_launcher:" <<<"$resources"; then
    printf "El manifiesto final no resuelve su icono al recurso vectorial de EnigmaBudget.\\n" >&2
    exit 1
  fi
  sha256sum "$apk"
  printf "APK debug verificado: %s\\n" "$apk"
  printf "Google, dominio/assetlinks.json y teléfono físico siguen pendientes.\\n"
' bounded-build "$ROOT"
