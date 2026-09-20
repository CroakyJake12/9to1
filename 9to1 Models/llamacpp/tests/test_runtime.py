from __future__ import annotations

import errno
import hashlib
import json
import pathlib
import socket
import struct
import sys
import tempfile
import unittest
from unittest import mock

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
REPOSITORY = RUNTIME.parents[1]
sys.path.insert(0, str(RUNTIME))

import broker  # noqa: E402
import gguf  # noqa: E402


class GgufTests(unittest.TestCase):
    def _write(self, root: pathlib.Path, version: int, magic: bytes = b"GGUF") -> pathlib.Path:
        path = root / "model.gguf"
        path.write_bytes(magic + struct.pack("<I", version) + b"payload")
        return path

    def test_accepts_v2_and_v3(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            self.assertEqual(2, gguf.read_gguf_version(self._write(root, 2)))
            self.assertEqual(3, gguf.read_gguf_version(self._write(root, 3)))

    def test_rejects_wrong_magic(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = self._write(pathlib.Path(tmp), 3, b"NOPE")
            with self.assertRaises(gguf.GgufValidationError):
                gguf.read_gguf_version(path)

    def test_rejects_future_version(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = self._write(pathlib.Path(tmp), 99)
            with self.assertRaises(gguf.GgufValidationError):
                gguf.read_gguf_version(path)


class ManifestTests(unittest.TestCase):
    def test_rejects_blob_escape_or_noncanonical_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp) / "blobs"
            root.mkdir()
            manifest = broker.ModelManifest("id", "Model", "0" * 64, 8, "../escape.gguf", "MIT", "local", (3,))
            with mock.patch.object(broker, "BLOB_ROOT", root):
                with self.assertRaises(broker.BrokerError):
                    _ = manifest.blob_path

    def test_rejects_noncanonical_path_inside_store(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp) / "blobs"
            root.mkdir()
            digest = "a" * 64
            manifest = broker.ModelManifest("id", "Model", digest, 8, "aa/model.gguf", "MIT", "local", (3,))
            with mock.patch.object(broker, "BLOB_ROOT", root):
                with self.assertRaises(broker.BrokerError):
                    _ = manifest.blob_path

    def test_verify_blob_checks_gguf_size_hash_and_content_address(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp) / "blobs"
            payload = b"GGUF" + struct.pack("<I", 3) + b"payload"
            digest = hashlib.sha256(payload).hexdigest()
            relative = broker.canonical_blob_relative(digest)
            target = root / relative
            target.parent.mkdir(parents=True)
            target.write_bytes(payload)
            manifest = broker.ModelManifest("id", "Model", digest, len(payload), relative, "MIT", "local", (3,))
            with mock.patch.object(broker, "BLOB_ROOT", root):
                self.assertEqual(target.resolve(), broker.verify_blob(manifest))

    def test_load_manifests_rejects_unapproved_gguf_version(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            manifest = {
                "id": "bad",
                "displayName": "Bad",
                "sha256": "0" * 64,
                "size": 8,
                "blob": broker.canonical_blob_relative("0" * 64),
                "license": "MIT",
                "source": "local",
                "ggufVersions": [4]
            }
            (root / "bad.json").write_text(json.dumps(manifest), encoding="utf-8")
            with mock.patch.object(broker, "MANIFEST_ROOT", root):
                with self.assertRaises(broker.BrokerError):
                    broker.load_manifests()

    def test_load_manifests_requires_filename_to_match_id(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            digest = "0" * 64
            manifest = {
                "id": "expected",
                "displayName": "Expected",
                "sha256": digest,
                "size": 8,
                "blob": broker.canonical_blob_relative(digest),
                "license": "MIT",
                "source": "local",
                "ggufVersions": [3]
            }
            (root / "different.json").write_text(json.dumps(manifest), encoding="utf-8")
            with mock.patch.object(broker, "MANIFEST_ROOT", root):
                with self.assertRaises(broker.BrokerError):
                    broker.load_manifests()


class RequestAndWorkerTests(unittest.TestCase):
    def test_request_id_accepts_safe_provider_style_token(self) -> None:
        self.assertEqual("chat-1:turn_2", broker.validate_request_id("chat-1:turn_2"))

    def test_request_id_rejects_header_injection(self) -> None:
        with self.assertRaises(broker.BrokerError):
            broker.validate_request_id("ok\r\nInjected: yes")

    def test_worker_args_disable_network_logs_ui_slots_and_bound_context(self) -> None:
        manifest = broker.ModelManifest("model-1", "Model", "0" * 64, 8, broker.canonical_blob_relative("0" * 64), "MIT", "local", (3,))
        args = broker.build_worker_args(manifest, pathlib.Path("/models/model.gguf"))
        self.assertIn("--offline", args)
        self.assertIn("--log-disable", args)
        self.assertIn("--no-ui", args)
        self.assertIn("--no-slots", args)
        self.assertEqual("model-1", args[args.index("--alias") + 1])
        self.assertEqual(str(broker.CONTEXT_LIMIT), args[args.index("--ctx-size") + 1])
        self.assertEqual("0", args[args.index("--cache-ram") + 1])

    def test_worker_environment_strips_configuration_injection(self) -> None:
        with mock.patch.dict("os.environ", {
            "LLAMA_ARG_TOOLS": "all",
            "GGML_SOMETHING": "unsafe",
            "HTTP_PROXY": "http://proxy.invalid",
            "LD_PRELOAD": "/tmp/inject.so",
            "KEEP_ME": "yes",
        }, clear=True):
            env = broker.worker_environment()
        self.assertNotIn("LLAMA_ARG_TOOLS", env)
        self.assertNotIn("GGML_SOMETHING", env)
        self.assertNotIn("HTTP_PROXY", env)
        self.assertNotIn("LD_PRELOAD", env)
        self.assertEqual("yes", env["KEEP_ME"])

    def test_active_request_cancel_aborts_connection(self) -> None:
        connection = mock.Mock()
        active = broker.ActiveRequest(connection)
        active.cancel()
        self.assertTrue(active.cancelled.is_set())
        connection.abort.assert_called_once_with()


class BoundaryTests(unittest.TestCase):
    def test_broker_can_bind_unix_socket_with_private_mode(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            socket_path = pathlib.Path(tmp) / "broker.sock"
            server = broker.ThreadingUnixServer(str(socket_path), broker.Handler)
            try:
                self.assertTrue(socket_path.exists())
                self.assertEqual(0o600, socket_path.stat().st_mode & 0o777)
            finally:
                server.server_close()
                socket_path.unlink(missing_ok=True)

    def test_broker_replaces_only_a_stale_socket(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            socket_path = pathlib.Path(tmp) / "broker.sock"
            stale = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            stale.bind(str(socket_path))
            stale.close()
            server = broker.ThreadingUnixServer(str(socket_path), broker.Handler)
            try:
                self.assertTrue(socket_path.exists())
            finally:
                server.server_close()
                socket_path.unlink(missing_ok=True)

    def test_broker_refuses_to_unlink_live_socket(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            socket_path = pathlib.Path(tmp) / "broker.sock"
            first = broker.ThreadingUnixServer(str(socket_path), broker.Handler)
            try:
                with self.assertRaises(OSError) as caught:
                    broker.ThreadingUnixServer(str(socket_path), broker.Handler)
                self.assertEqual(errno.EADDRINUSE, caught.exception.errno)
            finally:
                first.server_close()
                socket_path.unlink(missing_ok=True)

    def test_broker_refuses_to_replace_regular_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            socket_path = pathlib.Path(tmp) / "broker.sock"
            socket_path.write_text("do not delete", encoding="utf-8")
            with self.assertRaises(OSError) as caught:
                broker.ThreadingUnixServer(str(socket_path), broker.Handler)
            self.assertEqual(errno.EEXIST, caught.exception.errno)
            self.assertEqual("do not delete", socket_path.read_text(encoding="utf-8"))

    def test_systemd_unit_keeps_network_privilege_and_resource_boundary(self) -> None:
        unit = (REPOSITORY / "packaging/systemd/user/haven-inference-broker.service").read_text(encoding="utf-8")
        required = (
            "RuntimeDirectory=haven",
            "RuntimeDirectoryMode=0700",
            "NoNewPrivileges=yes",
            "ProtectSystem=strict",
            "RestrictAddressFamilies=AF_UNIX",
            "ReadOnlyPaths=-%h/.local/share/haven/models",
            "ReadWritePaths=%t/haven",
            "OOMPolicy=stop",
            "Environment=HAVEN_LLAMA_CONTEXT_LIMIT=8192",
        )
        for line in required:
            self.assertIn(line, unit)

    def test_provider_contract_preserves_ollama_legacy_names(self) -> None:
        contract = json.loads((RUNTIME / "provider-contract.json").read_text(encoding="utf-8"))
        self.assertEqual("llamacpp", contract["providerId"])
        self.assertEqual("llamacpp:<model-id>", contract["modelKeys"]["format"])
        self.assertEqual("ollama", contract["modelKeys"]["legacyUnqualifiedProvider"])
        self.assertFalse(contract["modelLifecycle"]["brokerCanWriteModelStore"])


if __name__ == "__main__":
    unittest.main()
