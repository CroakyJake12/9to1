"""Explicit opt-in integration test: synthetic readings, dummy services, no GPU load."""
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest

import native_guard as native
import thermal_guard as thermal
from engine_profiles import INKLING
from engines import EngineManager, _members, _unit


@unittest.skipUnless(os.environ.get("DULCHE_GUARD_SYSTEMD_TESTS") == "1", "requires explicit isolated-systemd test opt-in")
class OwnedStopIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="dulche-native-guard-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.manager = EngineManager(self.root / "state")
        self.unit = self.manager._name(INKLING)
        self.addCleanup(lambda: subprocess.run(["systemctl", "--user", "stop", self.unit], capture_output=True, timeout=10))
        self.foreign = subprocess.Popen(["/usr/bin/sleep", "120"])
        self.addCleanup(self.stop_foreign)
        self.v100 = _unit("astra-v100-inference.service").get("MainPID")

    def stop_foreign(self):
        if self.foreign.poll() is None:
            self.foreign.terminate()
        self.foreign.wait(timeout=3)

    def start(self, argv):
        subprocess.run(["systemd-run", "--user", "--collect", "--unit=" + self.unit,
                        "-p", "Type=exec", "-p", "KillMode=control-group", "-p", "Restart=no",
                        "-p", "WorkingDirectory=" + str(self.root), *argv],
                       capture_output=True, text=True, check=True, timeout=10)
        for _ in range(50):
            state = _unit(self.unit)
            if int(state.get("MainPID") or 0) > 1:
                return
            time.sleep(.05)
        self.fail("Dummy unit failed to start")

    def test_synthetic80_stops_owned_parent_and_child_only(self):
        package = self.root / "serve"
        package.mkdir()
        (package / "__init__.py").write_text("")
        (package / "server.py").write_text("import subprocess,sys,time\np=subprocess.Popen([sys.executable,'-c','import time;time.sleep(120)'])\nwhile True: time.sleep(1)\n")
        config = self.manager.root / "inkling-owned.json"
        config.write_text('{"model_name":"' + INKLING + '"}')
        python = Path.home() / ".local/share/strata/components/control/0.1.68/python/bin/python3.12"
        self.start([str(python), "-m", "serve.server", "--config", str(config)])
        deadline = time.monotonic() + 3
        while len(members := _members(_unit(self.unit))) < 2 and time.monotonic() < deadline:
            time.sleep(.05)
        self.assertGreaterEqual(len(members), 2)
        called = []
        def action(level, *_):
            if level == "stop":
                called.extend(native.control("stop", self.manager))
        guard = thermal.DeviceGuard(thermal.Rule(dict(id="rtx5080", stop_c=80, jump_c=None, rise_c_per_min=None,
                min_host_available_mib=8192)), None, action, lambda _: None)
        guard.evaluate({"temp": 80}, time.time(), 8192)
        self.assertEqual(called, [self.unit])
        self.assertFalse(_members(_unit(self.unit)))
        self.assertEqual(native.control("verify", self.manager), [])
        self.assertIsNone(self.foreign.poll())
        self.assertEqual(_unit("astra-v100-inference.service").get("MainPID"), self.v100)

    def test_wrong_process_identity_is_not_killed(self):
        self.start(["/usr/bin/sleep", "120"])
        with self.assertRaises(RuntimeError):
            native.control("stop", self.manager)
        self.assertTrue(_members(_unit(self.unit)))
        self.assertIsNone(self.foreign.poll())


if __name__ == "__main__":
    unittest.main()
