import ctypes
from importlib.util import find_spec
import json
import os
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch
from uuid import UUID, uuid4

from custody.artifacts import CustodyError
from custody.dual_gpu_smoke import _MARKER, _cuda_device, _cuda_driver_identity, _pipe_pair, _verified_receipts

_IDS = ("GPU-00000000-0000-0000-0000-000000000001", "GPU-00000000-0000-0000-0000-000000000002")
_ROOT = str(Path(__file__).resolve().parents[1])


def command(code, *args):
    return [sys.executable, "-I", "-B", "-c", f"import sys; sys.path.insert(0, {_ROOT!r})\n" + code, *args]


class CudaDriverIdentityTests(unittest.TestCase):
    def driver(self, *, count=1, uuid_api_error=0, legacy=False):
        raw_uuid = UUID(_IDS[0][4:]).bytes
        def write_int(ptr, value):
            ctypes.cast(ptr, ctypes.POINTER(ctypes.c_int))[0] = value
            return 0
        def fill_uuid(ptr, device):
            self.assertEqual(device, 17)
            if not uuid_api_error:
                ctypes.memmove(ptr, raw_uuid, 16)
            return uuid_api_error
        def capability(major, minor, device):
            self.assertEqual(device, 17)
            write_int(major, 12)
            return write_int(minor, 0)
        def get_device(ptr, ordinal):
            self.assertEqual(ordinal, 0)
            return write_int(ptr, 17)
        driver = {"cuInit": lambda flags: flags, "cuDeviceGetCount": lambda ptr: write_int(ptr, count),
                  "cuDeviceGet": get_device, "cuDeviceComputeCapability": capability,
                  "cuDeviceGetUuid": fill_uuid}
        if not legacy:
            driver["cuDeviceGetUuid_v2"] = fill_uuid
            driver["cuDeviceGetUuid"] = lambda *_: self.fail("v2 must be preferred")
        return SimpleNamespace(**driver)

    def test_driver_fallback_checks_ordinal_uuid_and_compute_capability(self):
        torch = SimpleNamespace(cuda=SimpleNamespace(is_available=lambda: True, device_count=lambda: 1,
                              get_device_properties=lambda _: SimpleNamespace(major=12, minor=0, uuid=object())),
                                device=lambda device: device)
        for legacy in (False, True):
            with patch("custody.dual_gpu_smoke.ctypes.CDLL", return_value=self.driver(legacy=legacy)):
                self.assertEqual(_cuda_driver_identity(), (_IDS[0], (12, 0)))
                with patch.dict(os.environ, {"CUDA_VISIBLE_DEVICES": _IDS[0]}):
                    self.assertEqual(_cuda_device(torch, 0, _IDS[0]), "cuda:0")
                with patch.dict(os.environ, {"CUDA_VISIBLE_DEVICES": _IDS[1]}):
                    with self.assertRaisesRegex(CustodyError, "differs"):
                        _cuda_device(torch, 0, _IDS[1])

    def test_driver_fails_closed_for_bad_count_or_uuid_kernel_status(self):
        for driver, expected in ((self.driver(count=2), "exactly one"),
                                 (self.driver(uuid_api_error=100), "cuDeviceGetUuid_v2.*100")):
            with patch("custody.dual_gpu_smoke.ctypes.CDLL", return_value=driver):
                with self.assertRaisesRegex(CustodyError, expected):
                    _cuda_driver_identity()


class OwnedPairTests(unittest.TestCase):
    def test_operator_pair_has_real_two_process_duplex_binary_not_stdout_logging(self):
        early = command("sys.stdout.buffer.write(b'abc'); sys.stdout.buffer.flush(); "
                        "assert sys.stdin.buffer.read(3) == b'xyz'; sys.stderr.write('early')")
        tail = command("assert sys.stdin.buffer.read(3) == b'abc'; "
                       "sys.stdout.buffer.write(b'xyz'); sys.stdout.buffer.flush(); sys.stderr.write('tail')")
        self.assertEqual(_pipe_pair(early, tail, 5), ((0, b"early"), (0, b"tail")))

    def test_pair_timeout_and_two_required_distinct_rank_receipts(self):
        blocked = command("sys.stdin.buffer.read(1)")
        with self.assertRaisesRegex(CustodyError, "wall budget"):
            _pipe_pair(blocked, blocked, 1)
        attempt = str(uuid4())
        wrong = _MARKER + json.dumps({"attempt": attempt, "rank": 0, "gpu_uuid": _IDS[1]}).encode()
        with self.assertRaisesRegex(CustodyError, "did not match"):
            _verified_receipts(attempt, _IDS, ((0, wrong), (0, wrong)))

    def test_real_cuda_cli_refuses_missing_visibility_before_cuda_discovery(self):
        code = """import os
os.environ.pop('CUDA_VISIBLE_DEVICES', None)
import sys, types
sys.modules['torch'] = types.SimpleNamespace()
from custody.dual_gpu_smoke import main
main(sys.argv[1:])
"""
        rank = command(code, "rank", "--attempt", str(uuid4()), "--rank", "0", "--gpu-uuid", _IDS[0])
        other = command("sys.stdin.buffer.read(1)")
        outcomes = _pipe_pair(rank, other, 5)
        self.assertNotEqual(outcomes[0][0], 0)
        self.assertIn(b"CUDA visibility must be exactly", outcomes[0][1])

    @unittest.skipUnless(find_spec("torch"), "requires installed PyTorch")
    def test_tiny_exact_numeric_graph_over_two_actual_cpu_process_pipes(self):
        code = """import json, os, sys
os.environ['CUDA_VISIBLE_DEVICES'] = '-1'
import torch
from custody.dual_gpu_smoke import _tiny_step
torch.set_num_threads(1)
assert not torch.cuda.is_initialized()
evidence = _tiny_step(torch, int(sys.argv[1]), sys.argv[2], 'cpu', sys.stdin.buffer, sys.stdout.buffer)
assert not torch.cuda.is_initialized()
print(json.dumps(evidence), file=sys.stderr)
"""
        attempt = str(uuid4())
        outcomes = _pipe_pair(command(code, "0", attempt), command(code, "1", attempt), 25)
        for rank, (exit_code, diagnostic) in enumerate(outcomes):
            self.assertEqual(exit_code, 0, diagnostic)
            expected = ({"input_grad": [1.0, 2.0, 3.0, 4.0], "weight_grad": 15.0}
                        if rank == 0 else {"loss": 15.0, "weight_grad": 60.0})
            self.assertEqual(json.loads(diagnostic.splitlines()[-1]), expected)
