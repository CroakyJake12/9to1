#!/usr/bin/env python3
"""Real isolated Linux processes; no auth identities/network/production policy."""
import importlib.util
import subprocess
import tempfile
import time
import unittest
from pathlib import Path
spec = importlib.util.spec_from_file_location('custodian', Path(__file__).with_name('linux-fixture-custodian.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class Controls(unittest.TestCase):
    def setUp(self):
        self.c = module.Custody()
        self.owner = dict(self.c.owner)
        self.child = subprocess.Popen(['python3', '-c', 'import time; time.sleep(60)'], start_new_session=True, stdin=subprocess.DEVNULL)
        self.c.observe()
        self.saved = {pid: dict(record['row']) for pid, record in self.c.records.items()}
    def tearDown(self):
        self.c.owner = self.owner
        for pid, row in self.saved.items():
            self.c.records[pid]['row'] = row
        self.c.drain()
        self.child.returncode = 0
    def test_normal_strict_reaping(self):
        r = self.c.drain()
        self.assertTrue(r['strictReaped'] and r['originalsDisappeared'])
        self.assertIsNone(module.stat(self.child.pid))
        self.assertTrue(r['signals'])
    def test_changed_birth_no_signal(self):
        self.c.records[self.child.pid]['row']['birth'] += 1
        with self.assertRaisesRegex(RuntimeError, 'identity changed'):
            self.c.signal_owned(module.signal.SIGKILL)
        self.assertEqual([], self.c.signals)
        self.assertIsNotNone(module.stat(self.child.pid))
    def test_creator_identity_loss_no_signal(self):
        self.c.owner['birth'] += 1
        with self.assertRaisesRegex(RuntimeError, 'creator identity lost'):
            self.c.signal_owned(module.signal.SIGKILL)
        self.assertEqual([], self.c.signals)
    def test_changed_session_no_signal(self):
        self.c.records[self.child.pid]['row']['sid'] += 1
        with self.assertRaisesRegex(RuntimeError, 'identity changed'):
            self.c.signal_owned(module.signal.SIGKILL)
        self.assertEqual([], self.c.signals)
    def test_timeout_no_receipt(self):
        original = self.c.signal_owned
        self.c.signal_owned = lambda sig: None # Controlled inability to terminate a live test child.
        try:
            with self.assertRaisesRegex(RuntimeError, 'drain timeout'):
                self.c.drain(timeout=.1)
            self.assertIsNotNone(module.stat(self.child.pid))
        finally:
            self.c.signal_owned = original
    def test_escaped_session_descendant_adopted_and_reaped(self):
        with tempfile.TemporaryDirectory() as folder:
            marker = Path(folder) / 'descendant'
            script = 'import os,time; pid=os.fork();\nif pid==0:\n os.setsid(); open(' + repr(str(marker)) + ',"w").write(str(os.getpid())); time.sleep(60)\nelse:\n time.sleep(60)'
            parent = subprocess.Popen(['python3', '-c', script], start_new_session=True, stdin=subprocess.DEVNULL)
            for _ in range(100):
                if marker.exists(): break
                time.sleep(.01)
            self.assertTrue(marker.exists())
            descendant = int(marker.read_text())
            self.c.observe()
            self.assertNotEqual(self.c.records[parent.pid]['row']['sid'], self.c.records[descendant]['row']['sid'])
            receipt = self.c.drain()
            self.assertIsNone(module.stat(parent.pid))
            self.assertIsNone(module.stat(descendant))
            self.assertIn(descendant, [item['pid'] for item in receipt['reaped']])
            parent.returncode = 0
if __name__ == '__main__': unittest.main(verbosity=2)
