"""Real small ELF process fixtures; these checks are not native Haven acceptance."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import threading
import unittest
from unittest import mock

import live_preview as preview
from workflow import atomic, process_identity


@unittest.skipUnless(os.name == "posix" and Path("/proc").is_dir() and shutil.which("cc"), "Linux and cc required")
class LivePreviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.compiled = tempfile.TemporaryDirectory(prefix="live-preview-elf-")
        source = Path(cls.compiled.name) / "fixture.c"
        source.write_text(r'''
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
int main(void) {
    char file[4096]; snprintf(file, sizeof(file), "%s/close.request", getenv("HAVEN_DATA_DIR"));
    for (int i = 0; i < 2000; i++) {
        FILE *input = fopen(file, "r");
        if (input) { int code = -1; fscanf(input, "%d", &code); fclose(input); if (code >= 0) return code; }
        usleep(10000);
    }
    return 9;
}
''')
        cls.elf = Path(cls.compiled.name) / "fixture"
        subprocess.run(["cc", str(source), "-o", str(cls.elf)], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.compiled.cleanup()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="live-preview-test-")
        self.root = Path(self.temp.name)
        self.state = self.root / "state"
        self.data = self.root / "profile"
        self.data.mkdir(mode=0o700)
        preview.init(self.state, self.data, "9to1", "Explicit dedicated fixture profile, not an installed app observation")
        (self.data / "user-document.txt").write_text("preserve me")
        self.manifest = self.runtime("a" * 40)

    def tearDown(self):
        (self.data / "close.request").write_text("0")
        run = preview.current(self.state)
        if run:
            directory = preview.run_directory(self.state, run)
            deadline = time.monotonic() + 5
            while (directory / "launch.json").exists() and not (directory / "exit.json").exists() and time.monotonic() < deadline:
                time.sleep(0.02)
        for guardian in preview._guardians:
            guardian.wait(timeout=5)
        preview._guardians.clear()
        for folder, _, names in os.walk(self.root):
            os.chmod(folder, 0o700)
            for name in names:
                os.chmod(Path(folder) / name, 0o600)
        self.temp.cleanup()

    def runtime(self, revision):
        runtime = self.root / ("runtime-" + revision[:4])
        runtime.mkdir()
        shutil.copy2(self.elf, runtime / "Haven")
        for name in ("Haven.dll", "Haven.deps.json", "Haven.runtimeconfig.json"):
            (runtime / name).write_text("fixture only; not a .NET payload")
            os.chmod(runtime / name, 0o444)
        os.chmod(runtime / "Haven", 0o555)
        os.chmod(runtime, 0o555)
        receipt = self.root / ("build-" + revision[:4] + ".json")
        atomic(receipt, {"result": "PASS", "exit_code": 0, "admission": {"result": "PASS"}, "source_revision": revision,
                         "command": ["/fixture/dotnet", "publish", "9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj", "--output", str(runtime)]})
        manifest = self.root / ("manifest-" + revision[:4] + ".json")
        preview.prepare(runtime, receipt, preview.hashed(receipt), revision, manifest)
        return manifest

    def wait_exit(self):
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            state = preview.status(self.state)
            if state["state"] in ("EXITED_CLEANLY", "EXIT_FAILED"):
                return state
            time.sleep(0.02)
        self.fail("Fixture guardian did not retain exit")

    def test_start_status_records_actual_runtime_and_profile_then_clean_exit(self):
        state = preview.start(self.state, self.manifest)
        self.assertEqual("RUNNING", state["state"])
        self.assertEqual("a" * 40, state["source_revision"])
        self.assertEqual(str(self.data), state["data_directory"])
        self.assertEqual(preview.hashed(self.root / "runtime-aaaa" / "Haven.dll"), state["runtime_hashes"]["Haven.dll"])
        with self.assertRaisesRegex(preview.Refusal, "not exited cleanly"):
            preview.start(self.state, self.manifest)
        (self.data / "close.request").write_text("0")
        self.assertEqual(0, self.wait_exit()["exit_code"])
        self.assertEqual("preserve me", (self.data / "user-document.txt").read_text())

    def test_reload_waits_for_real_exit_zero_and_keeps_profile_and_history(self):
        first = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        def normal_close(launch, title):
            preview.owned_process(launch)
            self.assertEqual("9to1", title)
            (self.data / "close.request").write_text("0")
        # Remove the fixture close request after its original exit, before new activation.
        original = preview.start_locked
        def activate(root, manifest):
            self.assertEqual("EXITED_CLEANLY", preview.status(root)["state"])
            (self.data / "close.request").unlink()
            return original(root, manifest)
        with mock.patch.object(preview, "start_locked", side_effect=activate):
            second = preview.reload(self.state, following, timeout=3, close=normal_close)
        self.assertEqual("b" * 40, second["source_revision"])
        self.assertNotEqual(first["process"]["pid"], second["process"]["pid"])
        self.assertTrue((self.state / "runs" / first["run_id"] / "exit.json").exists())
        self.assertEqual("preserve me", (self.data / "user-document.txt").read_text())

    def test_veto_or_timeout_keeps_original_process_and_runtime(self):
        original = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        with self.assertRaisesRegex(preview.Refusal, "vetoed, timed out or failed"):
            preview.reload(self.state, following, timeout=0.1, close=lambda *_: None)
        self.assertEqual(original["run_id"], preview.status(self.state)["run_id"])
        self.assertEqual("RUNNING", preview.status(self.state)["state"])

    def test_nonzero_exit_never_activates_new_runtime(self):
        first = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        with self.assertRaisesRegex(preview.Refusal, "vetoed, timed out or failed"):
            preview.reload(self.state, following, timeout=3,
                           close=lambda *_: (self.data / "close.request").write_text("7"))
        state = preview.status(self.state)
        self.assertEqual("EXIT_FAILED", state["state"])
        self.assertEqual(7, state["exit_code"])
        self.assertEqual(first["run_id"], state["run_id"])

    def failed_run_review(self, first, following):
        exit_path = self.state / "runs" / first["run_id"] / "exit.json"
        launch_path = exit_path.parent / "launch.json"
        launch = preview.read_json(launch_path)
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            guardian = process_identity(launch["guardian"]["pid"])
            if not guardian or guardian["state"] == "Z":
                break
            time.sleep(.01)
        review = self.root / "local-failure-review.json"
        atomic(review, {"schema": "9to1.live-preview-failed-run-review/v1", "result": "RETRY_SAFE",
                        "failed_run_id": first["run_id"],
                        "failed_launch": {"path": str(launch_path), "sha256": preview.hashed(launch_path)},
                        "failed_exit": {"path": str(exit_path), "sha256": preview.hashed(exit_path)},
                        "failed_process": launch["process"], "data_directory": str(self.data),
                        "candidate_manifest_sha256": preview.hashed(following),
                        "diagnosis": "Actual fixture child intentionally exited7; no native Haven diagnosis",
                        "reviewer": "local fixture reviewer", "reason": "Explicit fixture-only review; no native Haven evidence"})
        return review

    def test_explicit_failed_run_recovery_retains_nonzero_exit_and_profile_not_a_normal_reload(self):
        first = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        (self.data / "close.request").write_text("7")
        self.assertEqual(7, self.wait_exit()["exit_code"])
        exit_path = self.state / "runs" / first["run_id"] / "exit.json"
        failed_bytes = exit_path.read_bytes()
        with self.assertRaises(preview.Refusal):
            preview.start(self.state, following)
        with self.assertRaises(preview.Refusal):
            preview.reload(self.state, following)
        review = self.failed_run_review(first, following)
        (self.data / "close.request").unlink()  # Fixture request only, not application or launcher state.
        second = preview.recover_failed(self.state, following, review, preview.hashed(review))
        self.assertNotEqual(first["run_id"], second["run_id"])
        self.assertEqual("REVIEWED_FAILED_EXIT_RESTART_NOT_NORMAL_RELOAD", second["activation"])
        self.assertFalse(preview.read_json(Path(second["recovery_receipt"]))["normal_close_verified"])
        successor = preview.read_json(preview.run_directory(self.state, preview.current(self.state)) / "launch.json")
        self.assertEqual(str(second["recovery_receipt"]), successor["recovery"]["path"])
        with self.assertRaises(preview.Refusal):
            preview.recover_failed(self.state, following, review, preview.hashed(review))
        self.assertEqual(failed_bytes, exit_path.read_bytes())
        self.assertEqual("preserve me", (self.data / "user-document.txt").read_text())

    def test_failed_run_review_rejects_changed_wrong_or_stale_receipts_without_activation(self):
        first = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        (self.data / "close.request").write_text("7")
        self.wait_exit()
        review = self.failed_run_review(first, following)
        before = preview.read_json(review)
        for patch in ({"failed_run_id": str(preview.uuid.uuid4())},
                      {"candidate_manifest_sha256": "0" * 64}, {"reason": ""},
                      {"failed_process": {**before["failed_process"], "start_ticks": 0}},
                      {"data_directory": "/wrong/profile"}, {"diagnosis": ""},
                      {"failed_launch": {"path": "/wrong/launch", "sha256": "0" * 64}},
                      {"failed_exit": {"path": "/wrong/exit", "sha256": "0" * 64}}):
            atomic(review, {**before, **patch})
            with self.assertRaises(preview.Refusal):
                preview.recover_failed(self.state, following, review, preview.hashed(review))
            self.assertEqual(first["run_id"], preview.current(self.state)["run_id"])
        atomic(review, before)
        with self.assertRaisesRegex(preview.Refusal, "receipt changed"):
            preview.recover_failed(self.state, following, review, "0" * 64)
        self.assertFalse((self.state / "recoveries").exists())

    def test_failed_run_recovery_refuses_live_original_even_if_an_exit_record_appears(self):
        first = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        exit_path = self.state / "runs" / first["run_id"] / "exit.json"
        atomic(exit_path, {"run_id": first["run_id"], "process": first["process"], "exit_code": 7})
        review = self.root / "not-read-because-original-is-live.json"
        try:
            with self.assertRaisesRegex(preview.Refusal, "original process is still live"):
                preview.recover_failed(self.state, following, review, "0" * 64)
            self.assertEqual(first["run_id"], preview.current(self.state)["run_id"])
        finally:
            exit_path.unlink()  # Remove only this test's deliberately forged fixture record.

    def test_failed_run_recovery_rejects_reused_pid_live_guardian_and_remaining_group(self):
        first = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        (self.data / "close.request").write_text("7")
        self.wait_exit()
        review = self.failed_run_review(first, following)
        launch = preview.read_json(self.state / "runs" / first["run_id"] / "launch.json")
        original_identity = process_identity
        for field in ("process", "guardian"):
            expected = launch[field]
            def changed(pid):
                return {**expected, "start_ticks": expected["start_ticks"] + 1, "state": "R"} if pid == expected["pid"] else original_identity(pid)
            with mock.patch.object(preview, "process_identity", side_effect=changed):
                with self.assertRaises(preview.Refusal):
                    preview.recover_failed(self.state, following, review, preview.hashed(review))
        with mock.patch.object(preview, "remaining_group", return_value=[{"fixture": "unsettled descendant"}]):
            with self.assertRaisesRegex(preview.Refusal, "process-group members"):
                preview.recover_failed(self.state, following, review, preview.hashed(review))
        self.assertEqual(first["run_id"], preview.current(self.state)["run_id"])
        self.assertFalse((self.state / "recoveries").exists())

    def test_failed_run_review_is_one_shot_even_when_activation_does_not_start(self):
        first = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        (self.data / "close.request").write_text("7")
        self.wait_exit()
        review = self.failed_run_review(first, following)
        with mock.patch.object(preview, "start_locked", side_effect=RuntimeError("fixture before activation")):
            with self.assertRaisesRegex(RuntimeError, "before activation"):
                preview.recover_failed(self.state, following, review, preview.hashed(review))
        with self.assertRaisesRegex(preview.Refusal, "already consumed"):
            preview.recover_failed(self.state, following, review, preview.hashed(review))
        self.assertEqual(first["run_id"], preview.current(self.state)["run_id"])
        audit = preview.read_json(next((self.state / "recoveries").glob("*.json")))
        self.assertEqual("REVIEW_RECORDED_BEFORE_ACTIVATION", audit["stage"])
        self.assertFalse(audit["normal_close_verified"])

    def test_changed_pid_identity_is_refused_before_any_close_request(self):
        preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        directory = preview.run_directory(self.state, preview.current(self.state))
        launch = preview.read_json(directory / "launch.json")
        launch["process"]["start_ticks"] += 1
        atomic(directory / "launch.json", launch)
        close = mock.Mock()
        with self.assertRaisesRegex(preview.Refusal, "identity is absent or changed"):
            preview.reload(self.state, following, close=close)
        close.assert_not_called()

    def test_bad_candidate_is_refused_before_disturbing_running_copy(self):
        first = preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        file = self.root / "runtime-bbbb" / "Haven.dll"
        os.chmod(file, 0o644)
        file.write_text("different")
        os.chmod(file, 0o444)
        close = mock.Mock()
        with self.assertRaisesRegex(preview.Refusal, "runtime bytes"):
            preview.reload(self.state, following, close=close)
        close.assert_not_called()
        self.assertEqual(first["run_id"], preview.status(self.state)["run_id"])

    def test_unrelated_live_pid_is_never_closed(self):
        preview.start(self.state, self.manifest)
        following = self.runtime("b" * 40)
        directory = preview.run_directory(self.state, preview.current(self.state))
        launch = preview.read_json(directory / "launch.json")
        with subprocess.Popen(["sleep", "0.3"]) as peer:
            launch["process"] = process_identity(peer.pid)
            atomic(directory / "launch.json", launch)
            close = mock.Mock()
            with self.assertRaisesRegex(preview.Refusal, "executable identity changed"):
                preview.reload(self.state, following, close=close)
            close.assert_not_called()
            self.assertIsNone(peer.poll())

    def test_writable_runtime_and_extra_unapproved_files_are_refused(self):
        runtime = self.root / "runtime-aaaa"
        os.chmod(runtime, 0o755)
        with self.assertRaisesRegex(preview.Refusal, "immutable"):
            preview.validate_manifest(self.manifest)
        (runtime / "unexpected.txt").write_text("not in the approved build")
        os.chmod(runtime / "unexpected.txt", 0o444)
        os.chmod(runtime, 0o555)
        with self.assertRaisesRegex(preview.Refusal, "file set changed"):
            preview.validate_manifest(self.manifest)

    def test_failed_build_or_mismatched_output_receipt_cannot_be_approved(self):
        receipt = self.root / "build-aaaa.json"
        value = preview.read_json(receipt)
        for change, message in (({"result": "FAIL_OR_BLOCKED"}, "successful admitted"),
                                ({"command": ["/fixture/dotnet", "build", "Haven.Desktop.csproj", "-o", "/wrong/output"]}, "exact runtime")):
            atomic(receipt, {**value, **change})
            with self.assertRaisesRegex(preview.Refusal, message):
                preview.prepare(self.root / "runtime-aaaa", receipt, preview.hashed(receipt), "a" * 40, self.root / "not-created.json")
        self.assertFalse((self.root / "not-created.json").exists())

    def test_configuration_and_approval_files_are_never_overwritten(self):
        before = (self.state / "config.json").read_bytes()
        with self.assertRaises(FileExistsError):
            preview.init(self.state, self.data, "different title", "Retained fixture profile")
        self.assertEqual(before, (self.state / "config.json").read_bytes())
        with self.assertRaises(FileExistsError):
            receipt = self.root / "build-aaaa.json"
            preview.prepare(self.root / "runtime-aaaa", receipt, preview.hashed(receipt), "a" * 40, self.manifest)

    def test_x11_close_uses_one_exact_pid_title_normal_window_and_never_signals(self):
        preview.start(self.state, self.manifest)
        launch = preview.read_json(preview.run_directory(self.state, preview.current(self.state)) / "launch.json")
        launch["display"] = ":fixture"
        pid = launch["process"]["pid"]
        good = f'_NET_WM_PID(CARDINAL) = {pid}\n_NET_WM_NAME(UTF8_STRING) = "9to1"\n_NET_WM_WINDOW_TYPE(ATOM) = _NET_WM_WINDOW_TYPE_NORMAL\nWM_TRANSIENT_FOR: not found.\n'
        wrong = good.replace(f"= {pid}", f"= {pid + 1}")
        calls = []
        def output(arguments):
            calls.append(arguments)
            if "-root" in arguments:
                return "_NET_CLIENT_LIST(WINDOW): window id # 0x1, 0x2"
            return wrong if "0x1" in arguments else good
        with mock.patch.dict(os.environ, DISPLAY=":fixture"), mock.patch.object(shutil, "which", side_effect=lambda name: "/fixture/" + name), mock.patch.object(preview, "command_output", side_effect=output):
            self.assertEqual("0x2", preview.request_normal_close(launch, "9to1"))
        self.assertEqual(["/fixture/wmctrl", "-ic", "0x2"], calls[-1])
        self.assertFalse(preview.window_matches(good.replace("not found.", "window id # 0x9"), pid, "9to1"))
        self.assertFalse(preview.window_matches(good, pid, "helper"))
        self.assertFalse(preview.window_matches(wrong, pid, "9to1"))

    def test_duplicate_guardian_cannot_launch_another_child(self):
        first = preview.start(self.state, self.manifest)
        with self.assertRaisesRegex(preview.Refusal, "Another start/reload"):
            preview.supervise(self.state, first["run_id"])
        self.assertEqual(first["process"], preview.status(self.state)["process"])

    def test_retained_source_manifest_pin_is_checked(self):
        receipt = self.root / "build-aaaa.json"
        value = preview.read_json(receipt)
        source_manifest = self.root / "source-manifest.json"
        atomic(source_manifest, {"fixture": "Pin fixture, not actual compiler execution evidence"})
        value["admission"]["source_manifest"] = {"path": str(source_manifest), "bytes": source_manifest.stat().st_size,
                                                  "sha256": preview.hashed(source_manifest)}
        atomic(receipt, value)
        preview.build_proof(receipt, preview.hashed(receipt), "a" * 40, self.root / "runtime-aaaa")
        source_manifest.write_text("changed")
        with self.assertRaisesRegex(preview.Refusal, "source-manifest pin changed"):
            preview.build_proof(receipt, preview.hashed(receipt), "a" * 40, self.root / "runtime-aaaa")

    def test_profile_observation_is_explicit_and_symlink_parent_is_refused(self):
        self.assertEqual("OPERATOR_RECORDED_NOT_APP_CONFIRMED", preview.config(self.state)["profile_confirmation"])
        alias = self.root / "alias"
        alias.symlink_to(self.root, target_is_directory=True)
        with self.assertRaisesRegex(preview.Refusal, "canonical owner profile"):
            preview.init(self.root / "other-state", alias / "profile", "9to1", "Fixture observation")

    def test_launch_receipt_write_failure_still_retains_and_reaps_original_child(self):
        run_id = str(preview.uuid.uuid4())
        directory = self.state / "runs" / run_id
        directory.mkdir(mode=0o700, parents=True)
        atomic(self.state / "current.json", {"run_id": run_id, "manifest": str(self.manifest), "manifest_sha256": preview.hashed(self.manifest)})
        original_create = preview.create_json
        errors = []
        def failing_create(path, value):
            if path.name == "launch.json":
                raise OSError("fixture launch-receipt write failure")
            return original_create(path, value)
        def guard():
            try:
                preview.supervise(self.state, run_id)
            except OSError as error:
                errors.append(str(error))
        with mock.patch.object(preview, "create_json", side_effect=failing_create):
            thread = threading.Thread(target=guard)
            thread.start()
            try:
                deadline = time.monotonic() + 3
                while not (directory / "error.json").exists() and time.monotonic() < deadline:
                    time.sleep(0.02)
                state = preview.status(self.state)
                self.assertEqual("LAUNCH_ERROR_REQUIRES_REVIEW", state["state"])
                self.assertIn("launch-receipt write failure", state["error"]["error"])
                self.assertIsNotNone(process_identity(state["error"]["process"]["pid"]))
                self.assertTrue(thread.is_alive(), "Guardian must keep waiting for its original child")
            finally:
                (self.data / "close.request").write_text("0")
                thread.join(timeout=5)
        self.assertFalse(thread.is_alive())
        self.assertEqual(0, preview.read_json(directory / "exit.json")["exit_code"])
        self.assertEqual(["fixture launch-receipt write failure"], errors)


if __name__ == "__main__":
    unittest.main()
