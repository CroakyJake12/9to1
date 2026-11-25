import hashlib
from io import BytesIO
import unittest

from .artifacts import CustodyError
from .v8_frozen_guest_step import FrozenGuestTranscript, _ReadTrace, _WriteTrace


class FrozenGuestTranscriptTests(unittest.TestCase):
    def test_host_and_guest_facing_split_reads_writes_agree_and_mismatches_differ(self):
        activation, gradient = b"activation stream", b"gradient stream"
        av, gv = hashlib.sha256(), hashlib.sha256()
        wire = BytesIO()
        write = _WriteTrace(wire, av)
        write.write(activation[:4])
        write.write(activation[4:])
        write.flush()
        read = _ReadTrace(BytesIO(gradient), gv)
        read.read(3)
        read.read(99)
        guest, host, reversed_io = (FrozenGuestTranscript() for _ in range(3))
        ids = [1, 3, 7]
        guest.record(9, 0, ids, av.digest(), gv.digest())
        host.record(9, 0, ids, hashlib.sha256(activation).digest(), hashlib.sha256(gradient).digest())
        reversed_io.record(9, 0, ids, gv.digest(), av.digest())
        self.assertEqual(host.hexdigest(), guest.hexdigest())
        self.assertNotEqual(host.hexdigest(), reversed_io.hexdigest())
        with self.assertRaises(CustodyError):
            guest.record(9, 0, ids, av.digest(), gv.digest())
