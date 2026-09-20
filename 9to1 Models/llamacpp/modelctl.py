#!/usr/bin/env python3
"""Explicit, local-only model store manager for Haven llama.cpp.

This tool performs user-requested model-store mutations. The inference broker is
kept read-only and never downloads, imports, updates, or deletes model files.
"""
from __future__ import annotations

import argparse
import errno
import hashlib
import json
import os
import pathlib
import re
import stat
import tempfile
from typing import Any, Iterable

from gguf import GgufValidationError, read_gguf_version
from model_lease import ModelLeaseBusy, ModelLeaseError, acquire_model_lease, acquire_store_lease, ensure_private_directory

MODEL_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
CAPABILITY_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")


class ModelManagerError(RuntimeError):
    pass


def _default_model_root() -> pathlib.Path:
    data_home = pathlib.Path(os.environ.get("XDG_DATA_HOME", pathlib.Path.home() / ".local/share"))
    return pathlib.Path(os.environ.get("HAVEN_MODEL_ROOT", data_home / "haven/models"))


def _default_runtime_dir() -> pathlib.Path:
    xdg_runtime = os.environ.get("XDG_RUNTIME_DIR")
    default = pathlib.Path(xdg_runtime) / "haven" if xdg_runtime else pathlib.Path("/tmp") / f"haven-{os.getuid()}"
    return pathlib.Path(os.environ.get("HAVEN_RUNTIME_DIR", default))


def _canonical_blob_relative(digest: str) -> str:
    digest = digest.lower()
    if len(digest) != 64 or any(char not in "0123456789abcdef" for char in digest):
        raise ModelManagerError("invalid SHA-256 digest")
    return str(pathlib.PurePosixPath(digest[:2]) / f"{digest}.gguf")


def _validate_model_id(model_id: str) -> str:
    value = model_id.strip()
    if not MODEL_ID_RE.fullmatch(value):
        raise ModelManagerError("model id must use only letters, digits, '.', '_', or '-' and be at most 128 characters")
    return value


def _validate_capabilities(values: Iterable[str]) -> tuple[str, ...]:
    result: list[str] = []
    for raw in values:
        value = raw.strip().lower()
        if not CAPABILITY_RE.fullmatch(value):
            raise ModelManagerError(f"invalid capability: {raw}")
        if value not in result:
            result.append(value)
    if not result:
        result.append("chat")
    return tuple(result)


def _write_all(fd: int, data: bytes) -> None:
    view = memoryview(data)
    while view:
        written = os.write(fd, view)
        if written <= 0:
            raise OSError(errno.EIO, "short write while staging model")
        view = view[written:]


def _fsync_directory(path: pathlib.Path) -> None:
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_DIRECTORY", 0)
    fd = os.open(path, flags)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def _open_regular_readonly(path: pathlib.Path) -> int:
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    fd = os.open(path, flags)
    try:
        info = os.fstat(fd)
        if not stat.S_ISREG(info.st_mode):
            raise ModelManagerError(f"not a regular file: {path}")
        return fd
    except BaseException:
        os.close(fd)
        raise


def _hash_regular_file(path: pathlib.Path) -> tuple[str, int]:
    try:
        fd = _open_regular_readonly(path)
    except OSError as exc:
        raise ModelManagerError(f"cannot open regular file safely: {path}: {exc}") from exc
    digest = hashlib.sha256()
    total = 0
    try:
        while True:
            chunk = os.read(fd, 1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
            total += len(chunk)
    finally:
        os.close(fd)
    return digest.hexdigest(), total


class ModelManager:
    def __init__(self, model_root: pathlib.Path | None = None, runtime_dir: pathlib.Path | None = None) -> None:
        self.model_root = pathlib.Path(model_root or _default_model_root())
        self.runtime_dir = pathlib.Path(runtime_dir or _default_runtime_dir())
        self.manifest_root = self.model_root / "manifests"
        self.blob_root = self.model_root / "blobs" / "sha256"
        self.staging_root = self.model_root / ".staging"

    def _ensure_store(self) -> None:
        try:
            ensure_private_directory(self.model_root)
            ensure_private_directory(self.manifest_root)
            ensure_private_directory(self.model_root / "blobs")
            ensure_private_directory(self.blob_root)
            ensure_private_directory(self.staging_root)
            ensure_private_directory(self.runtime_dir)
        except ModelLeaseError as exc:
            raise ModelManagerError(str(exc)) from exc

    def _manifest_path(self, model_id: str) -> pathlib.Path:
        return self.manifest_root / f"{_validate_model_id(model_id)}.json"

    def _read_manifest_path(self, path: pathlib.Path) -> dict[str, Any]:
        try:
            info = path.lstat()
        except OSError as exc:
            raise ModelManagerError(f"cannot stat manifest {path.name}: {exc}") from exc
        if not stat.S_ISREG(info.st_mode):
            raise ModelManagerError(f"invalid manifest {path.name}: expected regular file")
        try:
            fd = _open_regular_readonly(path)
            try:
                with os.fdopen(fd, "r", encoding="utf-8", closefd=False) as handle:
                    raw = json.load(handle)
            finally:
                os.close(fd)
        except (OSError, json.JSONDecodeError) as exc:
            raise ModelManagerError(f"invalid manifest {path.name}: {exc}") from exc
        if not isinstance(raw, dict):
            raise ModelManagerError(f"invalid manifest {path.name}: expected object")
        required = ("id", "displayName", "sha256", "size", "blob", "license", "source", "ggufVersions")
        missing = [key for key in required if key not in raw]
        if missing:
            raise ModelManagerError(f"invalid manifest {path.name}: missing {', '.join(missing)}")
        model_id = _validate_model_id(str(raw["id"]))
        if path.stem != model_id:
            raise ModelManagerError(f"manifest filename does not match id: {path.name}")
        digest = str(raw["sha256"]).lower()
        expected = _canonical_blob_relative(digest)
        if str(raw["blob"]) != expected:
            raise ModelManagerError(f"manifest {path.name} does not use its canonical content address")
        try:
            size = int(raw["size"])
        except (TypeError, ValueError) as exc:
            raise ModelManagerError(f"manifest {path.name} has invalid size") from exc
        if size < 8:
            raise ModelManagerError(f"manifest {path.name} has invalid size")
        versions = raw["ggufVersions"]
        if not isinstance(versions, list) or not versions:
            raise ModelManagerError(f"manifest {path.name} has unsupported GGUF versions")
        try:
            parsed_versions = tuple(int(v) for v in versions)
        except (TypeError, ValueError) as exc:
            raise ModelManagerError(f"manifest {path.name} has invalid GGUF versions") from exc
        if any(version not in (2, 3) for version in parsed_versions):
            raise ModelManagerError(f"manifest {path.name} has unsupported GGUF versions")
        return raw

    def _read_manifest(self, model_id: str) -> dict[str, Any]:
        path = self._manifest_path(model_id)
        if not path.exists():
            raise ModelManagerError(f"model is not installed: {model_id}")
        return self._read_manifest_path(path)

    def _all_manifests(self) -> list[dict[str, Any]]:
        if not self.manifest_root.exists():
            return []
        return [self._read_manifest_path(path) for path in sorted(self.manifest_root.glob("*.json"))]

    def _blob_path_for_digest(self, digest: str) -> pathlib.Path:
        relative = _canonical_blob_relative(digest)
        candidate = (self.blob_root / relative).resolve()
        root = self.blob_root.resolve()
        if root not in candidate.parents:
            raise ModelManagerError("content-addressed blob path escaped model store")
        return candidate

    def _verify_blob(self, manifest: dict[str, Any]) -> pathlib.Path:
        digest = str(manifest["sha256"]).lower()
        path = self._blob_path_for_digest(digest)
        if not path.exists():
            raise ModelManagerError(f"model blob is missing: {manifest['id']}")
        if path.is_symlink() or not path.is_file():
            raise ModelManagerError(f"model blob is not a regular file: {manifest['id']}")
        actual_digest, actual_size = _hash_regular_file(path)
        if actual_size != int(manifest["size"]):
            raise ModelManagerError(f"model blob size mismatch: {manifest['id']}")
        if actual_digest != digest:
            raise ModelManagerError(f"model blob SHA-256 mismatch: {manifest['id']}")
        try:
            version = read_gguf_version(path)
        except GgufValidationError as exc:
            raise ModelManagerError(str(exc)) from exc
        if version not in tuple(int(v) for v in manifest["ggufVersions"]):
            raise ModelManagerError(f"model blob GGUF version is not allowed: {manifest['id']}")
        return path

    def _stage(self, source_path: pathlib.Path) -> tuple[pathlib.Path, str, int, int]:
        try:
            source_fd = _open_regular_readonly(source_path)
        except (OSError, ModelManagerError) as exc:
            raise ModelManagerError(f"cannot import source safely: {source_path}: {exc}") from exc
        try:
            temp_fd, temp_name = tempfile.mkstemp(prefix="import-", suffix=".gguf.tmp", dir=self.staging_root)
            temp_path = pathlib.Path(temp_name)
            try:
                os.fchmod(temp_fd, 0o600)
                digest = hashlib.sha256()
                total = 0
                while True:
                    chunk = os.read(source_fd, 1024 * 1024)
                    if not chunk:
                        break
                    digest.update(chunk)
                    total += len(chunk)
                    _write_all(temp_fd, chunk)
                os.fsync(temp_fd)
            except BaseException:
                temp_path.unlink(missing_ok=True)
                raise
            finally:
                os.close(temp_fd)
        finally:
            os.close(source_fd)
        try:
            version = read_gguf_version(temp_path)
        except (OSError, GgufValidationError) as exc:
            temp_path.unlink(missing_ok=True)
            raise ModelManagerError(f"source is not an accepted GGUF: {exc}") from exc
        return temp_path, digest.hexdigest(), total, version

    def _adopt_staged_blob(self, staged: pathlib.Path, digest: str, size: int, version: int) -> pathlib.Path:
        final_path = self._blob_path_for_digest(digest)
        ensure_private_directory(final_path.parent)
        if final_path.exists() or final_path.is_symlink():
            try:
                if final_path.is_symlink() or not final_path.is_file():
                    raise ModelManagerError("existing content-address path is not a regular file")
                actual_digest, actual_size = _hash_regular_file(final_path)
                try:
                    actual_version = read_gguf_version(final_path)
                except GgufValidationError as exc:
                    raise ModelManagerError("existing content-addressed blob has invalid GGUF metadata") from exc
                if actual_digest != digest or actual_size != size or actual_version != version:
                    raise ModelManagerError("existing content-addressed blob is corrupt")
            finally:
                staged.unlink(missing_ok=True)
            return final_path
        os.replace(staged, final_path)
        os.chmod(final_path, 0o600)
        _fsync_directory(final_path.parent)
        return final_path

    def _atomic_write_manifest(self, manifest: dict[str, Any], *, replace: bool) -> pathlib.Path:
        target = self._manifest_path(str(manifest["id"]))
        if target.exists() and not replace:
            raise ModelManagerError(f"model id is already installed: {manifest['id']}")
        fd, temp_name = tempfile.mkstemp(prefix=f".{manifest['id']}.", suffix=".json.tmp", dir=self.manifest_root)
        temp = pathlib.Path(temp_name)
        payload = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")
        try:
            try:
                os.fchmod(fd, 0o600)
                _write_all(fd, payload)
                os.fsync(fd)
            finally:
                os.close(fd)
            if target.exists() and not replace:
                raise ModelManagerError(f"model id is already installed: {manifest['id']}")
            os.replace(temp, target)
            os.chmod(target, 0o600)
            _fsync_directory(self.manifest_root)
        except BaseException:
            temp.unlink(missing_ok=True)
            raise
        return target

    def import_model(
        self,
        source_path: pathlib.Path,
        *,
        model_id: str,
        display_name: str,
        license_id: str,
        source_reference: str,
        replace: bool = False,
        capabilities: Iterable[str] = ("chat",),
        redistribution: str = "review-required",
    ) -> dict[str, Any]:
        model_id = _validate_model_id(model_id)
        display_name = display_name.strip()
        license_id = license_id.strip()
        source_reference = source_reference.strip()
        redistribution = redistribution.strip()
        if not display_name or not license_id or not source_reference or not redistribution:
            raise ModelManagerError("display name, license, source reference, and redistribution policy are required")
        capabilities_tuple = _validate_capabilities(capabilities)
        self._ensure_store()
        try:
            store_lease = acquire_store_lease(self.runtime_dir, blocking=False)
        except (ModelLeaseBusy, ModelLeaseError) as exc:
            raise ModelManagerError(f"cannot acquire model-store mutation lease: {exc}") from exc
        with store_lease:
            try:
                model_lease = acquire_model_lease(self.runtime_dir, model_id, exclusive=True, blocking=False)
            except (ModelLeaseBusy, ModelLeaseError) as exc:
                raise ModelManagerError(f"model is currently loaded or cannot be leased safely: {model_id}: {exc}") from exc
            with model_lease:
                target_manifest = self._manifest_path(model_id)
                if target_manifest.exists() and not replace:
                    raise ModelManagerError(f"model id is already installed: {model_id}")
                staged: pathlib.Path | None = None
                try:
                    staged, digest, size, version = self._stage(pathlib.Path(source_path))
                    relative = _canonical_blob_relative(digest)
                    self._adopt_staged_blob(staged, digest, size, version)
                    staged = None
                    manifest = {
                        "schemaVersion": 1,
                        "id": model_id,
                        "displayName": display_name,
                        "sha256": digest,
                        "size": size,
                        "blob": relative,
                        "license": license_id,
                        "source": source_reference,
                        "ggufVersions": [version],
                        "capabilities": list(capabilities_tuple),
                        "redistribution": redistribution,
                    }
                    self._atomic_write_manifest(manifest, replace=replace)
                    return manifest
                finally:
                    if staged is not None:
                        staged.unlink(missing_ok=True)

    def delete_model(self, model_id: str, *, purge_blob: bool = False) -> dict[str, Any]:
        model_id = _validate_model_id(model_id)
        self._ensure_store()
        try:
            store_lease = acquire_store_lease(self.runtime_dir, blocking=False)
        except (ModelLeaseBusy, ModelLeaseError) as exc:
            raise ModelManagerError(f"cannot acquire model-store mutation lease: {exc}") from exc
        with store_lease:
            try:
                model_lease = acquire_model_lease(self.runtime_dir, model_id, exclusive=True, blocking=False)
            except (ModelLeaseBusy, ModelLeaseError) as exc:
                raise ModelManagerError(f"model is currently loaded or cannot be leased safely: {model_id}: {exc}") from exc
            with model_lease:
                manifest = self._read_manifest(model_id)
                digest = str(manifest["sha256"]).lower()
                blob = self._blob_path_for_digest(digest)
                remaining_references = 0
                if purge_blob:
                    manifests = self._all_manifests()
                    remaining_references = sum(
                        1 for item in manifests
                        if str(item["id"]) != model_id and str(item["sha256"]).lower() == digest
                    )
                    if remaining_references == 0 and (blob.exists() or blob.is_symlink()):
                        info = blob.lstat()
                        if not stat.S_ISREG(info.st_mode):
                            raise ModelManagerError("refusing to purge a non-regular blob path")
                manifest_path = self._manifest_path(model_id)
                manifest_path.unlink()
                _fsync_directory(self.manifest_root)
                blob_removed = False
                if purge_blob and remaining_references == 0 and blob.exists():
                    blob.unlink()
                    _fsync_directory(blob.parent)
                    blob_removed = True
                return {
                    "id": model_id,
                    "sha256": digest,
                    "manifestRemoved": True,
                    "blobRemoved": blob_removed,
                    "remainingBlobReferences": remaining_references,
                }

    def list_models(self, *, verify: bool = False) -> list[dict[str, Any]]:
        manifests = self._all_manifests()
        result: list[dict[str, Any]] = []
        for manifest in manifests:
            item = dict(manifest)
            item["key"] = f"llamacpp:{manifest['id']}"
            item["installed"] = self._blob_path_for_digest(str(manifest["sha256"])).is_file()
            if verify:
                self._verify_blob(manifest)
                item["verified"] = True
            result.append(item)
        return result


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Manage Haven's local llama.cpp GGUF store without network access.")
    parser.add_argument("--model-root", type=pathlib.Path, default=None)
    parser.add_argument("--runtime-dir", type=pathlib.Path, default=None)
    subparsers = parser.add_subparsers(dest="command", required=True)

    list_parser = subparsers.add_parser("list", help="List installed model manifests")
    list_parser.add_argument("--verify", action="store_true", help="Re-hash and validate every listed GGUF")

    import_parser = subparsers.add_parser("import", help="Import an already-local GGUF file")
    import_parser.add_argument("file", type=pathlib.Path)
    import_parser.add_argument("--id", required=True, dest="model_id")
    import_parser.add_argument("--name", required=True, dest="display_name")
    import_parser.add_argument("--license", required=True, dest="license_id")
    import_parser.add_argument("--origin", required=True, dest="source_reference")
    import_parser.add_argument("--capability", action="append", dest="capabilities")
    import_parser.add_argument("--redistribution", default="review-required")
    import_parser.add_argument("--replace", action="store_true")

    delete_parser = subparsers.add_parser("delete", help="Delete a model manifest")
    delete_parser.add_argument("model_id")
    delete_parser.add_argument("--purge-blob", action="store_true", help="Also remove an unreferenced GGUF blob")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    manager = ModelManager(args.model_root, args.runtime_dir)
    try:
        if args.command == "list":
            value = {"models": manager.list_models(verify=args.verify)}
        elif args.command == "import":
            manifest = manager.import_model(
                args.file,
                model_id=args.model_id,
                display_name=args.display_name,
                license_id=args.license_id,
                source_reference=args.source_reference,
                replace=args.replace,
                capabilities=args.capabilities or ("chat",),
                redistribution=args.redistribution,
            )
            value = {"status": "imported", "model": manifest}
        else:
            value = {"status": "deleted", **manager.delete_model(args.model_id, purge_blob=args.purge_blob)}
    except (ModelManagerError, ModelLeaseError, OSError, ValueError) as exc:
        print(json.dumps({"error": "model_manager_error", "detail": str(exc)}, separators=(",", ":")), file=os.sys.stderr)
        return 2
    print(json.dumps(value, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
