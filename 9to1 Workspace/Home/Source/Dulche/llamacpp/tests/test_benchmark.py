from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import benchmark  # noqa: E402


class BenchmarkTests(unittest.TestCase):
    def test_sse_parser_handles_json_done_and_noise(self) -> None:
        event = benchmark.parse_sse_data_line(b'data: {"usage":{"completion_tokens":7}}\n')
        self.assertEqual(7, event["usage"]["completion_tokens"])
        self.assertEqual("DONE", benchmark.parse_sse_data_line(b"data: [DONE]\n"))
        self.assertIsNone(benchmark.parse_sse_data_line(b": keepalive\n"))

    def test_process_tree_handles_missing_proc_entry(self) -> None:
        result = benchmark.process_tree(999999999)
        self.assertEqual({999999999}, result)

    def test_rss_parser_is_read_only_and_returns_none_for_missing_pid(self) -> None:
        self.assertIsNone(benchmark._rss_bytes(999999999))


if __name__ == "__main__":
    unittest.main()
