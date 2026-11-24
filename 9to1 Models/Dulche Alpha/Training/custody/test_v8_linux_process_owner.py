from dataclasses import replace
import os
import socket
import subprocess
import sys
import unittest
from unittest.mock import patch

from .artifacts import CustodyError
from .v8_linux_process_owner import LinuxRankHandle, _read_process, pin_local_peer


@unittest.skipUnless(hasattr(os, "pidfd_open") and os.path.exists("/proc/self/cgroup"),
                     "requires cgroup v2 process metadata and Linux pidfds")
class LinuxProcessTests(unittest.TestCase):
    def test_pidfd_never_accepts_replaced_exited_or_moved_child(self):
        child = subprocess.Popen([sys.executable, "-c", "import sys; sys.stdin.read()"],
                                 stdin=subprocess.PIPE)
        self.addCleanup(child.wait, 5)
        self.addCleanup(child.stdin.close)
        try:
            original = _read_process(child.pid)
            # The cloud itself uses a restricted cgroup namespace. Test the
            # dedicated cgroup policy without creating a real service cgroup.
            admitted = replace(original, cgroup="/trusted-v8/attempt")
            with patch("custody.v8_linux_process_owner._read_process", return_value=admitted) as observe:
                with LinuxRankHandle(child.pid, expected_uid=original.uid, expected_cgroup=admitted.cgroup) as rank:
                    self.assertTrue(rank.live())
                    observe.return_value = replace(admitted, start_ticks=admitted.start_ticks + 1)
                    self.assertFalse(rank.live())
                    observe.return_value = replace(admitted, cgroup="/foreign")
                    self.assertFalse(rank.live())
                    observe.return_value = admitted
                    child.stdin.close()
                    child.wait(5)
                    self.assertFalse(rank.live())
                self.assertFalse(rank.live())
        finally:
            if child.poll() is None:
                child.kill()
                child.wait(5)

    @unittest.skipUnless(hasattr(socket, "SO_PEERCRED"), "requires Linux Unix peer credentials")
    def test_kernel_peer_credentials_not_a_caller_claim(self):
        a, b = socket.socketpair()
        with a, b:
            original = _read_process(os.getpid())
            admitted = replace(original, cgroup="/trusted-v8/broker")
            with patch("custody.v8_linux_process_owner._read_process", return_value=admitted):
                with self.assertRaises(CustodyError):
                    pin_local_peer(a, expected_uid=original.uid + 1, expected_cgroup=admitted.cgroup)
                with pin_local_peer(a, expected_uid=original.uid, expected_cgroup=admitted.cgroup) as peer:
                    self.assertEqual(peer.original.pid, os.getpid())
                    self.assertTrue(peer.live())
