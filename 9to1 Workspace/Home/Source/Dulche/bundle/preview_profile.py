"""Laptop preview asset profile builder (source-only, supplied-assets-only).

This module produces an honest, immutable *preview* profile over files the
integrator actually supplies at a local root. It is a staging-time tool, not a
runtime inference claim:

  * it never downloads anything, never reaches a network, and never invents an
    identity (hash/size) for a file that is not present;
  * every supplied file is re-hashed and bounds-checked with the SAME bounded
    structural sniffer the bundle ships (``bundle.formats.sniff_format``);
  * platform compatibility is only claimed when the target OS/architecture is
    explicitly confirmed; the profile rejects blank/invalid targets instead of
    reporting ``confirmed=true``;
  * runtime binaries are checked to be the correct ELF machine for the host arch
    (aarch64 -> e_machine 183; x86_64 -> 62). A wrong-machine binary is rejected,
    never renamed/relabelled;
  * licence/source/redistribution evidence is REQUIRED for deployment admission;
    absent evidence is a blocker, never silently accepted as evidence;
  * TESTONLY asset fixtures are rejected and never become preview assets.

Structural checks and deployment admission are kept separate: an asset can be
structurally complete yet fail deployment admission (wrong machine, absent
evidence, TESTONLY). ``claimedReady`` is always ``false``.

Q4_K_M / quant-gate note: ``formats.py`` structurally supports only Q6_K (tag
14) among quantized types. A GGUF whose filename says Q4_K_M typically carries
several k-quant block types; until authentic, matching pinned upstream block-size
definitions and real tensor inventory are available, such containers are rejected
as unknown-format and are never relabelled. The actual selected preview model
(Qwen3-0.6B-Q6_K) is structurally Q6_K and passes the gate when supplied.
"""
from __future__ import annotations

import hashlib
import json
import pathlib
import sys
from typing import Any

from .formats import elf_machine_matches, sniff_format
from .identity import IdentityError
from .licence import KNOWN_SPDX_IDS

PREVIEW_SCHEMA_VERSION = 1
DEFAULT_ENGINE = "llamacpp"
STATE_PRESENT = "present"
STATE_ABSENT = "absent"
STATE_REJECTED = "rejected"

# Declared-format inference by file suffix (mirrors structural_validate.py).
_SUFFIX_FORMAT = {
    ".gguf": "gguf",
    ".safetensors": "safetensors",
    ".elf": "elf",
}

_KNOWN_ARCH = {"aarch64", "arm64", "x86_64", "amd64", "riscv64", "arm"}
_KNOWN_OS = {"linux", "linux-gnu", "linux-musl", "darwin", "macos", "windows", "win32", "android", "ios", "freebsd", "openbsd", "netbsd"}
# The only runtime OS this preview supports for native runtime.binary ELF assets
# is Debian Linux. A Linux ELF is NOT compatible with Windows/macOS/iOS/BSD/
# musl merely because the target OS name is "known"; those are rejected for
# deployment admission (structural identity is kept separate).
_SUPPORTED_RUNTIME_OS = {"linux"}


def _classify_declared_format(suffix: str) -> str:
    return _SUFFIX_FORMAT.get(suffix, "opaque")


def _file_identity(path: pathlib.Path) -> tuple[str, int]:
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
            size += len(chunk)
    return digest.hexdigest(), size


class PreviewProfileBuilder:
    """Accumulates supplied assets and renders an honest preview profile."""

    def __init__(
        self,
        *,
        target_os: str | None = None,
        target_arch: str | None = None,
    ) -> None:
        self._target_os = target_os
        self._target_arch = target_arch
        self._assets: list[dict[str, Any]] = []
        self._testonly_seen = 0

    # -- internal helpers --------------------------------------------------

    @staticmethod
    def _evidence_present(license_id: str | None, source: str | None, redistribution: str) -> bool:
        # Deployment admission requires an explicit, *known* licence and explicit
        # permission to redistribute. A blank/unknown licence cannot confer
        # admission, and review-required/prohibited/unknown redistribution stays
        # BLOCKED — this never weakens the independent structural check.
        if not license_id or not str(license_id).strip():
            return False
        if str(license_id).strip() not in KNOWN_SPDX_IDS:
            return False
        if not source or not str(source).strip():
            return False
        if str(redistribution).strip().lower() != "allowed":
            return False
        return True

    @staticmethod
    def _machine_check(meta: dict, arch: str, runtime_format: str) -> bool:
        if runtime_format != "elf":
            return True  # non-ELF assets have no machine gate
        if arch not in _KNOWN_ARCH:
            return False  # unknown host arch => cannot confirm machine match
        return bool(elf_machine_matches(meta, arch))

    def add_asset(
        self,
        *,
        logical: str,
        path: str,
        declared_format: str = "",
        engine: str = DEFAULT_ENGINE,
        kind: str = "model.gguf",
        license_id: str | None = None,
        source: str | None = None,
        redistribution: str = "unknown",
        testonly: bool = False,
    ) -> None:
        fmt = declared_format or _classify_declared_format(pathlib.Path(path).suffix.lower())
        entry: dict[str, Any] = {
            "logical": logical,
            "kind": kind,
            "engine": engine,
            "runtimeFormat": fmt,
            "license": license_id,
            "source": source,
            "redistribution": redistribution,
            "state": STATE_ABSENT,
            "sha256": None,
            "size": None,
            "structuralAdmission": False,
            "deploymentAdmission": False,
            "admissionState": "BLOCKED",
        }

        if testonly:
            self._testonly_seen += 1
            entry["state"] = STATE_REJECTED
            entry["admissionState"] = "BLOCKED"
            entry["blocker"] = "TESTONLY fixture cannot be a preview asset"
            self._assets.append(entry)
            return

        p = pathlib.Path(path)
        if not p.is_file():
            entry["blocker"] = "asset bytes are not present (supplied-assets-only)"
            self._assets.append(entry)
            return

        sha, size = _file_identity(p)
        entry["state"] = STATE_PRESENT
        entry["sha256"] = sha
        entry["size"] = size

        # Structural admission (format gate) — separate from deployment admission.
        try:
            meta = sniff_format(p, fmt)
        except IdentityError as exc:
            entry["blocker"] = f"structural sniffer rejected: {exc}"
            self._assets.append(entry)
            return
        # Opaque (non-ELF, non-GGUF) bundle artifacts such as licence notices carry
        # no binary structure to validate; presence + identity + evidence is their
        # structural admission. ELF/GGUF/safetensors still require the sniffer to
        # mark structurallyComplete (and ELF additionally the machine gate below).
        entry["structuralAdmission"] = bool(meta.get("structurallyComplete")) or fmt == "opaque"
        entry["formatMeta"] = {k: v for k, v in meta.items() if k != "structurallyComplete"}

        # Deployment admission: evidence + platform/machine gate + confirmed target.
        blockers: list[str] = []
        os_name = str(self._target_os or "").strip().lower()
        arch = str(self._target_arch or "").strip().lower()
        if os_name not in _KNOWN_OS or arch not in _KNOWN_ARCH:
            blockers.append("target platform is not a confirmed known OS/architecture; deployment admission requires a confirmed target")
        if not self._evidence_present(license_id, source, redistribution):
            blockers.append("absent/unknown licence/source/redistribution evidence")
        if not self._machine_check(meta, arch, fmt):
            machine = meta.get("machine", "?")
            blockers.append(f"architecture-incompatible:host={arch or '<none>'} elf-machine={machine}")
        if kind == "runtime.binary" and fmt == "elf" and os_name not in _SUPPORTED_RUNTIME_OS:
            blockers.append(
                f"runtime.binary ELF is not compatible with target OS {os_name!r}; "
                "supported preview runtime OS is Debian Linux"
            )
        entry["deploymentAdmission"] = entry["structuralAdmission"] and not blockers
        entry["admissionState"] = "ADMITTED" if entry["deploymentAdmission"] else "BLOCKED"
        if blockers:
            entry["blocker"] = "; ".join(blockers)
        self._assets.append(entry)

    def build(self) -> dict[str, Any]:
        os_name = str(self._target_os or "").strip().lower()
        arch = str(self._target_arch or "").strip().lower()
        target_confirmed = os_name in _KNOWN_OS and arch in _KNOWN_ARCH

        blockers: list[str] = []
        if not target_confirmed:
            blockers.append("target platform is blank/not a known OS/architecture; platform compatibility not claimed")

        present = [a for a in self._assets if a["state"] == STATE_PRESENT]
        absent = [a for a in self._assets if a["state"] == STATE_ABSENT]
        testonly = [a for a in self._assets if a["state"] == STATE_REJECTED]
        if not present:
            blockers.append("no supplied assets are present; no real binary/model bytes to stage")
        for a in self._assets:
            if a["admissionState"] == "BLOCKED":
                blockers.append(
                    f"asset '{a['logical']}' is not admitted: {a.get('blocker', 'rejection')}"
                )

        return {
            "schemaVersion": PREVIEW_SCHEMA_VERSION,
            "purpose": "laptop-preview-asset-profile-offline",
            "targetPlatform": {
                "os": os_name or None,
                "arch": arch or None,
                "confirmed": target_confirmed,
            },
            "assets": self._assets,
            "summary": {
                "presentAssets": len(present),
                "absentAssets": len(absent),
                "rejectedTestOnly": len(testonly),
                "structurallyAdmitted": sum(1 for a in present if a["structuralAdmission"]),
                "deploymentAdmitted": sum(1 for a in present if a["deploymentAdmission"]),
            },
            "blocked": blockers,
            "claimedReady": False,
        }


def main(argv: list[str]) -> int:
    """CLI: read a requested-assets JSON, render a preview profile.

    usage: python -m bundle.preview_profile <request.json> [--out profile.json]

    The request JSON shape::

        {
          "targetPlatform": {"os": "linux", "arch": "aarch64"},
          "assets": [
            {"logical": "model", "path": "/abs/m.gguf",
             "declaredFormat": "gguf", "engine": "llamacpp",
             "kind": "model.gguf",
             "license": "Apache-2.0", "source": "urn:local",
             "redistribution": "review-required"}
          ]
        }

    ``--out`` is optional (defaults to stdout).
    """
    if len(argv) < 2:
        print("usage: python -m bundle.preview_profile <request.json> [--out profile.json]", file=sys.stderr)
        return 2
    request_path = pathlib.Path(argv[1])
    if not request_path.is_file():
        print(f"preview_profile: no such request file: {request_path}", file=sys.stderr)
        return 2
    try:
        request = json.loads(request_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        print(f"preview_profile: could not read request: {exc}", file=sys.stderr)
        return 2

    target = request.get("targetPlatform") or {}
    builder = PreviewProfileBuilder(target_os=target.get("os"), target_arch=target.get("arch"))
    for spec in request.get("assets", []):
        builder.add_asset(
            logical=str(spec.get("logical", "")),
            path=str(spec.get("path", "")),
            declared_format=str(spec.get("declaredFormat", "")),
            engine=str(spec.get("engine", DEFAULT_ENGINE)),
            kind=str(spec.get("kind", "model.gguf")),
            license_id=spec.get("license"),
            source=spec.get("source"),
            redistribution=str(spec.get("redistribution", "unknown")),
            testonly=bool(spec.get("testonly", False)),
        )
    profile = builder.build()
    rendered = json.dumps(profile, indent=2, sort_keys=True) + "\n"

    out = None
    if "--out" in argv:
        i = argv.index("--out")
        if i + 1 < len(argv):
            out = pathlib.Path(argv[i + 1])
    if out is not None:
        out.write_text(rendered, encoding="utf-8")
    else:
        sys.stdout.write(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
