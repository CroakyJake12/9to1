from dataclasses import replace
from io import BytesIO
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from uuid import uuid4

from custody.artifacts import CustodyError
from custody.rank_stream import BoundaryFrame, receive_frame, send_frame


class RankStreamTests(unittest.TestCase):
    def setUp(self):
        self.frame = BoundaryFrame(str(uuid4()), 1, 0, 0, "activation", "float32", (1, 2, 2))
        self.hidden = struct.pack("<4f", 1.25, -2.5, 0.75, 0.5)

    def test_actual_cpu_process_returns_matching_boundary_derivative(self):
        # For z = 3h - 1 and L = sum(z²)/2, dL/dh = 3(3h - 1).
        worker = r"""
from dataclasses import replace
import struct, sys
from custody.rank_stream import BoundaryFrame, receive_frame, send_frame
frame = BoundaryFrame(sys.argv[1], 1, 0, 0, "activation", "float32", (1, 2, 2))
hidden = struct.unpack("<4f", receive_frame(sys.stdin.buffer, frame, max_bytes=16))
gradient = struct.pack("<4f", *(3*(3*h-1) for h in hidden))
send_frame(sys.stdout.buffer, replace(frame, source_rank=1, direction="gradient"), gradient, max_bytes=16)
"""
        outbound = BytesIO()
        send_frame(outbound, self.frame, self.hidden, max_bytes=16)
        training = Path(__file__).resolve().parents[1]
        # -I omits ambient Python paths. Only the reviewed source directory is added.
        code = "import sys; sys.path.insert(0, " + repr(str(training)) + ")\n" + worker
        env = {key: os.environ[key] for key in ("SYSTEMROOT", "WINDIR") if key in os.environ}
        env["CUDA_VISIBLE_DEVICES"] = "-1"
        with tempfile.TemporaryDirectory() as work:
            child = subprocess.run([sys.executable, "-I", "-B", "-c", code, self.frame.attempt_id],
                                   cwd=work, env=env, input=outbound.getvalue(), capture_output=True,
                                   timeout=5, check=True)
        self.assertEqual(child.stderr, b"")
        incoming = BytesIO(child.stdout)
        grad = replace(self.frame, source_rank=1, direction="gradient")
        actual = struct.unpack("<4f", receive_frame(incoming, grad, max_bytes=16))
        self.assertEqual(actual, (8.25, -25.5, 3.75, 1.5))
        self.assertEqual(incoming.read(), b"")

    def _encoded(self):
        outgoing = BytesIO()
        send_frame(outgoing, self.frame, self.hidden, max_bytes=16)
        return outgoing.getvalue()

    def test_refuses_wrong_attempt_step_microbatch_direction_dtype_or_shape_before_payload(self):
        data = self._encoded()
        for expected in (replace(self.frame, attempt_id=str(uuid4())), replace(self.frame, step=2),
                         replace(self.frame, microbatch=1), replace(self.frame, source_rank=1, direction="gradient"),
                         replace(self.frame, dtype="bfloat16"), replace(self.frame, shape=(2, 1, 2))):
            source = BytesIO(data)
            with self.subTest(expected=expected), self.assertRaisesRegex(CustodyError, "header differs"):
                receive_frame(source, expected, max_bytes=16)
            self.assertEqual(source.read(), self.hidden)

    def test_refuses_truncated_header_and_payload(self):
        data = self._encoded()
        for broken in (data[:8], data[:-1]):
            with self.assertRaisesRegex(CustodyError, "Truncated"):
                receive_frame(BytesIO(broken), self.frame, max_bytes=16)

    def test_refuses_bad_local_spec_or_oversize_before_io(self):
        output = BytesIO()
        for bad in (replace(self.frame, dtype="pickle"), replace(self.frame, shape=(1, 4)),
                    replace(self.frame, shape=(0, 2, 2)), replace(self.frame, source_rank=1)):
            with self.assertRaises(CustodyError):
                send_frame(output, bad, self.hidden, max_bytes=16)
        with self.assertRaisesRegex(CustodyError, "exceeds"):
            send_frame(output, self.frame, self.hidden, max_bytes=15)
        with self.assertRaisesRegex(CustodyError, "byte size"):
            send_frame(output, self.frame, self.hidden[:-1], max_bytes=16)
        self.assertEqual(output.getvalue(), b"")

    def test_split_reads_and_writes_preserve_frame_boundaries(self):
        class Choppy(BytesIO):
            def read(self, size):
                return super().read(min(size, 3))

            def write(self, data):
                return super().write(data[:4])

        stream = Choppy()
        second = replace(self.frame, microbatch=1)
        send_frame(stream, self.frame, self.hidden, max_bytes=16)
        send_frame(stream, second, self.hidden, max_bytes=16)
        stream.seek(0)
        for frame in (self.frame, second):
            self.assertEqual(receive_frame(stream, frame, max_bytes=16), self.hidden)
        self.assertEqual(stream.read(1), b"")

    def test_tampered_wire_length_is_refused_without_reading_payload(self):
        data = bytearray(self._encoded())
        data[-len(self.hidden)-4:-len(self.hidden)] = b"\xff" * 4
        source = BytesIO(data)
        with self.assertRaisesRegex(CustodyError, "header differs"):
            receive_frame(source, self.frame, max_bytes=16)
        self.assertEqual(source.read(), self.hidden)
