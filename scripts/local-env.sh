#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.env"
GARAGE_CONFIG="$ROOT/work/garage.toml"

if [[ ! -f "$ENV_FILE" ]] || grep -q '^POSTGRES_PASSWORD=REEMPLAZAR_' "$ENV_FILE"; then
  if ! command -v openssl >/dev/null 2>&1; then
    echo "Se necesita openssl para generar credenciales locales aleatorias." >&2
    exit 1
  fi
  mkdir -p "$ROOT/work"
  umask 077
  POSTGRES_PASSWORD="$(openssl rand -hex 24)"
  GARAGE_DEFAULT_ACCESS_KEY="GK$(openssl rand -hex 16)"
  GARAGE_DEFAULT_SECRET_KEY="$(openssl rand -hex 32)"
  GARAGE_RPC_SECRET="$(openssl rand -hex 32)"
  GARAGE_ADMIN_TOKEN="$(openssl rand -base64 32 | tr -d '\n')"
  GARAGE_METRICS_TOKEN="$(openssl rand -base64 32 | tr -d '\n')"
  cat > "$ENV_FILE" <<EOF
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
GARAGE_DEFAULT_ACCESS_KEY=$GARAGE_DEFAULT_ACCESS_KEY
GARAGE_DEFAULT_SECRET_KEY=$GARAGE_DEFAULT_SECRET_KEY
GARAGE_RPC_SECRET=$GARAGE_RPC_SECRET
GARAGE_ADMIN_TOKEN=$GARAGE_ADMIN_TOKEN
GARAGE_METRICS_TOKEN=$GARAGE_METRICS_TOKEN
POSTGRES_HOST_PORT=55432
GARAGE_S3_HOST_PORT=3900
APP_HOST_PORT=5080
GOOGLE_CLIENT_ID=
GOOGLE_CLIENT_SECRET=
ENIGMABUDGET_TEST_CONNECTION="Host=127.0.0.1;Port=55432;Database=enigmabudget_test;Username=enigmabudget;Password=$POSTGRES_PASSWORD"
ENIGMABUDGET_E2E_CONNECTION=
EOF
  chmod 600 "$ENV_FILE"
fi

set -a
# This is the developer's ignored local configuration file, created above.
. "$ENV_FILE"
set +a

if [[ -z "${GARAGE_RPC_SECRET:-}" || -z "${GARAGE_ADMIN_TOKEN:-}" || -z "${GARAGE_METRICS_TOKEN:-}" ]]; then
  echo "El .env local debe contener GARAGE_RPC_SECRET, GARAGE_ADMIN_TOKEN y GARAGE_METRICS_TOKEN." >&2
  exit 1
fi

mkdir -p "$(dirname "$GARAGE_CONFIG")"
umask 077
cat > "$GARAGE_CONFIG" <<EOF
metadata_dir = "/var/lib/garage/meta"
data_dir = "/var/lib/garage/data"
db_engine = "sqlite"
replication_factor = 1
rpc_bind_addr = "[::]:3901"
rpc_public_addr = "127.0.0.1:3901"
rpc_secret = "$GARAGE_RPC_SECRET"

[s3_api]
s3_region = "garage"
api_bind_addr = "[::]:3900"

[admin]
api_bind_addr = "[::]:3903"
admin_token = "$GARAGE_ADMIN_TOKEN"
metrics_token = "$GARAGE_METRICS_TOKEN"
EOF
chmod 600 "$GARAGE_CONFIG"
echo "Configuración local privada creada en .env y work/garage.toml."
