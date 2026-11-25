import hashlib
import os
from pathlib import Path
import shlex
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest
from uuid import uuid4
from zipfile import ZipFile

from .artifacts import CustodyError
from .v8_owned_endpoints import (_BOOTSTRAP, _GUEST_SITE, _NATIVE_RUNTIME_REL, _RTX, _V100,
                                 RegisteredEndpoint, WORKER_ID, owned_endpoint_argv)


class OwnedEndpointTests(unittest.TestCase):
    def options(self):
        return dict(registration_id=WORKER_ID, attempt_id=str(uuid4()),
                    native=RegisteredEndpoint("/home/jacob" + _NATIVE_RUNTIME_REL,
                                              "/approved/runtime host.zip", "a" * 64, "/pinned/site", _RTX),
                    guest=RegisteredEndpoint("/usr/bin/python3", "/approved/runtime guest.zip",
                                             "b" * 64, _GUEST_SITE, _V100),
                    approved_home="/home/jacob", known_hosts="/pinned/sealed guests",
                    verified_joint_launch=lambda *_: True)

    def test_exact_lanes_and_independent_pinned_ssh(self):
        opt = self.options()
        tail, early, weights = owned_endpoint_argv(**opt)
        self.assertEqual(tail[-3:], ("tail", opt["attempt_id"], _RTX))
        self.assertEqual(early[:-1], weights[:-1])
        self.assertEqual(early[-2], "v100@127.0.0.1")
        self.assertIn("StrictHostKeyChecking=yes", early)
        self.assertIn("UserKnownHostsFile=/pinned/sealed guests", early)
        self.assertEqual(shlex.split(early[-1])[-4:], [_GUEST_SITE, "early", opt["attempt_id"], _V100])
        self.assertEqual(shlex.split(weights[-1])[-3:], ["weights", opt["attempt_id"], _V100])
        with self.assertRaises(CustodyError):
            owned_endpoint_argv(**(opt | {"registration_id": "old-native-v8"}))
        with self.assertRaises(CustodyError):
            owned_endpoint_argv(**(opt | {"verified_joint_launch": lambda *_: False}))

    @unittest.skipUnless(hasattr(os, "memfd_create"), "Linux sealed code bootstrap required")
    def test_same_pinned_synthetic_archive_executes_and_mismatch_does_not(self):
        with TemporaryDirectory() as root:
            archive = Path(root) / "source.zip"
            with ZipFile(archive, "w") as z:
                z.writestr("custody/__init__.py", "")
                z.writestr("custody/v8_registered_rank.py", "import sys\nprint(sys.argv[1])\n")
            digest = hashlib.sha256(archive.read_bytes()).hexdigest()
            args = [sys.executable, "-I", "-S", "-B", "-c", _BOOTSTRAP, str(archive),
                    digest, root, "early", str(uuid4()), _V100]
            result = subprocess.run(args, check=False, capture_output=True, timeout=10)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(result.stdout, b"early\n")
            args[7] = "0" * 64
            result = subprocess.run(args, check=False, capture_output=True, timeout=10)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(result.stdout)
