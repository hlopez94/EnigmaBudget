#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
source "$SCRIPT_DIR/lib/bounded-process.sh"

CONNECTION_FILE="${ENIGMABUDGET_TEST_CONNECTION_FILE:-$ROOT/work/workspace-test-connection.txt}"
DOTNET_COMMAND="${DOTNET_COMMAND:-dotnet}"
if [[ ! -r "$CONNECTION_FILE" ]]; then
  printf 'No se encuentra la conexión privada a PostgreSQL de prueba. Definí ENIGMABUDGET_TEST_CONNECTION_FILE o creá el archivo local work/workspace-test-connection.txt.\n' >&2
  exit 2
fi
if [[ "$DOTNET_COMMAND" != */* ]]; then
  DOTNET_COMMAND="$(command -v "$DOTNET_COMMAND" || true)"
fi
if [[ -z "$DOTNET_COMMAND" || ! -x "$DOTNET_COMMAND" ]]; then
  printf 'No se encontró dotnet ejecutable. Instalá .NET 10 y definí DOTNET_COMMAND con su ruta.\n' >&2
  exit 2
fi

CONNECTION="$(head -n 1 "$CONNECTION_FILE")"
python3 - "$CONNECTION" <<'PY'
import re, sys
parts = {}
for item in sys.argv[1].split(';'):
    if '=' in item:
        key, value = item.split('=', 1)
        parts[key.strip().lower()] = value.strip().strip('"')
host = parts.get('host', '').lower()
try:
    port = int(parts.get('port', '5432'))
except ValueError:
    port = -1
if host not in {'localhost', '127.0.0.1', '::1'} or port != 55432:
    raise SystemExit('La aceptación requiere PostgreSQL aislado local en el puerto 55432; no se abrirá otra conexión.')
if not parts.get('database') or not parts.get('username'):
    raise SystemExit('El archivo local no contiene los campos esperados de una conexión PostgreSQL de prueba.')
PY
require_ram
printf 'Ejecutando aceptación nativa contra esquemas PostgreSQL aislados en el puerto 55432.\n'
DOTNET_EXE="$(readlink -f -- "$DOTNET_COMMAND" 2>/dev/null || printf '%s' "$DOTNET_COMMAND")"
export DOTNET_ROOT="$(cd -- "$(dirname -- "$DOTNET_EXE")" && pwd)"
run_bounded env \
  ASPNETCORE_ENVIRONMENT=Development \
  Authentication__DevelopmentLoginEnabled=true \
  Authentication__Native__DevelopmentLoginEnabled=true \
  ENIGMABUDGET_TEST_CONNECTION="$CONNECTION" \
  "$DOTNET_COMMAND" test "$ROOT/tests/EnigmaBudget.Api.Tests/EnigmaBudget.Api.Tests.csproj" \
  --no-restore --filter 'FullyQualifiedName~Development_native_session_can_read_and_write_only_its_workspace_then_refresh_replay_and_logout_revoke_it' \
  --logger 'console;verbosity=minimal'
