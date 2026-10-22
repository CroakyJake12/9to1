"""Deterministic build-free tests for the Thunder offline harness guards/parser/lifetime.

These use synthetic subprocess fixtures (never a GPU). They prove guard logic, the candidate
serve-protocol parser, interleaved scheduling, bounded process lifetime, descendant custody, and the
fail-closed comparison validation.
"""
import json
import os
import pathlib
import signal
import subprocess
import unittest

from thunder_bench import (BoundedProcess, ConfigManifest, DoneMetrics, EngineManifest, GuardState,
                           Job, ModelManifest, ProcessOutcome, RunResult, _utc_epoch,
                           build_interleaved_plan, comparability_key, compare_report, main, median,
                           median_absolute_deviation, parse_done_line, parse_gen_line,
                           parse_serve_stream, provenance_key, run_result_to_dict,
                           run_result_from_dict, tps, validate_sample_comparability)


def engine(kind="existing", source_version="0.1.47", hardware="RTX 5080"):
    return EngineManifest(id=kind, kind=kind, binary=pathlib.Path("/nonexistent/engine"),
                          build_receipt_sha="a" * 64, source_lock_sha="b" * 64, profile=kind,
                          source_version=source_version, hardware=hardware)


def model(file_sha256="c" * 64):
    return ModelManifest(id="qwen3", path=pathlib.Path("/m/gguf"), quantisation="Q4_K",
                         context_tokens=8192, expected_registration="qwen35moe",
                         file_sha256=file_sha256)


def config(repeats=5):
    return ConfigManifest(id="c1", temperature=0.7, top_p=1.0, max_new_tokens=128,
                          prompts=("Hi", "Explain X"), repeats_per_config=repeats)


def _success_result(kind, run_index=0, model_obj=None, cfg_obj=None, phases=None, done=None,
                    source_version="0.1.47", hardware="RTX 5080",
                    build_receipt_sha="a" * 64, source_lock_sha="b" * 64):
    model_obj = model_obj or model()
    cfg_obj = cfg_obj or config()
    outcome = ProcessOutcome(exit_code=0, timed_out=False, elapsed_seconds=1.0)
    if done is None:
        done = DoneMetrics(generated=4, prompt_tokens=16, prompt_ms=5.0, decode_ms=8.0,
                           finish="length", drafts_accepted=0, drafts_offered=0, reused=0, read_n=0)
    eng = EngineManifest(id=kind, kind=kind, binary=pathlib.Path("/nonexistent/engine"),
                         build_receipt_sha=build_receipt_sha, source_lock_sha=source_lock_sha,
                         profile=kind, source_version=source_version, hardware=hardware)
    return RunResult(Job(eng, model_obj, cfg_obj, run_index), outcome=outcome,
                     token_ids=[], done=done, errors=[],
                     phases=phases if phases is not None else {"load": 1.0, "prefill": 5.0,
                                                               "decode": 8.0})


def _failed_result(kind, run_index=0, model_obj=None, cfg_obj=None, exit_code=1):
    outcome = ProcessOutcome(exit_code=exit_code, timed_out=False, elapsed_seconds=1.0)
    return RunResult(Job(engine(kind), model_obj or model(), cfg_obj or config(), run_index),
                     outcome=outcome, token_ids=[], done=None, errors=["engine exit-6"], phases={})


def _timedout_result(kind, run_index=0, model_obj=None, cfg_obj=None):
    outcome = ProcessOutcome(exit_code=None, timed_out=True, elapsed_seconds=5.0)
    return RunResult(Job(engine(kind), model_obj or model(), cfg_obj or config(), run_index),
                     outcome=outcome, token_ids=[], done=None, errors=[], phases={})


class GuardTests(unittest.TestCase):
    # authorised owner window: 2026-10-22 17:10-21:00 UTC
    def test_pass_inside_window_all_verified(self):
        s = GuardState(now_utc=_utc_epoch(2026, 10, 22, 18, 0), endpoint_verified=True,
                       device_verified=True, model_verified=True, rollback_retained=True)
        self.assertFalse(s.fail_closed())

    def test_fail_closed_before_start(self):
        s = GuardState(now_utc=_utc_epoch(2026, 10, 22, 17, 9), endpoint_verified=True,
                       device_verified=True, model_verified=True, rollback_retained=True)
        self.assertTrue(s.fail_closed())
        self.assertIn("window", " ".join(s.check()))

    def test_fail_closed_at_end_boundary_exclusive(self):
        s = GuardState(now_utc=_utc_epoch(2026, 10, 22, 21, 0), endpoint_verified=True,
                       device_verified=True, model_verified=True, rollback_retained=True)
        self.assertTrue(s.fail_closed())

    def test_fail_closed_after_end(self):
        s = GuardState(now_utc=_utc_epoch(2026, 10, 22, 21, 30), endpoint_verified=True,
                       device_verified=True, model_verified=True, rollback_retained=True)
        self.assertTrue(s.fail_closed())

    def test_fail_closed_wrong_date(self):
        # a different day entirely must NOT be inferred from time-of-day
        s = GuardState(now_utc=_utc_epoch(2026, 10, 23, 18, 0), endpoint_verified=True,
                       device_verified=True, model_verified=True, rollback_retained=True)
        self.assertTrue(s.fail_closed())

    def test_fail_closed_on_missing_endpoint(self):
        s = GuardState(now_utc=_utc_epoch(2026, 10, 22, 18, 0), endpoint_verified=False,
                       device_verified=True, model_verified=True, rollback_retained=True)
        self.assertTrue(s.fail_closed())
        self.assertIn("endpoint", " ".join(s.check()))


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
        self.assertEqual(kinds[0:3], ["existing", "upstream", "optimised"])
        self.assertEqual(kinds[3:6], ["upstream", "optimised", "existing"])


class AggregationTests(unittest.TestCase):
    def test_median_and_mad(self):
        self.assertEqual(median([3, 1, 2]), 2)
        self.assertEqual(median([4, 1, 2, 3]), 2.5)
        self.assertEqual(median_absolute_deviation([1, 2, 3], 2), 1)

    def test_median_empty(self):
        self.assertIsNone(median([]))


class ComparabilityTests(unittest.TestCase):
    def _valid_group(self, kind, n=5):
        return [_success_result(kind, run_index=i) for i in range(n)]

    def test_matching_model_config_is_comparable(self):
        baseline = self._valid_group("existing")
        candidate = self._valid_group("upstream")
        self.assertEqual(validate_sample_comparability(baseline, candidate), [])
        self.assertEqual(comparability_key(baseline[0].job), comparability_key(candidate[0].job))

    def test_provenance_key_complete_and_incomplete(self):
        self.assertIsNotNone(provenance_key(_success_result("existing", run_index=0).job))
        nn = ModelManifest(id="qwen3", path=pathlib.Path("/m/gguf"), quantisation="Q4_K",
                           context_tokens=8192, expected_registration="qwen35moe",
                           file_sha256=None)
        self.assertIsNone(provenance_key(_success_result("existing", run_index=0, model_obj=nn).job))

    def test_mismatched_model_id_fails(self):
        other = ModelManifest(id="other-model", path=pathlib.Path("/m/other"),
                              quantisation="Q4_K", context_tokens=8192,
                              expected_registration="qwen35moe")
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i, model_obj=other) for i in range(5)]
        self.assertTrue(validate_sample_comparability(baseline, candidate))

    def test_mismatched_quantisation_fails(self):
        other = ModelManifest(id="qwen3", path=pathlib.Path("/m/gguf"),
                              quantisation="Q8_0", context_tokens=8192,
                              expected_registration="qwen35moe")
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i, model_obj=other) for i in range(5)]
        self.assertTrue(validate_sample_comparability(baseline, candidate))

    def test_mismatched_context_fails(self):
        other = ModelManifest(id="qwen3", path=pathlib.Path("/m/gguf"),
                              quantisation="Q4_K", context_tokens=16384,
                              expected_registration="qwen35moe")
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i, model_obj=other) for i in range(5)]
        self.assertTrue(validate_sample_comparability(baseline, candidate))

    def test_mismatched_prompt_or_decoding_fails(self):
        alt_cfg = ConfigManifest(id="c1", temperature=0.7, top_p=1.0, max_new_tokens=128,
                                 prompts=("Hi", "DIFFERENT"), repeats_per_config=5)
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i, cfg_obj=alt_cfg) for i in range(5)]
        self.assertTrue(validate_sample_comparability(baseline, candidate))

    def test_fewer_than_five_valid_runs_fails(self):
        baseline = self._valid_group("existing", n=5)
        candidate = self._valid_group("upstream", n=3)
        self.assertTrue(validate_sample_comparability(baseline, candidate))

    def test_mixed_identities_within_group_fails(self):
        other = ModelManifest(id="other-model", path=pathlib.Path("/m/other"),
                              quantisation="Q4_K", context_tokens=8192,
                              expected_registration="qwen35moe")
        baseline = self._valid_group("existing", n=5) + \
            [_success_result("existing", run_index=5, model_obj=other)]
        candidate = self._valid_group("upstream")
        self.assertTrue(validate_sample_comparability(baseline, candidate))

    # ---- negative-receipt FAIL 2 / 3: duplicate + failed runs rejected ----

    def test_five_duplicate_failed_runs_rejected(self):
        # the exact source33 negative: SAME FAILED RunResult repeated 5 times per group
        baseline = [_failed_result("existing", run_index=0) for _ in range(5)]
        candidate = [_failed_result("upstream", run_index=0) for _ in range(5)]
        failures = validate_sample_comparability(baseline, candidate)
        self.assertTrue(failures)
        joined = " ".join(failures)
        self.assertIn("valid successful", joined)

    def test_five_duplicate_timedout_runs_rejected(self):
        baseline = [_timedout_result("existing", run_index=0) for _ in range(5)]
        candidate = [_timedout_result("upstream", run_index=0) for _ in range(5)]
        failures = validate_sample_comparability(baseline, candidate)
        self.assertTrue(failures)

    def test_duplicate_engine_run_index_rejected(self):
        baseline = self._valid_group("existing", n=5)
        candidate = [_success_result("upstream", run_index=0) for _ in range(5)]
        failures = validate_sample_comparability(baseline, candidate)
        self.assertIn("duplicate engine/run_index", " ".join(failures))

    def test_missing_provenance_rejected(self):
        nn = ModelManifest(id="qwen3", path=pathlib.Path("/m/gguf"), quantisation="Q4_K",
                           context_tokens=8192, expected_registration="qwen35moe",
                           file_sha256=None)
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i, model_obj=nn) for i in range(5)]
        failures = validate_sample_comparability(baseline, candidate)
        self.assertIn("incomplete provenance", " ".join(failures))

    def test_nonfinite_phase_rejected(self):
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i,
                                     phases={"decode": float("nan")}) for i in range(5)]
        failures = validate_sample_comparability(baseline, candidate)
        self.assertIn("invalid phase", " ".join(failures))

    def test_contradictory_provenance_rejected(self):
        baseline = self._valid_group("existing")
        other_file = model(file_sha256="d" * 64)
        candidate = [_success_result("upstream", run_index=i, model_obj=other_file) for i in range(5)]
        failures = validate_sample_comparability(baseline, candidate)
        self.assertIn("provenance differ", " ".join(failures))


class OfflineCompareTests(unittest.TestCase):
    def test_compare_report_refuses_failed_inputs(self):
        baseline = [_failed_result("existing", run_index=0) for _ in range(5)]
        candidate = [_failed_result("upstream", run_index=0) for _ in range(5)]
        report = compare_report(baseline, candidate)
        self.assertFalse(report["comparable"])
        self.assertFalse(report["gainReliable"])
        self.assertIn("failures", report)

    def test_compare_report_descriptive_not_reliable_gain(self):
        # comparable metadata/5 runs does NOT prove a reliable gain: gainReliable must be false
        baseline = self._valid_group("existing")
        candidate = self._valid_group("upstream")
        report = compare_report(baseline, candidate)
        self.assertTrue(report["comparable"])
        self.assertFalse(report["gainReliable"])
        self.assertEqual(report["baseline"]["validRuns"], 5)
        self.assertEqual(report["candidate"]["validRuns"], 5)
        self.assertAlmostEqual(report["deltaPct"], 0.0)  # descriptive only
        self.assertIn("baseline", report["raw"])
        self.assertIn("candidate", report["raw"])
        self.assertIn("limitations", report)

    def test_wide_variability_equal_medians_no_reliable_gain(self):
        # equal medians with wide variability => comparable metadata but never a reliable gain
        def mk(vals):
            return [_success_result("existing", run_index=i, done=DoneMetrics(
                generated=g, prompt_tokens=16, prompt_ms=5.0, decode_ms=10.0,
                finish="length", drafts_accepted=0, drafts_offered=0, reused=0, read_n=0))
                for i, g in enumerate(vals)]
        baseline = mk([1, 2, 3, 4, 5])      # TPS [100..500]
        candidate = mk([7, 1, 5, 3, 2])      # TPS sorted [100,200,300,500,700], same median 300
        report = compare_report(baseline, candidate)
        self.assertTrue(report["comparable"])
        self.assertFalse(report["gainReliable"])
        self.assertAlmostEqual(report["baseline"]["medianTps"], 300.0)
        self.assertAlmostEqual(report["candidate"]["medianTps"], 300.0)
        self.assertGreater(report["candidate"]["madTps"], report["baseline"]["madTps"])

    def test_min_runs_below_five_rejected_in_function(self):
        group = self._valid_group("existing")
        for bad in (0, -1, 1, 3, 4):
            self.assertTrue(validate_sample_comparability(group, group, min_runs=bad))
        self.assertEqual(validate_sample_comparability(group, group, min_runs=5), [])

    def test_cli_min_runs_lt_five_refused(self):
        import tempfile
        import pathlib as pl
        with tempfile.TemporaryDirectory(prefix="thunder-cli-", dir=pl.Path(__file__).resolve().parent) as td:
            td = pl.Path(td)
            good = td / "good.json"
            good.write_text(json.dumps([run_result_to_dict(_success_result("existing", run_index=i))
                                        for i in range(5)]))
            rc = main(["--offline-compare", "--baseline", str(good), "--candidate", str(good),
                       "--min-runs", "1"])
            self.assertEqual(rc, 2)

    def test_zero_or_invalid_done_metrics_rejected(self):
        bad_dones = [
            DoneMetrics(generated=0, prompt_tokens=16, prompt_ms=5.0, decode_ms=8.0,
                        finish="length", drafts_accepted=0, drafts_offered=0, reused=0, read_n=0),
            DoneMetrics(generated=4, prompt_tokens=16, prompt_ms=5.0, decode_ms=0.0,
                        finish="length", drafts_accepted=0, drafts_offered=0, reused=0, read_n=0),
            DoneMetrics(generated=4, prompt_tokens=16, prompt_ms=5.0, decode_ms=float("nan"),
                        finish="length", drafts_accepted=0, drafts_offered=0, reused=0, read_n=0),
            DoneMetrics(generated=4, prompt_tokens=16, prompt_ms=5.0, decode_ms=float("inf"),
                        finish="length", drafts_accepted=0, drafts_offered=0, reused=0, read_n=0),
        ]
        for d in bad_dones:
            self.assertIsNone(tps(_success_result("existing", run_index=0, done=d)))
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i, done=bad_dones[0]) for i in range(5)]
        failures = validate_sample_comparability(baseline, candidate)
        self.assertTrue(failures)
        self.assertIn("generated", " ".join(failures))

    def test_noninteger_or_negative_done_counts_rejected(self):
        bad = DoneMetrics(generated=4.5, prompt_tokens=16, prompt_ms=5.0, decode_ms=8.0,
                          finish="length", drafts_accepted=0, drafts_offered=0, reused=0, read_n=0)
        self.assertIsNone(tps(_success_result("existing", run_index=0, done=bad)))
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i, done=bad) for i in range(5)]
        self.assertTrue(validate_sample_comparability(baseline, candidate))

    def test_engine_identity_mixed_within_group_fails(self):
        baseline = self._valid_group("existing", n=3) + \
            [_success_result("existing", run_index=i, build_receipt_sha="e" * 64)
             for i in (3, 4)]
        candidate = self._valid_group("upstream")
        failures = validate_sample_comparability(baseline, candidate)
        self.assertIn("engine identities", " ".join(failures))

    def test_blank_or_nonhex_sha_provenance_rejected(self):
        blank = ModelManifest(id="qwen3", path=pathlib.Path("/m/gguf"), quantisation="Q4_K",
                              context_tokens=8192, expected_registration="qwen35moe",
                              file_sha256="not-hex")
        baseline = self._valid_group("existing")
        candidate = [_success_result("upstream", run_index=i, model_obj=blank) for i in range(5)]
        failures = validate_sample_comparability(baseline, candidate)
        self.assertIn("incomplete provenance", " ".join(failures))

    def _valid_group(self, kind, n=5):
        return [_success_result(kind, run_index=i) for i in range(n)]

    def test_run_result_roundtrip(self):
        r = _success_result("upstream", run_index=3)
        back = run_result_from_dict(run_result_to_dict(r))
        self.assertEqual(back.job.run_index, 3)
        self.assertEqual(back.job.engine.kind, "upstream")
        self.assertEqual(back.job.model.file_sha256, r.job.model.file_sha256)
        self.assertEqual(back.done.generated, 4)
        self.assertFalse(back.outcome.timed_out)
        self.assertEqual(back.outcome.exit_code, 0)


class ProcessLifetimeTests(unittest.TestCase):
    def test_bounded_process_kills_on_timeout(self):
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

    def _proc_state(self, pid: int):
        """Return 'Z'/'R'/'S'/etc from /proc/<pid>/stat, or None if the process entry is gone."""
        try:
            with open("/proc/%d/stat" % pid) as fh:
                data = fh.read()
        except FileNotFoundError:
            return None
        rparen = data.rfind(")")
        return data[rparen + 2:].split()[0] if rparen >= 0 else None

    def test_bounded_process_reaps_descendant_after_parent_exit(self):
        # A Python parent spawns a long-lived background child, prints its PID, then exits. The child
        # is in our owned session/process group and must be terminated (retired) when the context
        # closes, while an unrelated process in a separate group must survive (owned session custody).
        import tempfile
        import time
        with tempfile.TemporaryDirectory(prefix="thunder-desc-", dir=pathlib.Path(__file__).resolve().parent) as td:
            # unrelated sentinel in its OWN new session/group — must not be touched
            sentinel = subprocess.Popen(["sleep", "30"], stdout=subprocess.DEVNULL,
                                        stderr=subprocess.DEVNULL, start_new_session=True)
            try:
                code = ("import subprocess, sys\n"
                        "child = subprocess.Popen(['sleep', '30'],\n"
                        "    stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)\n"
                        "print(child.pid, flush=True)\n")
                with BoundedProcess(["python3", "-c", code], timeout_seconds=5) as p:
                    outcome = p.communicate()
                self.assertFalse(outcome.timed_out)
                self.assertEqual(outcome.exit_code, 0)
                child_pid = int(outcome.stdout.strip().split()[-1])
                self.assertIsNotNone(p._pgid)
                # our owned descendant must be retired (terminated, i.e. zombie or gone), never running
                deadline = time.monotonic() + 3.0
                state = self._proc_state(child_pid)
                while state in ("R", "S", "D", "T") and time.monotonic() < deadline:
                    time.sleep(0.02)
                    state = self._proc_state(child_pid)
                self.assertIn(state, (None, "Z"))  # gone or terminated-zombie => retired
                # unrelated sentinel must still be alive and running
                self.assertEqual(self._proc_state(sentinel.pid), "S")
            finally:
                try:
                    os.killpg(os.getpgid(sentinel.pid), signal.SIGKILL)
                except OSError:
                    pass
                sentinel.wait()


if __name__ == "__main__":
    unittest.main()
