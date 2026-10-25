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

npm --prefix "$ROOT/web" ci
npm --prefix "$ROOT/web" run build
"$ROOT/scripts/sync-frontend.sh"
"$DOTNET_COMMAND" publish "$ROOT/src/EnigmaBudget.Api/EnigmaBudget.Api.csproj" --configuration Release --output "$ROOT/artifacts/publish" --maxcpucount:1 -p:UseSharedCompilation=false
