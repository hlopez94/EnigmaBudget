#!/usr/bin/env python3
"""Fail closed unless the current process is inside a bounded cgroup-v2 scope."""
from pathlib import Path
import sys


def main() -> int:
    cgroup = Path("/proc/self/cgroup").read_text().splitlines()
    entry = next((line.partition("::")[2] for line in cgroup if "::" in line), None)
    if entry is None:
        print("No se pudo comprobar el límite cgroup v2 del proceso.", file=sys.stderr)
        return 1
    group = Path("/sys/fs/cgroup") / entry.lstrip("/")
    try:
        memory = int((group / "memory.max").read_text().strip())
        swap = int((group / "memory.swap.max").read_text().strip())
    except (OSError, ValueError):
        print("El sistema no expone límites cgroup v2 verificables; se cancela el trabajo pesado.", file=sys.stderr)
        return 1
    if memory > 4 * 1024**3 or swap > 512 * 1024**2:
        print("El límite real del proceso supera 4 GiB de RAM o 512 MiB de swap.", file=sys.stderr)
        return 1
    print(f"Límites cgroup verificados: RAM={memory} bytes; swap={swap} bytes.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
