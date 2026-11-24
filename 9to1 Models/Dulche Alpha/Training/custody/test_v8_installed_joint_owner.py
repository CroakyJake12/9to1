from dataclasses import asdict
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
from tempfile import TemporaryDirectory
from threading import Thread
from time import monotonic
import unittest
from uuid import uuid4

from .artifacts import CustodyError
from .v8_installed_joint_owner import LocalRankGrant, bind_worker, observe_process, serve_local_rank_grants
from .v8_owned_endpoints import _V100


@unittest.skipUnless(sys.platform.startswith("linux") and hasattr(socket, "SO_PEERCRED"),
                     "Requires Linux local peer credentials and /proc cgroup v2.")
class InstalledOwnerTests(unittest.TestCase):
    def test_no_cli_or_naked_python_binding(self):
        with self.assertRaisesRegex(CustodyError, "no immutable"):
            with bind_worker("early", str(uuid4()), _V100):
                self.fail("executed unowned rank")

    def test_local_socketpair_observes_kernel_peer_and_actual_grants(self):
        left, right = socket.socketpair()
        attempt = str(uuid4())
        identity = observe_process(os.getpid())
        events = []
        errors = []

        def authority():
            try:
                def allowed(process, request_attempt, rank, step):
                    events.append((process, request_attempt, rank, step))
                    return monotonic() + 0.4 if step == 1 else None

                with left:
                    left.settimeout(3)
                    serve_local_rank_grants(left, attempt=attempt, rank=0,
                                            expected_worker=identity, authorize_live=allowed)
            except BaseException as e:
                errors.append(e)

        t = Thread(target=authority, daemon=True)
        t.start()
        try:
            right.settimeout(3)
            grant = LocalRankGrant(right, attempt=attempt, rank=0, expected_authority=identity)
            try:
                self.assertTrue(grant.check_admission(0, 1))
                self.assertTrue(grant.mentor_quiesced())
                with self.assertRaisesRegex(CustodyError, "outside"):
                    grant.check_admission(1, 1)
                with self.assertRaisesRegex(CustodyError, "No current"):
                    grant.check_admission(0, 2)
                self.assertFalse(grant.mentor_quiesced())
            finally:
                grant.close()
        finally:
            right.close()
            t.join(3)
        self.assertFalse(t.is_alive())
        self.assertEqual(errors, [])
        self.assertEqual(events, [(identity, attempt, 0, 1), (identity, attempt, 0, 2)])

    def test_bound_registered_worker_invokes_actual_application_entry_through_server(self):
        from unittest.mock import Mock, patch
        from .v8_installed_joint_owner import BoundRankWorker, run_owned_binding
        from .v8_joint_registration import JointRegistration, RankProcessReceipt, _GPU_ROWS
        from .v8_joint_step import JointV8Ports
        from .v8_registered_rank import RegisteredRankRun

        left, right = socket.socketpair()
        attempt = str(uuid4())
        process = observe_process(os.getpid())
        calls, errors = [], []
        bindings = tuple((*row, "a" * 64, "b" * 64) for row in _GPU_ROWS)
        registry = JointRegistration("c" * 64, "d" * 64, "e" * 64, "a" * 64, "b" * 64,
                                     "f" * 64, ("a" * 64, "b" * 64), bindings)
        receipt = RankProcessReceipt(attempt, 0, "guest", "owner", _V100, "a" * 64, "b" * 64,
                                     process.pid, process.start_ticks, process.cgroup, (7, 0), 1)
        report = Mock()

        def authority():
            try:
                with left:
                    left.settimeout(3)
                    serve_local_rank_grants(left, attempt=attempt, rank=0, expected_worker=process,
                                            authorize_live=lambda p, a, r, s: (
                                                monotonic() + 0.4 if (p, a, r, s) ==
                                                (process, attempt, 0, 19) else None))
            except BaseException as exc:
                errors.append(exc)

        t = Thread(target=authority, daemon=True)
        t.start()
        try:
            right.settimeout(3)
            grant = LocalRankGrant(right, attempt=attempt, rank=0, expected_authority=process)
            try:
                ports = JointV8Ports(Mock(), Mock(), Mock(), Mock(), Mock(), Mock())
                admission = RegisteredRankRun(attempt, 19, "owner", "early", _V100,
                                              None, ports, None, 256, None)
                worker = BoundRankWorker(registry, receipt, admission, grant, lambda r: r == receipt, report)

                def use_bound_runtime(authorized, *, verify_owner):
                    self.assertTrue(verify_owner(authorized))
                    self.assertTrue(authorized.ports.mentor_quiesced())
                    self.assertIs(authorized.local_control, grant)
                    calls.append(authorized.step)
                    return "synthetic proof"

                with patch("custody.v8_registered_rank.execute_owned_rank", side_effect=use_bound_runtime):
                    self.assertEqual(run_owned_binding(worker), 0)
            finally:
                grant.close()
        finally:
            right.close()
            t.join(3)
        self.assertFalse(t.is_alive())
        self.assertEqual(errors, [])
        self.assertEqual(calls, [19])
        report.assert_called_once_with("synthetic proof")

    def test_exact_owned_child_gets_short_step_grant_but_cannot_continue_after_revocation(self):
        code = """
import json, socket, sys
from custody.artifacts import CustodyError
from custody.v8_installed_joint_owner import LinuxOwnedProcess, LocalRankGrant
path, attempt, supervisor = sys.argv[1:]
with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
    connection.settimeout(3)
    connection.connect(path)
    grant = LocalRankGrant(connection, attempt=attempt, rank=0,
                           expected_authority=LinuxOwnedProcess(**json.loads(supervisor)))
    assert grant.check_admission(0, 17) and grant.mentor_quiesced()
    try:
        grant.check_admission(0, 18)
    except CustodyError:
        print('properly revoked')
    else:
        raise AssertionError('continued without active admission')
    finally:
        grant.close()
"""
        attempt = str(uuid4())
        requests = []
        try:
            listener = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        except PermissionError:
            self.skipTest("Sandbox does not allow a separate AF_UNIX listener")
        with TemporaryDirectory() as temp, listener:
            os.chmod(temp, 0o700)
            sock = str(Path(temp) / "lease")
            listener.bind(sock)
            listener.listen(1)
            listener.settimeout(3)
            process = subprocess.Popen(
                [sys.executable, "-B", "-c", code, sock, attempt,
                 json.dumps(asdict(observe_process(os.getpid())))],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=False)
            try:
                with listener.accept()[0] as conn:
                    conn.settimeout(3)
                    worker = observe_process(process.pid)

                    def authorize(receipt, run, rank, step):
                        requests.append((receipt.pid, run, rank, step))
                        return monotonic() + 0.4 if step == 17 else None

                    serve_local_rank_grants(conn, attempt=attempt, rank=0,
                                            expected_worker=worker, authorize_live=authorize)
                stdout, stderr = process.communicate(timeout=3)
                self.assertEqual(process.returncode, 0, stderr)
                self.assertEqual(stdout, b"properly revoked\n")
                self.assertEqual(requests, [(process.pid, attempt, 0, 17), (process.pid, attempt, 0, 18)])
            finally:
                if process.poll() is None:
                    process.kill()
                    process.communicate(timeout=3)
