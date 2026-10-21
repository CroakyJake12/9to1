"""Bounded build-free tests for the truthful capability-based model/format/tokenizer/quant dispatch
(Thunder pass 9).

Owner-confirmed models (2026-10-21, independent official metadata):
  * ornith-ai/Ornith-1.5-122B  (MIT; qwen3_5_moe / qwen3_5_moe_text; 125.1B BF16; GGUF family qwen35moe)
  * InternScience/Sage-A1.5     (Apache-2.0; qwen3_5_moe / qwen3_5_moe_text; 35.95B BF16; GGUF family qwen35moe)

This suite crosses-checks the native `dulche_strata_candidate_worker.cpp` dispatch table and the
managed `StrataCapabilityRegistry` (in `StrataNativeWorker.cs`) so both sides agree on:

  * which registrations are ACCEPTED (verified qwen35moe-family spellings) — manual no-fallback
    model identity: unknown registrations are refused, never silently downgraded;
  * a genuine compatible fallback within the family (a verified family spelling carrying a genuine
    quant/format suffix, e.g. "qwen35moe-Q4_K_M");
  * that tools/vision/MTP are NEVER advertised from an architecture string or card claim — the
    candidate relay is text-only;
  * that no "Orinth" typo aliases are accepted.

These are source-level functional checks of the dispatch logic (no compile/run).

It also re-runs the actual dispatch logic by parsing the C++/C# tables into Python and applying the
same matching rules, which is a build-free way to exercise the real table entries.
"""
import pathlib
import re
import unittest
import json

INFERENCE = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines"
NATIVE = INFERENCE / "NativeStrata"
CANDIDATE_CPP = NATIVE / "dulche_strata_candidate_worker.cpp"
MANAGED = INFERENCE / "StrataNativeWorker.cs"


def cpp_src():
    return CANDIDATE_CPP.read_text(encoding="utf-8")


def managed_src():
    return MANAGED.read_text(encoding="utf-8")


# The verified spellings that must be accepted (both sides), matching the real tables.
VERIFIED_SPELLINGS = [
    "qwen35moe",                  # engine-native GGUF family
    "qwen3_5_moe",                # verified HF model_type (both confirmed models)
    "qwen3_5_moe_text",           # verified HF text_config.model_type
    "ornith-ai/Ornith-1.5-122B",  # owner-confirmed model identity
    "InternScience/Sage-A1.5",    # owner-confirmed model identity
]

RECEIPT = NATIVE / "strata-version-receipt.json"

# Registrations that must be REFUSED (no fabricated fallback, no typo aliases).
REFUSED_REGISTRATIONS = [
    "orinth1.5",                  # typo alias (NOT accepted)
    "orinth-ai/Ornith-1.5-122B",  # typo alias
    "Sage-A1.5-text",             # not a verified spelling
    "Mixtral-8x7B",               # unrelated family
    "llama3",                     # unrelated family
    "qwen4exp",                   # engine-registered but NOT a confirmed/verified candidate model
    "",
]


def match_tolerant(registration, spellings):
    """Python mirror of resolve_capability_tolerant / StrataCapabilityRegistry.Resolve."""
    if not registration:
        return False
    for spelling in spellings:
        if registration == spelling:
            return True
        if len(registration) > len(spelling) and registration.startswith(spelling):
            if registration[len(spelling)] in "-_/":
                return True
    return False


def extract_cpp_spellings(src):
    """Extract the spelling literals from the C++ kRegistrationAliases table."""
    block = src[src.find("RegistrationAlias kRegistrationAliases"):]
    block = block[:block.find("};")]
    return re.findall(r'\{\s*"([^"]+)"\s*,', block)


def extract_managed_spellings(src):
    """Extract the spelling literals from the C# StrataCapabilityRegistry.VerifiedSpellings."""
    block = src[src.find("VerifiedSpellings"):]
    block = block[:block.find("};")]
    return re.findall(r'"([^"]+)"', block)


class NativeCapabilityDispatchTests(unittest.TestCase):
    def test_cpp_dispatch_table_present_and_pins_family(self):
        src = cpp_src()
        self.assertIn("struct ModelCapability", src)
        self.assertIn("kQwen35MoeCapability", src)
        self.assertIn("RegistrationAlias kRegistrationAliases", src)
        self.assertIn('"qwen35moe"', src)
        self.assertIn("resolve_capability_tolerant", src)
        # Tools/vision/MTP are structurally never marketed.
        self.assertIn("supports_tools", src)
        self.assertIn("supports_vision", src)
        self.assertIn("claims_mtp", src)
        self.assertIn("text_only", src)

    def test_cpp_table_accepts_all_verified_spellings(self):
        spellings = extract_cpp_spellings(cpp_src())
        self.assertEqual(set(spellings), set(VERIFIED_SPELLINGS), "C++ table mismatch")
        for reg in VERIFIED_SPELLINGS:
            self.assertTrue(match_tolerant(reg, spellings), f"C++ should accept {reg!r}")

    def test_cpp_table_refuses_typos_and_unrelated(self):
        spellings = extract_cpp_spellings(cpp_src())
        for reg in REFUSED_REGISTRATIONS:
            self.assertFalse(match_tolerant(reg, spellings), f"C++ should refuse {reg!r}")

    def test_cpp_tolerant_quant_suffix_acceptance(self):
        spellings = extract_cpp_spellings(cpp_src())
        self.assertTrue(match_tolerant("qwen35moe-Q4_K_M", spellings))
        self.assertTrue(match_tolerant("qwen35moe_Q4_K_M", spellings))
        self.assertTrue(match_tolerant("qwen3_5_moe-bf16", spellings))
        # Not a verified family + suffix, so refused.
        self.assertFalse(match_tolerant("qwen4exp-bf16", spellings))
        self.assertFalse(match_tolerant("qwen35moeQ4_K_M", spellings))  # no separator

    def test_load_validation_uses_resolver_not_loose_substring(self):
        src = cpp_src()
        self.assertIn("resolve_capability_tolerant(registration)", src)
        # The old loose substring check must be gone.
        self.assertNotIn('registration.find("qwen35") == std::string::npos', src)


class ManagedCapabilityDispatchTests(unittest.TestCase):
    def test_managed_registry_present(self):
        src = managed_src()
        self.assertIn("class StrataCapabilityRegistry", src)
        self.assertIn("VerifiedSpellings", src)
        self.assertIn("public static bool Resolve(string registration, out string? family)", src)

    def test_managed_registry_matches_native_table(self):
        native = extract_cpp_spellings(cpp_src())
        managed = extract_managed_spellings(managed_src())
        self.assertEqual(set(managed), set(native), "managed registry must mirror native table")
        self.assertEqual(set(managed), set(VERIFIED_SPELLINGS))

    def test_managed_load_uses_registry(self):
        src = managed_src()
        self.assertIn("StrataCapabilityRegistry.Resolve(Model.NativeRegistration!, out _)", src)
        # The loose substring check must be gone.
        self.assertNotIn('Model.NativeRegistration.Contains("qwen35"', src)

    def test_managed_and_native_refuse_same_set(self):
        native = extract_cpp_spellings(cpp_src())
        managed = extract_managed_spellings(managed_src())
        for reg in REFUSED_REGISTRATIONS:
            self.assertFalse(match_tolerant(reg, native), f"native should refuse {reg!r}")
            self.assertFalse(match_tolerant(reg, managed), f"managed should refuse {reg!r}")

    def test_no_tool_vision_mtp_advertising(self):
        # The candidate path must never advertise tools/vision/MTP capability.
        m = managed_src()
        self.assertIn("public bool OriginalStructuredToolsAvailable => false;", m)
        c = cpp_src()
        self.assertIn('term.number(static_cast<uint32_t>(0));                        // kv: do not claim incremental KV', c)


class VersionPinReceiptTests(unittest.TestCase):
    """The version-pin mechanics separate the origin/engine-version/license receipt from the blob
    lock. The hardcoded origin string in both workers must equal `candidate.commit` in the receipt,
    so a future bump is a data change (receipt + lock), not code surgery in the origin string."""

    def test_receipt_exists_and_is_valid_json(self):
        self.assertTrue(RECEIPT.exists(), "strata-version-receipt.json missing")
        data = json.loads(RECEIPT.read_text(encoding="utf-8"))
        self.assertEqual(data["schema"], "strata-version-receipt/v1")
        self.assertIn("candidate", data)
        self.assertIn("latestVerifiedUpstream", data)
        self.assertIn("confirmedModels", data)

    def test_candidate_commit_matches_worker_origin(self):
        data = json.loads(RECEIPT.read_text(encoding="utf-8"))
        commit = data["candidate"]["commit"]
        # native worker origin text
        self.assertIn(commit, cpp_src())
        # managed candidate origin constant
        self.assertIn(commit, managed_src())
        # both must be the SAME pin
        self.assertEqual(commit, "011c9e600a93bbc9fd451c563501b5831c32bbca")

    def test_no_fabricated_v0222_lock(self):
        data = json.loads(RECEIPT.read_text(encoding="utf-8"))
        upstream = data["latestVerifiedUpstream"]
        self.assertEqual(upstream["archiveStatus"], "ACQUIRED_IN_MEMORY_BYTES_SHA_VERIFIED")
        self.assertEqual(upstream["status"], "VERIFIED_ACQUIRED")
        # candidate is now pinned to the verified v0.2.2 commit (same as latest upstream)
        self.assertEqual(data["candidate"]["commit"], upstream["commit"])

    def test_receipt_records_confirmed_models(self):
        data = json.loads(RECEIPT.read_text(encoding="utf-8"))
        models = data["confirmedModels"]
        self.assertEqual(models["ornith"]["family"], "qwen35moe")
        self.assertEqual(models["sage"]["family"], "qwen35moe")
        for m in models.values():
            self.assertIn("text-only", m["capability"])
            self.assertIn("tools/vision/MTP never advertised", m["capability"])


if __name__ == "__main__":
    unittest.main()
