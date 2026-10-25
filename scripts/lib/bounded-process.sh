#!/usr/bin/env bash
set -euo pipefail

TASK4_SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"

ram_available_kib() {
  if [[ "${ENIGMABUDGET_GUARD_SELF_TEST:-}" == "1" && -n "${ENIGMABUDGET_TEST_RAM_AVAILABLE_KIB:-}" ]]; then
    printf '%s\n' "$ENIGMABUDGET_TEST_RAM_AVAILABLE_KIB"
    return
  fi
  awk '/^MemAvailable:/ { print $2; exit }' /proc/meminfo
}

require_ram() {
  local available_kib
  available_kib="$(ram_available_kib)"
  if [[ -z "$available_kib" || "$available_kib" -lt $((6 * 1024 * 1024)) ]]; then
    printf 'Memoria disponible insuficiente (%s KiB). Se requieren al menos 6 GiB; liberá memoria y volvé a intentarlo.\n' "${available_kib:-desconocida}" >&2
    return 1
  fi
}

systemd_run_path() {
  if [[ "${ENIGMABUDGET_GUARD_SELF_TEST:-}" == "1" && -n "${ENIGMABUDGET_TEST_SYSTEMD_RUN:-}" ]]; then
    printf '%s\n' "$ENIGMABUDGET_TEST_SYSTEMD_RUN"
  else
    command -v systemd-run || true
  fi
}

require_cgroup_runner() {
  local runner fs_type
  runner="$(systemd_run_path)"
  fs_type="$(stat -fc %T /sys/fs/cgroup 2>/dev/null || true)"
  if [[ -z "$runner" || ! -x "$runner" || "$fs_type" != "cgroup2fs" ]] || ! command -v systemctl >/dev/null 2>&1 || ! systemctl --user show-environment >/dev/null 2>&1; then
    printf 'No se puede crear un límite cgroup v2 de usuario verificable. Instalá/habilitá systemd de usuario con control de memoria y swap, y volvé a intentarlo.\n' >&2
    return 1
  fi
}

run_bounded() {
  require_ram || return $?
  require_cgroup_runner || return $?
  local runner="$(systemd_run_path)"
  "$runner" --user --scope --quiet \
    --property=MemoryMax=4294967296 --property=MemorySwapMax=536870912 \
    -- /bin/bash -c 'python3 "$1" || exit $?; shift; exec "$@"' bounded-process \
    "$TASK4_SCRIPT_DIR/lib/verify-cgroup-limit.py" "$@"
}

self_test_guards() {
  local temp marker status
  temp="$(mktemp -d)"
  marker="$temp/child-ran"
  set +e
  ENIGMABUDGET_GUARD_SELF_TEST=1 ENIGMABUDGET_TEST_RAM_AVAILABLE_KIB=1024 \
    run_bounded /bin/bash -c 'printf ran > "$1"' _ "$marker" >/dev/null 2>&1
  status=$?
  set -e
  if [[ "$status" -eq 0 || -e "$marker" ]]; then
    python3 -c 'import shutil, sys; shutil.rmtree(sys.argv[1])' "$temp"
    printf 'Falló la prueba de rechazo por RAM insuficiente antes de iniciar el proceso.\n' >&2
    return 1
  fi
  printf 'PASS: RAM bajo el mínimo rechazado antes de iniciar el proceso.\n'

  marker="$temp/runner-unavailable-child-ran"
  set +e
  ENIGMABUDGET_GUARD_SELF_TEST=1 ENIGMABUDGET_TEST_SYSTEMD_RUN="$temp/missing-systemd-run" \
    run_bounded /bin/bash -c 'printf ran > "$1"' _ "$marker" >/dev/null 2>&1
  status=$?
  set -e
  if [[ "$status" -eq 0 || -e "$marker" ]]; then
    python3 -c 'import shutil, sys; shutil.rmtree(sys.argv[1])' "$temp"
    printf 'Falló la prueba de rechazo sin ejecutor de límites antes de iniciar el proceso.\n' >&2
    return 1
  fi
  printf 'PASS: falta de ejecutor cgroup rechazado antes de iniciar el proceso.\n'

  if [[ "$(stat -fc %T /sys/fs/cgroup 2>/dev/null || true)" == "cgroup2fs" ]] && command -v systemd-run >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1; then
    run_bounded python3 "$TASK4_SCRIPT_DIR/lib/verify-cgroup-limit.py"
    set +e
    run_bounded /bin/bash -c 'exit 37'
    status=$?
    set -e
    if [[ "$status" -ne 37 ]]; then
      python3 -c 'import shutil, sys; shutil.rmtree(sys.argv[1])' "$temp"
      printf 'El wrapper no propagó el código de salida del proceso limitado (esperado 37, obtenido %s).\n' "$status" >&2
      return 1
    fi
    printf 'PASS: el proceso hijo verificó los límites cgroup y se propagó su código de salida.\n'
  else
    printf 'SKIP: cgroup v2 de usuario no disponible para comprobar el límite hijo real; los scripts rechazan este estado.\n'
  fi
  python3 -c 'import shutil, sys; shutil.rmtree(sys.argv[1])' "$temp"
}
