from contextlib import ExitStack
import json
import os
import subprocess
import sys
from tempfile import TemporaryFile
from time import monotonic
import unittest
from unittest.mock import patch
from uuid import uuid4

from .artifacts import CustodyError
from .v8_joint_jobs import _close_owned_child, _hello, _launch
from .v8_owned_endpoints import _V100


class V8JointJobsTests(unittest.TestCase):
    def test_owned_popen_hello_waits_for_explicit_parent_proceed(self):
        code = """
import os, sys
from custody.v8_joint_jobs import identify_owned_worker
from custody.v8_joint_registration import RankProcessReceipt
r = RankProcessReceipt(sys.argv[1], 0, 'fixture-guest', 'fixture-owner', sys.argv[2],
                       'a' * 64, 'b' * 64, os.getpid(), 9, '/test-scope', (7, 0), 1)
identify_owned_worker(r, sys.stdin.buffer, sys.stdout.buffer)
sys.stdout.buffer.write(b'after-parent\n')
sys.stdout.buffer.flush()
"""
        # Keep the literal newline escaped in this synthetic interpreter input.
        code = code.replace("b'after-parent\n'", "b'after-parent\\n'")
        attempt = str(uuid4())
        with TemporaryFile() as errors, ExitStack() as cleanup:
            p = _launch((sys.executable, "-B", "-c", code, attempt, _V100), errors)
            cleanup.callback(_close_owned_child, p)
            row = _hello(p, monotonic() + 3)
            self.assertEqual((row.attempt_id, row.pid, row.gpu_uuid), (attempt, p.pid, _V100))
            self.assertIsNone(p.poll())   # no startup auto-admission
            p.stdin.write(b"G")
            self.assertEqual(p.stdout.read(), b"after-parent\n")
            self.assertEqual(p.wait(timeout=3), 0)

    def test_oversized_startup_is_never_accepted(self):
        code = "import sys;sys.stdout.buffer.write((8193).to_bytes(4,'big'));sys.stdout.buffer.flush()"
        with TemporaryFile() as errors, ExitStack() as cleanup:
            p = _launch((sys.executable, "-B", "-c", code), errors)
            cleanup.callback(_close_owned_child, p)
            with self.assertRaisesRegex(CustodyError, "oversized"):
                _hello(p, monotonic() + 3)
