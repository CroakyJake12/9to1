from __future__ import annotations

import http.client
import os
import pathlib
import socket
import subprocess
import sys
import tempfile
import time
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
BROKER = RUNTIME / "broker.py"


class UnixHTTPConnection(http.client.HTTPConnection):
    def __init__(self, path: pathlib.Path, timeout: float = 1.0):
        super().__init__("localhost", timeout=timeout)
        self.path = path

    def connect(self) -> None:
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout)
        self.sock.connect(str(self.path))


@unittest.skipUnless(os.name == "posix" and hasattr(socket, "AF_UNIX"), "requires POSIX flock and Unix sockets")
class BrokerSingleInstanceTests(unittest.TestCase):
    def _start(self, env: dict[str, str]) -> subprocess.Popen[bytes]:
        return subprocess.Popen(
            [sys.executable, str(BROKER)],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env,
            close_fds=True,
        )

    def _wait_healthy(self, process: subprocess.Popen[bytes], socket_path: pathlib.Path) -> None:
        deadline = time.monotonic() + 5
        last_error: BaseException | None = None
        while time.monotonic() < deadline:
            if process.poll() is not None:
                stdout, stderr = process.communicate()
                self.fail(f"broker exited early ({process.returncode}): {stdout!r} {stderr!r}")
            if socket_path.exists():
                connection = UnixHTTPConnection(socket_path)
                try:
                    connection.request("GET", "/health")
                    response = connection.getresponse()
                    response.read()
                    if response.status == 200:
                        return
                except (OSError, http.client.HTTPException) as exc:
                    last_error = exc
                finally:
                    connection.close()
            time.sleep(0.02)
        self.fail(f"broker did not become healthy: {last_error}")

    @staticmethod
    def _kill(process: subprocess.Popen[bytes] | None) -> None:
        if process is None:
            return
        if process.poll() is None:
            process.kill()
        process.communicate(timeout=5)

    def test_second_instance_cannot_touch_socket_and_crash_releases_ownership(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            runtime_dir = root / "xdg" / "haven"
            broker_socket = runtime_dir / "inference.sock"
            worker_socket = runtime_dir / "worker.sock"
            env = os.environ.copy()
            env.update({
                "XDG_RUNTIME_DIR": str(root / "xdg"),
                "XDG_DATA_HOME": str(root / "data"),
                "HAVEN_INFERENCE_SOCKET": str(broker_socket),
                "HAVEN_LLAMA_WORKER_SOCKET": str(worker_socket),
            })

            first: subprocess.Popen[bytes] | None = None
            replacement: subprocess.Popen[bytes] | None = None
            try:
                first = self._start(env)
                self._wait_healthy(first, broker_socket)
                socket_identity = broker_socket.stat().st_ino
                self.assertEqual(0o600, (runtime_dir / "inference-broker.lease").stat().st_mode & 0o777)

                contender = self._start(env)
                contender_stdout, contender_stderr = contender.communicate(timeout=5)
                self.assertNotEqual(0, contender.returncode, contender_stdout)
                self.assertIn(b"another inference broker already owns", contender_stderr)
                self.assertEqual(socket_identity, broker_socket.stat().st_ino)

                self._kill(first)
                first = None
                self.assertTrue(broker_socket.exists(), "crash should leave a stale socket for recovery")

                replacement = self._start(env)
                self._wait_healthy(replacement, broker_socket)
            finally:
                self._kill(replacement)
                self._kill(first)


if __name__ == "__main__":
    unittest.main()
