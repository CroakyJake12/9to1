from __future__ import annotations

import pathlib
import io
import json
import sys
import tempfile
import unittest
from unittest import mock

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


class StreamCompletionTests(unittest.TestCase):
    """Deterministic protocol/measurement controls; no socket/model is contacted."""

    def stream(self, events: list[dict | bytes], *, times=(0.0, 8.0, 10.0)):
        body = b"".join(event if isinstance(event, bytes) else
                        b"data: " + json.dumps(event).encode() + b"\n\n"
                        for event in events)
        response = io.BytesIO(body)
        response.status = 200
        connection = mock.Mock()
        connection.getresponse.return_value = response
        sampler = mock.Mock()
        sampler.peak_bytes = 123
        with mock.patch.object(benchmark, "UnixHTTPConnection", return_value=connection), \
             mock.patch.object(benchmark.time, "monotonic", side_effect=times):
            try:
                return benchmark._stream_chat(pathlib.Path("/unused-test-only"), {},
                                              timeout=1, sampler=sampler)
            finally:
                sampler.stop.assert_called_once()
                connection.close.assert_called_once()

    @staticmethod
    def content():
        return {"choices": [{"delta": {"content": "answer"}, "finish_reason": None}]}

    @staticmethod
    def terminal(reason="stop", tokens=100):
        return {"choices": [{"delta": {}, "finish_reason": reason}],
                "usage": {"completion_tokens": tokens}}

    def test_complete_stop_retains_legacy_scalar_and_exposes_correct_request_basis(self):
        r = self.stream([self.content(), self.terminal(), b"data: [DONE]\n\n"])
        self.assertEqual(50.0, r["completionTokensPerSecond"])
        self.assertEqual(10.0, r["requestCompletionTokensPerSecond"])
        self.assertEqual(8000.0, r["timeToFirstTokenMs"])
        self.assertEqual(10000.0, r["totalGenerationMs"])
        self.assertTrue(r["streamCompleted"])
        self.assertFalse(r["completionTokensPerSecondIsNativeDecodeRate"])
        self.assertIsNone(r["nativeDecodeTokensPerSecond"])
        self.assertIn("FirstVisibleContent", r["completionTokensPerSecondBasis"])
        self.assertIn("requestStartToTerminal", r["requestCompletionTokensPerSecondBasis"])

    def test_length_finish_is_valid_but_retains_its_cap_reason(self):
        r = self.stream([self.content(), self.terminal("length"), b"data: [DONE]\n\n"])
        self.assertEqual("length", r["finishReason"])
        self.assertTrue(r["streamCompleted"])

    def test_tool_calls_without_visible_content_is_legitimate(self):
        r = self.stream([{"choices": [{"delta": {"tool_calls": [{"index": 0}]}}]},
                         self.terminal("tool_calls", 7), b"data: [DONE]\n\n"],
                        times=(0.0, 10.0))
        self.assertEqual("tool_calls", r["finishReason"])
        self.assertIsNone(r["timeToFirstTokenMs"])
        self.assertIsNone(r["completionTokensPerSecond"])
        self.assertEqual(0.7, r["requestCompletionTokensPerSecond"])

    def test_eof_after_finish_reason_is_not_complete_stream(self):
        with self.assertRaisesRegex(benchmark.BenchmarkError, "before terminal"):
            self.stream([self.content(), self.terminal()])

    def test_eof_before_finish_reason_is_not_complete_stream(self):
        with self.assertRaisesRegex(benchmark.BenchmarkError, "before terminal"):
            self.stream([self.content()])

    def test_done_without_finish_reason_is_not_complete_choice(self):
        with self.assertRaisesRegex(benchmark.BenchmarkError, "completed choice"):
            self.stream([self.content(), b"data: [DONE]\n\n"])

    def test_error_event_is_not_success_even_if_done_follows(self):
        with self.assertRaisesRegex(benchmark.BenchmarkError, "error event"):
            self.stream([self.content(), {"error": {"message": "fixture"}},
                         b"data: [DONE]\n\n"])

    def test_malformed_stream_json_refuses_but_legacy_parser_stays_tolerant(self):
        self.assertIsNone(benchmark.parse_sse_data_line(b"data: {broken\n"))
        with self.assertRaisesRegex(benchmark.BenchmarkError, "malformed data"):
            self.stream([b"data: {broken\n", self.terminal(), b"data: [DONE]\n\n"])

    def test_invalid_utf8_refuses(self):
        with self.assertRaisesRegex(benchmark.BenchmarkError, "invalid UTF-8"):
            self.stream([b"data: {\xff}\n"])

    def test_nonobject_data_refuses(self):
        for value in [b"data: []\n", b"data: null\n"]:
            with self.subTest(value=value):
                with self.assertRaisesRegex(benchmark.BenchmarkError, "must be an object"):
                    self.stream([value])

    def test_invalid_usage_refuses_bool_negative_and_noninteger(self):
        for value in [True, -1, 1.5, "7"]:
            with self.subTest(value=value):
                with self.assertRaisesRegex(benchmark.BenchmarkError, "nonnegative integer"):
                    self.stream([self.content(), self.terminal(tokens=value)])

    def test_invalid_finish_reason_refuses(self):
        for value in [True, "", " "]:
            with self.subTest(value=value):
                with self.assertRaisesRegex(benchmark.BenchmarkError, "nonempty string"):
                    self.stream([self.content(), self.terminal(reason=value)])

    def test_conflicting_finish_reason_refuses(self):
        with self.assertRaisesRegex(benchmark.BenchmarkError, "conflicting finish"):
            self.stream([self.content(), self.terminal("stop"), self.terminal("length")])

    def test_missing_usage_remains_null_without_estimating_text_tokens(self):
        r = self.stream([self.content(), {"choices": [{"finish_reason": "stop"}]},
                         b"data: [DONE]\n\n"])
        self.assertIsNone(r["completionTokens"])
        self.assertIsNone(r["completionTokensPerSecond"])
        self.assertIsNone(r["requestCompletionTokensPerSecond"])

    def test_data_line_limit_refuses_before_allocating_aggregate_buffer(self):
        with self.assertRaisesRegex(benchmark.BenchmarkError, "exceeds 1 MiB"):
            self.stream([b"data: " + b"x" * benchmark.MAX_SSE_LINE_BYTES + b"\n"])

    def test_ordinary_comments_and_blank_lines_do_not_affect_measurement(self):
        r = self.stream([b":keepalive\n\n", self.content(), self.terminal(),
                         b"data: [DONE]\n\n"])
        self.assertEqual("stop", r["finishReason"])
        self.assertEqual(10.0, r["requestCompletionTokensPerSecond"])

    def test_invalid_choices_shape_refuses(self):
        for value in [{}, ["bad"], [{}, {}]]:
            with self.subTest(value=value):
                with self.assertRaises(benchmark.BenchmarkError):
                    self.stream([{"choices": value}])

    def test_invalid_delta_or_content_shape_refuses(self):
        for delta in [[], {"content": 7}, {"content": True}]:
            with self.subTest(delta=delta):
                with self.assertRaises(benchmark.BenchmarkError):
                    self.stream([{"choices": [{"delta": delta}]}])

    def test_other_nonempty_finish_reason_is_preserved_for_future_compatibility(self):
        r = self.stream([self.content(), self.terminal("content_filter"), b"data: [DONE]\n\n"])
        self.assertEqual("content_filter", r["finishReason"])


if __name__ == "__main__":
    unittest.main()
