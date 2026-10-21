"""Data-only package staging helper for the bundled Dulche runtime.

This is a *staging-time* tool that assembles a fresh, private, data-only
package tree (`--stage-root`) from files the integrator already supplies
locally. It performs NO downloads, NO network access, NO execution of any
engine/model, NO SDK/.NET/native commands, and NO import into a user HOME or
model store. It reuses the existing, independently-validated ``bundle.cli
pack``/``verify`` path and the canonical ``modelctl`` source closure.

What it stages (mirrors the canonical install layout):

* engine closure at ``<stage>/usr/lib/haven/llama.cpp/<installPath>`` for every
  declared manifest asset (engine ELF, notices, provenance);
* bundle manifest + receipt at ``<stage>/usr/lib/haven/inference/bundle/``;
* python closure ``modelctl.py``/``gguf.py``/``model_lease.py`` at
  ``<stage>/usr/lib/haven/inference/``;
* default-model seed JSON + weights + Apache-2.0 notice under
  ``<stage>/usr/share/haven/default-models/`` (the model is HARDLINKED from the
  supplied immutable bytes when they share a filesystem — never copied/imported
  by this lane).

Safety guarantees:

* refuses to overwrite an existing non-empty stage root unless ``--force``;
* refuses to write anywhere outside the fixed stage subdirectories;
* refuses any symlink anywhere under the staged tree;
* validates every supplied asset's SHA-256/size against the manifest pins and the
  ELF machine against the spec host before staging;
* validates the canonical seed JSON model id/sha256/size/licence/source;
* re-runs ``verify_installed(..., strict_closure=True)`` on the staged install
  root so undeclared files/symlinks cannot pass.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import stat
import sys
from typing import Any

from .formats import sniff_format
from .identity import IdentityError, file_identity, is_regular_file_no_follow
from .manifest import parse_manifest
from .packager import pack_bundle
from .spec import load_spec
from .verifier import verify_installed

EXPECTED_MODEL_ID = "qwen3-06b-q6k"
EXPECTED_MODEL_LICENSE = "Apache-2.0"
EXPECTED_MODEL_SHA256 = "1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b"
EXPECTED_MODEL_SIZE = 495107776
EXPECTED_MODEL_BLOB = "1c/1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b.gguf"
EXPECTED_MODEL_SOURCE = "https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/50968a4468ef4233ed78cd7c3de230dd1d61a56b/Qwen3-0.6B-Q6_K.gguf"
PYTHON_CLOSURE_FILES = ("modelctl.py", "gguf.py", "model_lease.py")
MODEL_NOTICE_REL = "notices/Qwen3-Apache-2.0-LICENSE.txt"


class StagingError(RuntimeError):
    """Raised when staging cannot proceed safely."""


def _validate_stage_root(stage_root: pathlib.Path) -> None:
    """Reject unsafe stage roots BEFORE any write.

    A stage root may only be a fresh, private, empty directory (or not yet
    present). We reject, up front:
    * a stage root that is a symlink, or which has any existing symlink ancestor
      (never follow a link out of the scratch area);
    * an existing non-empty stage root (no `--force` bypass).
    """
    lexical = pathlib.Path(stage_root).absolute()
    probe = pathlib.Path("/")
    for part in lexical.parts[1:]:
        probe = probe / part
        try:
            if probe.is_symlink():
                raise StagingError(f"stage root or an ancestor is a symlink: {probe}")
        except OSError:
            pass  # component not present yet; it will be created fresh
    try:
        st = stage_root.lstat()
    except FileNotFoundError:
        return  # fresh: safe to create
    if stat.S_ISLNK(st.st_mode):
        raise StagingError("stage root is a symlink; refusing to write through it")
    if not stat.S_ISDIR(st.st_mode):
        raise StagingError("stage root is not a directory")
    try:
        nonempty = any(stage_root.iterdir())
    except OSError as exc:
        raise StagingError(f"cannot inspect stage root: {exc}")
    if nonempty:
        raise StagingError(
            f"stage root already exists and is not empty: {stage_root} (fresh private output only)"
        )


def _hash_file(path: pathlib.Path) -> tuple[str, int]:
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
            size += len(chunk)
    return digest.hexdigest(), size


def _hardlink_model(seed_model: pathlib.Path, dest: pathlib.Path) -> None:
    try:
        os.link(seed_model, dest)
    except OSError as exc:
        raise StagingError(
            f"cannot hardlink model into stage (cross-filesystem?): {exc}. "
            "Refusing to copy 500MB; make the stage root share a filesystem "
            "with the immutable model bytes."
        ) from exc


def _stage_asset(
    src: pathlib.Path,
    dst: pathlib.Path,
    manifest_digest: str,
    manifest_size: int,
    *,
    executable: bool = False,
) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    if not is_regular_file_no_follow(src):
        raise StagingError(f"supplied asset is not a regular file: {src}")
    identity = file_identity(src)
    if identity.digest != manifest_digest:
        raise StagingError(f"supplied asset digest mismatch: {src}")
    if identity.size != manifest_size:
        raise StagingError(f"supplied asset size mismatch: {src}")
    # Positive copy from the already-validated regular file; never a symlink.
    with src.open("rb") as r, dst.open("xb") as w:
        while True:
            chunk = r.read(1024 * 1024)
            if not chunk:
                break
            w.write(chunk)
    # Source inputs are never chmod'ed. We only set the staged destination mode:
    # runtime.binary ELF -> 0755; notices/provenance/seed metadata -> 0644.
    os.chmod(dst, 0o755 if executable else 0o644)


def _validate_seed(
    seed_json: dict[str, Any],
    *,
    model_path: pathlib.Path,
    model_digest: str,
    model_size: int,
) -> None:
    """Validate against the EXACT canonical pins, not attacker-consistent bytes.

    The seed is only accepted when every field matches the trusted canonical Qwen3
    seed and the supplied model bytes are the exact immutable pinned GGUF (and
    pass the existing canonical structural sniffer). This prevents a hand-crafted
    17-byte "model" with self-consistent fake hash/size/source from staging.
    """
    if str(seed_json.get("schemaVersion", "")) != "1":
        raise StagingError("seed schemaVersion must be 1")
    sid = str(seed_json.get("id", ""))
    if sid != EXPECTED_MODEL_ID:
        raise StagingError(f"seed model id mismatch: expected {EXPECTED_MODEL_ID!r}, got {sid!r}")
    if str(seed_json.get("license", "")) != EXPECTED_MODEL_LICENSE:
        raise StagingError(f"seed model licence mismatch: expected {EXPECTED_MODEL_LICENSE!r}")
    if str(seed_json.get("redistribution", "")).lower() != "allowed":
        raise StagingError("seed model redistribution must be 'allowed' for deployment staging")
    if str(seed_json.get("sha256", "")).lower() != EXPECTED_MODEL_SHA256:
        raise StagingError("seed model sha256 does not match the canonical pinned Qwen3-0.6B-Q6_K model")
    if int(seed_json.get("size", -1)) != EXPECTED_MODEL_SIZE:
        raise StagingError("seed model size does not match the canonical pinned Qwen3-0.6B-Q6_K model")
    if str(seed_json.get("source", "")) != EXPECTED_MODEL_SOURCE:
        raise StagingError("seed model source does not match the canonical immutable resolve URL")
    if str(seed_json.get("blob", "")) != EXPECTED_MODEL_BLOB:
        raise StagingError("seed model blob path does not match the canonical layout")
    caps = seed_json.get("capabilities")
    if not isinstance(caps, list) or "chat" not in caps:
        raise StagingError("seed model capabilities must include 'chat'")
    # The supplied model bytes must equal the exact canonical pin (fixes both the
    # fake-model case and any wrong-hash case).
    if model_digest != EXPECTED_MODEL_SHA256:
        raise StagingError(f"supplied model sha256 does not match canonical pin (got {model_digest[:16]}...)")
    if model_size != EXPECTED_MODEL_SIZE:
        raise StagingError("supplied model size does not match canonical pin")
    # Actual GGUF structural admission using the existing canonical sniffer (no
    # invented parser/schema).
    try:
        meta = sniff_format(model_path, "gguf")
    except IdentityError as exc:
        raise StagingError(f"seed model is not a valid GGUF: {exc}")
    if not bool(meta.get("structurallyComplete")):
        raise StagingError("seed model GGUF is not structurally complete")


def _no_symlinks_under(root: pathlib.Path) -> None:
    for p in root.rglob("*"):
        if p.is_symlink():
            raise StagingError(f"staged tree contains a symlink: {p}")


def stage_package(
    *,
    spec_path: pathlib.Path,
    payload_root: pathlib.Path,
    seed_model: pathlib.Path,
    seed_json_path: pathlib.Path,
    seed_notice: pathlib.Path,
    python_source_root: pathlib.Path,
    stage_root: pathlib.Path,
) -> dict[str, Any]:
    """Assemble the data-only package tree and verify it. Returns a summary."""
    if not payload_root.is_dir():
        raise StagingError(f"payload root is not a directory: {payload_root}")
    if not seed_model.is_file():
        raise StagingError(f"seed model missing: {seed_model}")
    if not seed_json_path.is_file():
        raise StagingError(f"seed JSON missing: {seed_json_path}")
    if not seed_notice.is_file():
        raise StagingError(f"seed notice missing: {seed_notice}")
    for name in PYTHON_CLOSURE_FILES:
        if not (python_source_root / name).is_file():
            raise StagingError(f"python closure file missing in {python_source_root}: {name}")

    # All read-only input validation happens BEFORE any stage write, so a fake
    # seed, wrong hash, or unsafe stage root can never leave partial stage writes.
    spec = load_spec(spec_path)
    pack = pack_bundle(spec, payload_root)
    if pack.state != "ready":
        reasons = [f"{r['logical']}: {r['reason']}" for r in pack.rejected]
        reasons += [f"missing:{m.logical}:{m.reason}" for m in pack.missing]
        raise StagingError(f"pack not ready (state={pack.state}); " + "; ".join(reasons) or "no assets")

    model_digest, model_size = _hash_file(seed_model)
    seed_data = json.loads(seed_json_path.read_text(encoding="utf-8"))
    _validate_seed(seed_data, model_path=seed_model, model_digest=model_digest, model_size=model_size)

    # Reject symlink stage-root/ancestors and non-empty roots before any write.
    _validate_stage_root(stage_root)

    install_root = stage_root / "usr" / "lib" / "haven"
    bundle_dir = install_root / "inference" / "bundle"

    # 1) Stage every declared engine-closure asset.
    path_by_logical = {a.logical_id: a for a in spec.assets}
    for asset in pack.admitted:
        dst = install_root / asset.install_path
        _stage_asset(
            payload_root / path_by_logical[asset.logical].path,
            dst,
            asset.digest,
            asset.size,
            executable=(asset.kind == "runtime.binary"),
        )

    # 2) Manifest + receipt at canonical bundle path.
    bundle_dir.mkdir(parents=True, exist_ok=True)
    written = pack.write(bundle_dir, prefix="bundle")
    os.chmod(bundle_dir / "bundle-manifest.json", 0o644)
    os.chmod(bundle_dir / "bundle-receipt.json", 0o644)

    # 3) Python closure.
    for name in PYTHON_CLOSURE_FILES:
        shutil_copy(python_source_root / name, install_root / "inference" / name)

    # 4) Default-model seed: hardlink weights, seed JSON, Apache notice.
    models_dir = stage_root / "usr" / "share" / "haven" / "default-models"
    models_dir.mkdir(parents=True, exist_ok=True)
    weights_path = models_dir / f"{model_digest}.gguf"
    _hardlink_model(seed_model, weights_path)
    models_dir.joinpath(f"{EXPECTED_MODEL_ID}.json").write_text(
        json.dumps(seed_data, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    os.chmod(models_dir / f"{EXPECTED_MODEL_ID}.json", 0o644)
    notices_dir = models_dir / "notices"
    notices_dir.mkdir(parents=True, exist_ok=True)
    shutil_copy(seed_notice, notices_dir / "Qwen3-Apache-2.0-LICENSE.txt")

    # 5) No symlinks anywhere in the staged tree.
    _no_symlinks_under(stage_root)

    # 6) Verify the staged install root with the existing strict verifier.
    result = verify_installed(install_root, written["manifest"], strict_closure=True)
    if result.state != "ready":
        bad = [f"{s.install_path}:{s.state}" for s in result.asset_statuses if s.state != "present"]
        raise StagingError(f"staged verifier not ready (state={result.state}); " + "; ".join(bad + result.unexpected_paths))

    return {
        "state": "ready",
        "stageRoot": str(stage_root),
        "installRoot": str(install_root),
        "manifest": written["manifest"],
        "receipt": written["receipt"],
        "modelHardlink": {
            "source": str(seed_model),
            "staged": str(weights_path),
            "sha256": model_digest,
            "size": model_size,
            "id": EXPECTED_MODEL_ID,
        },
        "pythonClosure": list(PYTHON_CLOSURE_FILES),
        "deploymentAdmitted": len(pack.admitted),
    }


def shutil_copy(src: pathlib.Path, dst: pathlib.Path) -> None:
    """Positive single-file copy (never a symlink), staged under our own tree."""
    dst.parent.mkdir(parents=True, exist_ok=True)
    with src.open("rb") as r, dst.open("xb") as w:
        while True:
            chunk = r.read(1024 * 1024)
            if not chunk:
                break
            w.write(chunk)
    os.chmod(dst, 0o644)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="bundle-data-stage",
        description="Stage a data-only Dulche package tree from supplied local files.",
    )
    parser.add_argument("--spec", type=pathlib.Path, default=None,
                        help="bundle spec JSON (default: arm64-runtime-bundle-spec.json next to this module)")
    parser.add_argument("--payload-root", type=pathlib.Path, required=True,
                        help="local Bookworm payload/evidence root (contains spec asset relative paths)")
    parser.add_argument("--seed-model", type=pathlib.Path, required=True, help="supplied model GGUF bytes")
    parser.add_argument("--seed-json", type=pathlib.Path, required=True, help="canonical model seed JSON")
    parser.add_argument("--seed-notice", type=pathlib.Path, required=True,
                        help="model Apache-2.0 licence notice file")
    parser.add_argument("--python-source-root", type=pathlib.Path, required=True,
                        help="root containing modelctl.py, gguf.py, model_lease.py")
    parser.add_argument("--stage-root", type=pathlib.Path, required=True,
                        help="fresh private scratch staging root (must be absent or an empty directory; no symlink ancestors)")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    spec = args.spec
    if spec is None:
        spec = pathlib.Path(__file__).with_name("arm64-runtime-bundle-spec.json")
    try:
        summary = stage_package(
            spec_path=spec,
            payload_root=args.payload_root,
            seed_model=args.seed_model,
            seed_json_path=args.seed_json,
            seed_notice=args.seed_notice,
            python_source_root=args.python_source_root,
            stage_root=args.stage_root,
        )
    except (StagingError, IdentityError) as exc:
        print(f"data_stage: error: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(summary, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
