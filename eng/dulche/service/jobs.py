"""Durable local run queue. Only owner-registered recipes may produce OS actions."""

import contextlib
import hashlib
import json
import os
import re
import stat
import tempfile
from pathlib import Path
import signal
import shutil
import math
import subprocess
import threading
import time

from state import Fault, identity


TERMINAL = {"completed", "failed", "stopped", "checkpointed", "review"}
MAX_STEPS = 128


class Scheduler:
    def __init__(self, state, engines, permitted, capacity):
        self.state, self.engines = state, engines
        self.permitted, self.capacity = permitted, capacity
        self.cv = threading.Condition(state.lock)
        self.active = {}
        self.closing = False
        with state.transaction() as db:
            db.execute(
                """UPDATE runs SET state=CASE WHEN reason='recipe dispatched' THEN 'review'
                ELSE 'checkpointed' END, reason='Service restarted; inspect the last saved step',updated=?
                WHERE state='running'""",
                (time.time(),),
            )
        self.thread = threading.Thread(
            target=self._dispatch, name="dulche-dispatch", daemon=True
        )
        self.thread.start()

    def create(self, request, key=None):
        model = request.get("model")
        steps = request.get("steps")
        if (
            request.get("kind") != "swarm"
            and isinstance(steps, list)
            and any(isinstance(x, dict) and "artifact" in x for x in steps)
        ):
            raise Fault(403, "Only scheduled swarm jobs carry artifact evidence")
        if (
            not isinstance(model, str)
            or not model
            or not isinstance(steps, list)
            or not 1 <= len(steps) <= MAX_STEPS
        ):
            raise Fault(400, "Supply a model and 1–128 ordered steps")
        schedule = request.get("not_before", 0)
        if (
            type(schedule) not in (int, float)
            or not math.isfinite(schedule)
            or schedule < 0
        ):
            raise Fault(400, "not_before must be a UTC epoch timestamp")
        if shutil.disk_usage(self.state.directory).free <= 50 * 2**30 + 32 * 2**20:
            raise Fault(507, "Dulche state volume has reached its storage floor")
        for step in steps:
            if not isinstance(step, dict):
                raise Fault(400, "Invalid step")
            if step.get("type") == "generate":
                args = step.get("payload")
                if (
                    not isinstance(args, dict)
                    or not isinstance(args.get("messages"), list)
                    or not args["messages"]
                ):
                    raise Fault(400, "A generation step requires messages")
            elif step.get("type") == "recipe":
                self._recipe(step.get("name"))
            else:
                raise Fault(400, "Unsupported step type")
        if key is not None and (not isinstance(key, str) or not 1 <= len(key) <= 256):
            raise Fault(400, "Invalid idempotency key")
        if any(s["type"] == "recipe" for s in steps) and not key:
            raise Fault(400, "Development actions require an Idempotency-Key")
        body = json.dumps(
            request, sort_keys=True, separators=(",", ":"), allow_nan=False
        )
        with self.cv, self.state.transaction() as db:
            prior = db.execute(
                "SELECT id,request FROM runs WHERE key=?", (key,)
            ).fetchone()
            if prior:
                if prior["request"] != body:
                    raise Fault(
                        409, "This idempotency key belongs to a different request"
                    )
                return prior["id"]
            if self.closing or (
                request.get("kind") != "chat" and self.state.get("stop_all", False)
            ):
                raise Fault(409, "Agent admission is stopped")
            self.permitted(model)
            queued = db.execute(
                "SELECT COUNT(*) FROM runs WHERE state IN ('queued','running')"
            ).fetchone()[0]
            if queued >= 32:
                raise Fault(429, "The local job queue is full")
            run, now = identity(), time.time()
            db.execute(
                "INSERT INTO runs(id,key,request,model,state,created,updated) VALUES(?,?,?,?,'queued',?,?)",
                (run, key, body, model, now, now),
            )
            self.state.record(run, 0, "queued", {"steps": len(steps)})
            self.cv.notify_all()
            return run

    def _recipe(self, name):
        if not isinstance(name, str) or not name:
            raise Fault(400, "A registered recipe name is required")
        path = self.state.directory / "approved-recipes.json"
        if not path.exists():
            raise Fault(
                403, "No development recipes have been registered by the local operator"
            )
        info = path.lstat()
        if (
            not path.is_file()
            or path.is_symlink()
            or info.st_uid != os.getuid()
            or info.st_mode & 0o077
        ):
            raise Fault(403, "Recipe registry must be a private operator-owned file")
        recipe = json.loads(path.read_text()).get(name)
        if not isinstance(recipe, dict):
            raise Fault(403, "This development recipe has not been registered")
        argv = recipe.get("argv")
        if (
            not isinstance(argv, list)
            or not argv
            or not all(isinstance(a, str) for a in argv)
            or not Path(argv[0]).is_absolute()
        ):
            raise Fault(
                400, "Recipe must specify an absolute executable and an argument list"
            )
        if not Path(recipe.get("cwd", "")).is_absolute():
            raise Fault(400, "Recipe must specify an absolute working directory")
        return recipe

    def _dispatch(self):
        while True:
            with self.cv:
                if self.closing:
                    return
                tickets = self.state.db.execute(
                    "SELECT id,model FROM runs WHERE state='queued' AND "
                    "COALESCE(json_extract(request,'$.not_before'),0)<=? ORDER BY created",
                    (time.time(),),
                ).fetchall()
                for row in tickets:
                    model = row["model"]
                    if (
                        sum(a["model"] == model for a in self.active.values())
                        >= self.capacity(model)
                        or len(self.active) >= 4
                    ):
                        continue
                    try:
                        self.permitted(model, queued=True)
                    except Fault:
                        continue
                    run = row["id"]
                    control = {
                        "model": model,
                        "cancel": threading.Event(),
                        "process": None,
                        "phase": "starting",
                        "finished": threading.Event(),
                    }
                    self.active[run] = control
                    self.state.db.execute(
                        "UPDATE runs SET state='running',reason=NULL,updated=? WHERE id=?",
                        (time.time(), run),
                    )
                    threading.Thread(
                        target=self._execute,
                        args=(run, control),
                        name=f"dulche-{run[:8]}",
                        daemon=True,
                    ).start()
                self.cv.wait(timeout=0.5)

    def _stream(self, run, attempt, payload, response, cancel, started):
        batch, content, reasoning, calls = [], [], [], {}
        usage, finish, last, response_id = {}, None, 0.0, run
        ttft = None
        timings = {}
        for raw in response:
            if cancel.is_set():
                raise Fault(409, "Execution interrupted")
            if not raw.startswith(b"data:"):
                continue
            value = raw[5:].strip()
            if value == b"[DONE]":
                break
            data = json.loads(value)
            if data.get("error"):
                raise Fault(502, str(data["error"])[:1000])
            if data.get("id"):
                response_id = data["id"]
            if data.get("usage"):
                usage = data["usage"]
            if data.get("timings"):
                timings = data["timings"]
            batch.append(data)
            for choice in data.get("choices", []):
                delta = choice.get("delta", {})
                if ttft is None and any(
                    delta.get(k)
                    for k in ("content", "reasoning_content", "reasoning", "tool_calls")
                ):
                    ttft = time.monotonic() - started
                content.append(delta.get("content") or "")
                reasoning.append(
                    delta.get("reasoning_content") or delta.get("reasoning") or ""
                )
                for piece in delta.get("tool_calls") or []:
                    call = calls.setdefault(
                        piece.get("index", 0),
                        {
                            "id": "",
                            "type": "function",
                            "function": {"name": "", "arguments": ""},
                        },
                    )
                    if piece.get("id"):
                        call["id"] = piece["id"]
                    for k in ("name", "arguments"):
                        call["function"][k] += (piece.get("function") or {}).get(
                            k
                        ) or ""
                if choice.get("finish_reason"):
                    finish = choice["finish_reason"]
            if time.monotonic() - last >= 0.10 or len(batch) >= 32:
                self._publish(run, attempt, "chunks", batch)
                batch, last = [], time.monotonic()
        if batch:
            self._publish(run, attempt, "chunks", batch)
        if finish is None:
            raise Fault(502, "Engine stream closed without a finish reason")
        message = {"role": "assistant", "content": "".join(content)}
        if any(reasoning):
            message["reasoning_content"] = "".join(reasoning)
        if calls:
            message["tool_calls"] = [c for _, c in sorted(calls.items())]
        return {
            "id": response_id,
            "object": "chat.completion",
            "model": payload["model"],
            "choices": [{"index": 0, "message": message, "finish_reason": finish}],
            "usage": usage,
            "elapsed_s": time.monotonic() - started,
            "ttft_s": ttft,
            "timings": timings,
        }

    def _publish(self, run, attempt, kind, value):
        with self.cv:
            self.state.record(run, attempt, kind, value)
            self.cv.notify_all()

    def _run_recipe(self, run, n, step, control):
        with self.cv:
            if control["cancel"].is_set():
                raise Fault(409, "Execution interrupted")
            recipe = self._recipe(step["name"])
            digest = hashlib.sha256(
                json.dumps(recipe, sort_keys=True).encode()
            ).hexdigest()
            # Pin the operator-approved exact recipe at enqueue time, including its arguments.
            if step.get("recipe_hash") != digest:
                raise Fault(409, "The registered recipe changed; approve a new run")
            logdir = self.state.directory / "logs"
            logdir.mkdir(mode=0o700, exist_ok=True)
            log = logdir / f"{run}-{n}-{identity()}.log"
            self.state.db.execute(
                "UPDATE runs SET reason='recipe dispatched' WHERE id=?", (run,)
            )
            self.state.record(
                run,
                n,
                "dispatch",
                {"recipe": step["name"], "hash": digest, "log": str(log)},
            )
            input_source = step.get("input_from_step")
            input_file = None
            if input_source is not None:
                receipts = self.state.status(run, full=True)["completed_steps"]
                if (
                    type(input_source) is not int
                    or input_source < 0
                    or input_source >= len(receipts)
                ):
                    raise Fault(400, "Input must refer to an earlier completed step")
                previous = receipts[input_source]
                content = (
                    previous.get("choices", [{}])[0].get("message", {}).get("content")
                )
                if not isinstance(content, str):
                    raise Fault(400, "Earlier step has no text output")
                input_file = tempfile.TemporaryFile(dir=logdir)
                input_file.write(content.encode())
                input_file.seek(0)
            if step.get("artifact") is not None:
                req = self.state.status(run, full=True)["request"]
                sid, tid = req.get("swarm_id"), req.get("task_id")
                if (
                    step.get("artifact") is not True
                    or input_file
                    or req.get("kind") != "swarm"
                    or recipe.get("swarm_input") != "artifact-json"
                    or not all(
                        isinstance(s, str) and re.fullmatch(r"[A-Za-z0-9_-]{1,128}", s)
                        for s in (sid, tid)
                    )
                ):
                    raise Fault(
                        403,
                        "Artifact input requires a registered swarm verifier and valid task identities",
                    )
                base = self.state.directory / "swarm-artifacts"
                path = base / sid / (tid + ".json")
                if base.is_symlink() or path.parent.is_symlink():
                    raise Fault(403, "Undeclared artifact location")
                fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
                with os.fdopen(fd, "rb") as source:
                    info = os.fstat(source.fileno())
                    if (
                        not stat.S_ISREG(info.st_mode)
                        or info.st_uid != os.getuid()
                        or info.st_mode & 0o077
                        or info.st_size > 8 * 1024**2
                    ):
                        raise Fault(
                            403, "Artifact must be a bounded private regular file"
                        )
                    data = source.read(8 * 1024**2 + 1)
                value = json.loads(data)
                deliverable = value.get("deliverable")
                if (
                    not isinstance(deliverable, str)
                    or value.get("artifact_sha256")
                    != hashlib.sha256(deliverable.encode()).hexdigest()
                ):
                    raise Fault(409, "Artifact failed content hash verification")
                if value.get("swarm_id") != sid or value.get("task_id") != tid:
                    raise Fault(
                        409, "Artifact identity does not match the scheduled task"
                    )
                input_file = tempfile.TemporaryFile(dir=logdir)
                input_file.write(data)
                input_file.seek(0)
            with contextlib.ExitStack() as stack:
                if input_file:
                    stack.enter_context(input_file)
                f = stack.enter_context(log.open("xb"))
                proc = subprocess.Popen(
                    recipe["argv"],
                    cwd=recipe["cwd"],
                    stdin=input_file or subprocess.DEVNULL,
                    stdout=f,
                    stderr=subprocess.STDOUT,
                    start_new_session=True,
                )
            control["process"] = proc
            control["phase"] = "tool"
        try:
            deadline = time.monotonic() + min(
                7200, max(1, int(recipe.get("timeout_s", 1200)))
            )
            while proc.poll() is None:
                if (
                    control["cancel"].wait(0.15)
                    or time.monotonic() >= deadline
                    or shutil.disk_usage(logdir).free < 60 * 2**30
                ):
                    self._terminate(proc)
                    raise Fault(
                        409,
                        "Interrupted development action; review the log before continuing",
                    )
            if proc.returncode:
                raise Fault(
                    422, f"Development action exited {proc.returncode}; log {log}"
                )
            return {
                "recipe": step["name"],
                "exit_code": proc.returncode,
                "log": str(log),
            }
        finally:
            with self.cv:
                control["process"] = None
                control["phase"] = "checkpoint"

    @staticmethod
    def _terminate(proc):
        if proc.poll() is not None:
            return
        try:
            os.killpg(proc.pid, signal.SIGTERM)
            try:
                proc.wait(timeout=2)
            except subprocess.TimeoutExpired:
                os.killpg(proc.pid, signal.SIGKILL)
                proc.wait(timeout=3)
        except ProcessLookupError:
            pass

    def prepare(self, request):
        for step in (
            request.get("steps", []) if isinstance(request.get("steps"), list) else []
        ):
            if isinstance(step, dict) and step.get("type") == "recipe":
                recipe = self._recipe(step.get("name"))
                step["recipe_hash"] = hashlib.sha256(
                    json.dumps(recipe, sort_keys=True).encode()
                ).hexdigest()
        return request

    def _execute(self, run, control):
        try:
            info = self.state.status(run, full=True)
            request = info["request"]
            for n in range(info["step"], len(request["steps"])):
                with self.cv:
                    if (
                        control["cancel"].is_set()
                        or self.state.status(run)["state"] != "running"
                    ):
                        return
                    self.permitted(info["model"], queued=True)
                step = request["steps"][n]
                if step["type"] == "recipe":
                    result = self._run_recipe(run, n, step, control)
                else:
                    payload = {**step["payload"], "model": info["model"]}
                    if step.get("include_previous_results"):
                        prior = self.state.status(run, full=True)["completed_steps"]
                        payload["messages"] = [
                            *payload["messages"],
                            {
                                "role": "user",
                                "content": "Prior steps in this task returned the following data:\n"
                                + json.dumps(prior, ensure_ascii=False),
                            },
                        ]
                    with self.cv:
                        control["phase"] = "inference"
                    stream = payload.get("stream", False)
                    started = time.monotonic()
                    attempt = self.state.begin_generation(run, n)
                    with self.engines.generate(
                        info["model"],
                        payload,
                        stream=stream,
                        cancel_event=control["cancel"],
                    ) as reply:
                        result = (
                            self._stream(
                                run, attempt, payload, reply, control["cancel"], started
                            )
                            if stream
                            else json.load(reply)
                        )
                    result["elapsed_s"] = time.monotonic() - started
                    result["dulche_attempt"] = attempt
                    if not result.get("choices"):
                        raise Fault(502, "Engine returned no completion")
                with self.cv, self.state.transaction() as db:
                    # Commit the receipt even if a stop raced the final return of an external action.
                    if step["type"] == "generate" and control["cancel"].is_set():
                        return
                    db.execute(
                        "INSERT INTO steps VALUES(?,?,?)", (run, n, json.dumps(result))
                    )
                    # A successful external step that raced a stop is no longer uncertain.
                    park = control.get("park_as", "stopped")
                    reason = control.get("park_reason", "Emergency stop")
                    db.execute(
                        """UPDATE runs SET step=?, reason=CASE WHEN state='review' THEN ? WHEN state='checkpointed' THEN reason ELSE NULL END,
                        state=CASE WHEN state='review' THEN ? ELSE state END,updated=? WHERE id=?""",
                        (n + 1, reason, park, time.time(), run),
                    )
                    if step["type"] == "generate":
                        self.state.set(
                            "generation-proof:" + info["model"],
                            {
                                "at": time.time(),
                                "run": run,
                                "profile": self.state.get(
                                    "model:" + info["model"], {}
                                ).get("profile", "current"),
                            },
                        )
                    if n + 1 == len(request["steps"]) and step["type"] == "recipe":
                        db.execute(
                            "UPDATE runs SET state='completed',reason=NULL WHERE id=? AND state='checkpointed' AND reason='Model suspended'",
                            (run,),
                        )
                    self.state.record(run, n, "step_complete", {"step": n})
                    self.cv.notify_all()
            with self.cv:
                self.state.db.execute(
                    "UPDATE runs SET state='completed',updated=? WHERE id=? AND state='running'",
                    (time.time(), run),
                )
        except Exception as exc:
            with self.cv:
                self.state.db.execute(
                    """UPDATE runs SET state=CASE WHEN reason='recipe dispatched' THEN 'review'
                    ELSE 'failed' END,reason=?,updated=? WHERE id=? AND state='running'""",
                    (str(exc)[:1500], time.time(), run),
                )
                self.state.record(run, -1, "error", {"message": str(exc)[:1500]})
        finally:
            with self.cv:
                self.active.pop(run, None)
                control["finished"].set()
                if (
                    control.get("resume_after_checkpoint")
                    and not self.closing
                    and self.state.status(run)["reason"] == "Model suspended"
                ):
                    try:
                        self.resume(run, auto=True)
                    except Fault:
                        pass
                self.cv.notify_all()

    def stop(self, run=None, model=None, emergency=True):
        with self.cv, self.state.transaction() as db:
            if run:
                self.state.status(run)
            if emergency and not run and not model:
                self.state.set("stop_all", True)
            sql = "SELECT id,state,reason,request FROM runs WHERE state IN ('queued','running','checkpointed','failed')"
            params = []
            if run:
                sql += " AND id=?"
                params.append(run)
            if model:
                sql += " AND model=?"
                params.append(model)
            affected = []
            for row in db.execute(sql, params).fetchall():
                if (
                    emergency
                    and not run
                    and json.loads(row["request"]).get("kind") == "chat"
                ):
                    continue
                if not emergency and row["state"] in ("checkpointed", "failed"):
                    continue
                rid = row["id"]
                control = self.active.get(rid)
                if control:
                    control["park_as"] = "stopped" if emergency else "checkpointed"
                    if not (model and not emergency and control.get("phase") == "tool"):
                        control["cancel"].set()
                status = "stopped" if emergency else "checkpointed"
                if row["reason"] == "recipe dispatched" and not (
                    model
                    and not emergency
                    and control
                    and control.get("phase") == "tool"
                ):
                    status = "review"
                detail = (
                    "Emergency stop"
                    if emergency
                    else ("Model suspended" if model else "Service shutdown checkpoint")
                )
                if control:
                    control["park_reason"] = detail
                db.execute(
                    "UPDATE runs SET state=?,reason=?,updated=? WHERE id=?",
                    (status, detail, time.time(), rid),
                )
                self.state.record(rid, -1, status, {"reason": detail})
                affected.append(rid)
            self.cv.notify_all()
        return affected

    def resume(self, run, *, auto=False):
        with self.cv:
            info = self.state.status(run)
            if run in self.active or self.state.get("stop_all", False):
                raise Fault(409, "A stop is still active")
            if auto and info["state"] != "checkpointed":
                return
            if info["state"] not in {"checkpointed", "stopped", "failed"}:
                raise Fault(409, "This run cannot be resumed without review")
            self.permitted(info["model"])
            self.state.db.execute(
                "UPDATE runs SET state='queued',reason=NULL,updated=? WHERE id=?",
                (time.time(), run),
            )
            self.state.record(run, -1, "resume", {"from_step": info["step"]})
            self.cv.notify_all()

    def recover_model(self, model):
        with self.cv:
            rows = self.state.db.execute(
                "SELECT id FROM runs WHERE model=? AND state='checkpointed' AND reason='Model suspended'",
                (model,),
            ).fetchall()
            for row in rows:
                if row["id"] in self.active:
                    self.active[row["id"]]["resume_after_checkpoint"] = True
                    continue
                try:
                    self.resume(row["id"], auto=True)
                except Fault:
                    pass

    def resolve(self, run, decision, note):
        if (
            decision not in ("completed", "retry")
            or not isinstance(note, str)
            or not note.strip()
        ):
            raise Fault(400, "Review requires an outcome and an operator note")
        with self.cv, self.state.transaction() as db:
            info = self.state.status(run, full=True)
            if run in self.active or info["state"] != "review":
                raise Fault(409, "There is no settled, uncertain action to review")
            n = info["step"]
            if decision == "completed" and n < len(info["request"]["steps"]):
                db.execute(
                    "INSERT INTO steps VALUES(?,?,?)",
                    (run, n, json.dumps({"operator_receipt": note})),
                )
                n += 1
            db.execute(
                "UPDATE runs SET state='stopped',reason='Action reviewed',step=?,updated=? WHERE id=?",
                (n, time.time(), run),
            )
            self.state.record(run, n, "reviewed", {"decision": decision, "note": note})
        return self.state.status(run)

    def shutdown(self):
        with self.cv:
            self.closing = True
            live = list(self.active.values())
            self.stop(emergency=False)
            self.cv.notify_all()
        self.thread.join(timeout=2)
        deadline = time.monotonic() + 8
        for control in live:
            control["finished"].wait(max(0, deadline - time.monotonic()))
