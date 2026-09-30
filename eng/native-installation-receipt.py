#!/usr/bin/env python3
"""Produce an explicitly configured signed receipt artifact; never install or provision trust.

Matches NineToOne.Os.Shell.Authority.InstallationReceipt schema 1. The issuer key
must already exist. A signature does not grant authority until a trusted
installer and the running-platform verifier validate the actual package.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()


def identifier(value):
    if not re.fullmatch(r"[a-z0-9][a-z0-9._-]{0,127}", value):
        raise ValueError("Identifiers must match the canonical installed-receipt format")
    return value


def payload(inventory, revision, provider, os_app, install_root, desktop_entry, desktop_digest, services, roles):
    if inventory.get("schemaVersion") != 1 or inventory.get("authority") != "UnsignedBuildInventory" or inventory.get("publisherSignature") is not None:
        raise ValueError("A schema-1 unsigned build inventory is required")
    original = {key: value for key, value in inventory.items() if key not in ("inventorySha256", "publisherSignature")}
    if hashlib.sha256(canonical(original)).hexdigest() != inventory.get("inventorySha256"):
        raise ValueError("Unsigned inventory digest mismatch")
    if revision < 1 or revision >= 9223372036854775807:
        raise ValueError("An explicit positive installation receipt revision is required")
    if not os_app or len(os_app)>4096:
        raise ValueError("An explicit supported OS application identifier is required")
    root = Path(install_root)
    if not root.is_absolute() or ".." in root.parts or str(root) != install_root or not install_root.startswith(("/usr/lib/9to1/apps/", "/opt/9to1/apps/")):
        raise ValueError("Configured Linux installation root must match the native verifier policy")
    desktop = Path(desktop_entry)
    if not desktop.is_absolute() or ".." in desktop.parts or str(desktop) != desktop_entry or not desktop_entry.startswith("/usr/share/applications/") or not desktop_entry.endswith(".desktop"):
        raise ValueError("An explicit canonical installed desktop entry path is required")
    if not re.fullmatch(r"[a-fA-F0-9]{64}", desktop_digest):
        raise ValueError("Actual desktop entry SHA256 is required")
    executable_relative = inventory["entryPoint"]
    platform_entrypoint = "desktop:" + desktop.name
    if provider != "linux.xdg-desktop" or os_app != platform_entrypoint:
        raise ValueError("Linux receipt must use the actual linux.xdg-desktop provider and matching desktop:<desktop-id> OS application/entrypoint")
    files = inventory["files"]
    if not isinstance(files, list) or not 1 <= len(files) <= 100000:
        raise ValueError("Payload inventory exceeds the verifier's supported range")
    for item in files:
        relative = item["path"]
        if not isinstance(relative, str) or len(relative)>4096 or any(ord(c)<32 or ord(c)==127 for c in relative) or not relative or relative.startswith("/") or "\\" in relative or any(part in ("", ".", "..") for part in relative.split("/")):
            raise ValueError("Payload file path must be a canonical relative path")
        if not isinstance(item["size"], int) or not 0 <= item["size"] <= 8 * 1024**3 or not re.fullmatch(r"[a-fA-F0-9]{64}", item["sha256"]):
            raise ValueError("Payload file size/digest is outside the verifier contract")
    if executable_relative not in {item["path"] for item in files} or len({item["path"] for item in files}) != len(files):
        raise ValueError("Unique payload files including the executable are required")
    if not services or len(services) > 128 or len(roles) > 16 or len(set(services)) != len(services) or len(set(roles)) != len(roles):
        raise ValueError("Explicit unique permitted services/roles are required")
    return {"schemaVersion": 1, "appId": identifier(inventory["appId"]), "receiptRevision": revision,
            "providerId": identifier(provider), "osApplicationId": os_app, "entrypoint": platform_entrypoint,
            "installRoot": install_root, "executablePath": str(root / executable_relative), "desktopEntryPath": desktop_entry,
            "desktopEntrySha256": desktop_digest, "files": files,
            "allowedServiceIds": [identifier(value) for value in services], "roles": [identifier(value) for value in roles]}


def sign(data, key, issuer):
    if not key.is_file():
        raise ValueError("Configured issuer signing key is absent; no development key is generated")
    public = subprocess.run(["openssl", "pkey", "-in", str(key), "-passin", "pass:", "-pubout", "-outform", "DER"], capture_output=True, timeout=30)
    if public.returncode:
        raise ValueError("Issuer public-key derivation failed; use the configured noninteractive signing adapter")
    description = subprocess.run(["openssl", "pkey", "-pubin", "-inform", "DER", "-text", "-noout"], input=public.stdout, capture_output=True, timeout=30)
    size = re.search(rb"Public-Key: \((\d+) bit\)", description.stdout)
    if description.returncode or size is None or not 3072 <= int(size.group(1)) <= 8192:
        raise ValueError("Issuer RSA key must match the configured native verifier's 3072..8192-bit range")
    signed = subprocess.run(["openssl", "dgst", "-sha256", "-sign", str(key), "-passin", "pass:",
                             "-sigopt", "rsa_padding_mode:pss", "-sigopt", "rsa_pss_saltlen:digest"], input=data, capture_output=True, timeout=30)
    if signed.returncode:
        raise ValueError("Configured RSA-PSS signing failed")
    return {"schemaVersion": 1, "issuerKeyId": identifier(issuer), "payload": base64.b64encode(data).decode(), "signature": base64.b64encode(signed.stdout).decode()}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--inventory", type=Path, required=True)
    p.add_argument("--receipt-revision", type=int, required=True)
    p.add_argument("--provider-id", required=True)
    p.add_argument("--os-application-id", required=True)
    p.add_argument("--install-root", required=True)
    p.add_argument("--desktop-entry", required=True)
    p.add_argument("--desktop-entry-sha256", required=True)
    p.add_argument("--allowed-service", action="append", required=True)
    p.add_argument("--role", action="append", default=[])
    p.add_argument("--issuer-key-id", required=True)
    p.add_argument("--issuer-key-file", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    try:
        if args.output.resolve() in (args.inventory.resolve(),args.issuer_key_file.resolve()):
            raise ValueError("Receipt output cannot overwrite inventory or issuer key")
        if args.inventory.stat().st_size>8*1024*1024:
            raise ValueError("Inventory exceeds supported receipt input size")
        inventory = json.loads(args.inventory.read_bytes())
        data = canonical(payload(inventory, args.receipt_revision, args.provider_id, args.os_application_id, args.install_root,
                                 args.desktop_entry, args.desktop_entry_sha256, args.allowed_service, args.role))
        if len(data)>6*1024*1024:
            raise ValueError("Receipt payload exceeds the native verifier limit")
        result = sign(data, args.issuer_key_file, args.issuer_key_id)
        if len(canonical(result))>8*1024*1024:
            raise ValueError("Signed receipt envelope exceeds the native verifier limit")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        handle, temporary = tempfile.mkstemp(prefix="." + args.output.name + ".", dir=args.output.parent)
        try:
            with os.fdopen(handle, "wb") as output:
                output.write(canonical(result) + b"\n")
                output.flush()
                os.fsync(output.fileno())
            os.replace(temporary, args.output)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
    except (OSError, ValueError, KeyError, TypeError, subprocess.TimeoutExpired) as error:
        p.exit(1, "Installation receipt artifact failed: " + str(error) + "\n")


if __name__ == "__main__":
    main()
