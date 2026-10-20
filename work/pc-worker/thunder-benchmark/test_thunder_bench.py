"""Deterministic build-free tests for the Thunder offline harness guards/parser/lifetime.

These use synthetic subprocess fixtures (never a GPU). They prove guard logic, the candidate
serve-protocol parser, interleaved scheduling and bounded process lifetime only.
"""
import pathlib
import time
import unittest

from thunder_bench import (BoundedProcess, ConfigManifest, EngineManifest, GuardState,
                           ModelManifest, build_interleaved_plan, median,
                           median_absolute_deviation, parse_done_line, parse_gen_line,
                           parse_serve_stream)


def engine(kind="existing"):
    return EngineManifest(id=kind, kind=kind, binary=pathlib.Path("/nonexistent/engine"),
                          build_receipt_sha="a" * 64, source_lock_sha="b" * 64, profile=kind)


def model():
    return ModelManifest(id="qwen3", path=pathlib.Path("/m/gguf"), quantisation="Q4_K",
                         context_tokens=8192, expected_registration="qwen35moe")


def config(repeats=5):
    return ConfigManifest(id="c1", temperature=0.7, top_p=1.0, max_new_tokens=128,
                          prompts=("Hi", "Explain X"), repeats_per_config=repeats)


class GuardTests(unittest.TestCase):
    def test_fail_closed_outside_window(self):
        s = GuardState(now_utc=10 * 3600, endpoint_verified=True, device_verified=True,
                       model_verified=True, rollback_retained=True)
        self.assertTrue(s.fail_closed())
        self.assertIn("window", " ".join(s.check()))

    def test_fail_closed_on_missing_endpoint(self):
        s = GuardState(now_utc=20.5 * 3600, endpoint_verified=False, device_verified=True,
                       model_verified=True, rollback_retained=True)
        self.assertTrue(s.fail_closed())
        self.assertIn("endpoint", " ".join(s.check()))

    def test_pass_inside_window_all_verified(self):
        s = GuardState(now_utc=20.5 * 3600, endpoint_verified=True, device_verified=True,
                       model_verified=True, rollback_retained=True)
        self.assertFalse(s.fail_closed())


class ParserTests(unittest.TestCase):
    def test_gen_line_greedy(self):
        self.assertEqual(parse_gen_line(16, [1, 2, 3]), "GEN 16 1,2,3\n")

    def test_gen_line_sampling(self):
        line = parse_gen_line(8, [5], temperature=0.7, top_p=0.9, top_k=32, seed=7)
        self.assertTrue(line.startswith("GEN 8 "))
        self.assertIn("temperature=0.7", line)
        self.assertIn("top_p=0.9", line)
        self.assertIn("top_k=32", line)
        self.assertIn("seed=7", line)

    def test_done_line_parse(self):
        m = parse_done_line("DONE 5 100 10.5 2.25 length 1 2 0 1 2 3 4 5.0 6 7 chunk=64 experts_streamed=8\n")
        self.assertEqual(m.generated, 5)
        self.assertEqual(m.prompt_tokens, 100)
        self.assertAlmostEqual(m.prompt_ms, 10.5)
        self.assertAlmostEqual(m.decode_ms, 2.25)
        self.assertEqual(m.finish, "length")
        self.assertEqual(m.drafts_accepted, 1)
        self.assertEqual(m.drafts_offered, 2)

    def test_done_requires_terminal(self):
        with self.assertRaises(ValueError):
            parse_done_line("T 5")

    def test_serve_stream_parse(self):
        stream = "T 10\nT 11\nDONE 2 20 5.0 1.0 length 0 0 0\n"
        tokens, done, errors = parse_serve_stream(stream)
        self.assertEqual(tokens, [10, 11])
        self.assertEqual(done.generated, 2)
        self.assertEqual(errors, [])

    def test_serve_stream_invalid_handling(self):
        with self.assertRaises(ValueError):
            parse_serve_stream("T 10\n")  # no DONE
        _, _, errors = parse_serve_stream("ERR bad model\nT 1\nDONE 1 2 3.0 4.0 stop 0 0 0\n")
        self.assertIn("bad model", errors)


class SchedulingTests(unittest.TestCase):
    def test_interleaved_repeats_at_least_five(self):
        engines = [engine("existing"), engine("upstream"), engine("optimised")]
        jobs = build_interleaved_plan(engines, model(), config(repeats=5))
        self.assertEqual(len(jobs), 3 * 5)
        kinds = [j.engine.kind for j in jobs]
        # interleaved: each repeat block has one of each engine
        self.assertEqual(kinds[0:3], ["existing", "upstream", "optimised"])
        self.assertEqual(kinds[3:6], ["upstream", "optimised", "existing"])


class AggregationTests(unittest.TestCase):
    def test_median_and_mad(self):
        self.assertEqual(median([3, 1, 2]), 2)
        self.assertEqual(median([4, 1, 2, 3]), 2.5)
        self.assertEqual(median_absolute_deviation([1, 2, 3], 2), 1)

    def test_median_empty(self):
        self.assertIsNone(median([]))


class ProcessLifetimeTests(unittest.TestCase):
    def test_bounded_process_kills_on_timeout(self):
        # deterministic fixture: a child that sleeps longer than the bound must be force-killed
        with BoundedProcess(["sh", "-c", "sleep 5"], timeout_seconds=0.4) as p:
            outcome = p.communicate()
        self.assertTrue(outcome.timed_out)
        self.assertIsNotNone(outcome.exit_code)
        self.assertLess(outcome.elapsed_seconds, 5)

    def test_bounded_process_normal_exit(self):
        with BoundedProcess(["sh", "-c", "echo ok"], timeout_seconds=5) as p:
            outcome = p.communicate()
        self.assertFalse(outcome.timed_out)
        self.assertEqual(outcome.exit_code, 0)
        self.assertIn("ok", outcome.stdout)


if __name__ == "__main__":
    unittest.main()
