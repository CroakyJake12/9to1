import io
import json
import unittest
from uuid import uuid4

from custody import ArtifactRef, AttemptRef

from .protocol import REQUEST_BYTES, RESPONSE_BYTES, WorkerError, attempt_record, encode_event, read_request, result_page


class ProtocolTests(unittest.TestCase):
    def request(self, command, **fields):
        return read_request(io.BytesIO(json.dumps({"schema": 1, "command": command, **fields}).encode()))

    def test_decodes_only_supported_cpu_self_check_commands(self):
        identifiers = {"run_id": str(uuid4()), "attempt_id": str(uuid4())}
        for command in ("status", "pause"):
            name, selected = self.request(command, **identifiers)
            self.assertEqual((name, selected.run_id, selected.attempt_id),
                             (command, identifiers["run_id"], identifiers["attempt_id"]))
        self.assertEqual(self.request("status"), ("status", None))
        self.assertEqual(self.request("launch"), ("launch", None))
        self.assertEqual(self.request("list"), ("list", (20, None)))
        name, ref = self.request("resume", checkpoint={
            **identifiers, "artifact_id": str(uuid4()), "sha256": "1" * 64, "size_bytes": 500,
        })
        self.assertEqual((name, ref.size_bytes), ("resume", 500))

    def test_refuses_configuration_fields_partial_ids_and_out_of_range_types(self):
        cases = (
            ("launch", {"python": "untrusted.exe"}), ("launch", {"gpu_uuid": "untrusted"}),
            ("launch", {"dataset": "untrusted"}), ("list", {"limit": True}), ("list", {"limit": 51}),
            ("status", {"run_id": str(uuid4())}), ("pause", {}), ("resume", {"checkpoint": []}),
        )
        for command, fields in cases:
            with self.subTest(command=command, fields=fields), self.assertRaises(WorkerError):
                self.request(command, **fields)

    def test_rejects_duplicate_keys_oversize_and_invalid_encoding(self):
        inputs = [
            b'{"schema":1,"command":"status","command":"launch"}',
            b'{"schema":true,"command":"status"}',
            b'{"schema":1,"command":"list","limit":NaN}',
            b'\xff',
            b' ' * (REQUEST_BYTES + 1),
        ]
        for data in inputs:
            with self.subTest(prefix=data[:40]), self.assertRaises(WorkerError):
                read_request(io.BytesIO(data))

    def test_paginate_by_encoded_line_size_preserving_the_final_included_cursor(self):
        refs = [AttemptRef(str(uuid4()), str(uuid4())) for _ in range(51)]

        def record(ref):
            artifacts = tuple(ArtifactRef(ref.run_id, ref.attempt_id, str(uuid4()), "d" * 64, 50_000)
                              for _ in range(3))
            return attempt_record(ref, "passed", updates=2, artifacts=artifacts, resume=artifacts[1])

        page = result_page(refs, 50, record)
        self.assertLess(len(page["attempts"]), 50)
        self.assertLessEqual(len(encode_event("result", result=page)), RESPONSE_BYTES)
        last = page["attempts"][-1]
        self.assertEqual(page["next"], {key: last[key] for key in ("run_id", "attempt_id")})


if __name__ == "__main__":
    unittest.main()
