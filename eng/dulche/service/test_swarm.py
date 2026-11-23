import contextlib
import io
import json
import sys
import tempfile
import threading
import time
import unittest
from pathlib import Path

from jobs import Scheduler
from state import Fault, State
from swarm import Swarm
from swarm_contract import configuration, participant, reasoning_options, validate_plan


class FakeEngines:
    def __init__(self):
        self.worker_inputs = []
        self.mentor_inputs = []
        self.gate = threading.Event()
        self.gate.set()
        self.active = 0
        self.max_active = 0
        self.lock = threading.Lock()
        self.bad_plan = None
        self.fake_recipe = None
        self.no_stream_counts = False

    @contextlib.contextmanager
    def generate(self, model, payload, *, stream, cancel_event):
        system = payload["messages"][0]["content"]
        data = json.loads(payload["messages"][1]["content"])
        if "Plan local development" in system:
            self.mentor_inputs.append(data)
            if "tasks" not in data:
                answer = {
                    "summary": "First independent tasks, then their dependent.",
                    "tasks": self.bad_plan
                    or [
                        {
                            "id": "a",
                            "role": "developer",
                            "instruction": "Produce artifact A.",
                            "acceptance": "Checks pass.",
                            "recipes": ["check"],
                        },
                        {
                            "id": "b",
                            "role": "developer",
                            "instruction": "Produce artifact B.",
                            "acceptance": "Checks pass.",
                            "recipes": ["check"],
                        },
                        {
                            "id": "c",
                            "role": "reviewer",
                            "instruction": "Summarize A.",
                            "acceptance": "Deliver summary.",
                            "depends_on": ["a"],
                        },
                    ],
                }
            else:
                finished = all(t["state"] == "done" for t in data["tasks"])
                answer = {
                    "decision": "complete" if finished else "continue",
                    "summary": "Reviewed saved receipts.",
                    "tasks": [],
                }
        else:
            self.worker_inputs.append((model, data))
            with self.lock:
                self.active += 1
                self.max_active = max(self.max_active, self.active)
            try:
                deadline = time.monotonic() + 3
                while not self.gate.is_set() and time.monotonic() < deadline:
                    if cancel_event.wait(0.02):
                        raise Fault(409, "Cancelled")
                time.sleep(0.035)
                answer = {
                    "status": "done",
                    "deliverable": "Completed " + data["instruction"],
                    "evidence": "Draft output.",
                    "recipes": [self.fake_recipe] if self.fake_recipe else [],
                }
            finally:
                with self.lock:
                    self.active -= 1
        body = json.dumps(answer)
        events = [
            {
                "choices": [{"delta": {"content": body}}],
                **({} if self.no_stream_counts else {"timings": {"predicted_n": 12}}),
            },
            {
                "choices": [{"delta": {}, "finish_reason": "stop"}],
                "usage": {"completion_tokens": 12},
            },
        ]
        yield io.BytesIO(
            (
                "".join("data: " + json.dumps(e) + "\n\n" for e in events)
                + "data: [DONE]\n\n"
            ).encode()
        )


class Fixture:
    def __init__(self, root):
        self.state = State(root)
        self.lock = self.state.lock
        self.phase = "ready"
        self.engines = FakeEngines()
        self.resources = {
            "ram_available_mib": 8000,
            "gpus": [{"name": "Tesla V100", "used_mib": 4000}],
        }
        self.models = {
            mid: {
                "id": mid,
                "engine": "test",
                "gpu": "Tesla V100" if mid != "mentor" else "RTX 5080",
                "context": 32768,
                "slots": 2,
                "state": "loaded",
                "manual_suspended": False,
                "pressure_suspended": False,
                "profile_id": "current",
                "active_profile_id": "current",
            }
            for mid in ("mentor", "worker", "reviewer", "local-mentor")
        }
        verifier = root / "check.py"
        verifier.write_text("""import json, pathlib,sys
root=pathlib.Path(sys.argv[1])
data=sys.stdin.read().strip()
files=[json.loads(data)]
for e in files:
 ok=e['deliverable']=='Completed Produce artifact '+e['task_id'].upper()+'.'
 print(json.dumps({**{k:e[k] for k in ['swarm_id','task_id','artifact_sha256']},'verified':ok,'evidence':'Expected task-specific artifact matched'}))
""")
        recipes = self.state.directory / "approved-recipes.json"
        recipes.write_text(
            json.dumps(
                {
                    "check": {
                        "cwd": str(root),
                        "argv": [sys.executable, str(verifier), str(root)],
                        "swarm_input": "artifact-json",
                    }
                }
            )
        )
        recipes.chmod(0o600)
        self.jobs = Scheduler(self.state, self.engines, self.permit, lambda m: 2)
        self.transitions = []
        self.partial_usage = 4032

    def model(self, mid):
        if mid not in self.models:
            raise Fault(404, "Unknown model")
        return dict(self.models[mid])

    def profiles(self, mid):
        return [
            {"id": "current", "settings": {}},
            {
                "id": "four-gib",
                "settings": {
                    "gpu": "Tesla V100",
                    "vram_ceiling_mib": 4096,
                    "gpu_layers": 7,
                },
            },
        ]

    def permit(self, mid, queued=False):
        if self.model(mid)["state"] != "loaded" or self.models[mid]["manual_suspended"]:
            raise Fault(409, "Not available")

    def swarm_residency(self, mid, target, profile, owner):
        self.transitions.append((mid, target, profile, owner))
        if target == "unload":
            # Mirrors Controller._apply: a running fixed tool is parked, never terminated by an inference unload.
            self.jobs.stop(model=mid, emergency=False)
        if target == "release":
            return self.model(mid)
        self.models[mid].update(
            state="unloaded" if target == "unload" else "loaded",
            active_profile_id=profile,
        )
        if target == "partial":
            self.models[mid]["observed_model_vram_mib"] = self.partial_usage
        return self.model(mid)


class SwarmTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.ctx = Fixture(self.root)
        self.s = Swarm(self.ctx, background=False)
        self.cfg = {
            "objective": "Implement and verify three scoped changes",
            "working_directory": str(self.root),
            "completion_criteria": "All tasks have durable deliverables.",
            "mentor": {"model": "mentor"},
            "workers": [
                {
                    "role": "developer",
                    "model": "worker",
                    "recipes": ["check"],
                    "autonomy": "approved_recipes",
                    "verify_recipe": "check",
                },
                {"role": "reviewer", "model": "reviewer"},
            ],
            "max_workers": 2,
            "strategy": "resident",
            "check_in": {"seconds": 0, "tasks": 0, "tokens": 0},
        }

    def tearDown(self):
        self.ctx.engines.gate.set()
        self.s.close()
        self.ctx.jobs.shutdown()
        self.ctx.state.db.close()
        self.tmp.cleanup()

    def start(self):
        self.sid = self.s.create(self.cfg, "same-key")["id"]
        return self.sid

    def status(self):
        return self.s.handle("GET", ["swarms", self.sid])[1]

    def until(self, condition):
        end = time.monotonic() + 5
        while time.monotonic() < end:
            self.s.tick()
            v = self.status()
            if condition(v):
                return v
            time.sleep(0.012)
        self.fail(str(self.status())[:2200])

    def test_shared_weight_workers_parallel_dependency_isolated_histories_and_real_recipe_receipts(
        self,
    ):
        self.start()
        out = self.until(lambda v: v["state"] in ("completed", "completed_unverified"))
        self.assertEqual("completed_unverified", out["state"])
        self.assertEqual(2, out["verified_tasks"])
        self.assertEqual(2, self.ctx.engines.max_active)
        inputs = [d for _, d in self.ctx.engines.worker_inputs]
        self.assertTrue(
            all(
                not d["dependency_deliverables"]
                for d in inputs
                if d["instruction"].startswith("Produce")
            )
        )
        self.assertEqual(
            ["a"],
            [
                v["task"]
                for v in next(d for d in inputs if d["role"] == "reviewer")[
                    "dependency_deliverables"
                ]
            ],
        )
        self.assertEqual({"developer", "reviewer"}, {r["role"] for r in out["tasks"]})
        self.assertEqual(60, out["metrics"]["finished"]["accepted_tokens"])
        self.assertGreater(out["metrics"]["finished"]["aggregate_tps"], 0)
        self.assertEqual(0, out["metrics"]["finished"]["missing_usage"])
        self.assertEqual([], self.ctx.transitions)

    def test_invalid_dag_and_permissions_block_before_worker_dispatch(self):
        self.ctx.engines.bad_plan = [
            {
                "id": "a",
                "role": "developer",
                "instruction": "x",
                "acceptance": "x",
                "recipes": ["not-granted"],
            }
        ]
        self.start()
        result = self.until(lambda v: v["state"] == "blocked")
        self.assertEqual([], self.ctx.engines.worker_inputs)
        self.assertIn("permission", result["reason"])
        cfg = configuration(self.ctx, self.cfg)
        with self.assertRaisesRegex(Fault, "cycle"):
            validate_plan(
                cfg,
                {},
                [
                    {
                        "id": "a",
                        "role": "developer",
                        "instruction": "x",
                        "acceptance": "x",
                        "depends_on": ["a"],
                    }
                ],
            )

    def test_explicit_context_check_and_idempotent_creation(self):
        self.cfg["workers"][0]["context"] = 8192
        with self.assertRaises(Fault):
            self.start()
        self.cfg["workers"][0]["context"] = 65536
        with self.assertRaises(Fault):
            self.start()
        self.cfg["workers"][0]["context"] = 16384
        sid = self.start()
        self.assertEqual(sid, self.s.create(self.cfg, "same-key")["id"])
        with self.assertRaises(Fault):
            self.s.create({**self.cfg, "objective": "another"}, "same-key")

    def test_alternating_reconstructs_mentor_and_keeps_worker_receipts_on_restart(self):
        self.cfg["strategy"] = "alternating"
        self.cfg["check_in"]["tasks"] = 1
        self.start()
        self.until(lambda v: any(r["state"] == "generating" for r in v["tasks"]))
        before = len(self.ctx.engines.mentor_inputs)
        self.s.close()
        self.s = Swarm(self.ctx, background=False)
        result = self.until(lambda v: v["state"] == "completed_unverified")
        self.assertEqual(3, result["completed_tasks"])
        self.assertEqual(1, before)
        self.assertTrue(
            any(t[0] == "mentor" and t[1] == "unload" for t in self.ctx.transitions)
        )
        self.assertTrue(
            any(t[0] == "mentor" and t[1] == "load" for t in self.ctx.transitions)
        )
        self.assertTrue(
            any(t["decisions"] for t in self.ctx.engines.mentor_inputs[2:])
            or result["check_ins"] >= 1
        )
        with self.ctx.lock:
            count = self.ctx.state.db.execute(
                "SELECT count(*) FROM runs WHERE json_extract(request,'$.swarm_id')=?",
                (self.sid,),
            ).fetchone()[0]
        self.assertEqual(count, len(result["jobs"]))

    def test_partial_requires_measured_allocation_and_manual_latch_precedes_reload(
        self,
    ):
        self.cfg["mentor"]["model"] = "local-mentor"
        self.cfg["strategy"] = "partial"
        self.cfg["partial_profile"] = "four-gib"
        self.ctx.partial_usage = 0
        self.start()
        result = self.until(lambda v: v["state"] == "blocked")
        self.assertIn("Measured", result["reason"])
        self.assertFalse(self.ctx.engines.worker_inputs)
        self.ctx.models["local-mentor"]["manual_suspended"] = True
        self.s.command(self.sid, "resume", {})
        self.assertIn(
            "suspended", self.until(lambda v: v["state"] == "blocked")["reason"].lower()
        )
        self.assertFalse(self.ctx.engines.worker_inputs)

    def test_global_emergency_is_durable_and_no_automatic_restart(self):
        self.ctx.engines.gate.clear()
        self.start()
        self.until(lambda v: v["active_workers"] > 0)
        self.ctx.jobs.stop()
        self.s.stop_all()
        self.assertEqual("stopped", self.status()["state"])
        self.ctx.state.set("stop_all", False)
        self.s.close()
        self.s = Swarm(self.ctx, background=False)
        self.s.tick()
        self.assertEqual("stopped", self.status()["state"])
        with self.assertRaises(Fault):
            self.s.command(self.sid, "resume", {})

    def test_running_verifier_lives_through_worker_inference_unload(self):
        # Force both participants onto the same test device and keep the approved tool alive.
        self.ctx.models["mentor"]["gpu"] = "Tesla V100"
        self.cfg["strategy"] = "alternating"
        self.ctx.engines.bad_plan = [
            {
                "id": "a",
                "role": "developer",
                "instruction": "Produce artifact A.",
                "acceptance": "Expected result",
                "recipes": ["check"],
            }
        ]
        helper = self.root / "check.py"
        helper.write_text("import time\ntime.sleep(.25)\n" + helper.read_text())
        self.start()
        self.until(
            lambda v: any(
                a.get("phase") == "tool" and a.get("process")
                for a in self.ctx.jobs.active.values()
            )
        )
        with self.ctx.lock:
            proc = next(
                a["process"]
                for a in self.ctx.jobs.active.values()
                if a.get("phase") == "tool"
            )
        self.s.command(self.sid, "check-in", {})
        self.until(lambda v: self.ctx.model("worker")["state"] == "unloaded")
        self.assertIsNone(proc.poll())
        finished = self.until(lambda v: v["state"] == "completed")
        self.assertEqual(1, finished["verified_tasks"])
        self.assertEqual(0, proc.poll())
        self.assertTrue(
            finished["tasks"][0]["verification_evidence"]["artifact_sha256"]
        )

    def test_zero_exit_alone_never_verifies_a_worker_artifact(self):
        recipe = self.root / "approved-recipes.json"
        recipe.write_text(
            json.dumps({"check": {"cwd": str(self.root), "argv": ["/usr/bin/true"]}})
        )
        self.start()
        result = self.until(
            lambda v: any(t["state"] == "escalated" for t in v["tasks"])
        )
        self.assertEqual(0, result["verified_tasks"])
        self.assertTrue(
            any("receipt" in (t.get("error") or "") for t in result["tasks"])
        )

    def test_pause_checkin_and_reconfigure_apply_only_at_safe_boundaries(self):
        self.ctx.engines.gate.clear()
        self.start()
        self.until(lambda v: v["active_workers"] > 0)
        self.s.command(
            self.sid,
            "configure",
            {
                "strategy": "alternating",
                "check_in": {"tasks": 2, "seconds": 0, "tokens": 0},
            },
        )
        self.assertEqual("resident", self.status()["strategy"])
        self.s.command(self.sid, "pause", {})
        self.assertEqual("pausing", self.status()["state"])
        self.ctx.engines.gate.set()
        self.until(lambda v: v["state"] == "paused")
        self.s.command(self.sid, "resume", {})
        self.until(lambda v: v["strategy"] == "alternating")
        self.assertTrue(
            any(t["decision"] == "continue" for t in self.status()["decisions"])
        )

    def test_explicit_reference_is_persisted_without_reading_other_files(self):
        (self.root / "scope.txt").write_text("Public fixture rule.")
        (self.root / "unselected.txt").write_text("Unselected file contents.")
        self.cfg["references"] = ["scope.txt"]
        self.start()
        self.until(lambda v: bool(self.ctx.engines.mentor_inputs))
        snapshot = self.ctx.engines.mentor_inputs[0]["project_state"]
        self.assertEqual(
            [{"reference": "scope.txt", "text": "Public fixture rule."}],
            snapshot["explicit_text_sources"],
        )

    def test_new_state_connection_and_scheduler_recover_completed_planning_exactly_once(
        self,
    ):
        self.start()
        self.s.tick()
        plan_run = self.status()["jobs"][0]["run"]
        deadline = time.monotonic() + 3
        while self.ctx.state.status(plan_run)["state"] != "completed":
            self.assertLess(time.monotonic(), deadline)
            time.sleep(0.01)
        # Crash boundary: scheduler has durable outputs, Swarm has not consumed them.
        self.s.close()
        self.ctx.jobs.shutdown()
        self.ctx.state.db.close()
        self.ctx = Fixture(self.root)
        self.s = Swarm(self.ctx, background=False)
        out = self.until(lambda v: v["state"] == "completed_unverified")
        self.assertEqual(
            plan_run, next(j["run"] for j in out["jobs"] if j["kind"] == "mentor_plan")
        )
        self.assertTrue(all("tasks" in m for m in self.ctx.engines.mentor_inputs))
        self.assertEqual(2, out["verified_tasks"])

    def test_actual_context_at_dispatch_and_output_reserve_are_enforced(self):
        self.start()
        self.ctx.models["worker"]["context"] = 8192
        outcome = self.until(lambda v: v["state"] == "blocked")
        self.assertIn("context", outcome["reason"].lower())
        self.assertFalse(self.ctx.engines.worker_inputs)
        self.s.command(self.sid, "stop", {})
        self.ctx.models["worker"]["context"] = 32768
        (self.root / "scope.txt").write_text("x" * 9500)
        self.cfg["references"] = ["scope.txt"]
        self.cfg["mentor"]["context"] = 16384
        self.cfg["limits"] = {"max_output_tokens": 8192}
        self.sid = self.s.create(self.cfg, "explicit-output-reserve")["id"]
        clipped = self.until(lambda v: v["state"] == "blocked")
        self.assertIn("no history was truncated", clipped["reason"])
        self.assertFalse(clipped["jobs"])

    def test_long_prompt_uses_counted_tokens_and_preserves_full_reference(self):
        (self.root / "scope.txt").write_text("xy" * 5500)
        self.cfg["references"] = ["scope.txt"]
        self.cfg["mentor"]["context"] = 16384
        self.cfg["limits"] = {"max_output_tokens": 8192}
        measured = []

        def count(model, messages):
            measured.append((model, messages))
            return {
                "input_tokens": 4700,
                "method": "mock template tokenizer",
                "margin_tokens": 0,
            }

        self.ctx.count_prompt = count
        self.start()
        self.until(lambda v: bool(self.ctx.engines.mentor_inputs))
        self.assertEqual("mentor", measured[0][0])
        self.assertIn("xy" * 5500, measured[0][1][-1]["content"])
        proof = next(r for r in self.status()["jobs"] if r["kind"] == "mentor_plan")[
            "context_check"
        ]
        self.assertEqual(4700, proof["input_tokens"])
        self.s.command(self.sid, "stop", {})

    def test_real_time_counters_visible_before_worker_finishes(self):
        original = self.ctx.engines.generate
        published = threading.Event()
        release = threading.Event()

        @contextlib.contextmanager
        def controlled(model, payload, *, stream, cancel_event):
            with original(
                model, payload, stream=stream, cancel_event=cancel_event
            ) as result:
                if model != "worker":
                    yield result
                    return

                def chunks():
                    yield next(result)
                    published.set()
                    while not release.wait(0.02):
                        if cancel_event.is_set():
                            return
                    yield from result

                yield chunks()

        self.ctx.engines.generate = controlled
        self.start()
        try:
            row = self.until(
                lambda v: (
                    published.is_set()
                    and any(
                        w["role"] == "developer" and w["accepted_tokens"] > 0
                        for w in v["metrics"]["rolling"]["workers"]
                    )
                )
            )
            self.assertGreater(row["active_workers"], 0)
            self.assertGreater(row["metrics"]["rolling"]["accepted_tokens"], 0)
            self.assertEqual(0, row["completed_tasks"])
        finally:
            release.set()

    def test_alternate_device_is_resolved_against_selected_engine_profile(self):
        self.ctx.models["local-mentor"].update(engine="Lightning / CPU", gpu="CPU")

        def validate(mid, settings):
            gpu = settings.get("gpu", "CPU")
            return {
                "context": 32768,
                "gpu": gpu,
                "engine": "Lightning / llama.cpp RPC"
                if gpu == "Tesla V100"
                else "Lightning / CPU",
            }

        self.ctx.engines.validate_settings = validate
        chosen = {
            "model": "local-mentor",
            "profile": "four-gib",
            "gpu": "Tesla V100",
            "engine": "Lightning / llama.cpp RPC",
        }
        actual = participant(self.ctx, chosen)
        self.assertEqual("Lightning / llama.cpp RPC", actual["engine"])
        with self.assertRaisesRegex(Fault, "inference engine"):
            participant(self.ctx, {**chosen, "engine": "Lightning / CPU"})
        self.ctx.models["local-mentor"].update(active_profile_id="four-gib")
        with self.assertRaisesRegex(Fault, "Loaded engine differs"):
            self.s._ensure(
                {"id": "test", "transitions": [], "reservations": []}, actual, "load"
            )

    def test_manual_checkin_note_is_durable_observation_not_an_authority_grant(self):
        self.ctx.engines.gate.clear()
        self.start()
        self.until(lambda v: v["active_workers"] > 0)
        note = "Please inspect the artifact boundary case."
        out = self.s.command(self.sid, "check-in", {"note": note})
        self.assertEqual("observation", out["human_observations"][0]["authority"])
        self.assertEqual(note, out["human_observations"][0]["note"])
        with self.assertRaises(Fault):
            self.s.command(self.sid, "check-in", {"note": "x" * 2049})
        self.ctx.engines.gate.set()
        self.until(lambda v: any("tasks" in i for i in self.ctx.engines.mentor_inputs))
        review = next(i for i in self.ctx.engines.mentor_inputs if "tasks" in i)
        self.assertEqual(note, review["human_observations"][0]["note"])
        self.assertEqual([["check"], []], [r["recipes"] for r in review["roles"]])

    def test_explicit_reasoning_routes_only_to_verified_backend_control(self):
        native = "qwen3.8-flash-next-unsloth-ud-iq4_xs"
        gguf = "qwen3.8-27b-v100"
        self.ctx.models[native] = {
            **self.ctx.models["mentor"],
            "id": native,
            "engine": "Thunder / Strata",
        }
        self.ctx.models[gguf] = {
            **self.ctx.models["worker"],
            "id": gguf,
            "engine": "Lightning / llama.cpp RPC",
        }
        self.cfg["mentor"] = {"model": native, "reasoning": "disabled"}
        self.cfg["workers"][0].update(model=gguf, reasoning="disabled")
        original = self.ctx.engines.generate
        payloads = []

        @contextlib.contextmanager
        def capture(model, payload, **kwargs):
            payloads.append((model, payload))
            with original(model, payload, **kwargs) as response:
                yield response

        self.ctx.engines.generate = capture
        self.start()
        self.until(lambda v: v["state"] == "completed_unverified")
        for model, payload in payloads:
            if model == native:
                self.assertEqual("none", payload["reasoning_effort"])
                self.assertNotIn("chat_template_kwargs", payload)
            elif model == gguf:
                self.assertEqual(
                    {"enable_thinking": False}, payload["chat_template_kwargs"]
                )
                self.assertNotIn("reasoning_effort", payload)
            else:
                self.assertNotIn("reasoning_effort", payload)
                self.assertNotIn("chat_template_kwargs", payload)
        self.assertEqual(
            {"reasoning_effort": "low"},
            reasoning_options(
                participant(
                    self.ctx,
                    {
                        "model": native,
                        "reasoning": "enabled",
                        "reasoning_effort": "low",
                    },
                )
            ),
        )
        with self.assertRaises(Fault):
            participant(
                self.ctx,
                {"model": gguf, "reasoning": "enabled", "reasoning_effort": "low"},
            )
        with self.assertRaises(Fault):
            participant(self.ctx, {"model": "reviewer", "reasoning": "disabled"})

    def test_reasoning_exhaustion_has_zero_visible_output_and_separate_quality_metric(
        self,
    ):
        original = self.ctx.engines.generate

        @contextlib.contextmanager
        def exhaust(model, payload, **kwargs):
            if model == "worker":
                stream = [
                    {"choices": [{"delta": {"reasoning_content": "still working"}}]},
                    {
                        "choices": [{"delta": {}, "finish_reason": "length"}],
                        "usage": {"completion_tokens": 4096},
                    },
                ]
                yield io.BytesIO(
                    (
                        "".join("data: " + json.dumps(v) + "\n\n" for v in stream)
                        + "data: [DONE]\n\n"
                    ).encode()
                )
            else:
                with original(model, payload, **kwargs) as response:
                    yield response

        self.ctx.engines.generate = exhaust
        self.start()
        status = self.until(
            lambda v: (
                v["metrics"]["quality_outcomes"]["reasoning_only_budget_exhausted"] > 0
            )
        )
        job = next(
            e
            for e in status["jobs"]
            if e["failure_class"] == "reasoning_only_budget_exhausted"
        )
        self.assertEqual(0, job["visible_characters"])
        self.assertGreater(job["reasoning_characters"], 0)
        self.assertEqual(0, status["verified_tasks"])

    def test_long_prompt_uses_selected_reasoning_template_and_records_margin(self):
        native = "qwen3.8-flash-next-unsloth-ud-iq4_xs"
        self.ctx.models[native] = {
            **self.ctx.models["mentor"],
            "id": native,
            "engine": "Thunder / Strata",
        }
        self.cfg["mentor"] = {
            "model": native,
            "reasoning": "disabled",
            "context": 16384,
        }
        self.cfg["limits"] = {"max_output_tokens": 8192}
        (self.root / "spec.txt").write_text("abcdefgh " * 1400)
        self.cfg["references"] = ["spec.txt"]
        observed = []

        def count(mid, messages, options):
            observed.append((mid, options))
            return {
                "input_tokens": 5100,
                "method": "native tokenizer with explicit template allowance",
                "margin_tokens": 100,
            }

        self.ctx.count_prompt_with_options = count
        self.start()
        self.until(lambda v: bool(v["jobs"]))
        self.assertEqual([(native, {"reasoning_effort": "none"})], observed)
        check = self.status()["jobs"][0]["context_check"]
        self.assertEqual(100, check["margin_tokens"])
        self.assertEqual(5100, check["input_tokens"])

    def test_worker_cannot_expand_its_own_grant(self):
        self.ctx.engines.fake_recipe = "unknown"
        self.start()
        result = self.until(lambda v: bool(v["errors"]))
        self.assertTrue(any("permission" in e["message"] for e in result["errors"]))
        self.assertEqual(0, result["verified_tasks"])
