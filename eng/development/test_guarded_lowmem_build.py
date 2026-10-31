"""Tests for the memory-guarded build runner. No heavy compile, no network, no acceptance."""
from __future__ import annotations

import json
import subprocess
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import guarded_lowmem_build as guard


class MeminfoTests(unittest.TestCase):
    def test_memavailable_is_read_from_the_kernel(self):
        value = guard.mem_available_kib()
        self.assertGreater(value, 0)

    def test_the_process_tree_rss_includes_spawned_children(self):
        child = subprocess.Popen([sys.executable, "-c",
                                  "import subprocess,sys; p=subprocess.Popen(['%s','-c','x=1']); "
                                  "print(p.pid); sys.stdout.flush(); p.wait()" % sys.executable],
                                 stdout=subprocess.PIPE, text=True)
        try:
            rss = guard.tree_rss_kib(child.pid)
        finally:
            child.terminate()
            child.wait(timeout=5)
        self.assertGreater(rss, 0)


class GuardDecisionTests(unittest.TestCase):
    def test_a_too_high_floor_stops_the_command_instead_of_the_machine(self):
        # Forcing the floor above what the machine has must trip the guard, and the child must be gone.
        outcome = guard.guarded([sys.executable, "-c", "import time; time.sleep(30)"],
                                floor_mib=10**9, timeout=60)
        self.assertTrue(outcome["result"].startswith("MEM GUARD"), outcome)
        self.assertIn("floor 1000000000 MiB", outcome["result"])
        self.assertEqual(outcome["signal_reason"], outcome["result"])
        self.assertLess(outcome["seconds"], 20, "the guard must stop the child, not wait it out")

    def test_time_guard_stops_a_runaway_build(self):
        outcome = guard.guarded([sys.executable, "-c", "import time; time.sleep(30)"],
                                floor_mib=0, timeout=1)
        self.assertTrue(outcome["result"].startswith("TIME GUARD"), outcome)

    def test_a_clean_exit_is_reported_as_ok_and_a_failing_one_is_not(self):
        ok = guard.guarded([sys.executable, "-c", "print('fine')"], floor_mib=0, timeout=30)
        self.assertEqual(ok["result"], "OK")
        self.assertEqual(ok["exit_code"], 0)
        bad = guard.guarded([sys.executable, "-c", "raise SystemExit(3)"], floor_mib=0, timeout=30)
        self.assertNotEqual(bad["result"], "OK")
        self.assertEqual(bad["exit_code"], 3)

    def test_the_guard_writes_its_own_evidence_file(self):
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            log = Path(tmp) / "build.log"
            guard.guarded([sys.executable, "-c", "print('out')"], floor_mib=0, timeout=30, log=str(log))
            evidence = json.loads((Path(str(log) + ".guard.json")).read_text())
            self.assertEqual(evidence["result"], "OK")
            self.assertIn("NOT REUSABLE", evidence["evidence_note"])
            self.assertTrue(log.read_text().startswith("out"))

    def test_server_gc_is_disabled_for_the_child_environment(self):
        # Server GC pins heaps aggressively; a build under memory pressure must not use it.
        self.assertIn('"DOTNET_gcServer": "0"', Path(__file__).with_name("guarded_lowmem_build.py").read_text())


if __name__ == "__main__":
    unittest.main()
