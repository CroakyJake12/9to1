"""Deterministic control-plane tests. Hardware and native UI receipts are recorded separately."""

import contextlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest

from controller import Controller
from state import Fault


def wait(fn, seconds=5):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        result = fn()
        if result:
            return result
        time.sleep(0.015)
    raise AssertionError("Timed out waiting for transition")


class Engines:
    def __init__(self):
        self.loaded = True
        self.stop_count = 0
        self.load_count = 0
        self.release = threading.Event()
        self.entered = threading.Event()
        self.release.set()
        self.config = {}

    def models(self):
        return [
            {
                "id": "q",
                "name": "Test engine",
                "state": "loaded" if self.loaded else "unloaded",
                "capacity": 1,
                "gpu": "RTX 5080",
                "settings": {"context": 32768},
            }
        ]

    def resources(self):
        return {
            "gpus": [
                {
                    "name": "NVIDIA GeForce RTX 5080",
                    "free_mib": 512 if self.loaded else 14500,
                }
            ]
        }

    def validate_settings(self, mid, settings):
        if set(settings) - {"context"}:
            raise ValueError("invalid option")

    def load(self, mid, settings):
        self.validate_settings(mid, settings)
        self.config = settings
        self.load_count += 1
        self.loaded = True

    def unload(self, mid):
        self.stop_count += 1
        self.loaded = False

    @contextlib.contextmanager
    def generate(self, mid, payload, *, stream, cancel_event):
        self.entered.set()
        while not self.release.wait(0.01):
            if cancel_event.is_set():
                raise Fault(409, "cancel acknowledged")
        if cancel_event.is_set():
            raise Fault(409, "cancel acknowledged")
        if stream:
            yield io.BytesIO(
                b'data: {"choices":[{"delta":{"content":"hi"},"finish_reason":"stop"}]}\n\ndata: [DONE]\n\n'
            )
        else:
            yield io.BytesIO(
                b'{"choices":[{"message":{"content":"hi"},"finish_reason":"stop"}]}'
            )


class Catalog:
    def installed(self):
        return []

    def close(self):
        pass


class ControlTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.eng = Engines()
        self.ctx = Controller(
            self.tmp.name, self.eng, Catalog(), interval=1000, cooldown=0.05
        )
        self.pid = os.getpid()
        self.ctx.frontend("connect", {"session_id": "app"}, self.pid)
        self.body = {
            "objective": "verify a task",
            "model": "q",
            "steps": [
                {
                    "type": "generate",
                    "payload": {"messages": [{"role": "user", "content": "hi"}]},
                }
            ],
        }

    def tearDown(self):
        self.eng.release.set()
        self.ctx.close()
        self.ctx.state.db.close()
        self.tmp.cleanup()

    def close_gui(self):
        return self.ctx.frontend(
            "close", {"session_id": "app", "reason": "user"}, self.pid
        )

    def ready(self, run):
        return self.ctx.state.status(run, full=True)

    def test_always_and_manual_suspend_survive_disconnect(self):
        self.ctx.set_policy("always")
        self.ctx.change("q", "suspend")
        wait(lambda: self.ctx.model("q")["state"] == "suspended")
        self.assertFalse(self.eng.loaded)
        with self.assertRaises(Fault):
            self.ctx.change("q", "load")
        self.close_gui()
        self.assertEqual("ready", self.ctx.phase)
        self.ctx.frontend("connect", {"session_id": "again"}, self.pid)
        self.assertTrue(self.ctx.model("q")["manual_suspended"])
        self.ctx.change("q", "resume")
        wait(lambda: self.ctx.model("q")["state"] == "loaded")
        self.assertTrue(self.eng.loaded)

    def test_resources_explicit_release_and_manual_override(self):
        self.ctx.set_policy("resources")
        self.close_gui()
        lease = self.ctx.resource_request({"gpu": "RTX 5080", "free_mib": 7168})
        self.ctx._poll()
        wait(lambda: self.ctx.model("q")["state"] == "waiting_for_resources")
        self.assertGreaterEqual(self.eng.resources()["gpus"][0]["free_mib"], 7168)
        self.ctx.resource_request({"id": lease["id"], "release": True})
        self.ctx._poll()
        self.assertFalse(self.eng.loaded)
        time.sleep(0.06)
        self.ctx._poll()
        wait(lambda: self.ctx.model("q")["state"] == "loaded")
        self.ctx.change("q", "suspend")
        wait(lambda: self.ctx.model("q")["state"] == "suspended")
        self.ctx.resource_request(
            {"gpu": "RTX 5080", "free_mib": 7168, "id": "another"}
        )
        self.ctx._poll()
        self.ctx.resource_request({"release": True, "id": "another"})
        self.ctx._poll()
        time.sleep(0.06)
        self.ctx._poll()
        self.assertFalse(self.eng.loaded)
        self.assertEqual(1, self.eng.load_count)

    def test_while_active_drains_previously_authorised_work(self):
        self.ctx.set_policy("active")
        self.eng.release.clear()
        a = self.ctx.jobs.create(self.body, "1")
        b = self.ctx.jobs.create(self.body, "2")
        self.assertTrue(self.eng.entered.wait(2))
        self.close_gui()
        self.assertEqual("draining", self.ctx.phase)
        with self.assertRaises(Fault):
            self.ctx.jobs.create(self.body, "3")
        self.eng.release.set()
        wait(self.ctx.shutdown_requested.is_set)
        self.assertEqual(
            ["completed", "completed"], [self.ready(r)["state"] for r in (a, b)]
        )
        self.assertFalse(self.eng.loaded)

    def test_never_checkpoints_and_unloads(self):
        self.ctx.set_policy("never")
        self.eng.release.clear()
        r = self.ctx.jobs.create(self.body)
        self.assertTrue(self.eng.entered.wait(2))
        self.close_gui()
        wait(self.ctx.shutdown_requested.is_set)
        self.assertEqual("checkpointed", self.ready(r)["state"])
        self.assertFalse(self.eng.loaded)

    def test_development_lease_bypasses_all_exit_modes(self):
        for policy in ["never", "resources", "active", "always"]:
            self.ctx.frontend("connect", {"session_id": "app"}, self.pid)
            self.ctx.set_policy(policy)
            lease = self.ctx.frontend(
                "lease",
                {"pid": self.pid, "ttl_seconds": 2, "reason": "restart"},
                self.pid,
            )
            self.close_gui()
            self.assertEqual("ready", self.ctx.phase)
            self.assertNotIn(lease["lease_id"], self.ctx.leases)
            self.assertTrue(self.eng.loaded)
        self.ctx.frontend("connect", {"session_id": "app"}, self.pid)
        with self.assertRaises(Fault):
            self.ctx.frontend(
                "close", {"session_id": "app", "reason": "restart"}, self.pid
            )

    def test_selected_and_global_emergency_stop(self):
        self.eng.release.clear()
        a = self.ctx.jobs.create(self.body, "a")
        b = self.ctx.jobs.create(self.body, "b")
        self.assertTrue(self.eng.entered.wait(2))
        self.ctx.jobs.stop(run=a)
        self.assertEqual("stopped", self.ready(a)["state"])
        self.ctx.jobs.stop()
        self.eng.release.set()
        wait(lambda: not self.ctx.jobs.active)
        with self.assertRaises(Fault):
            self.ctx.jobs.create(self.body, "c")
        with self.assertRaises(Fault):
            self.ctx.jobs.resume(b)
        self.ctx.state.set("stop_all", False)
        self.assertEqual("stopped", self.ready(b)["state"])
        self.ctx.jobs.resume(b)
        wait(lambda: self.ready(b)["state"] == "completed")
        self.assertEqual(0, self.ready(a)["step"])
        self.assertEqual(1, self.ready(b)["step"])

    def test_idempotent_owner_recipe_has_one_receipt(self):
        trace = Path(self.tmp.name) / "trace"
        command = {
            "cwd": self.tmp.name,
            "argv": [
                sys.executable,
                "-c",
                'import sys;f=open(sys.argv[1],"a");f.write("x");f.close()',
                str(trace),
            ],
        }
        f = Path(self.tmp.name) / "approved-recipes.json"
        f.write_text(json.dumps({"test": command}))
        f.chmod(0o600)
        req = self.ctx.jobs.prepare(
            {
                **self.body,
                "steps": [{"type": "recipe", "name": "test"}, *self.body["steps"]],
            }
        )
        run = self.ctx.jobs.create(req, "ticket")
        wait(lambda: self.ready(run)["state"] == "completed")
        self.assertEqual(run, self.ctx.jobs.create(req, "ticket"))
        self.assertEqual("x", trace.read_text())
        self.assertEqual(2, len(self.ready(run)["completed_steps"]))
        with self.assertRaises(Fault):
            self.ctx.jobs.create(self.body, "ticket")

    def test_profile_protection_and_switch(self):
        res = self.ctx.edit_profile(
            "q", {"name": "Long", "settings": {"context": 65536}}
        )
        self.ctx.edit_profile("q", {}, res["id"], "select")
        wait(lambda: self.ctx.model("q")["state"] == "loaded")
        self.assertEqual({"context": 65536}, self.eng.config)
        with self.assertRaises(Fault):
            self.ctx.edit_profile("q", {"name": "overwrite"}, "current", "rename")
        self.ctx.edit_profile("q", {"name": "New name"}, res["id"], "rename")
        self.assertIn("New name", [p["name"] for p in self.ctx.profiles("q")])

    def test_stream_recovery_and_explicit_cancel(self):
        req = json.loads(json.dumps(self.body))
        req["steps"][0]["payload"]["stream"] = True
        run = self.ctx.jobs.create(req)
        wait(lambda: self.ready(run)["state"] == "completed")
        self.assertEqual(
            "hi",
            self.ready(run)["completed_steps"][0]["choices"][0]["message"]["content"],
        )
        self.assertTrue(any(e["kind"] == "chunks" for e in self.ctx.state.events(run)))


if __name__ == "__main__":
    unittest.main()
