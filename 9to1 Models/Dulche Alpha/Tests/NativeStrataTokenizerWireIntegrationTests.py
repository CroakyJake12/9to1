"""Actual C++ candidate -> production helper protocol, using synthetic CPU inputs.

The independent native allocation first compiles the ACTUAL candidate translation unit with
STRATA_HAS_CUDA=0 and empty STRATA_NATIVE_EXT. This suite never compiles/downloads, calls a model,
or accesses a desktop. Required environment:
  THUNDER_TOKENIZER_WIRE_WORKER=/absolute/compiled/worker
  THUNDER_TOKENIZER_WIRE_EVIDENCE=/absolute/retained/evidence
Run with the real pinned Jinja2 3.1.6 runtime: python3 -B <this-file>.

Only tokenizer byte->id mapping and resident engine output are synthetic. Real C++ serialization,
helper framing/ENC validation, Jinja sandbox/rendering, DEC, error recovery, cancel and shutdown
execute. This does NOT verify donor BPE, model weights, CUDA or inference performance. Missing
binary/renderer/evidence is an explicit preflight ERROR, never a pass/skip. Every fixture, frame,
audit and stderr log is retained on success AND failure under the supplied evidence directory.
"""
import contextlib
import hashlib
import importlib.metadata
import json
import os
import pathlib
import select
import struct
import subprocess
import sys
import tempfile
import time
import unittest

HELPER = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata" / "strata_tokenizer_helper.py"
LIMIT = 16 * 1024 * 1024
TEMPLATE = ("{% for m in messages %}[{{ m.role }}{% if m.name is defined %}:{{ m.name }}{% endif %}]"
            "{{ m.content }}\n{% endfor %}{% if add_generation_prompt %}[assistant]{% endif %}")

# Inject only a synthetic tokenizer into the actual helper; never replace render_messages/Jinja.
HELPER_FIXTURE = r'''
import importlib.util, json, os
spec = importlib.util.spec_from_file_location("production_tokenizer_helper", os.environ["WIRE_HELPER_SOURCE"])
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
class Tokenizer:
    pre = "wire-fixture"
    tokens = [chr(i) for i in range(256)]
    special_tokens = {}
    chat_template = os.environ["WIRE_CHAT_TEMPLATE"]
    def encode(self, text, parse_special=False):
        with open(os.environ["WIRE_HELPER_AUDIT"], "a", encoding="utf-8") as audit:
            audit.write(json.dumps({"text": text, "parse_special": parse_special}, ensure_ascii=False) + "\n")
        return list(text.encode("utf-8"))
    def token_bytes(self, token):
        return bytes([token])
raise SystemExit(helper.serve(Tokenizer(), None, None))
'''
ENGINE_FIXTURE = r'''
import json, os, sys
modes = os.environ["WIRE_ENGINE_MODES"].split(",")
request, active_prompt = 0, None
def record(text):
    with open(os.environ["WIRE_ENGINE_AUDIT"], "a", encoding="utf-8") as audit:
        audit.write(json.dumps(text) + "\n")
def done(prompt, finish="stop", generated=2):
    print("DONE %d %d 1 2 %s 0 0 0" % (generated, prompt, finish), flush=True)
print("READY 4096 stop", flush=True)
for line in sys.stdin:
    line = line.rstrip("\n")
    record(line)
    if line.startswith("GEN "):
        ids = line.rsplit(" ", 1)[1].split(",")
        mode = modes[min(request, len(modes) - 1)]
        request += 1
        if mode == "cancel":
            active_prompt = len(ids)
        elif mode == "error":
            print("ERR fixture generation refused", flush=True)
        else:
            print("T 79\nT 75", flush=True)
            done(len(ids))
    elif line == "STOP" and active_prompt is not None:
        done(active_prompt, "cancel", 0)
        active_prompt = None
    elif line == "QUIT":
        break
'''


def text(value):
    raw = value.encode("utf-8")
    return struct.pack("<I", len(raw)) + raw


def generation(messages):
    out = bytearray(struct.pack("<II", 2, len(messages)))
    for role, content, name in messages:
        out.extend(struct.pack("<I", role) + text(content) + text(name) + struct.pack("<I", 0))
    out.extend(struct.pack("<IddIQ", 8, 0.7, 1.0, 0, 17))
    out.extend(struct.pack("<I", 0) + text("medium"))
    return out


class NativeTokenizerWireTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        worker = os.environ.get("THUNDER_TOKENIZER_WIRE_WORKER")
        if not worker or not pathlib.Path(worker).is_absolute() or not os.access(worker, os.X_OK):
            raise RuntimeError("native preflight: THUNDER_TOKENIZER_WIRE_WORKER must identify the externally compiled candidate")
        try:
            import jinja2  # noqa: F401 - real renderer required in the helper child
            renderer_version = importlib.metadata.version("Jinja2")
        except ImportError as error:
            raise RuntimeError("native preflight: real pinned Jinja2 runtime required; no renderer substitute") from error
        if renderer_version != "3.1.6":
            raise RuntimeError("native preflight: expected pinned Jinja2 3.1.6, observed " + renderer_version)
        evidence = os.environ.get("THUNDER_TOKENIZER_WIRE_EVIDENCE")
        if not evidence or not pathlib.Path(evidence).is_absolute():
            raise RuntimeError("native preflight: absolute retained THUNDER_TOKENIZER_WIRE_EVIDENCE required")
        cls.evidence = pathlib.Path(evidence)
        cls.evidence.mkdir(parents=True, exist_ok=True)
        cls.worker = worker
        identity = {"worker": worker, "worker_sha256": hashlib.sha256(pathlib.Path(worker).read_bytes()).hexdigest(),
                    "helper": str(HELPER), "helper_sha256": hashlib.sha256(HELPER.read_bytes()).hexdigest(),
                    "test_sha256": hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),
                    "python": sys.executable, "python_version": sys.version, "Jinja2": renderer_version,
                    "scope": "CPU synthetic tokenizer/engine, actual producer/consumer; no GPU/model performance"}
        with (cls.evidence / "source-runtime-identity.json").open("x", encoding="utf-8") as receipt:
            json.dump(identity, receipt, indent=2)

    @contextlib.contextmanager
    def session(self, *, modes="normal", template=TEMPLATE):
        # Deliberately never remove these test-owned files, including on assertion/process errors.
        self.folder = pathlib.Path(tempfile.mkdtemp(prefix=self._testMethodName + "-", dir=self.evidence))
        engine = self.folder / "engine"
        engine.write_text("#!" + sys.executable + "\n" + ENGINE_FIXTURE, encoding="utf-8")
        engine.chmod(0o700)
        helper = self.folder / "helper.py"
        helper.write_text(HELPER_FIXTURE, encoding="utf-8")
        model = self.folder / "synthetic-model.gguf"
        model.write_bytes(b"protocol fixture; never parsed as model weights")
        self.engine_audit, self.helper_audit = self.folder / "engine.jsonl", self.folder / "helper.jsonl"
        environment = dict(os.environ, STRATA_ENGINE_PATH=str(engine), STRATA_HELPER_PATH=str(helper),
                           STRATA_MODEL_PATH=str(model), STRATA_PYTHON=sys.executable,
                           WIRE_HELPER_SOURCE=str(HELPER), WIRE_CHAT_TEMPLATE=template,
                           WIRE_ENGINE_MODES=modes, WIRE_HELPER_AUDIT=str(self.helper_audit),
                           WIRE_ENGINE_AUDIT=str(self.engine_audit), PYTHONDONTWRITEBYTECODE="1")
        environment.pop("STRATA_HELPER_DIR", None)
        with (self.folder / "worker.stderr").open("wb") as errors:
            self.proc = subprocess.Popen([self.worker], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                         stderr=errors, env=environment, bufsize=0)
            try:
                kind, hello = self.receive()
                self.assertEqual(kind, 0)
                self.assertEqual(struct.unpack_from("<I", hello)[0], 2)
                load = (struct.pack("<I", 1) + text(str(self.folder)) + text("qwen35moe")
                        + struct.pack("<I", 4096) + text("") + struct.pack("<II", 1, 0))
                self.send(load)
                self.assertEqual(self.receive(), (4, struct.pack("<II", 0, 0)))
                yield self
                self.send(struct.pack("<I", 3))
                self.assertEqual(self.receive(), (3, b""))
                self.assertEqual(self.proc.wait(timeout=5), 0)
            finally:
                if self.proc.poll() is None:
                    # Ask the actual owner to unwind/retire its children, even after assertion failure.
                    try:
                        self.proc.stdin.write(struct.pack("<II", 4, 3))
                        self.proc.stdin.flush()
                    except (BrokenPipeError, OSError):
                        pass
                    self.proc.stdin.close()
                    try:
                        self.proc.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        self.proc.kill()  # only this exact test-owned process
                        self.proc.wait(timeout=5)
                else:
                    self.proc.stdin.close()
                self.proc.stdout.close()

    def send(self, body):
        self.assertLessEqual(len(body), LIMIT)
        framed = struct.pack("<I", len(body)) + body
        with (self.folder / "host-input.bin").open("ab") as capture:
            capture.write(framed)
        pending = memoryview(framed)
        while pending:
            written = self.proc.stdin.write(pending)
            self.assertTrue(written, "native worker input closed")
            pending = pending[written:]
        self.proc.stdin.flush()

    def read_exact(self, count):
        result, deadline = bytearray(), time.monotonic() + 8
        while len(result) < count:
            remaining = deadline - time.monotonic()
            self.assertGreater(remaining, 0, "native response exceeded bounded test deadline")
            ready, _, _ = select.select([self.proc.stdout], [], [], remaining)
            self.assertTrue(ready, "native response timed out")
            chunk = os.read(self.proc.stdout.fileno(), count - len(result))
            self.assertTrue(chunk, "native worker ended before expected frame")
            result.extend(chunk)
        with (self.folder / "worker-output.bin").open("ab") as capture:
            capture.write(result)
        return bytes(result)

    def receive(self):
        kind, count = struct.unpack("<II", self.read_exact(8))
        self.assertLessEqual(count, LIMIT)
        return kind, self.read_exact(count)

    def success(self, *, expected_prompt, stopped=False):
        output = bytearray()
        while True:
            kind, payload = self.receive()
            if kind != 1:
                break
            self.assertGreaterEqual(len(payload), 4)
            self.assertEqual(struct.unpack_from("<I", payload)[0], len(payload) - 4)
            output.extend(payload[4:])
        self.assertEqual(kind, 2, payload)
        expected = struct.pack("<IIIQQQddIII", 0, 0, 0, expected_prompt, expected_prompt,
                               0 if stopped else 2, 0.001, 0.002, 0, 0, int(stopped))
        self.assertEqual(payload, expected)  # exact terminal fields, including no trailing bytes
        self.assertEqual(bytes(output), b"" if stopped else b"OK")

    def failure(self, cause):
        kind, payload = self.receive()
        self.assertEqual(kind, 5)
        self.assertEqual(struct.unpack_from("<II", payload), (3, 1))
        size = struct.unpack_from("<I", payload, 8)[0]
        self.assertEqual(len(payload), 12 + size)
        self.assertIn(cause, payload[12:].decode("utf-8"))

    def audit(self, path):
        if not path.exists():
            return []
        # Polling cannot mistake a partially written JSON row for a completed engine event.
        return [json.loads(row) for row in path.read_text(encoding="utf-8").splitlines(keepends=True)
                if row.endswith("\n")]

    def test_actual_producer_preserves_roles_names_controls_and_unicode(self):
        rows = [(0, 'quote" slash\\ and\t', ""), (1, "héllo 🙂\nnull\x00 and\rreturn", ""),
                (2, "", ""), (3, "result", 'look"up')]
        expected = ("[system]" + rows[0][1] + "\n[user]" + rows[1][1]
                    + '\n[assistant]\n[tool:look"up]result\n[assistant]')
        with self.session():
            self.send(generation(rows))
            self.success(expected_prompt=len(expected.encode("utf-8")))
            self.assertEqual(self.audit(self.helper_audit), [{"text": expected, "parse_special": True}])
            engine = self.audit(self.engine_audit)
            self.assertEqual(len(engine), 1)
            self.assertEqual([int(v) for v in engine[0].rsplit(" ", 1)[1].split(",")],
                             list(expected.encode("utf-8")))

    def test_valid_empty_content_and_following_generation(self):
        with self.session():
            for content in ("", "next"):
                expected = "[user]" + content + "\n[assistant]"
                self.send(generation([(1, content, "")]))
                self.success(expected_prompt=len(expected))
            self.assertEqual(len(self.audit(self.helper_audit)), 2)

    def test_invalid_role_is_request_scoped_and_does_not_reach_helper(self):
        with self.session():
            self.send(generation([(99, "foreign", "")]))
            self.failure("Unknown native chat role")
            self.assertEqual(self.audit(self.helper_audit), [])
            self.assertEqual(self.audit(self.engine_audit), [])
            expected = "[user]after\n[assistant]"
            self.send(generation([(1, "after", "")]))
            self.success(expected_prompt=len(expected))

    def test_actual_template_refusal_does_not_send_engine_gen(self):
        with self.session(template="{{ raise_exception('fixture template refused') }}"):
            self.send(generation([(1, "hello", "")]))
            self.failure("chat template render refused the request: fixture template refused")
            self.assertEqual(self.audit(self.helper_audit), [])
            self.assertEqual(self.audit(self.engine_audit), [])

    def test_engine_error_retains_loaded_session_and_helper(self):
        with self.session(modes="error,normal"):
            self.send(generation([(1, "first", "")]))
            self.failure("Engine reported generation error: fixture generation refused")
            expected = "[user]second\n[assistant]"
            self.send(generation([(1, "second", "")]))
            self.success(expected_prompt=len(expected))
            self.assertEqual(len(self.audit(self.helper_audit)), 2)
            self.assertEqual(len(self.audit(self.engine_audit)), 2)

    def test_cancel_and_shutdown_keep_actual_protocol(self):
        with self.session(modes="cancel,normal"):
            expected = "[user]waiting\n[assistant]"
            self.send(generation([(1, "waiting", "")]))
            deadline = time.monotonic() + 5
            while not self.audit(self.engine_audit):
                self.assertLess(time.monotonic(), deadline, "engine did not receive actual GEN")
                time.sleep(0.01)
            self.send(struct.pack("<I", 5))
            self.success(expected_prompt=len(expected), stopped=True)
            self.assertIn("STOP", self.audit(self.engine_audit))
            expected = "[user]after\n[assistant]"
            self.send(generation([(1, "after", "")]))
            self.success(expected_prompt=len(expected))

    def test_escaped_payload_bound_fails_before_write_and_helper_recovers(self):
        with self.session():
            # Valid host frame <16MiB; escaping control bytes crosses helper's same limit.
            self.send(generation([(1, "\x00" * (LIMIT // 6), "")]))
            self.failure("Tokenizer helper request exceeds frame limit")
            self.assertEqual(self.audit(self.helper_audit), [])
            self.assertEqual(self.audit(self.engine_audit), [])
            expected = "[user]after\n[assistant]"
            self.send(generation([(1, "after", "")]))
            self.success(expected_prompt=len(expected))


if __name__ == "__main__":
    unittest.main()
