from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.manifest import parse_manifest  # noqa: E402
from bundle.packager import pack_bundle  # noqa: E402
from bundle.spec import parse_spec  # noqa: E402
from bundle.tests.helpers import (  # noqa: E402
    make_elf,
    make_gguf,
    minimal_spec,
    write_fixture,
)


class PackagerTests(unittest.TestCase):
    def _set_up_root(self) -> tuple[pathlib.Path, pathlib.Path]:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = pathlib.Path(tmp.name)
        assets = root / "assets"
        assets.mkdir(parents=True)
        return root, assets

    def test_ready_bundle_emits_manifest_and_deterministic_receipt(self) -> None:
        root, assets = self._set_up_root()
        gguf = make_gguf(3)
        write_fixture(assets, "fixtures/smollm.gguf", gguf)
        binary = make_elf(62)
        write_fixture(assets, "fixtures/llama-server", binary)
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
                    },
                    {
                        "logical": "llama-server-cpu",
                        "kind": "runtime.binary",
                        "engine": "llamacpp",
                        "platform": {"os": "linux", "arch": "x86_64"},
                        "runtimeFormat": "elf",
                        "source": "https://github.com/ggml-org/llama.cpp",
                        "license": "MIT",
                        "path": "fixtures/llama-server",
                        "required": True,
                    },
                ]
            )
        )
        result = pack_bundle(spec, assets)
        self.assertEqual("ready", result.state)
        self.assertEqual(2, len(result.admitted))
        manifest = result.manifest
        parse_manifest(manifest)
        self.assertEqual("ready", manifest["state"])
        self.assertEqual(False, manifest["admission"]["networkUsed"])
        # Receipt carries all admitted digests and is byte-reproducible.
        self.assertEqual(2, result.receipt["assetCount"])
        self.assertEqual(0, result.receipt["missingRequiredCount"])
        self.assertNotIn("generatedAtUtc", manifest)

    def test_missing_required_asset_is_invalid(self) -> None:
        root, assets = self._set_up_root()
        spec = parse_spec(minimal_spec())
        result = pack_bundle(spec, assets)
        self.assertEqual("invalid", result.state)
        self.assertEqual(1, len(result.missing))
        self.assertTrue(result.missing[0].required)

    def test_missing_optional_asset_is_degraded(self) -> None:
        root, assets = self._set_up_root()
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
                        "logical": "extra-optional",
                        "kind": "model.gguf",
                        "engine": "llamacpp",
                        "platform": {"os": "linux", "arch": "x86_64"},
                        "runtimeFormat": "gguf",
                        "source": "https://example.invalid/other",
                        "license": "MIT",
                        "path": "fixtures/missing.gguf",
                        "required": False,
                    },
                ]
            )
        )
        result = pack_bundle(spec, assets)
        self.assertEqual("degraded", result.state)

    def test_pinned_size_mismatch_is_rejected(self) -> None:
        root, assets = self._set_up_root()
        gguf = make_gguf()
        write_fixture(assets, "fixtures/smollm.gguf", gguf)
        spec_dict = minimal_spec()
        spec_dict["assets"][0]["expectedSize"] = len(gguf) + 1
        spec = parse_spec(spec_dict)
        result = pack_bundle(spec, assets)
        self.assertEqual("invalid", result.state)
        self.assertTrue(any("size-mismatch" in r["reason"] for r in result.rejected))

    def test_pinned_sha256_mismatch_is_rejected(self) -> None:
        root, assets = self._set_up_root()
        write_fixture(assets, "fixtures/smollm.gguf", make_gguf())
        spec_dict = minimal_spec()
        spec_dict["assets"][0]["expectedSha256"] = "e" * 64
        spec = parse_spec(spec_dict)
        result = pack_bundle(spec, assets)
        self.assertEqual("invalid", result.state)
        self.assertTrue(any("sha256-mismatch" in r["reason"] for r in result.rejected))

    def test_declared_format_mismatch_is_rejected(self) -> None:
        root, assets = self._set_up_root()
        write_fixture(assets, "fixtures/smollm.gguf", b"not-a-gguf")
        spec = parse_spec(minimal_spec())
        result = pack_bundle(spec, assets)
        self.assertEqual("invalid", result.state)
        self.assertTrue(any("runtime-format" in r["reason"] for r in result.rejected))

    def test_architecture_incompatible_binary_is_rejected(self) -> None:
        root, assets = self._set_up_root()
        write_fixture(assets, "fixtures/smollm.gguf", make_gguf())
        write_fixture(assets, "fixtures/arm-binary", make_elf(183))  # aarch64
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
                    },
                    {
                        "logical": "native-worker",
                        "kind": "runtime.binary",
                        "engine": "strata",
                        "platform": {"os": "linux", "arch": "aarch64"},
                        "runtimeFormat": "elf",
                        "source": "https://example.invalid/strata",
                        "license": "Apache-2.0",
                        "path": "fixtures/arm-binary",
                    },
                ]
            )
        )
        result = pack_bundle(spec, assets)
        self.assertEqual("invalid", result.state)
        self.assertTrue(any("architecture-incompatible" in r["reason"] for r in result.rejected))

    def test_zero_hash_placeholder_pin_is_rejected_at_spec(self) -> None:
        spec_dict = minimal_spec()
        spec_dict["assets"][0]["expectedSha256"] = "0" * 64
        with self.assertRaises(ValueError):
            parse_spec(spec_dict)

    def test_symlink_escape_is_rejected(self) -> None:
        root, assets = self._set_up_root()
        outside = root / "outside.bin"
        outside.write_bytes(make_gguf())
        link = assets / "fixtures" / "smollm.gguf"
        link.parent.mkdir(parents=True, exist_ok=True)
        link.symlink_to(outside)
        spec = parse_spec(minimal_spec())
        result = pack_bundle(spec, assets)
        self.assertEqual("invalid", result.state)
        self.assertTrue(any("path-unsafe" in r["reason"] for r in result.rejected))

    def test_conflicting_install_path_with_different_content_is_rejected(self) -> None:
        root, assets = self._set_up_root()
        write_fixture(assets, "fixtures/a.gguf", make_gguf(payload=b"aaaa"))
        write_fixture(assets, "fixtures/b.gguf", make_gguf(payload=b"bbbb"))
        spec = parse_spec(
            minimal_spec(
                assets=[
                    {
                        "logical": "asset-a",
                        "kind": "model.gguf",
                        "engine": "llamacpp",
                        "platform": {"os": "linux", "arch": "x86_64"},
                        "runtimeFormat": "gguf",
                        "source": "https://example.invalid/a",
                        "license": "MIT",
                        "path": "fixtures/a.gguf",
                        "installPath": "assets/models/shared.gguf",
                    },
                    {
                        "logical": "asset-b",
                        "kind": "model.gguf",
                        "engine": "llamacpp",
                        "platform": {"os": "linux", "arch": "x86_64"},
                        "runtimeFormat": "gguf",
                        "source": "https://example.invalid/b",
                        "license": "MIT",
                        "path": "fixtures/b.gguf",
                        "installPath": "assets/models/shared.gguf",
                    },
                ]
            )
        )
        result = pack_bundle(spec, assets)
        self.assertEqual("invalid", result.state)
        self.assertTrue(any("conflicting-install-path" in r["reason"] for r in result.rejected))


if __name__ == "__main__":
    unittest.main()
