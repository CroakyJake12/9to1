from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.manifest import AdmittedAsset, MissingAsset, render_manifest  # noqa: E402
from bundle.packager import pack_bundle  # noqa: E402
from bundle.spec import parse_spec  # noqa: E402
from bundle.tests.helpers import make_elf, make_gguf, minimal_spec, sha256_of, write_fixture  # noqa: E402
from bundle.verifier import verify_installed  # noqa: E402


def _ready_manifest(host_arch: str = "x86_64") -> dict:
    gguf = make_gguf()
    binary = make_elf(62)
    admitted = [
        AdmittedAsset(
            logical="smollm-test",
            kind="model.gguf",
            engine="llamacpp",
            platform={"os": "linux", "arch": host_arch},
            runtime_format="gguf",
            source="https://example.invalid/model-card",
            license_id="Apache-2.0",
            redistribution="review-required",
            install_path="assets/models/smollm.gguf",
            digest=sha256_of(gguf),
            size=len(gguf),
            capabilities=("chat",),
            format_meta={"format": "gguf", "ggufVersion": 3},
        ),
        AdmittedAsset(
            logical="llama-server-cpu",
            kind="runtime.binary",
            engine="llamacpp",
            platform={"os": "linux", "arch": host_arch},
            runtime_format="elf",
            source="https://github.com/ggml-org/llama.cpp",
            license_id="MIT",
            redistribution="review-required",
            install_path="assets/engines/llamacpp/llama-server",
            digest=sha256_of(binary),
            size=len(binary),
            capabilities=(),
            format_meta={"format": "elf", "machine": 62},
        ),
    ]
    return render_manifest(
        bundle_id="dulche-bundle-test",
        bundle_version="0.0.1-test",
        host={"os": "linux", "arch": host_arch},
        admitted=admitted,
        missing=[],
    )


class VerifierTests(unittest.TestCase):
    def _install(self, manifest: dict, *, mutate: str | None = None) -> tuple[pathlib.Path, bytes]:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = pathlib.Path(tmp.name)
        gguf = make_gguf()
        binary = make_elf(62)
        write_fixture(root, "assets/models/smollm.gguf", gguf)
        write_fixture(root, "assets/engines/llamacpp/llama-server", binary)
        if mutate == "missing":
            (root / "assets/models/smollm.gguf").unlink()
        elif mutate == "tamper":
            (root / "assets/models/smollm.gguf").write_bytes(make_gguf(payload=b"tampered"))
        elif mutate == "unexpected":
            write_fixture(root, "assets/models/extra.bin", b"unexpected")
        return root, gguf + binary

    def test_ready_install_verifies(self) -> None:
        manifest = _ready_manifest()
        root, _ = self._install(manifest)
        result = verify_installed(root, manifest)
        self.assertTrue(result.ok())
        self.assertEqual("ready", result.state)
        self.assertEqual([], result.unexpected_paths)
        self.assertTrue(all(s.state == "present" for s in result.asset_statuses))

    def test_missing_asset_fails(self) -> None:
        manifest = _ready_manifest()
        root, _ = self._install(manifest, mutate="missing")
        result = verify_installed(root, manifest)
        self.assertFalse(result.ok())
        self.assertEqual("invalid", result.state)
        self.assertTrue(any(s.state == "missing" for s in result.asset_statuses))

    def test_tampered_asset_fails_hash(self) -> None:
        manifest = _ready_manifest()
        root, _ = self._install(manifest, mutate="tamper")
        result = verify_installed(root, manifest)
        self.assertFalse(result.ok())
        self.assertTrue(any(s.state == "hash-mismatch" for s in result.asset_statuses))

    def test_symlink_escaped_install_fails(self) -> None:
        manifest = _ready_manifest()
        root, _ = self._install(manifest)
        outside = root / "outside.gguf"
        outside.write_bytes(make_gguf())
        target = root / "assets/models/smollm.gguf"
        target.unlink()
        target.symlink_to(outside)
        result = verify_installed(root, manifest)
        self.assertFalse(result.ok())
        # Either the symlink is rejected as non-regular/unsafe.
        self.assertNotEqual("ready", result.state)

    def test_strict_closure_rejects_unexpected_file(self) -> None:
        manifest = _ready_manifest()
        root, _ = self._install(manifest, mutate="unexpected")
        result = verify_installed(root, manifest, strict_closure=True)
        self.assertFalse(result.ok())
        self.assertTrue(any("extra.bin" in path for path in result.unexpected_paths))

    def test_non_strict_closure_allows_unexpected_file(self) -> None:
        manifest = _ready_manifest()
        root, _ = self._install(manifest, mutate="unexpected")
        result = verify_installed(root, manifest, strict_closure=False)
        self.assertTrue(result.ok())

    def test_architecture_incompatible_installed_binary_fails(self) -> None:
        # An authentic aarch64 binary installed on an x86_64 host closure is an
        # architecture conflict even when the hash/size/format all match.
        binary = make_elf(183)  # aarch64
        gguf = make_gguf()
        admitted = [
            AdmittedAsset(
                logical="smollm-test",
                kind="model.gguf",
                engine="llamacpp",
                platform={"os": "linux", "arch": "x86_64"},
                runtime_format="gguf",
                source="https://example.invalid/model-card",
                license_id="Apache-2.0",
                redistribution="review-required",
                install_path="assets/models/smollm.gguf",
                digest=sha256_of(gguf),
                size=len(gguf),
                capabilities=("chat",),
                format_meta={"format": "gguf", "ggufVersion": 3},
            ),
            AdmittedAsset(
                logical="native-worker",
                kind="runtime.binary",
                engine="strata",
                platform={"os": "linux", "arch": "aarch64"},
                runtime_format="elf",
                source="https://example.invalid/strata",
                license_id="Apache-2.0",
                redistribution="review-required",
                install_path="assets/engines/strata/native-worker",
                digest=sha256_of(binary),
                size=len(binary),
                capabilities=(),
                format_meta={"format": "elf", "machine": 183},
            ),
        ]
        manifest = render_manifest(
            bundle_id="dulche-bundle-test",
            bundle_version="0.0.1-test",
            host={"os": "linux", "arch": "x86_64"},
            admitted=admitted,
            missing=[],
        )
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = pathlib.Path(tmp.name)
        write_fixture(root, "assets/models/smollm.gguf", gguf)
        write_fixture(root, "assets/engines/strata/native-worker", binary)
        result = verify_installed(root, manifest)
        self.assertFalse(result.ok())
        self.assertTrue(any(s.state == "architecture-incompatible" for s in result.asset_statuses))


class ClosureReadinessTests(unittest.TestCase):
    """Required-missing / optional-missing / malformed closure readiness."""

    def _new_root(self) -> pathlib.Path:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        return pathlib.Path(tmp.name)

    def test_required_missing_asset_pack_install_verify_not_ready(self) -> None:
        # Reproduces acceptance probe row 1: one supplied TESTONLY GGUF plus a
        # second REQUIRED missing model. pack_bundle => invalid + required
        # missingAssets. Installing only the admitted file must NOT verify ready.
        root = self._new_root()
        assets = root / "assets"
        assets.mkdir(parents=True)
        write_fixture(assets, "fixtures/smollm.gguf", make_gguf())
        spec = parse_spec(
            minimal_spec(
                assets=[
                    {
                        "logical": "smollm-test",
                        "kind": "model.gguf",
                        "engine": "llamacpp",
                        "platform": {"os": "linux", "arch": "x86_64"},
                        "runtimeFormat": "gguf",
                        "source": "https://example.invalid/model-card",
                        "license": "Apache-2.0",
                        "path": "fixtures/smollm.gguf",
                        "required": True,
                    },
                    {
                        "logical": "missing-required",
                        "kind": "model.gguf",
                        "engine": "llamacpp",
                        "platform": {"os": "linux", "arch": "x86_64"},
                        "runtimeFormat": "gguf",
                        "source": "https://example.invalid/missing",
                        "license": "Apache-2.0",
                        "path": "fixtures/missing.gguf",
                        "required": True,
                    },
                ]
            )
        )
        packed = pack_bundle(spec, assets)
        self.assertEqual("invalid", packed.state)
        self.assertTrue(any(m.logical == "missing-required" and m.required for m in packed.missing))
        manifest = packed.manifest
        self.assertEqual("invalid", manifest["state"])

        # Install ONLY the admitted existing file at its canonical install path.
        installed = root / "installed"
        for asset in manifest["assets"]:
            dest = installed / asset["installPath"]
            dest.parent.mkdir(parents=True, exist_ok=True)
            write_fixture(dest.parent, dest.name, make_gguf())
        result = verify_installed(installed, manifest)
        self.assertNotEqual("ready", result.state)
        self.assertEqual("invalid", result.state)

    def test_optional_missing_asset_verifies_degraded(self) -> None:
        root = self._new_root()
        gguf = make_gguf()
        admitted = [
            AdmittedAsset(
                logical="smollm-test",
                kind="model.gguf",
                engine="llamacpp",
                platform={"os": "linux", "arch": "x86_64"},
                runtime_format="gguf",
                source="https://example.invalid/model-card",
                license_id="Apache-2.0",
                redistribution="review-required",
                install_path="assets/models/smollm.gguf",
                digest=sha256_of(gguf),
                size=len(gguf),
                capabilities=("chat",),
                format_meta={"format": "gguf", "ggufVersion": 3},
            )
        ]
        missing = [MissingAsset(logical="optional-extra", kind="model.gguf", engine="llamacpp", required=False)]
        manifest = render_manifest(
            bundle_id="dulche-bundle-test",
            bundle_version="0.0.1-test",
            host={"os": "linux", "arch": "x86_64"},
            admitted=admitted,
            missing=missing,
        )
        self.assertEqual("degraded", manifest["state"])
        installed = root / "installed"
        write_fixture(installed, "assets/models/smollm.gguf", gguf)
        result = verify_installed(installed, manifest)
        self.assertEqual("degraded", result.state)

    def test_malformed_missing_record_verifies_invalid(self) -> None:
        root = self._new_root()
        gguf = make_gguf()
        admitted = [
            AdmittedAsset(
                logical="smollm-test",
                kind="model.gguf",
                engine="llamacpp",
                platform={"os": "linux", "arch": "x86_64"},
                runtime_format="gguf",
                source="https://example.invalid/model-card",
                license_id="Apache-2.0",
                redistribution="review-required",
                install_path="assets/models/smollm.gguf",
                digest=sha256_of(gguf),
                size=len(gguf),
                capabilities=("chat",),
                format_meta={"format": "gguf", "ggufVersion": 3},
            )
        ]
        manifest = render_manifest(
            bundle_id="dulche-bundle-test",
            bundle_version="0.0.1-test",
            host={"os": "linux", "arch": "x86_64"},
            admitted=admitted,
            missing=[],
        )
        # Malformed missing record (missing required fields / non-bool required).
        manifest["missingAssets"] = [{"logical": "x", "kind": "model.gguf"}]
        installed = root / "installed"
        write_fixture(installed, "assets/models/smollm.gguf", gguf)
        result = verify_installed(installed, manifest)
        self.assertEqual("invalid", result.state)

    def test_contradictory_advertised_ready_with_required_missing_is_invalid(self) -> None:
        root = self._new_root()
        gguf = make_gguf()
        admitted = [
            AdmittedAsset(
                logical="smollm-test",
                kind="model.gguf",
                engine="llamacpp",
                platform={"os": "linux", "arch": "x86_64"},
                runtime_format="gguf",
                source="https://example.invalid/model-card",
                license_id="Apache-2.0",
                redistribution="review-required",
                install_path="assets/models/smollm.gguf",
                digest=sha256_of(gguf),
                size=len(gguf),
                capabilities=("chat",),
                format_meta={"format": "gguf", "ggufVersion": 3},
            )
        ]
        missing = [MissingAsset(logical="missing-required", kind="model.gguf", engine="llamacpp", required=True)]
        manifest = render_manifest(
            bundle_id="dulche-bundle-test",
            bundle_version="0.0.1-test",
            host={"os": "linux", "arch": "x86_64"},
            admitted=admitted,
            missing=missing,
        )
        # Rewrite the state field to "ready" to simulate tampering; the verifier
        # must recompute from the required closure and refuse to trust it.
        manifest["state"] = "ready"
        installed = root / "installed"
        write_fixture(installed, "assets/models/smollm.gguf", gguf)
        result = verify_installed(installed, manifest)
        self.assertEqual("invalid", result.state)


if __name__ == "__main__":
    unittest.main()
