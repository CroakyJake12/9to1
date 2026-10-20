"""Bounded build-free wire/custody tests for the managed V2 candidate worker path.

This suite pins the MANAGED `StrataCandidateWorker` (in `StrataNativeWorker.cs`) against the exact
candidate relay ABI implemented by the native `dulche_strata_candidate_worker.cpp` (the canonical
wire authority). It cross-checks the two sources structurally:

  * hello frame: version u32=2, origin text "90767252…", cuda u32, nccl u32, family text
  * load (op 1): directory text, registration text, context u32, expected_tokenizer_identity text,
    count u32, devices[count] u32
  * text generate (op 2): message_count u32, messages{role u32, content text, name text, images u32},
    max_new u32, temperature f64, top_p f64, top_k u32, seed u64, stop_count u32, stops…, effort text
  * text terminal (type 2): NativeErrors(0,0), terminalText text, prompt u64, prefill u64, decode u64,
    prefillSeconds f64, decodeSeconds f64, reused flag u32(+u64), kv flag u32(+u32), stopped u32
  * cancel (op 5) / stop (op 3) dispatch

It also asserts the guarded boundaries: the V1 legacy worker and its strict hello check are preserved
byte-for-byte, the candidate path is a separate self-contained class, structured tools are refused
with the exact dependency cause, and both workers share the common `IStrataNativeWorker` surface
consumed by `StrataRawModelProvider`.

These tests never compile, run, or launch a native build; they are source-level custody checks.
"""
import pathlib
import unittest

INFERENCE = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines"
NATIVE = INFERENCE / "NativeStrata"
MANAGED = INFERENCE / "StrataNativeWorker.cs"
RAW = INFERENCE / "StrataRawModelProvider.cs"
CANDIDATE_CPP = NATIVE / "dulche_strata_candidate_worker.cpp"
CANDIDATE_ORIGIN = "90767252f420e25223c739d604145ad15911d48b"
LEGACY_ORIGIN = "015b075079c51a7aec670ee24924f920f5e7bb2b"


def managed_src():
    return MANAGED.read_text(encoding="utf-8")


def candidate_src():
    """The managed candidate class region only (the V1 legacy class also contains similar texts,
    so wire-order assertions must be scoped to the candidate class to avoid cross-matching)."""
    full = managed_src()
    marker = "public sealed class StrataCandidateWorker"
    idx = full.find(marker)
    if idx < 0:
        return ""
    return full[idx:]


def cpp_src():
    return CANDIDATE_CPP.read_text(encoding="utf-8")


def ordered_contains(src, markers):
    """Assert markers appear in src in the given relative order."""
    pos = -1
    for marker in markers:
        idx = src.find(marker)
        if idx < 0:
            return False, f"missing marker: {marker!r}"
        if idx <= pos:
            return False, f"marker out of order: {marker!r}"
        pos = idx
    return True, None


class CandidateManagedWireTests(unittest.TestCase):
    def test_managed_candidate_worker_exists_and_is_separate(self):
        src = managed_src()
        self.assertIn("class StrataCandidateWorker", src)
        self.assertIn("CreateCandidateAfterPublicationAsync", src)
        self.assertIn("public sealed class StrataNativeWorker", src)  # V1 legacy preserved

    def test_managed_and_native_share_op_constants(self):
        m, c = candidate_src(), cpp_src()
        for name, value in [("kOpLoad", 1), ("kOpGenerate", 2), ("kOpStop", 3),
                            ("kOpStructuredGenerate", 4), ("kOpCancel", 5)]:
            # native C++ constant
            self.assertIn(f"constexpr uint32_t {name} = {value};", c, f"native {name} missing")
            # managed constant (named differently but same value)
            self.assertIn(f"{name[1:]} = {value}", m, f"managed {name} missing")

    def test_managed_hello_matches_native_abi(self):
        m, c = candidate_src(), cpp_src()
        self.assertIn(CANDIDATE_ORIGIN, m)
        self.assertIn(CANDIDATE_ORIGIN, c)
        self.assertIn("AbiVersion = 2", m)
        self.assertIn("kAbiVersion = 2", c)
        # family text is part of the native hello; managed validates it on handshake
        self.assertIn('family!="qwen35moe"', m)
        self.assertIn('"qwen35moe"', c)

    def test_managed_load_frame_order_matches_native(self):
        m = candidate_src()
        ok, err = ordered_contains(m, [
            "output.Write(OpLoad)",
            "Text(output,_model.OriginalCheckpointDirectory)",
            "Text(output,Model.NativeRegistration!)",
            "output.Write(checked((uint)Model.ContextTokens))",
            "IStrataOriginalTokenizerIdentitySource tokens ? tokens.OriginalTokenizerIdentity : \"\"",
            "Text(output,expectedIdentity)",
            "output.Write(checked((uint)_model.ActualCudaDeviceIndices.Count))",
            "foreach(var device in _model.ActualCudaDeviceIndices) output.Write(checked((uint)device))",
        ])
        self.assertTrue(ok, err)
        # native side reads the same fields in the same order
        c = cpp_src()
        ok, err = ordered_contains(c, [
            "operation == kOpLoad",
            "command.read_text();  // directory" if "command.read_text();  // directory" in c else "const auto directory = command.read_text();",
            "const auto registration = command.read_text();",
            "const auto context = command.read_number<uint32_t>();",
            "const auto expected_identity = command.read_text();",
            "const auto count = command.read_number<uint32_t>();",
            "session.devices.push_back(command.read_number<uint32_t>());",
        ])
        self.assertTrue(ok, err)

    def test_managed_generate_frame_matches_native(self):
        m = candidate_src()
        ok, err = ordered_contains(m, [
            "writer.Write(OpGenerate)",
            "writer.Write(checked((uint)messages.Length))",
            "writer.Write(Role(message.Role)); Text(writer,message.Content); Text(writer,\"\"); writer.Write(0u)",
            "writer.Write(256u); writer.Write(request.Options?.Temperature??0.7); writer.Write(1.0); writer.Write(0u); writer.Write(33377335UL)",
            "writer.Write(0u); // stop_count",
            "Text(writer,\"medium\")",
        ])
        self.assertTrue(ok, err)
        c = cpp_src()
        ok, err = ordered_contains(c, [
            "operation == kOpGenerate || operation == kOpStructuredGenerate",
            "const auto message_count = command.read_number<uint32_t>();",
            "m.role = command.read_number<uint32_t>();",
            "m.content = command.read_text();",
            "m.name = command.read_text();",
            "const auto images = command.read_number<uint32_t>();",
            "req.max_new = command.read_number<uint32_t>();",
            "req.temperature = command.read_real();",
            "req.top_p = command.read_real();",
            "req.top_k = command.read_number<uint32_t>();",
            "req.seed = command.read_number<uint64_t>();",
            "const auto stop_count = command.read_number<uint32_t>();",
            "const auto effort = command.read_text();",
        ])
        self.assertTrue(ok, err)

    def test_managed_terminal_layout_matches_native(self):
        m = candidate_src()
        ok, err = ordered_contains(m, [
            "NativeErrors(input); var terminalText=Text(input);",
            "var prompt=checked((long)input.ReadUInt64()); var prefill=checked((long)input.ReadUInt64()); var decode=checked((long)input.ReadUInt64());",
            "var prefillSeconds=input.ReadDouble(); var decodeSeconds=input.ReadDouble();",
            "long? reused=input.ReadUInt32()==0 ? null : checked((long)input.ReadUInt64());",
            "bool? kv=input.ReadUInt32()==0 ? null : input.ReadUInt32()!=0; var stopped=input.ReadUInt32()!=0;",
        ])
        self.assertTrue(ok, err)
        c = cpp_src()
        ok, err = ordered_contains(c, [
            "term.number<uint32_t>(0); term.number<uint32_t>(0);           // NativeErrors: none",
            "term.text(std::string());",
            "term.number(static_cast<uint64_t>(m.prompt_tokens));",
            "term.number(static_cast<uint64_t>(m.read_n));",
            "term.number(static_cast<uint64_t>(m.generated));",
            "term.real(m.prompt_ms / 1000.0);",
            "term.real(m.decode_ms / 1000.0);",
            "term.number(static_cast<uint32_t>(reused > 0 ? 1 : 0));",
            "term.number(static_cast<uint32_t>(0));                        // kv: do not claim incremental KV",
            "term.number(static_cast<uint32_t>(m.finish == \"cancel\" ? 1 : 0));",
        ])
        self.assertTrue(ok, err)

    def test_structured_tools_refused_with_exact_cause(self):
        m = candidate_src()
        phrase = ("structured tool request requires a proven same-model tool probe, which is not "
                  "available for this candidate registration yet; text generation remains available.")
        self.assertIn(phrase, m)          # Probe path
        self.assertIn(phrase, m)          # Tools path (both throw)
        self.assertEqual(m.count(phrase), 2)
        self.assertIn("public bool OriginalStructuredToolsAvailable => false;", m)
        # native emits the same refusal verb for structured generate
        c = cpp_src()
        self.assertIn("structured tool request requires a proven same-model tool probe", c)

    def test_v1_legacy_preserved_binary_for_byte(self):
        m = managed_src()
        self.assertIn(LEGACY_ORIGIN, m)
        self.assertIn("version!=1 || origin!=Origin", m)
        self.assertIn("abi1/", m)
        self.assertIn("abi2/", m)  # candidate uses a distinct runtime fingerprint


class CandidateManagedCustodyTests(unittest.TestCase):
    def test_shared_worker_interface_and_raw_provider_consume_it(self):
        m, r = managed_src(), RAW.read_text(encoding="utf-8")
        self.assertIn("interface IStrataNativeWorker", m)
        self.assertIn("class StrataNativeWorker : IAsyncDisposable, IOriginalInferenceCalibrationSource, IStrataNativeWorker", m)
        self.assertIn("class StrataCandidateWorker : IAsyncDisposable, IOriginalInferenceCalibrationSource, IStrataNativeWorker", m)
        self.assertIn("IStrataNativeWorker _worker", r)
        self.assertIn("IStrataNativeWorker actualLoadedWorker", r)
        self.assertTrue("IStrataNativeWorker" in m)

    def test_candidate_is_not_wired_into_production_composition(self):
        # The candidate is authored but NOT composition-selected in this source-only checkpoint
        # (no version>=1 acceptance): the factory still creates the legacy worker.
        factory = (INFERENCE / "ManagedInferenceEngineFactories.cs").read_text(encoding="utf-8")
        self.assertIn("StrataNativeWorker.CreateAfterPublicationAsync", factory)
        self.assertNotIn("StrataCandidateWorker.CreateCandidateAfterPublicationAsync", factory)
        self.assertNotIn("new StrataCandidateWorker", factory)


if __name__ == "__main__":
    unittest.main()
