#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_COMMAND="${DOTNET_COMMAND:-$ROOT/work/tooling/dotnet/dotnet}"
if [[ ! -x "$DOTNET_COMMAND" ]]; then
  DOTNET_COMMAND="$(command -v dotnet || true)"
fi
if [[ -z "$DOTNET_COMMAND" ]]; then
  echo "No se encontró .NET SDK 10. Definí DOTNET_COMMAND o instalá dotnet." >&2
  exit 1
fi

if [[ -f "$ROOT/.env" ]]; then
  EXPORTED_TEST_CONNECTION="${ENIGMABUDGET_TEST_CONNECTION:-}"
  EXPORTED_E2E_CONNECTION="${ENIGMABUDGET_E2E_CONNECTION:-}"
  set -a
  . "$ROOT/.env"
  set +a
  if [[ -n "$EXPORTED_TEST_CONNECTION" ]]; then
    ENIGMABUDGET_TEST_CONNECTION="$EXPORTED_TEST_CONNECTION"
  fi
  if [[ -n "$EXPORTED_E2E_CONNECTION" ]]; then
    ENIGMABUDGET_E2E_CONNECTION="$EXPORTED_E2E_CONNECTION"
  fi
fi
if [[ -z "${ENIGMABUDGET_TEST_CONNECTION:-}" ]]; then
  echo "Definí ENIGMABUDGET_TEST_CONNECTION con una base PostgreSQL aislada." >&2
  exit 1
fi

npm --prefix "$ROOT/web" ci
ENIGMABUDGET_TEST_CONNECTION="$ENIGMABUDGET_TEST_CONNECTION" "$DOTNET_COMMAND" test "$ROOT/EnigmaBudget.sln" --configuration Release --maxcpucount:1 -p:UseSharedCompilation=false
npm --prefix "$ROOT/web" test -- --run --maxWorkers=2
npm --prefix "$ROOT/web" run build

"$ROOT/scripts/sync-frontend.sh"

if [[ -n "${ENIGMABUDGET_E2E_CONNECTION:-}" ]]; then
  DOTNET_COMMAND="$DOTNET_COMMAND" ENIGMABUDGET_E2E_CONNECTION="$ENIGMABUDGET_E2E_CONNECTION" npm --prefix "$ROOT/web" run test:e2e
else
  echo "Se omitió Playwright: definí ENIGMABUDGET_E2E_CONNECTION para probar el host y la base persistente." >&2
fi
