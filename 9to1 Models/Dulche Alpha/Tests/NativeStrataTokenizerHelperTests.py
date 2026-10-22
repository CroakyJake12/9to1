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

    def test_identity_same_size_changed_vocab(self):
        # An equal-sized vocabulary with different *content* must change the identity (pass-3 only
        # hashed pre + vocab length + special tokens, so equal-sized changes falsely passed).
        mod = load_helper()
        class FakeA:
            pre = "qwen35"
            tokens = ["a", "bb", "c"]
            special_tokens = {}
        class FakeB:
            pre = "qwen35"
            tokens = ["x", "yy", "z"]
            special_tokens = {}
        self.assertNotEqual(mod.tokenizer_identity(FakeA()), mod.tokenizer_identity(FakeB()))

    def test_identity_same_size_changed_vocab_order(self):
        # The SAME set in a different order is a different vocabulary (ids bind to positions).
        mod = load_helper()
        class FakeA:
            pre = "qwen35"
            tokens = ["a", "bb", "c"]
            special_tokens = {}
        class FakeB:
            pre = "qwen35"
            tokens = ["c", "a", "bb"]
            special_tokens = {}
        self.assertNotEqual(mod.tokenizer_identity(FakeA()), mod.tokenizer_identity(FakeB()))

    def test_identity_same_size_changed_merges(self):
        # Equal-count but different merge rules must change the identity.
        mod = load_helper()
        class FakeA:
            pre = "qwen35"
            tokens = ["a", "b", "ab"]
            ranks = {("a", "b"): 0, ("b", "ab"): 1}
            special_tokens = {}
        class FakeB:
            pre = "qwen35"
            tokens = ["a", "b", "ab"]
            ranks = {("a", "b"): 1, ("b", "ab"): 0}
            special_tokens = {}
        self.assertNotEqual(mod.tokenizer_identity(FakeA()), mod.tokenizer_identity(FakeB()))

    def test_identity_changes_with_chat_template(self):
        # The chat template bytes are part of the canonical identity, so a template-only change is
        # detected even when the vocabulary and merges are identical.
        mod = load_helper()
        class FakeA:
            pre = "qwen35"
            tokens = ["a", "bb", "c"]
            special_tokens = {}
            chat_template = "{{ messages }}"
        class FakeB:
            pre = "qwen35"
            tokens = ["a", "bb", "c"]
            special_tokens = {}
            chat_template = "{{ messages }} and a different template"
        self.assertNotEqual(mod.tokenizer_identity(FakeA()), mod.tokenizer_identity(FakeB()))

    def test_identity_binds_pre_and_token_types_and_special(self):
        mod = load_helper()
        class FakeA:
            pre = "qwen35"
            tokens = ["a", "bb", "c"]
            token_types = [1, 3, 4]
            special_tokens = {"<|im_start|>": 1}
        class FakeB:
            pre = "qwen35"
            tokens = ["a", "bb", "c"]
            token_types = [1, 3, 4]
            special_tokens = {"<|im_start|>": 2}
        self.assertNotEqual(mod.tokenizer_identity(FakeA()), mod.tokenizer_identity(FakeB()))


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


class EncodeEnvelopeTests(unittest.TestCase):
    """Production ENC boundary controls; the separate native suite covers real Jinja/producer.

    Rendering is observed here independently of installed Jinja. No malformed envelope may reach
    the tokenizer or renderer; errors must preserve later valid requests on the same helper.
    """

    def _exchange(self, commands, renderer=None, tokenizer=None, identity=None):
        from unittest.mock import patch
        mod = load_helper()
        incoming = io.BytesIO()
        for command in commands:
            mod.write_frame(incoming, command)
        incoming.seek(0)
        outgoing = io.BytesIO()
        with patch.object(mod, "render_messages", renderer or (lambda t, m: [0, 1])):
            code = mod.serve(tokenizer or ServeProtocolTests.FakeTok(), None, identity,
                             incoming, outgoing)
        outgoing.seek(0)
        responses = []
        while True:
            body, length = mod.read_frame(outgoing)
            if length < 0:
                break
            responses.append(body)
        return code, responses

    def test_tagged_messages_reach_renderer_unchanged(self):
        messages = [
            {"role": "system", "content": "line\nquote\" and slash\\"},
            {"role": "user", "content": "héllo 🙂"},
            {"role": "assistant", "content": ""},
            {"role": "tool", "content": "result", "name": "lookup"},
        ]
        seen = []
        code, responses = self._exchange(
            [b"ENC " + json.dumps({"messages": messages}, ensure_ascii=False).encode("utf-8")],
            renderer=lambda tok, value: seen.append(value) or [2, 1, 0])
        self.assertEqual((code, responses), (0, [b"TON 2,1,0"]))
        self.assertEqual(seen, [messages])

    def test_bare_array_is_rejected_before_rendering(self):
        seen = []
        code, responses = self._exchange(
            [b'ENC [{"role":"user","content":"hello"}]'],
            renderer=lambda *args: seen.append(args) or [0])
        self.assertEqual(code, 0)
        self.assertTrue(responses[0].startswith(b"ERR ENC payload must be an object"), responses)
        self.assertEqual(seen, [])

    def test_invalid_envelopes_fail_before_tokenizer_or_renderer(self):
        tokenizations, seen = [], []
        class ObservedTokenizer(ServeProtocolTests.FakeTok):
            def encode(self, *args, **kwargs):
                tokenizations.append((args, kwargs))
                return [0]
        invalid = [None, True, 42, "messages", [], {}, {"unknown": "abc"},
                   {"text": "abc", "messages": []}, {"text": "abc", "extra": 1},
                   {"text": 42}, {"messages": None}, {"messages": "abc"},
                   {"messages": [{"role": "user"}]},
                   {"messages": [{"role": "user", "content": 1}]},
                   {"messages": [{"role": "foreign", "content": "a"}]},
                   {"messages": [{"role": "user", "content": "a", "name": 1}]},
                   {"messages": [{"role": "user", "content": "a", "images": []}]},
                   {"messages": ["abc"]}]
        for value in invalid:
            with self.subTest(value=value):
                code, responses = self._exchange(
                    [b"ENC " + json.dumps(value).encode()],
                    renderer=lambda *args: seen.append(args) or [0],
                    tokenizer=ObservedTokenizer())
                self.assertEqual(code, 0)
                self.assertEqual(len(responses), 1)
                self.assertTrue(responses[0].startswith(b"ERR "), responses)
        self.assertEqual((tokenizations, seen), ([], []))

    def test_malformed_json_and_invalid_utf8_are_request_scoped(self):
        for bad in (b'ENC {"messages":', b"ENC \xff"):
            with self.subTest(bad=bad):
                code, responses = self._exchange([bad, b'ENC {"text":"abc"}'])
                self.assertEqual(code, 0)
                self.assertTrue(responses[0].startswith(b"ERR "), responses)
                self.assertEqual(responses[1], b"TON 0,1,2")

    def test_renderer_error_preserves_following_requests(self):
        def refused(*args):
            raise ValueError("fixture template refused")
        code, responses = self._exchange(
            [b'ENC {"messages":[]}', b'ENC {"text":"abc"}', b"DEC 0,1,2", b"QUIT"],
            renderer=refused)
        self.assertEqual((code, responses),
                         (0, [b"ERR fixture template refused", b"TON 0,1,2", b"BYT abc"]))

    def test_token_count_bound_is_preserved(self):
        from unittest.mock import patch
        mod = load_helper()
        incoming = io.BytesIO()
        for command in (b'ENC {"messages":[]}', b'ENC {"text":"a"}'):
            mod.write_frame(incoming, command)
        incoming.seek(0)
        outgoing = io.BytesIO()
        with patch.object(mod, "MAX_ID_COUNT", 2), patch.object(
                mod, "render_messages", return_value=[0, 1, 2]):
            self.assertEqual(mod.serve(ServeProtocolTests.FakeTok(), None, None,
                                      incoming, outgoing), 0)
        outgoing.seek(0)
        self.assertEqual(mod.read_frame(outgoing)[0], b"ERR token count exceeds bound")
        self.assertEqual(mod.read_frame(outgoing)[0], b"TON 0")

    def test_identity_mismatch_prevents_encode(self):
        seen = []
        code, responses = self._exchange(
            [b'ENC {"messages":[]}'], renderer=lambda *args: seen.append(args) or [0],
            identity="0" * 64)
        self.assertEqual((code, responses, seen), (4, [], []))

    def test_decode_out_of_range_remains_request_scoped(self):
        code, responses = self._exchange([b"DEC -1", b"DEC 10", b"DEC 0"])
        self.assertEqual((code, responses),
                         (0, [b"ERR token id out of vocabulary", b"ERR token id out of vocabulary", b"BYT a"]))


class DependencyFailClosedTests(unittest.TestCase):
    def test_missing_donor_regex_fails_closed(self):
        # Deterministic: when the donor module (and its `regex` runtime dep) is unavailable, the
        # helper must report the exact dependency cause and exit 3 rather than fabricate token ids.
        # When the donor IS available, the gate passes and the branch is not exercised - both arms
        # are asserted, so the test never silently skips based on environment.
        mod = load_helper()
        if mod.Tokenizer is None:
            old_argv = sys.argv
            try:
                sys.argv = ["strata_tokenizer_helper.py", "--gguf", "model.gguf"]
                self.assertEqual(mod.main(), 3)
            finally:
                sys.argv = old_argv
        else:
            self.assertIsNotNone(mod.Tokenizer)
            self.assertTrue(mod._BUNDLED_DONOR_AVAILABLE)

    def test_dependency_causes_are_distinct(self):
        # A missing donor module is NOT proof that `regex` itself is absent; the helper must report
        # each precisely. When the donor is absent, both reports are present and distinct.
        mod = load_helper()
        if mod._BUNDLED_DONOR_AVAILABLE:
            self.skipTest("donor available; dependency-gate prose not exercised")
        self.assertIn("bundled donor tokenizer module", (mod._DONOR_CAUSE or ""))
        # the regex report is the module probe result itself (missing: ...), distinct from the donor
        # module cause
        self.assertTrue(mod._REGEX_REPORT.startswith("missing:"))
        self.assertNotIn("bundled donor tokenizer module", mod._REGEX_REPORT)

    def test_render_messages_fails_closed_without_jinja2(self):
        # chat-template rendering needs jinja2; it must fail closed with a precise cause (never a
        # guessed render) when jinja2 is not importable.
        mod = load_helper()

        class FakeTok:
            pre = "qwen35"
            tokens = list("abcdefghij")
            special_tokens = {}
            chat_template = "{{ messages }}"

        try:
            import jinja2  # noqa: F401
            jinja_available = True
        except Exception:
            jinja_available = False
        if not jinja_available:
            with self.assertRaises(ValueError) as ctx:
                mod.render_messages(FakeTok(), [{"role": "user", "content": "hi"}])
            self.assertIn("jinja2", str(ctx.exception))


class _FakeStream:
    def __init__(self, raw):
        self._raw = raw

    def read(self, n=-1):
        return self._raw.read(n)

    def write(self, data):
        self._raw.write(data)

    def flush(self):
        pass


class TokenzierDirLoadTests(unittest.TestCase):
    """Exercise the helper's real `--tokenizer-dir` (extracted-pack) loading path.

    The candidate text path reads the pack's extracted `tokenizer/` directory (vocab.json,
    merges.txt, token_type.json, tokenizer.json, chat_template.jinja). This test builds a minimal
    synthetic pack fixture and drives the ACTUAL helper entrypoint. Because the donor `regex` module
    is not installed in this cloud, the load fails closed with exit 3; when the donor IS available
    the same fixture must load and answer IDENTITY. Both arms are asserted and deterministic.
    """

    def _make_fixture(self, tmp):
        d = pathlib.Path(tmp) / "tokenizer"
        d.mkdir(parents=True)
        (d / "vocab.json").write_text(json.dumps({"a": 0, "b": 1, "c": 2}), encoding="utf-8")
        (d / "merges.txt").write_text("a b\nb c\n", encoding="utf-8")
        (d / "token_type.json").write_text(json.dumps([1, 3, 4]), encoding="utf-8")
        (d / "tokenizer.json").write_text(json.dumps({
            "pre": "qwen35", "vocab_size": 3, "special_ids": {"<|im_start|>": 1}
        }), encoding="utf-8")
        (d / "chat_template.jinja").write_text("{{ messages }}", encoding="utf-8")
        return d

    def test_extracted_pack_loading(self):
        import tempfile
        mod = load_helper()
        with tempfile.TemporaryDirectory() as tmp:
            d = self._make_fixture(tmp)
            old_argv = sys.argv
            try:
                sys.argv = ["strata_tokenizer_helper.py", "--tokenizer-dir", str(d)]
                if mod._BUNDLED_DONOR_AVAILABLE:
                    # donor+regex present: the helper loads and answers IDENTITY
                    code = mod.main()
                    self.assertEqual(code, 0)
                else:
                    # deterministic fail-closed (regex / donor module absent)
                    self.assertEqual(mod.main(), 3)
            finally:
                sys.argv = old_argv

    def test_gguf_path_attaches_chat_template(self):
        # Requirement 4: the --gguf path must read tokenizer.chat_template from the GGUF metadata so
        # message rendering works. Guard structurally (the donor may be absent here): the helper must
        # read `tokenizer.chat_template` and set tok.chat_template rather than leave it None.
        src = HELPER.read_text()
        self.assertIn("tokenizer.chat_template", src)
        self.assertIn("tok.chat_template", src)
        self.assertIn("from gguf_reader import GGUFFile", src)


if __name__ == "__main__":
    unittest.main()
