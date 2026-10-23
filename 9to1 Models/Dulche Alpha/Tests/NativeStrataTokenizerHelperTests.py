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
    is not installed in this cloud, the load fails closed with exit 3; successful startup also
    requires the sibling dependency lock and exact importable distribution pins. Synthetic startup
    controls below separately exercise successful IDENTITY without requiring installed dependencies.
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
        import types
        from unittest.mock import patch
        mod = load_helper()
        with tempfile.TemporaryDirectory() as tmp:
            d = self._make_fixture(tmp)
            old_argv = sys.argv
            try:
                sys.argv = ["strata_tokenizer_helper.py", "--tokenizer-dir", str(d)]
                try:
                    mod.verify_runtime_dependency_versions()
                    dependencies_available = True
                except ValueError:
                    dependencies_available = False
                # Empty bounded stdio prevents a donor-present host from waiting on real stdin.
                with patch.object(sys, "stdin", types.SimpleNamespace(buffer=io.BytesIO())):
                    expected = 0 if mod._BUNDLED_DONOR_AVAILABLE and dependencies_available else 3
                    self.assertEqual(mod.main(), expected)
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


class RuntimeDependencyAdmissionTests(unittest.TestCase):
    """Synthetic distribution metadata controls around the actual main/stdio boundary.

    No installed dependency, real donor implementation, native worker or model is used. Matching
    versions establish admission only; these controls do not authenticate distribution bytes.
    """

    @staticmethod
    def _lock():
        return {"schema": "strata-runtime-dependencies/v1", "runtimeModules": {
            "regex": {"pin": "2026.9.29"}, "jinja2": {"pin": "3.1.6"}}}

    def _run_main(self, lock_bytes=None, versions=None, lock_writer=None, helper_root=None,
                  unavailable_import=None, missing_sandbox=False):
        import contextlib
        import importlib.metadata
        import tempfile
        import types
        from unittest.mock import patch

        mod = load_helper()
        observed = versions if versions is not None else {"regex": "2026.9.29", "jinja2": "3.1.6"}
        loads, version_requests = [], []

        def version(name):
            version_requests.append(name)
            if name not in observed:
                raise importlib.metadata.PackageNotFoundError(name)
            return observed[name]

        def runtime_import(name):
            if name == unavailable_import:
                raise ImportError("synthetic missing runtime implementation: " + name)
            if name == "jinja2.sandbox":
                return types.SimpleNamespace() if missing_sandbox else types.SimpleNamespace(
                    ImmutableSandboxedEnvironment=object)
            return types.ModuleType(name)

        class SyntheticTokenizer(ServeProtocolTests.FakeTok):
            def __init__(self, *args):
                loads.append(args)

        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            tokenizer = TokenzierDirLoadTests()._make_fixture(root)
            bundled = root / "held-helper"
            bundled.mkdir()
            lock_path = bundled / "strata-runtime-dependencies.json"
            if lock_writer is not None:
                lock_writer(lock_path)
            elif helper_root is None:
                lock_path.write_bytes(lock_bytes if lock_bytes is not None else
                                      json.dumps(self._lock()).encode("utf-8"))
            incoming, outgoing, diagnostic = io.BytesIO(), io.BytesIO(), io.StringIO()
            for body in (b"IDENTITY", b'ENC {"text":"abc"}'):
                mod.write_frame(incoming, body)
            incoming.seek(0)
            with patch.object(mod, "_helper_dir", pathlib.Path(helper_root) if helper_root else bundled), \
                    patch.object(mod, "_BUNDLED_DONOR_AVAILABLE", True), \
                    patch.object(mod, "Tokenizer", SyntheticTokenizer), \
                    patch.object(mod, "PromptEncoder", lambda tok: object()), \
                    patch.object(mod, "import_module", side_effect=runtime_import, create=True), \
                    patch.object(importlib.metadata, "version", side_effect=version), \
                    patch.object(sys, "argv", ["strata_tokenizer_helper.py", "--tokenizer-dir", str(tokenizer)]), \
                    patch.object(sys, "stdin", types.SimpleNamespace(buffer=incoming)), \
                    patch.object(sys, "stdout", types.SimpleNamespace(buffer=outgoing)), \
                    contextlib.redirect_stderr(diagnostic):
                code = mod.main()
            return code, outgoing.getvalue(), diagnostic.getvalue(), loads, version_requests

    def _assert_refused(self, result, cause):
        code, output, diagnostic, loads, _ = result
        self.assertEqual(code, 3, "LOCK_PIN_ADMISSION_REQUIRED: incompatible startup reached the protocol")
        self.assertEqual(output, b"", "refusal must precede IDENTITY and token output")
        self.assertEqual(loads, [], "refusal must precede model-tokenizer construction")
        self.assertIn("dependency:", diagnostic)
        self.assertIn(cause, diagnostic)

    def test_exact_locked_versions_preserve_identity_and_encode_protocol(self):
        code, output, diagnostic, loads, requests = self._run_main()
        self.assertEqual((code, diagnostic), (0, ""))
        stream = io.BytesIO(output)
        mod = load_helper()
        self.assertTrue(mod.read_frame(stream)[0].startswith(b"IDENT "))
        self.assertEqual(mod.read_frame(stream)[0], b"TON 0,1,2")
        self.assertEqual(mod.read_frame(stream), (b"", -1))
        self.assertEqual(len(loads), 1)
        self.assertEqual(requests, ["regex", "jinja2"])

    def test_historical_regex_mismatch_refuses_before_identity(self):
        self._assert_refused(self._run_main(versions={"regex": "2026.9.10", "jinja2": "3.1.6"}),
                             "regex version mismatch: required 2026.9.29; observed 2026.9.10")

    def test_jinja2_mismatch_refuses_before_identity(self):
        self._assert_refused(self._run_main(versions={"regex": "2026.9.29", "jinja2": "3.1.2"}),
                             "jinja2 version mismatch: required 3.1.6; observed 3.1.2")

    def test_missing_distribution_is_precise_and_refuses_before_identity(self):
        for missing in ("regex", "jinja2"):
            with self.subTest(missing=missing):
                versions = {"regex": "2026.9.29", "jinja2": "3.1.6"}
                del versions[missing]
                self._assert_refused(self._run_main(versions=versions),
                                     "runtime dependency " + missing + " distribution is unavailable")

    def test_matching_metadata_without_importable_implementations_refuses(self):
        for name in ("regex", "jinja2", "jinja2.sandbox"):
            with self.subTest(module=name):
                result = self._run_main(unavailable_import=name)
                self._assert_refused(result, "synthetic missing runtime implementation: " + name)
                cause = "jinja2 sandbox" if name == "jinja2.sandbox" else name + " import"
                self.assertIn("runtime dependency " + cause + " is unavailable", result[2])
        self._assert_refused(self._run_main(missing_sandbox=True),
                             "ImmutableSandboxedEnvironment is unavailable")

    def test_missing_lock_refuses_before_identity(self):
        self._assert_refused(self._run_main(lock_writer=lambda path: None),
                             "runtime dependency lock is unavailable")

    def test_invalid_lock_json_duplicate_keys_and_schema_refuse(self):
        cases = [(b"{", "invalid JSON"), (b"\xff", "invalid JSON"),
                 (b'{"schema":"strata-runtime-dependencies/v1","schema":"strata-runtime-dependencies/v1"}',
                  "duplicate JSON key"),
                 (json.dumps({**self._lock(), "schema": "strata-runtime-dependencies/v2"}).encode(),
                  "unsupported schema"),
                 (b"[]", "unsupported schema")]
        for blob, cause in cases:
            with self.subTest(blob=blob):
                self._assert_refused(self._run_main(lock_bytes=blob), cause)

    def test_lock_requires_exact_known_modules_and_nonempty_bounded_pins(self):
        cases = []
        for name in ("regex", "jinja2"):
            lock = self._lock()
            del lock["runtimeModules"][name]
            cases.append((lock, "must name exactly regex and jinja2"))
        lock = self._lock()
        lock["runtimeModules"]["unknown"] = {"pin": "1.0"}
        cases.append((lock, "must name exactly regex and jinja2"))
        for pin in (None, "", " 3.1.6", "3.1.6\n", "x" * 129):
            lock = self._lock()
            lock["runtimeModules"]["jinja2"]["pin"] = pin
            cases.append((lock, "jinja2 has an invalid version pin"))
        for lock, cause in cases:
            with self.subTest(lock=lock):
                self._assert_refused(self._run_main(lock_bytes=json.dumps(lock).encode()), cause)

    def test_oversized_and_nonregular_lock_refuse(self):
        self._assert_refused(self._run_main(lock_bytes=b" " * (16 * 1024 + 1)),
                             "runtime dependency lock exceeds 16384 bytes")
        self._assert_refused(self._run_main(lock_writer=lambda path: path.mkdir()),
                             "runtime dependency lock is not a regular file")

    @unittest.skipUnless(sys.platform == "linux", "Linux held-directory descriptor contract")
    def test_lexical_held_directory_lock_survives_original_path_replacement(self):
        import os
        import tempfile
        with tempfile.TemporaryDirectory() as tmp:
            original = pathlib.Path(tmp) / "original"
            original.mkdir()
            (original / "strata-runtime-dependencies.json").write_text(json.dumps(self._lock()))
            fd = os.open(original, os.O_RDONLY | os.O_DIRECTORY)
            try:
                original.rename(pathlib.Path(tmp) / "held")
                original.mkdir()
                (original / "strata-runtime-dependencies.json").write_text("invalid replacement")
                code, output, diagnostic, loads, requests = self._run_main(helper_root=f"/proc/{os.getpid()}/fd/{fd}")
                self.assertEqual((code, diagnostic, len(loads), requests), (0, "", 1, ["regex", "jinja2"]))
                self.assertTrue(output[4:].startswith(b"IDENT "))
            finally:
                os.close(fd)


if __name__ == "__main__":
    unittest.main()
