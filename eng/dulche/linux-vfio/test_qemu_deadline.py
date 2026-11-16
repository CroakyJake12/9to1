import os
import subprocess
import sys
import time
import unittest
from unittest.mock import patch
import qemu_deadline as deadline


class DeadlineTests(unittest.TestCase):
    def test_only_owned_cpu_child_stops(self):
        children = [subprocess.Popen([sys.executable, '-I', '-S', '-B', '-c',
                                      'import time; time.sleep(20)']) for _ in range(2)]
        fd = os.pidfd_open(children[0].pid)
        try:
            self.assertTrue(deadline.stop_pinned(fd))
            children[0].wait(timeout=1)
            self.assertIsNone(children[1].poll())
            self.assertTrue(deadline.stop_pinned(fd))
        finally:
            os.close(fd)
            for child in children:
                if child.poll() is None: child.kill()
                child.wait(timeout=1)

    def test_invalid_unit_is_rejected_before_process_open(self):
        with patch.object(deadline.os, 'pidfd_open') as opened:
            with self.assertRaises(deadline.Refusal):
                deadline.pin_identity('user.service', 'a'*32, 2, '1', 'fake')
            opened.assert_not_called()

    def test_already_stopped_is_safe(self):
        self.assertTrue(deadline.stop_pinned(None))


if __name__ == '__main__': unittest.main()
