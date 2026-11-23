"""Engine allocation, settings, transport and production recovery boundaries."""

import contextlib
import http.server
import os
import tempfile
import threading
import unittest
from pathlib import Path
from unittest import mock

import engines
import v100_authority
import v100_recover
from engine_profiles import PRIMARY, EngineError


class ServiceFixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.manager = engines.EngineManager(self.root / "state")
        self.model_path = self.root / "fixture.gguf"
        self.model_path.write_bytes(b"GGUF" + b"0" * 32)
        self.spec = {
            "id": "fixture",
            "name": "Fixture",
            "gpu": "CPU",
            "engine": "Lightning / llama.cpp",
            "context": 2048,
            "max_context": 4096,
            "slots": 1,
            "quantization": "Q4_K_M",
            "path": str(self.model_path),
            "bytes": self.model_path.stat().st_size,
        }

    def test_registered_artifact_respects_canonical_catalogue_deletion(self):
        record = {
            **self.spec,
            "backend": "llama.cpp",
            "format": "gguf",
            "context_length": 4096,
        }
        catalog = mock.Mock()
        catalog.installed.return_value = [record]
        self.manager.use_catalog(catalog)
        with mock.patch.object(engines, "_unit", return_value={}):
            self.assertEqual(
                self.manager.register_gguf("fixture", self.model_path, record)["id"],
                "fixture",
            )
            catalog.installed.return_value = []
            with self.assertRaisesRegex(EngineError, "Unknown installed model"):
                self.manager.status("fixture")
        self.assertFalse(self.manager.registry.exists())

    def test_arbitrary_executables_and_gpu_changes_are_rejected_without_load(self):
        with mock.patch.object(self.manager, "_spec", return_value=self.spec):
            for overrides in (
                {"exe": "/bin/true"},
                {"gpu": "RTX 5080"},
                {"context": 5000},
                {"threads": True},
                {"quantization": "Q8_0"},
                {"profile": ["untrusted"]},
            ):
                with self.subTest(overrides=overrides), self.assertRaises(EngineError):
                    self.manager.validate_settings("fixture", overrides)
            result = self.manager.validate_settings(
                "fixture", {"context": 1024, "threads": 2, "profile": "experimental"}
            )
            self.assertEqual(result["threads"], 2)
            self.assertEqual(result["context"], 1024)

    def test_native_reserve_and_previous_context_are_protected(self):
        spec = {
            **self.spec,
            "id": PRIMARY,
            "gpu": "RTX 5080",
            "engine": "Thunder / Strata",
            "context": 524288,
        }
        with mock.patch.object(self.manager, "_spec", return_value=spec):
            for overrides in (
                {"vram_reserve_mib": 0},
                {"context": 2048},
                {"threads": 1},
            ):
                with self.subTest(overrides=overrides), self.assertRaises(EngineError):
                    self.manager.validate_settings(PRIMARY, overrides)

    def test_unload_rejects_unrelated_process_even_when_unit_name_matches(self):
        observed = (
            {"state": "loading"},
            None,
            {"ActiveState": "active", "MainPID": "123"},
        )
        with (
            mock.patch.object(
                self.manager, "_change", return_value=contextlib.nullcontext()
            ),
            mock.patch.object(self.manager, "_spec", return_value=self.spec),
            mock.patch.object(self.manager, "_observe", return_value=observed),
            mock.patch.object(self.manager, "_owns", return_value=False),
            mock.patch.object(engines, "_members", return_value={123}),
            mock.patch.object(engines, "_command") as command,
        ):
            with self.assertRaisesRegex(EngineError, "ownership"):
                self.manager.unload("fixture")
            command.assert_not_called()

    def test_release_counts_only_owned_gpu_pids_not_display_or_another_model(self):
        spec = {**self.spec, "id": PRIMARY}
        live = (
            {"state": "loaded", "ownership": "dulche"},
            (18500, "not-used", "dulche", 123),
            {"ActiveState": "active"},
        )
        with (
            mock.patch.object(
                self.manager, "_change", return_value=contextlib.nullcontext()
            ),
            mock.patch.object(self.manager, "_spec", return_value=spec),
            mock.patch.object(self.manager, "_observe", return_value=live),
            mock.patch.object(self.manager, "_owns", return_value=True),
            mock.patch.object(
                self.manager, "status", return_value={"state": "unloaded"}
            ),
            mock.patch.object(
                engines, "_unit", return_value={"ActiveState": "inactive"}
            ),
            mock.patch.object(engines, "_members", return_value={123, 124}),
            mock.patch.object(
                engines,
                "_gpu_allocations",
                side_effect=[
                    [(124, "gpu", 7000), (9, "gpu", 9000)],
                    [(9, "gpu", 11000)],
                ],
            ),
            mock.patch.object(engines, "_command") as command,
        ):
            result = self.manager.unload(PRIMARY)
            self.assertEqual(result["released_vram_mib"], 7000)
            self.assertTrue(result["release_verified"])
            self.assertEqual(
                command.call_args.args[0],
                ["systemctl", "--user", "stop", self.manager._name(PRIMARY)],
            )

    def test_cancel_interrupts_wait_for_headers_and_a_blocked_sse_read(self):
        for with_headers in (False, True):
            with self.subTest(with_headers=with_headers):
                started = threading.Event()
                hang = threading.Event()
                cancel = threading.Event()
                done = threading.Event()
                errors = []

                class Slow(http.server.BaseHTTPRequestHandler):
                    def do_POST(self):
                        self.rfile.read(int(self.headers["Content-Length"]))
                        if with_headers:
                            self.send_response(200)
                            self.send_header("Content-Type", "text/event-stream")
                            self.end_headers()
                            self.wfile.flush()
                        started.set()
                        hang.wait(5)

                    def log_message(self, *_):
                        pass

                fake = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Slow)
                thread = threading.Thread(target=fake.serve_forever, daemon=True)
                thread.start()
                port = fake.server_address[1]
                self.assertTrue(engines._listener(os.getpid(), port))
                self.assertFalse(engines._listener(0, port))

                def consume():
                    try:
                        with self.manager.generate(
                            "fixture",
                            {"messages": []},
                            stream=True,
                            cancel_event=cancel,
                        ) as reply:
                            reply.readline()
                    except OSError as exc:
                        errors.append(exc)
                    finally:
                        done.set()

                try:
                    with (
                        mock.patch.object(
                            self.manager, "_spec", return_value=self.spec
                        ),
                        mock.patch.object(
                            self.manager,
                            "_observe",
                            return_value=(
                                {"state": "loaded"},
                                (port, "key", "dulche", 123),
                                {},
                            ),
                        ),
                    ):
                        consumer = threading.Thread(target=consume, daemon=True)
                        consumer.start()
                        self.assertTrue(started.wait(2))
                        cancel.set()
                        self.assertTrue(
                            done.wait(1),
                            "request transport remained blocked after cancel",
                        )
                finally:
                    hang.set()
                    fake.shutdown()
                    fake.server_close()

    def test_missing_authority_does_not_start_guest(self):
        with (
            mock.patch.object(v100_recover, "authority", side_effect=FileNotFoundError),
            mock.patch.object(v100_recover, "boot_guest") as boot,
            mock.patch.object(v100_recover, "emergency_detach") as detach,
        ):
            with self.assertRaises(FileNotFoundError):
                v100_recover.up(self.root)
            boot.assert_not_called()
            detach.assert_not_called()

    def test_v100_cold_recovery_failure_before_ownership_does_not_detach(self):
        with (
            mock.patch.object(v100_recover, "authority", return_value={"ok": True}),
            mock.patch.object(v100_recover, "assets"),
            mock.patch.object(v100_recover, "owned_vm", return_value=False),
            mock.patch.object(v100_recover, "active", return_value=False),
            mock.patch.object(v100_recover, "other_guard", return_value=False),
            mock.patch.object(
                v100_recover, "boot_guest", side_effect=RuntimeError("port occupied")
            ),
            mock.patch.object(v100_recover, "emergency_detach") as detach,
        ):
            with self.assertRaisesRegex(RuntimeError, "port occupied"):
                v100_recover.up(self.root)
            detach.assert_not_called()

    def test_mismatching_driver_never_modified_by_privileged_authority(self):
        with (
            mock.patch.object(
                v100_authority, "identity", return_value=self.root / "group"
            ),
            mock.patch.object(v100_authority, "driver", return_value="other-driver"),
            mock.patch.object(v100_authority.subprocess, "run") as run,
        ):
            with self.assertRaisesRegex(RuntimeError, "unexpected driver"):
                v100_authority.attach(1000, 1000)
            run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
