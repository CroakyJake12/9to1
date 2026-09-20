from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest
from unittest import mock

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import broker  # noqa: E402
import model_lease  # noqa: E402


class WorkerLeaseTests(unittest.TestCase):
    def _manifest(self, model_id: str) -> broker.ModelManifest:
        digest = "a" * 64
        return broker.ModelManifest(
            model_id,
            model_id,
            digest,
            8,
            broker.canonical_blob_relative(digest),
            "MIT",
            "local-test",
            (3,),
        )

    def test_busy_target_does_not_unload_current_worker(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            worker = broker.Worker()
            current = self._manifest("current")
            target = self._manifest("target")
            process = mock.Mock()
            process.poll.return_value = None
            current_lease = mock.Mock(spec=model_lease.ModelLease)
            worker._process = process
            worker._model = current
            worker._lease = current_lease
            worker_socket = pathlib.Path(tmp) / "worker.sock"
            worker_socket.touch()
            with (
                mock.patch.object(broker, "WORKER_SOCKET", worker_socket),
                mock.patch.object(broker, "RUNTIME_DIR", pathlib.Path(tmp) / "runtime"),
                mock.patch.object(
                    broker,
                    "acquire_model_lease",
                    side_effect=model_lease.ModelLeaseBusy("busy"),
                ),
            ):
                with self.assertRaises(broker.BrokerError):
                    worker.load(target)
            self.assertIs(worker.model, current)
            self.assertIs(worker._process, process)
            current_lease.release.assert_not_called()

    def test_unload_releases_worker_lifetime_lease_after_process_exit(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            worker = broker.Worker()
            worker._model = self._manifest("current")
            process = mock.Mock()
            process.poll.return_value = 0
            worker._process = process
            lease = mock.Mock(spec=model_lease.ModelLease)
            worker._lease = lease
            worker_socket = pathlib.Path(tmp) / "worker.sock"
            with mock.patch.object(broker, "WORKER_SOCKET", worker_socket):
                worker.unload()
            lease.release.assert_called_once_with()
            self.assertIsNone(worker._lease)
            self.assertIsNone(worker.model)


if __name__ == "__main__":
    unittest.main()
