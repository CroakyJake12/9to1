#!/usr/bin/env python3
"""Acquire and authenticate the pinned native ARM64 Chromium package set and its
corresponding signed source inputs from official Debian repositories.

This is a fail-closed, reproducible acquisition pipeline:

  * Trust is pinned: the official ``debian-archive-keyring`` package is fetched and
    verified against a SHA256 recorded in the pinned manifest BEFORE any archive key
    is used. The trixie main and trixie-security archive key fingerprints are pinned
    and verified by parsing gpg's machine-readable STATUS records (no untrusted
    stderr/UID text is consulted).
  * Every binary .deb is verified against the signed Packages index AND against the
    pinned manifest (exact version, architecture, byte size and SHA256). Downloads are
    atomic (temp + hash + rename) with a capacity guard.
  * The corresponding source inputs (Chromium .dsc / orig / orig-pre-gen / Debian
    archive, plus the private libjpeg62-turbo source inputs) are authenticated against
    the signed Sources index and recorded in a build-graph ledger. Nothing is ever
    "verified" by self-asserted text.
  * ``--offline`` verifies all cached inputs and fails (never succeeds) if any cached
    input is missing, corrupt, or mismatched.

Nothing here installs packages, runs a browser, or mutates system apt state.
"""
from __future__ import annotations

import argparse
import lzma
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from chromium_native_common import (  # noqa: E402
    download_atomic,
    dump_json,
    ensure_input,
    find_index_sha256,
    find_sha256_by_name,
    load_json,
    parse_all_package_stanzas,
    parse_package_stanza,
    parse_sources_sha256,
    sha256_file,
    verify_inrelease_strict,
)

DEBIAN_POOL = "https://deb.debian.org/debian"
DEBIAN_SECURITY = "https://security.debian.org/debian-security"
ARCH = "arm64"

# Pinned trust anchors (public constants, from the official keyring package).
PINNED_TRIXIE_MAIN_KEY_FPR = "B8E5F13176D2A7A75220028078DBA3BC47EF2265"
PINNED_TRIXIE_SECURITY_KEY_FPR = "5E04A1E3223A19A20706E20F9904613D4CCE68C6"

KEYRING_TRIXIE_AUTOMATIC = "debian-archive-trixie-automatic.gpg"
KEYRING_TRIXIE_SECURITY_AUTOMATIC = "debian-archive-trixie-security-automatic.gpg"

# keyring basename + pinned fingerprint used for each archive/provider.
ARCHIVE_KEYRING = {
    "debian": (KEYRING_TRIXIE_AUTOMATIC, PINNED_TRIXIE_MAIN_KEY_FPR),
    "security": (KEYRING_TRIXIE_SECURITY_AUTOMATIC, PINNED_TRIXIE_SECURITY_KEY_FPR),
}

# Suite -> archive base.
SUITE_BASE = {
    "trixie-security": DEBIAN_SECURITY,
    "trixie": DEBIAN_POOL,
}


def _provider_of_suite(suite: str) -> str:
    return "security" if suite == "trixie-security" else "debian"


# --------------------------------------------------------------------------- #
# Keyring bootstrap
# --------------------------------------------------------------------------- #
def extract_keyrings(keyring_pkg: Path, evidence: Path) -> dict[str, Path]:
    want = {KEYRING_TRIXIE_AUTOMATIC, KEYRING_TRIXIE_SECURITY_AUTOMATIC}
    with tempfile.TemporaryDirectory() as tmp:
        tree = Path(tmp) / "x"
        tree.mkdir()
        subprocess.run(["dpkg-deb", "-x", str(keyring_pkg), str(tree)], check=True)
        available = {c.name: c for c in (tree / "usr/share/keyrings").glob("*.gpg")}
        missing = want - set(available)
        if missing:
            raise RuntimeError(f"missing keyrings in debian-archive-keyring: {sorted(missing)}")
        target_dir = evidence / "keys"
        target_dir.mkdir(parents=True, exist_ok=True)
        out = {}
        for name in sorted(want):
            target = target_dir / name
            target.write_bytes(available[name].read_bytes())
            out[name] = target
        return out


def check_pinned_keyrings(keyrings: dict[str, Path]) -> dict[str, bool]:
    result = {}
    for kind, (keyring_name, fpr) in ARCHIVE_KEYRING.items():
        kr = keyrings.get(keyring_name)
        ok = False
        if kr is not None:
            out = subprocess.run(
                ["gpg", "--show-keys", "--with-colons", str(kr)],
                capture_output=True, text=True,
            ).stdout
            ok = fpr in out
        result[kind] = ok
    return result


# --------------------------------------------------------------------------- #
# Index fetch/verify
# --------------------------------------------------------------------------- #
def fetch_inrelease(base: str, suite: str, evidence: Path, keyring: Path,
                    required_fpr: str, offline: bool) -> dict:
    dest = evidence / f"InRelease.{suite}"
    if offline:
        if not dest.exists():
            raise RuntimeError(f"--offline: cached InRelease missing for {suite}")
    else:
        download_atomic(f"{base}/dists/{suite}/InRelease", dest, reserve_bytes=0)
    chk = verify_inrelease_strict(keyring, dest, required_fpr)
    if not chk["verified"]:
        raise RuntimeError(f"InRelease signature verification failed for {suite}: "
                           f"{chk['reason']} (exit={chk['exit']})")
    return {"url": f"{base}/dists/{suite}/InRelease", "path": dest, "check": chk}


def fetch_index(base: str, suite: str, component: str, kind: str,
                evidence: Path, inrelease_text: str, offline: bool) -> Path:
    suffix = f"{component}/{kind}-{ARCH}/Packages.xz" if kind == "binary" else f"{component}/source/Sources.xz"
    sha, size = find_index_sha256(inrelease_text, suffix)
    if not sha:
        raise RuntimeError(f"index {suffix} not listed in {suite} InRelease")
    xz_dest = evidence / f"{suite}-{component}-{kind}-{ARCH}-Packages.xz" if kind == "binary" \
        else evidence / f"{suite}-{component}-source-Sources.xz"
    if offline:
        if not xz_dest.exists():
            raise RuntimeError(f"--offline: cached index missing: {xz_dest.name}")
        if sha256_file(xz_dest) != sha:
            raise RuntimeError(f"--offline: cached index corrupt: {xz_dest.name}")
    else:
        download_atomic(f"{base}/dists/{suite}/{suffix}", xz_dest, sha, size)
    return xz_dest


# --------------------------------------------------------------------------- #
# Main
# --------------------------------------------------------------------------- #
def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--evidence-dir", type=Path, required=True)
    ap.add_argument("--pinned-manifest", type=Path, required=True)
    ap.add_argument("--source-pins", type=Path, required=True)
    ap.add_argument("--offline", action="store_true",
                    help="Verify only cached evidence; never touch the network")
    args = ap.parse_args(argv)

    evidence = args.evidence_dir
    evidence.mkdir(parents=True, exist_ok=True)
    pinned = load_json(args.pinned_manifest)
    source_pins = load_json(args.source_pins)

    sel = pinned["selected_version"]
    pinned_version = sel["binary_package_version"]
    pinned_arch = sel.get("architecture", ARCH)
    if pinned_arch != ARCH:
        raise RuntimeError(f"pinned architecture {pinned_arch} != {ARCH}")
    keyring_sha = pinned["archive_trust"]["keyring_package"]["sha256"]
    keyring_name = pinned["archive_trust"]["keyring_package"].get("name",
                                                                 "debian-archive-keyring_2025.1_all.deb")

    # 1. Trust anchor -----------------------------------------------------
    keyring_pkg = evidence / keyring_name
    if not ensure_input(args.offline, keyring_pkg, keyring_sha,
                        pinned["archive_trust"]["keyring_package"].get("bytes"),
                        f"{DEBIAN_POOL}/pool/main/d/debian-archive-keyring/{keyring_name}"):
        raise RuntimeError("keyring package could not be verified")
    keyrings = extract_keyrings(keyring_pkg, evidence)
    pinned_status = check_pinned_keyrings(keyrings)
    failed_fpr = [k for k, ok in pinned_status.items() if not ok]
    if failed_fpr:
        raise RuntimeError(f"pinned archive key fingerprint(s) not found: {failed_fpr}")

    # 2. InReleases ------------------------------------------------------
    sec_inrel = fetch_inrelease(DEBIAN_SECURITY, "trixie-security", evidence,
                                keyrings[KEYRING_TRIXIE_SECURITY_AUTOMATIC],
                                PINNED_TRIXIE_SECURITY_KEY_FPR, args.offline)
    main_inrel = fetch_inrelease(DEBIAN_POOL, "trixie", evidence,
                                 keyrings[KEYRING_TRIXIE_AUTOMATIC],
                                 PINNED_TRIXIE_MAIN_KEY_FPR, args.offline)
    sec_text = sec_inrel["path"].read_text(errors="replace")
    main_text = main_inrel["path"].read_text(errors="replace")

    # 3. Indexes ----------------------------------------------------------
    sec_pkg_xz = fetch_index(DEBIAN_SECURITY, "trixie-security", "main", "binary",
                             evidence, sec_text, args.offline)
    main_pkg_xz = fetch_index(DEBIAN_POOL, "trixie", "main", "binary",
                              evidence, main_text, args.offline)
    sec_src_xz = fetch_index(DEBIAN_SECURITY, "trixie-security", "main", "source",
                             evidence, sec_text, args.offline)
    main_src_xz = fetch_index(DEBIAN_POOL, "trixie", "main", "source",
                              evidence, main_text, args.offline)

    sec_pkgs = lzma.decompress(sec_pkg_xz.read_bytes()).decode("utf-8", errors="replace")
    main_pkgs = lzma.decompress(main_pkg_xz.read_bytes()).decode("utf-8", errors="replace")
    sec_src = lzma.decompress(sec_src_xz.read_bytes()).decode("utf-8", errors="replace")
    main_src = lzma.decompress(main_src_xz.read_bytes()).decode("utf-8", errors="replace")

    pkgs_by_provider = {"security": sec_pkgs, "debian": main_pkgs}

    # 4. Source build graph ------------------------------------------------
    source_graph = {"chromium": None, "libjpeg62-turbo": None}
    # 4a. Chromium source (trixie-security) — select the stanza for the exact
    # pinned source version (the security archive may carry multiple versions).
    source_version = source_pins.get("source_version")
    chromium_stanzas = [s for s in parse_all_package_stanzas(sec_src)
                        if s.get("Package") == "chromium"]
    chromium_src = next((s for s in chromium_stanzas
                         if s.get("Version") == source_version), None)
    if chromium_src is None:
        raise RuntimeError(
            f"chromium source stanza version {source_version!r} not found in "
            "signed trixie-security Sources")
    chromium_src_entries = parse_sources_sha256(chromium_src)
    chromium_inputs = []
    for inp in source_pins["inputs"]:
        entry = find_sha256_by_name(chromium_src_entries, inp["name"])
        if entry is None:
            raise RuntimeError(f"source file {inp['name']} not listed in signed Sources")
        if entry["sha256"] != inp["sha256"] or entry["size"] != inp["bytes"]:
            raise RuntimeError(f"source pin mismatch for {inp['name']}: "
                               f"sources {entry['sha256']}/{entry['size']} vs "
                               f"pinned {inp['sha256']}/{inp['bytes']}")
        local = evidence / "source" / inp["name"]
        chromium_directory = chromium_src.get("Directory") or "pool/updates/main/c/chromium"
        url = f"{DEBIAN_SECURITY}/{chromium_directory}/{inp['name']}"
        if not ensure_input(args.offline, local, inp["sha256"], inp["bytes"], url):
            raise RuntimeError(f"source download failed: {inp['name']}")
        chromium_inputs.append({"name": inp["name"], "url": url, "bytes": inp["bytes"],
                                "sha256": inp["sha256"], "verified": True,
                                "local_path": str(local)})
    source_graph["chromium"] = {
        "package": "chromium", "version": chromium_src.get("Version"),
        "directory": chromium_src.get("Directory"), "inputs": chromium_inputs,
    }

    # 4b. Private JPEG source (trixie main) — libjpeg-turbo.
    jpeg_stanzas = [s for s in parse_all_package_stanzas(main_src)
                    if s.get("Package") == "libjpeg-turbo"]
    jpeg_src = next((s for s in jpeg_stanzas
                     if "2.1.5-4" in (s.get("Version") or "")), None)
    if jpeg_src is None:
        raise RuntimeError(
            "libjpeg-turbo source stanza for the built 2.1.5-4 binary not found "
            "in signed trixie Sources")
    jpeg_src_entries = parse_sources_sha256(jpeg_src)
    jpeg_inputs = []
    jpeg_directory = jpeg_src.get("Directory") or "pool/main/libj/libjpeg-turbo"
    # Iterate ALL Checksums-Sha256 entries for the JPEG source stanza — do not
    # guess extensions. Download only genuinely missing inputs, verify their exact
    # bytes + SHA256 against the signed Sources list, and record actual local paths.
    for f in jpeg_src_entries:
        local = evidence / "source" / f["name"]
        url = f"{DEBIAN_POOL}/{jpeg_directory}/{f['name']}"
        if not ensure_input(args.offline, local, f["sha256"], f["size"], url):
            raise RuntimeError(f"JPEG source download failed: {f['name']}")
        jpeg_inputs.append({
            "name": f["name"], "bytes": f["size"], "sha256": f["sha256"],
            "verified": True, "from_signed_sources": True,
            "local_path": str(local),
        })
    source_graph["libjpeg62-turbo"] = {
        "package": "libjpeg-turbo", "version": jpeg_src.get("Version"),
        "directory": jpeg_src.get("Directory"), "inputs": jpeg_inputs,
    }

    # 5. Binary packages ----------------------------------------------------
    pinned_pkgs = {p["debian_package"]: p for p in pinned["packages"]}
    packages = []
    for suite, component, pkg, version_floor, provider in (
            ("trixie-security", "main", "chromium", "155.0.8059.79-1~deb13u1", "security"),
            ("trixie-security", "main", "chromium-common", "155.0.8059.79-1~deb13u1", "security"),
            ("trixie-security", "main", "chromium-sandbox", "155.0.8059.79-1~deb13u1", "security"),
            ("trixie", "main", "libjpeg62-turbo", "1:2.1.5-4", "debian")):
        pin = pinned_pkgs.get(pkg)
        if pin is None:
            raise RuntimeError(f"package {pkg} missing from pinned manifest")
        if pin.get("version") != version_floor:
            raise RuntimeError(f"pinned manifest version mismatch for {pkg}")
        index_text = pkgs_by_provider[provider]
        stanza = parse_package_stanza(index_text, pkg)
        if not stanza:
            raise RuntimeError(f"package {pkg} not found in {suite} {component} index")
        arch_ok = stanza.get("Architecture") in (ARCH, "all")
        if not arch_ok:
            raise RuntimeError(f"unexpected architecture {stanza.get('Architecture')} for {pkg}")
        if stanza.get("Version") != version_floor:
            raise RuntimeError(f"index version {stanza.get('Version')} != pinned {version_floor} for {pkg}")
        sha = stanza["SHA256"]
        size = int(stanza.get("Size", 0))
        if sha != pin["sha256"]:
            raise RuntimeError(f"sha256 mismatch for {pkg}: index {sha} vs pinned {pin['sha256']}")
        if size != pin["bytes"]:
            raise RuntimeError(f"size mismatch for {pkg}: index {size} vs pinned {pin['bytes']}")
        filename = stanza["Filename"]
        base = SUITE_BASE[suite]
        url = f"{base}/{filename}"
        local = evidence / "chromium-debs" / Path(filename).name
        if not ensure_input(args.offline, local, sha, size, url):
            raise RuntimeError(f"binary download failed: {pkg}")
        packages.append({
            "package": pkg, "suite": suite, "component": component,
            "version": stanza.get("Version"), "architecture": stanza.get("Architecture"),
            "url": url, "filename": filename, "sha256": sha, "bytes": size,
            "local_path": str(local), "verified": True,
        })

    receipt = {
        "schema": "9to1.chromium-native.acquire-receipt/v2",
        "at": None,
        "mode": "offline" if args.offline else "network",
        "trust": {
            "keyrings": {k: str(v) for k, v in keyrings.items()},
            "keyring_package_sha256": keyring_sha,
            "pinned_main_key_fpr": PINNED_TRIXIE_MAIN_KEY_FPR,
            "pinned_security_key_fpr": PINNED_TRIXIE_SECURITY_KEY_FPR,
            "pinned_key_status": pinned_status,
            "trixie_security_signature": sec_inrel["check"],
            "trixie_main_signature": main_inrel["check"],
        },
        "source_graph": source_graph,
        "packages": packages,
    }
    receipt_path = evidence / "chromium-acquire-receipt.json"
    dump_json(receipt, receipt_path)
    print(receipt_path)
    for p in packages:
        print(f"  {p['package']:22} {p['version']:28} {p['sha256'][:16]}… verified={p['verified']}")
    for name, g in source_graph.items():
        n = len(g["inputs"]) if g else 0
        print(f"  source {name}: {n} authenticated input(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
