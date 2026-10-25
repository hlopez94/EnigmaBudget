#!/usr/bin/env python3
"""Checks generated public configuration and APK contents without printing secrets."""
from pathlib import Path
import json
import os
import re
import sys
from urllib.parse import urlsplit
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def fail(message: str) -> int:
    print(message, file=sys.stderr)
    return 1


def validate_source() -> int:
    config = ROOT / "web/capacitor.config.ts"
    manifest = ROOT / "web/android/app/src/appLinks/AndroidManifest.xml"
    if not config.is_file() or not manifest.is_file():
        return fail("Falta la configuración fuente de Capacitor o el manifiesto App Links.")
    if re.search(r"\bserver\s*:\s*\{[^}]*\burl\s*:", config.read_text(), re.S):
        return fail("La configuración Capacitor pública no debe fijar server.url.")
    xml = manifest.read_text()
    if xml.count('android:autoVerify="true"') != 1 or xml.count('android:scheme="https"') != 1 or xml.count('android:host="${inviteHost}"') != 1 or xml.count('android:path="/invitar"') != 1:
        return fail("El manifiesto fuente debe limitar App Links a HTTPS, host configurado y /invitar.")
    api_base = os.environ.get("ENIGMABUDGET_API_BASE_URL", "")
    if api_base:
        try:
            parsed = urlsplit(api_base)
            if parsed.scheme not in {"https", "http"} or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
                return fail("ENIGMABUDGET_API_BASE_URL debe ser un origen HTTP(S) sin credenciales, query ni fragmento.")
            parsed.port
        except ValueError:
            return fail("ENIGMABUDGET_API_BASE_URL no es un origen HTTP(S) válido.")
    print("PASS: guardas fuente Capacitor y política de manifiesto.")
    return 0


def inspect_payload(name: str, payload: bytes) -> str | None:
    lower = payload.lower()
    if b"client_secret" in lower or b"clientsecret" in lower:
        return f"Se encontró un campo de secreto de cliente en {name}."
    if re.search(rb"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", payload):
        return f"Se encontró un token con formato JWT en {name}."
    configured_secret = __import__("os").environ.get("ENIGMABUDGET_GOOGLE_CLIENT_SECRET", "").encode()
    if configured_secret and configured_secret in payload:
        return f"El APK contiene el valor de ENIGMABUDGET_GOOGLE_CLIENT_SECRET en {name}."
    return None


def validate_bundle(apk_path: Path) -> int:
    config_path = ROOT / "web/android/app/src/main/assets/capacitor.config.json"
    if not config_path.is_file():
        return fail("Falta el archivo de configuración empaquetado de Capacitor.")
    try:
        config = json.loads(config_path.read_text())
    except (OSError, json.JSONDecodeError):
        return fail("La configuración empaquetada de Capacitor no es JSON válido.")
    if isinstance(config.get("server"), dict) and config["server"].get("url"):
        return fail("El APK no puede empaquetar server.url de live reload.")
    if inspect_payload(str(config_path), config_path.read_bytes()):
        return fail(inspect_payload(str(config_path), config_path.read_bytes()) or "Configuración no válida.")
    public_dir = ROOT / "web/android/app/src/main/assets/public"
    candidates = [config_path]
    if public_dir.is_dir():
        candidates.extend(path for path in public_dir.rglob("*") if path.is_file())
    try:
        with zipfile.ZipFile(apk_path) as apk:
            names = apk.namelist()
            for name in names:
                if name.startswith("assets/public/") or name == "assets/capacitor.config.json":
                    error = inspect_payload(name, apk.read(name))
                    if error:
                        return fail(error)
    except (OSError, zipfile.BadZipFile):
        return fail("No se pudo leer el APK de salida.")
    for path in candidates:
        error = inspect_payload(str(path), path.read_bytes())
        if error:
            return fail(error)
    print("PASS: APK y configuración pública sin server.url, secreto de cliente ni JWT.")
    return 0


if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "--source":
        raise SystemExit(validate_source())
    if len(sys.argv) == 3 and sys.argv[1] == "--bundle":
        raise SystemExit(validate_bundle(Path(sys.argv[2])))
    raise SystemExit(f"Uso: {Path(sys.argv[0]).name} --source | --bundle APK")
