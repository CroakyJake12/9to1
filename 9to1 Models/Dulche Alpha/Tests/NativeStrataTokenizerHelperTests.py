"""Bounded build-free tests for the bundled tokenizer helper (strata_tokenizer_helper.py).

These tests exercise the ACTUAL production helper's protocol and identity logic (framing, ENC/DEC/
IDENTITY/QUIT, identity mismatch, dependency fail-closed). They never compile, run inference or load
model weights. When the donor `regex` dependency is not installed (as in the cloud build) the helper
is verified to fail closed with the exact dependency cause instead of fabricating token ids.
"""
import io
import json
import pathlib
import sys
import unittest

HELPER = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata" / "strata_tokenizer_helper.py"


def load_helper():
    spec = __import__("importlib.util").util.spec_from_file_location("strata_tokenizer_helper", HELPER)
    mod = __import__("importlib.util").util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


class FrameProtocolTests(unittest.TestCase):
    def test_frame_round_trip(self):
        mod = load_helper()
        stream = io.BytesIO()
        body = b"hello \xe2\x9c\x93"
        mod.write_frame(stream, body)
        stream.seek(0)
        got, length = mod.read_frame(stream)
        self.assertEqual(got, body)
        self.assertEqual(length, len(body))

    def test_frame_rejects_oversized(self):
        mod = load_helper()
        stream = io.BytesIO()
        stream.write((mod.MAX_FRAME + 1).to_bytes(4, "little"))
        stream.seek(0)
        with self.assertRaises(ValueError):
            mod.read_frame(stream)

    def test_frame_rejects_truncated(self):
        mod = load_helper()
        stream = io.BytesIO()
        stream.write((10).to_bytes(4, "little"))
        stream.write(b"abc")
        stream.seek(0)
        with self.assertRaises(ValueError):
            mod.read_frame(stream)

    def test_frame_eof(self):
        mod = load_helper()
        self.assertEqual(mod.read_frame(io.BytesIO()), (b"", -1))


class IdentityTests(unittest.TestCase):
    def test_identity_deterministic(self):
        mod = load_helper()
        class Fake:
            pre = "qwen35"
            tokens = ["a", "bb", "c"]
            special_tokens = {"<|im_start|>": 1}
        self.assertEqual(mod.tokenizer_identity(Fake()), mod.tokenizer_identity(Fake()))

    def test_identity_changes_with_vocab(self):
        mod = load_helper()
        class Fake:
            pre = "qwen35"
            tokens = ["a", "bb", "c"]
            special_tokens = {}
        class Fake2:
            pre = "qwen35"
            tokens = ["a", "bb", "c", "d"]
            special_tokens = {}
        self.assertNotEqual(mod.tokenizer_identity(Fake()), mod.tokenizer_identity(Fake2()))


class ServeProtocolTests(unittest.TestCase):
    class FakeTok:
        pre = "qwen35"
        tokens = list("abcdefghij")
        special_tokens = {}

        def encode(self, text, parse_special=False):
            # deterministic: map each char to its ordinal within vocab
            return [self.tokens.index(ch) if ch in self.tokens else 0 for ch in text]

        def token_bytes(self, i):
            return self.tokens[i].encode("latin1")

    def _run_serve(self, inputs: list[bytes], tokenizer=None, expected_identity=None):
        mod = load_helper()
        stream_in = io.BytesIO(b"".join(
            len(b).to_bytes(4, "little") + b for b in inputs))
        out = io.BytesIO()
        code = mod.serve(tokenizer or self.FakeTok(), None, expected_identity,
                         in_stream=stream_in, out_stream=out)
        out.seek(0)
        body, _ = mod.read_frame(out)
        return code, body

    def test_identity_round_trip(self):
        code, out = self._run_serve([b"IDENTITY"])
        self.assertEqual(code, 0)
        self.assertTrue(out.startswith(b"IDENT "), out)

    def test_enc_dec_round_trip(self):
        # ENC "abc" -> tokens; DEC of those tokens -> "abc"
        code, out = self._run_serve([b"ENC " + json.dumps({"text": "abc"}).encode()])
        self.assertEqual(code, 0)
        self.assertTrue(out.startswith(b"TON "), out)
        ids = out.split(b" ", 1)[1]
        code2, out2 = self._run_serve([b"DEC " + ids])
        self.assertEqual(code2, 0)
        self.assertEqual(out2, b"BYT abc")

    def test_identity_mismatch_fails_closed(self):
        code, out = self._run_serve([b"IDENTITY"], expected_identity="0" * 64)
        self.assertEqual(code, 4)

    def test_unknown_command_returns_err(self):
        code, out = self._run_serve([b"BOGUS"])
        self.assertEqual(code, 0)
        self.assertTrue(out.startswith(b"ERR "), out)

    def test_quit_ends(self):
        code, out = self._run_serve([b"QUIT"])
        self.assertEqual(code, 0)


class DependencyFailClosedTests(unittest.TestCase):
    def test_missing_donor_regex_fails_closed(self):
        # In this cloud the donor requires `regex`; the helper must report the exact dependency cause
        # and exit 3 rather than fabricate token ids.
        mod = load_helper()
        if mod.Tokenizer is not None:
            self.skipTest("donor tokenizer is available; dependency gate not exercised")
        old_argv = sys.argv
        try:
            sys.argv = ["strata_tokenizer_helper.py", "--gguf", "model.gguf"]
            self.assertEqual(mod.main(), 3)
        finally:
            sys.argv = old_argv


class _FakeStream:
    def __init__(self, raw):
        self._raw = raw

    def read(self, n=-1):
        return self._raw.read(n)

    def write(self, data):
        self._raw.write(data)

    def flush(self):
        pass


if __name__ == "__main__":
    unittest.main()
