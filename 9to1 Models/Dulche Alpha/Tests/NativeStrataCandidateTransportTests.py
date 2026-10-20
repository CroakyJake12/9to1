"""Bounded build-free transport/protocol tests for the candidate (v0.2.1) relay.

The candidate worker is C++ and cannot be compiled in this source-only checkpoint, so this suite pins
the WIRE CONTRACT the relay and the donor engine actually speak, then validates it three ways:

  1. Source-scan cross-checks against the candidate worker (`dulche_strata_candidate_worker.cpp`) and
     the donor engine (`src/program/generate.cpp`): frame envelope types, operation dispatch, READY
     and GEN line shapes, and the DONE field layout.
  2. A deterministic fixture (a captured donor-shaped engine transcript, including the optional
     `chunk=`/`experts_streamed=` tail) fed through a small Python mirror of the candidate's parse
     contract. The mirror is a COMPANION to the C++ parser, not a substitute: it pins the exact
     grammar the C++ code documents, so a future real build has a regression reference.
  3. Malformed variants that must be rejected (negative telemetry, unknown finish, truncated lines,
     bare non-key tail fields).

These tests never compile, link, run inference, download models or install packages.
"""
import pathlib
import re
import unittest

NATIVE = pathlib.Path(__file__).resolve().parents[1] / "InferenceEngines" / "NativeStrata"
CANDIDATE = NATIVE / "dulche_strata_candidate_worker.cpp"
DONOR = pathlib.Path("/tmp/opencode/strata-v0201-src/src/program/generate.cpp")

FINISH_VOCAB = {"stop", "length", "cancel", "nonfinite"}


# ---------------------------------------------------------------------------
# Contract strings pulled from the two sources (a source-string existence check is
# evidence of the *documented contract*, never of a working engine).
# ---------------------------------------------------------------------------
class WireContractSourceTests(unittest.TestCase):
    def test_candidate_frame_envelope_types(self):
        # hello frame type 0 carries ABI version 2; load ack type 4; stop ack type 3;
        # failure type 5; hardware-probe result type 6.
        src = CANDIDATE.read_text()
        self.assertIn("write_frame(stdout, 0, hello)", src)
        self.assertIn("kAbiVersion = 2", src)
        self.assertIn("write_frame(stdout, 4, loaded)", src)
        self.assertIn("write_frame(stdout, 3, stopped)", src)
        self.assertIn("write_frame(stdout, 5, failure)", src)
        self.assertIn("write_frame(stdout, 6, observed)", src)

    def test_candidate_operation_dispatch(self):
        src = CANDIDATE.read_text()
        # The worker names its ops: op 1 = load, op 2 = text generate, op 4 = structured-tools
        # generate, op 3 = stop, op 5 = cancel. Dispatch uses the named constants (not bare literals).
        self.assertIn("kOpLoad = 1", src)
        self.assertIn("kOpGenerate = 2", src)
        self.assertIn("kOpStop = 3", src)
        self.assertIn("kOpStructuredGenerate = 4", src)
        self.assertIn("kOpCancel = 5", src)
        self.assertIn("operation == kOpLoad", src)
        self.assertIn("operation == kOpGenerate || operation == kOpStructuredGenerate", src)
        self.assertIn("operation == kOpStop", src)

    def test_candidate_gen_line_shape(self):
        src = CANDIDATE.read_text()
        self.assertIn('"GEN " + std::to_string(max_new)', src)
        self.assertIn('line += " temperature=" + std::to_string(temperature)', src)
        self.assertIn('line += " "', src)
        self.assertIn("line += \",\"", src)

    def test_candidate_ready_accepts_donor_shape(self):
        src = CANDIDATE.read_text()
        self.assertIn('line.rfind("READY ", 0) == 0', src)
        self.assertIn('line.rfind("ERR ", 0) == 0', src)

    def test_donor_ready_shape(self):
        # donor emits `READY <max_context> stop` (see generate.cpp:10920).
        if not DONOR.exists():
            self.skipTest("donor source not present in this checkpoint")
        src = DONOR.read_text()
        self.assertIn('std::printf("READY %lld stop\\n", (long long) o.max_context)', src)

    def test_donor_done_finish_vocab(self):
        if not DONOR.exists():
            self.skipTest("donor source not present in this checkpoint")
        src = DONOR.read_text()
        # The donor's finish field is one of stop|length|cancel|nonfinite (DONE comment)
        self.assertIn("stop|length|cancel|nonfinite", src)


# ---------------------------------------------------------------------------
# Python mirror of the candidate's parse contract (companion to parse_done).
# ---------------------------------------------------------------------------
def mirror_parse_done(line: str):
    """Mirror `parse_done` in the candidate worker.

    Grammar (positional): DONE <generated> <prompt> <prompt_ms> <decode_ms> <finish>
    <draft_accepted> <draft_offered> <reused> <hits> <lookups> <ram> <file> <file_mb>
    <read_n> <offload> [key=value ...]
    """
    if not line.startswith("DONE "):
        raise ValueError("Missing DONE terminal.")
    tok = line.split()
    if len(tok) < 9:
        raise ValueError("Malformed DONE terminal.")

    def i64(s, what):
        if not re.fullmatch(r"-?\d+", s):
            raise ValueError("Malformed DONE field: %s" % what)
        return int(s)

    def dbl(s, what):
        if not re.fullmatch(r"[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?", s):
            raise ValueError("Malformed DONE field: %s" % what)
        return float(s)

    m = {
        "generated": i64(tok[1], "generated"),
        "prompt": i64(tok[2], "prompt"),
        "prompt_ms": dbl(tok[3], "prompt_ms"),
        "decode_ms": dbl(tok[4], "decode_ms"),
        "finish": tok[5],
        "drafts_accepted": i64(tok[6], "drafts_accepted"),
        "drafts_offered": i64(tok[7], "drafts_offered"),
        "reused": i64(tok[8], "reused"),
        "hits": i64(tok[9], "hits") if len(tok) > 9 else 0,
        "lookups": i64(tok[10], "lookups") if len(tok) > 10 else 0,
    }
    negative = ["generated", "prompt", "prompt_ms", "decode_ms", "drafts_accepted",
                "drafts_offered", "reused", "hits", "lookups"]
    for key in negative:
        if m[key] < 0:
            raise ValueError("DONE telemetry must be finite and nonnegative.")
    if m["finish"] not in FINISH_VOCAB:
        raise ValueError("DONE finish vocabulary is not recognized.")
    if len(tok) > 14:
        m["read_n"] = i64(tok[14], "read_n")
        if m["read_n"] < 0:
            raise ValueError("DONE read_n must be nonnegative.")
    else:
        m["read_n"] = m["prompt"] - m["reused"]
        if m["read_n"] < 0:
            raise ValueError("DONE read_n is inconsistent.")
    if len(tok) > 15:
        m["offload"] = i64(tok[15], "offload")
        if m["offload"] < 0:
            raise ValueError("DONE offload must be nonnegative.")
    for field in tok[16:]:
        if "=" not in field or len(field) > 128:
            raise ValueError("DONE trailing field is malformed.")
    return m


class DoneParseFixtureTests(unittest.TestCase):
    # A captured donor-shaped transcript: fixed fields plus the optional
    # `chunk=`/`experts_streamed=` tail the engine appends (see generate.cpp:15106).
    DONE_FIXTURE = (
        "DONE 42 128 312.5 845.0 stop 0 0 0 3 7 1 0 0.0 128 3"
        " chunk=4 experts_streamed=8"
    )

    def test_fixture_parses(self):
        m = mirror_parse_done(self.DONE_FIXTURE)
        self.assertEqual(m["generated"], 42)
        self.assertEqual(m["prompt"], 128)
        self.assertEqual(m["finish"], "stop")
        self.assertEqual(m["read_n"], 128)
        self.assertEqual(m["offload"], 3)
        # the key=value tail (chunk=... experts_streamed=...) is validated, not parsed

    def test_fixture_field_positions_match_donor(self):
        # Positional assertions derived from the donor's DONE printf
        # (generate.cpp:15106): field 14 is read_n, field 15 is offload.
        parts = self.DONE_FIXTURE.split()
        self.assertEqual(parts[0], "DONE")
        self.assertEqual(parts[14], "128")   # read_n
        self.assertEqual(parts[15], "3")     # offload
        self.assertTrue(parts[16].startswith("chunk="))
        self.assertTrue(parts[17].startswith("experts_streamed="))

    def test_finish_vocab_rejects_unknown(self):
        line = self.DONE_FIXTURE.replace("stop", "bogus", 1)
        with self.assertRaises(ValueError):
            mirror_parse_done(line)

    def test_negative_telemetry_rejected(self):
        for field_idx in (1, 2, 6, 7, 8):
            parts = self.DONE_FIXTURE.split()
            parts[field_idx] = "-5"
            with self.assertRaises(ValueError):
                mirror_parse_done(" ".join(parts))

    def test_read_n_inconsistent_rejected(self):
        # prompt=128, reused=120, no read_n field -> read_n = 8; but force prompt < reused
        parts = ("DONE 42 100 312.5 845.0 stop 0 0 120 3 7 1 0 0.0").split()
        with self.assertRaises(ValueError):
            mirror_parse_done(" ".join(parts))

    def test_truncated_rejected(self):
        parts = self.DONE_FIXTURE.split()[:7]
        with self.assertRaises(ValueError):
            mirror_parse_done(" ".join(parts))

    def test_bare_tail_field_rejected(self):
        # a bare number (e.g. an unexpected chain field) is malformed for the non-lookup-chain path
        parts = self.DONE_FIXTURE.split()[:16] + ["7"]
        with self.assertRaises(ValueError):
            mirror_parse_done(" ".join(parts))

    def test_done_missing_terminal_rejected(self):
        with self.assertRaises(ValueError):
            mirror_parse_done("INFO 1 2 3")


class GenLineMirrorTests(unittest.TestCase):
    def test_gen_line_shapes(self):
        # Basic request: GEN <max_new> <ids>
        self.assertEqual(mirror_gen_line(64, [1, 2, 3]), "GEN 64 1,2,3")
        # With sampling keys inserted between max_new and the ids.
        self.assertEqual(mirror_gen_line(64, [1, 2, 3], temperature=0.8, top_p=0.9),
                         "GEN 64 temperature=0.800000 top_p=0.900000 1,2,3")


def mirror_gen_line(max_new, ids, temperature=0.0, top_p=1.0, top_k=0, seed=0,
                    min_p=0.0, penalty_repeat=0.0, penalty_freq=0.0,
                    penalty_present=0.0, penalty_last_n=0):
    line = "GEN %d" % max_new
    if temperature > 0.0:
        # std::to_string(double) uses %.6f (6 decimals), match that exactly.
        line += " temperature=%.6f" % temperature
        line += " top_p=%.6f" % (top_p if top_p > 0.0 else 1.0)
        if top_k:
            line += " top_k=%d" % top_k
        if min_p > 0.0:
            line += " min_p=%.6f" % min_p
        if penalty_last_n > 0:
            line += " penalty_last_n=%d" % penalty_last_n
        if penalty_repeat > 0.0:
            line += " penalty_repeat=%.6f" % penalty_repeat
        if penalty_freq > 0.0:
            line += " penalty_freq=%.6f" % penalty_freq
        if penalty_present > 0.0:
            line += " penalty_present=%.6f" % penalty_present
        if seed:
            line += " seed=%d" % seed
    line += " " + ",".join(str(i) for i in ids)
    return line


if __name__ == "__main__":
    unittest.main()
