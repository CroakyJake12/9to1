#!/usr/bin/env python3
"""Stage the authenticated ARM64 Chromium bundle into a private engine path.

The bundle is built ONLY from verified inputs:

  * The four binary .debs are independently verified against the pinned manifest
    (exact version, arm64 architecture, byte size and SHA256) and then extracted
    with ``dpkg-deb``. An unverified or mismatched input aborts before any runtime
    output exists.
  * The vendor relative hierarchy under ``usr/lib/chromium`` is preserved (no
    basename flattening), including the nested ``locales/en-US.pak`` resource and
    every required vendor library/resource.
  * The private compatibility library ``libjpeg.so.62`` is staged under
    ``chromium/lib`` as a real file plus a relative symlink; the symlink is
    recorded in the integrity manifest with its target.
  * The generation is gated on the verified corresponding source build graph from
    the acquisition receipt (``--acquire-receipt``): without verified sources the
    stage fails BEFORE writing runtime output.
  * Notices are copied into the stage: the exact 155 Chromium copyright, the
    referenced common-license texts, the private libjpeg62-turbo copyright, and the
    required Independent JPEG Group binary documentation acknowledgement.

The SUID sandbox mode is applied (unprivileged chmod 4755); applying the root
ownership (uid/gid 0) is the root-only packager's step, so the manifest records both
the expected vendor owner and the truthful observed uid/gid separately.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from chromium_native_common import (  # noqa: E402
    BINARY_REL,
    BROWSER_STAGE_REL,
    LAUNCHER_REL,
    MANIFEST_REL,
    PRIVATE_LIB_REL,
    SANDBOX_EXPECTED_GID,
    SANDBOX_EXPECTED_UID,
    SANDBOX_MODE,
    SANDBOX_REL,
    dump_json,
    find_soname_elf,
    iter_stage_entries,
    load_json,
    sha256_file,
    validate_source_build_graph,
)

CHROMIUM_LIBDIR = "usr/lib/chromium"
JPEG_SONAME = "libjpeg.so.62"
JPEG_FULL = "libjpeg.so.62.3.0"
JPEG_ARCH_LIBDIR = "usr/lib/aarch64-linux-gnu"
CHROMIUM_VERSION = "155.0.8059.79-1~deb13u1"
FLAC_SONAME = "libFLAC.so.14"
OPENH264_SONAME = "libopenh264.so.8"

LAUNCHER_SCRIPT = """#!/bin/sh
# 9to1 Chromium-native bundled launcher (private engine path).
# Sets LD_LIBRARY_PATH to the private compatibility-library directory (which carries
# SONAME libjpeg.so.62 from the pinned Debian libjpeg62-turbo) and then execs the
# real Chromium binary. No system library is replaced; no --no-sandbox or OS security
# workaround is introduced.
set -eu
here=$(CDPATH= CD_PATH= cd -- "$(dirname -- "$0")" && pwd)
libdir="$here/chromium/lib"
export LD_LIBRARY_PATH="$libdir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec "$here/chromium/chromium" "$@"
"""

IJG_ACKNOWLEDGEMENT = """This software is based in part on the work of the Independent JPEG Group.

The staged binary object libjpeg.so.62 (from the Debian libjpeg62-turbo package,
1:2.1.5-4) contains code that is based in part on the work of the Independent JPEG
Group. The full libjpeg62-turbo copyright and license text are included in this
notices directory as libjpeg62-turbo-copyright.
"""


def _verify_deb_against_pin(deb: Path, pin: Path) -> None:
    """Fail if ``deb`` does not match the pinned version/arch/sha256/bytes."""
    if not deb.is_file():
        raise RuntimeError(f"missing verified package {deb.name}")
    want_sha = pin["sha256"]
    want_size = pin["bytes"]
    if sha256_file(deb) != want_sha:
        raise RuntimeError(f"SHA256 mismatch for {deb.name} against pinned manifest")
    if deb.stat().st_size != want_size:
        raise RuntimeError(f"size mismatch for {deb.name} against pinned manifest")
    fields = subprocess.run(
        ["dpkg-deb", "--field", str(deb), "Package", "Version", "Architecture"],
        capture_output=True, text=True, check=True).stdout
    pkg = pin["debian_package"]
    if f"Package: {pkg}" not in fields:
        raise RuntimeError(f"{deb.name}: package field mismatch")
    if f"Version: {pin['version']}" not in fields:
        raise RuntimeError(f"{deb.name}: version field mismatch")
    arch = pin.get("architecture")
    if arch and f"Architecture: {arch}" not in fields and "Architecture: all" not in fields:
        raise RuntimeError(f"{deb.name}: architecture field mismatch")


def _copy_vendor_tree(src_root: Path, dst_chromium: Path) -> None:
    """Copy the full usr/lib/chromium subtree preserving relative hierarchy."""
    src = src_root / CHROMIUM_LIBDIR
    if not src.is_dir():
        raise RuntimeError(f"vendor tree missing: {CHROMIUM_LIBDIR}")
    for child in src.rglob("*"):
        rel = child.relative_to(src)
        dst = dst_chromium / rel
        if child.is_dir():
            dst.mkdir(parents=True, exist_ok=True)
        elif child.is_symlink():
            dst.parent.mkdir(parents=True, exist_ok=True)
            if dst.exists() or dst.is_symlink():
                dst.unlink()
            os.symlink(os.readlink(child), dst)
        else:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(child, dst)


def _copy_binary(src_root: Path, dst_chromium: Path) -> None:
    src = src_root / CHROMIUM_LIBDIR / "chromium"
    if not src.is_file():
        raise RuntimeError("chromium binary missing from package")
    shutil.copy2(src, dst_chromium / "chromium")


def _install_private_lib(src_root: Path, stage_browser: Path, soname: str,
                         label: str) -> None:
    """Stage one private provider library under the private engine path.

    The real library file is located by its ELF SONAME inside the extracted package
    (no invented filename), copied as a real file, and a relative SONAME symlink is
    created alongside it. This is the same private pattern used for libjpeg.so.62.
    """
    lib_dir = stage_browser / PRIVATE_LIB_REL
    lib_dir.mkdir(parents=True, exist_ok=True)
    src = find_soname_elf(src_root, soname)
    if src is None:
        raise RuntimeError(f"{label}: missing {soname} ELF in extracted package")
    real_name = src.name
    shutil.copy2(src, lib_dir / real_name)
    if real_name == soname:
        # The real library file already carries the SONAME as its basename. Keep it
        # as the regular file; creating a symlink here would be self-referential
        # and would destroy the copied file.
        return
    link = lib_dir / soname
    if link.exists() or link.is_symlink():
        link.unlink()
    link.symlink_to(real_name)


def _install_private_jpeg(src_root: Path, stage_browser: Path) -> None:
    _install_private_lib(src_root, stage_browser, JPEG_SONAME, "libjpeg62-turbo")


def _set_sandbox_mode(stage_browser: Path) -> dict:
    sb = stage_browser / SANDBOX_REL
    if not sb.is_file():
        raise RuntimeError("chrome-sandbox missing from stage")
    st = sb.stat()
    observed_uid, observed_gid = st.st_uid, st.st_gid
    os.chmod(sb, SANDBOX_MODE)
    owner_applied = False
    try:
        os.chown(sb, SANDBOX_EXPECTED_UID, SANDBOX_EXPECTED_GID)
        owner_applied = True
    except PermissionError:
        owner_applied = False
    return {
        "mode_hex": oct(SANDBOX_MODE),
        "expected_owner": {"uid": SANDBOX_EXPECTED_UID, "gid": SANDBOX_EXPECTED_GID},
        "observed_uid": observed_uid,
        "observed_gid": observed_gid,
        "owner_applied": owner_applied,
        "root_packager_owns": True,
    }


def _copy_notices(stage_browser: Path, notices_dir: Path,
                  copyright_sources: dict[str, Path]) -> None:
    dest = stage_browser / "NOTICES"
    dest.mkdir(parents=True, exist_ok=True)
    # Exact 155 Chromium copyright (already checked-in as notices/COPYRIGHT).
    src_copyright = notices_dir / "COPYRIGHT"
    if not src_copyright.is_file():
        raise RuntimeError("missing Chromium copyright notice (notices/COPYRIGHT)")
    shutil.copy2(src_copyright, dest / "chromium-copyright")
    lic_dir = notices_dir / "common-licenses"
    if lic_dir.is_dir():
        (dest / "common-licenses").mkdir(exist_ok=True)
        for f in lic_dir.iterdir():
            if f.is_file():
                shutil.copy2(f, dest / "common-licenses" / f.name)
    # Private provider copyrights (JPEG / FLAC / OpenH264) + IJG acknowledgement.
    for dest_name, src in copyright_sources.items():
        if not src.is_file():
            raise RuntimeError(f"missing {dest_name} copyright")
        shutil.copy2(src, dest / dest_name)
    (dest / "IJG-acknowledgement.txt").write_text(IJG_ACKNOWLEDGEMENT)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--debs-dir", type=Path, required=True)
    ap.add_argument("--output-stage", type=Path, required=True)
    ap.add_argument("--pinned-manifest", type=Path, required=True)
    ap.add_argument("--acquire-receipt", type=Path, required=True,
                    help="Verified acquisition receipt with the signed source build graph")
    ap.add_argument("--notices", type=Path, required=True)
    ap.add_argument("--source-dir", type=Path, required=True,
                    help="Directory holding the verified physical source inputs (evidence/source)")
    ap.add_argument("--source-pins", type=Path, required=True,
                    help="Pinned authenticated source manifest (source-pins.json)")
    args = ap.parse_args(argv)

    pinned = load_json(args.pinned_manifest)
    receipt = load_json(args.acquire_receipt)
    source_pins = load_json(args.source_pins)

    # Source gate: the real executable gate must pass BEFORE any runtime output. It
    # verifies the complete expected Chromium+JPEG input set exists physically with
    # matching bytes+streamSHA and exact versions — no self-asserted bool/nonempty
    # list is accepted.
    sg_check = validate_source_build_graph(args.source_dir, source_pins, receipt)
    if not sg_check["ok"]:
        raise SystemExit("source gate failed before staging: "
                         + "; ".join(sg_check["problems"][:8]))
    # The verified source build graph is embedded in the manifest so the root
    # packager re-runs the same executable gate via the manifest's build graph.
    source_graph = receipt.get("source_graph")

    debs = args.debs_dir
    pinned_pkgs = {p["debian_package"]: p for p in pinned["packages"]}
    want = {
        "chromium": f"chromium_{CHROMIUM_VERSION}_arm64.deb",
        "chromium-common": f"chromium-common_{CHROMIUM_VERSION}_arm64.deb",
        "chromium-sandbox": f"chromium-sandbox_{CHROMIUM_VERSION}_arm64.deb",
        "libjpeg62-turbo": "libjpeg62-turbo_2.1.5-4_arm64.deb",
        "libflac14": "libflac14_1.5.0+ds-2_arm64.deb",
        "libopenh264-8": "libopenh264-8_2.6.0+dfsg-2_arm64.deb",
    }

    tmp = Path(tempfile.mkdtemp(prefix="chromium-stage-"))
    try:
        roots = {}
        for key, fname in want.items():
            deb = debs / fname
            pin = pinned_pkgs[key]
            _verify_deb_against_pin(deb, pin)
            root = tmp / key
            root.mkdir()
            subprocess.run(["dpkg-deb", "-x", str(deb), str(root)], check=True)
            roots[key] = root

        out = args.output_stage.resolve()
        if out.exists():
            raise SystemExit(f"Refuse existing stage: {out}")
        stage_browser = out / BROWSER_STAGE_REL
        stage_chromium = stage_browser / "chromium"
        stage_chromium.mkdir(parents=True)

        _copy_binary(roots["chromium"], stage_chromium)
        _copy_vendor_tree(roots["chromium-common"], stage_chromium)
        # chrome-sandbox comes from its own package.
        shutil.copy2(roots["chromium-sandbox"] / CHROMIUM_LIBDIR / "chrome-sandbox",
                     stage_chromium / "chrome-sandbox")
        _install_private_jpeg(roots["libjpeg62-turbo"], stage_browser)
        _install_private_lib(roots["libflac14"], stage_browser, FLAC_SONAME,
                             "libflac14")
        _install_private_lib(roots["libopenh264-8"], stage_browser, OPENH264_SONAME,
                             "libopenh264-8")

        launcher = stage_browser / LAUNCHER_REL
        launcher.write_text(LAUNCHER_SCRIPT)
        os.chmod(launcher, 0o755)
        os.chmod(stage_browser / BINARY_REL, 0o755)

        sandbox_info = _set_sandbox_mode(stage_browser)
        copyright_sources = {
            "libjpeg62-turbo-copyright": (roots["libjpeg62-turbo"]
                                          / "usr/share/doc/libjpeg62-turbo/copyright"),
            "libflac14-copyright": (roots["libflac14"]
                                    / "usr/share/doc/libflac14/copyright"),
            "libopenh264-8-copyright": (roots["libopenh264-8"]
                                        / "usr/share/doc/libopenh264-8/copyright"),
        }
        _copy_notices(stage_browser, args.notices.resolve(), copyright_sources)

        files = [entry for _, entry in iter_stage_entries(stage_browser)]
        manifest = {
            "schema": "9to1.chromium-native.stage-manifest/v1",
            "version": CHROMIUM_VERSION,
            "sandbox": sandbox_info,
            "sources_verified": True,
            "source_pins": {
                "build_graph": source_graph,
                "inputs": [i for i in source_graph["chromium"]["inputs"]],
            },
            "runtime_acceptance": "NOT RUN",
            "files": files,
        }
        dump_json(manifest, stage_browser / MANIFEST_REL)
        print(out)
        print(f"  {len(files)} entries staged (files+symlinks), "
              f"sandbox {sandbox_info['mode_hex']}")
        return 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
