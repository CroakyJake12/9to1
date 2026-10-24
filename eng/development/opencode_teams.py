#!/usr/bin/env python3
"""One-shot native OpenCode v2 adjunct to the existing coordinator ledger.

No server, independent ledger, worker daemon, model fallback or interrupt operation.
The existing coordinator invokes ticks and supplies fresh supported API observations.
"""
import argparse
import copy
import hashlib
import json
import time
import os
import contextlib
import fcntl
import urllib.request
import urllib.error
from pathlib import Path
import workflow

TEAMS = {
    "platform": ["Home/Haven", "CUI/frameworks", "Accounts", "Build delivery"],
    "workspaces": ["Spaces including Assistants", "Voice & Vision", "Build", "Automations"],
    "productivity": ["Write", "Present", "Boards", "Canvas"],
    "data-services": ["Data", "Files", "Planner", "Connect Mail"],
}
LABELS = {"leader": "GPT-6.1 Sol", "worker": "GPT-6 Luna", "supervisor": "GPT-6 Astra"}
PHASES = [
    ["Haven/Home Shell", "CUI/nodes/Action Graphs/settings/Home Core/Dulche/shared runtime/permissions/package lifecycle/model routing"],
    ["Spaces including Study/Tasks/Assistants/Research/Shopping/Translate/Space Builder", "Voice & Vision"],
    ["Build including Dev/Sites/Stacks workspaces"], ["Automations"],
    ["Write", "Present", "Boards", "Canvas", "Data/shared productivity engine"],
    ["Files", "web services/accounts/cloud storage"], ["Planner", "Connect including Mail"],
    ["Cards"], ["Forms"], ["AI Studio"], ["Picture", "Wave", "Motion"], ["Games", "Play"],
    ["Mini Computer"], ["Link", "Prototype", "Paths", "Replay", "all other independently specified retained products"],
]
Refusal = workflow.Refusal


class NativeV2:
    """Only the inspected v2 operations; never auto-starts a service."""
    def __init__(self, base):
        from urllib.parse import urlparse
        parsed = urlparse(base)
        if parsed.scheme != "http" or parsed.hostname not in ("127.0.0.1", "localhost", "::1") or parsed.username or parsed.password or parsed.query or parsed.fragment:
            raise Refusal("Use the existing loopback server without URL credentials")
        self.base = base.rstrip("/")

    def request(self, method, path, body=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.base + path, data=data, method=method,
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=15) as response:
            raw = response.read(8 * 1024 * 1024)
        return json.loads(raw) if raw else None


def extension(state):
    return state.setdefault("opencode_teams", {"schema": 1, "bindings": {}, "clocks": {}, "outbox": {}})


def configure(state, catalog):
    if not set(TEAMS).issubset(catalog.get("teams", {})) or any(not 3 <= len(v) <= 4 for v in catalog["teams"].values()):
        raise Refusal("Initial four teams require three to four surfaces each")
    if len(catalog.get("phases", [])) != 14 or [p.get("id") for p in catalog["phases"]] != list(range(1, 15)):
        raise Refusal("Preserve the full fourteen-phase canonical catalogue")
    for phase in catalog["phases"]:
        if not phase.get("surfaces") or not isinstance(phase.get("requirements"), list):
            raise Refusal("Each phase requires exact surfaces and existing coverage task references")
        if not set(PHASES[phase["id"] - 1]).issubset(phase["surfaces"]):
            raise Refusal("Canonical phase scope cannot be narrowed")
    extension(state)["catalogue"] = copy.deepcopy(catalog)


def phase_complete(state, phase):
    custody = state.get("execution_custody", {}).get("cloud-owning-v2", {})
    revision = custody.get("revision")
    identity = custody.get("source", {}).get("manifest")
    if not revision or not identity: return False
    retained(identity)
    if workflow.read(identity["path"]).get("candidate_revision") != revision:
        raise Refusal("Current coordinator integration candidate identity differs")
    refs = phase["requirements"]
    if not refs: return False
    for ref in refs:
        task = state.get("tasks", {}).get(ref, {})
        if task.get("status") != "COMPLETED" or task.get("source_revision") != revision:
            return False
        artifacts = [state.get("evidence", {}).get(key, {}) for key in task.get("evidence", [])]
        coverage = [e for e in artifacts if e.get("kind") == "implementation-coverage" and e.get("result") == "PASS" and e.get("source_revision") == revision]
        if not coverage: return False
        for evidence in coverage:
            retained(evidence.get("artifact"))
            manifest = workflow.read(evidence["artifact"]["path"])
            requirements = manifest.get("requirements", [])
            if (manifest.get("source_revision") != revision or not requirements
                    or manifest.get("applicable_scope_complete") is not True
                    or any(not r.get("requirement_id") or r.get("implemented") is not True or not r.get("source_evidence") for r in requirements)):
                return False
    return True


def phase_allowed(state, binding):
    catalogue = extension(state).get("catalogue")
    if not catalogue: raise Refusal("Full canonical team/phase catalogue required")
    phase = binding.get("phase")
    if not isinstance(phase, int) or phase not in range(1, 15): raise Refusal("Exact canonical phase required")
    # First six priority phases run in parallel. Later apps wait for genuine coverage.
    predecessors = catalogue["phases"][:max(6, phase - 1)] if phase > 6 else []
    if any(not phase_complete(state, p) for p in predecessors):
        raise Refusal("Priority or earlier canonical implementation requirements remain incomplete")


def retained(identity):
    if not isinstance(identity, dict) or not identity.get("path"):
        raise Refusal("Retained provider entitlement evidence required")
    actual = workflow.file_identity(identity["path"])
    if actual != {k: v for k, v in identity.items() if k != "path"}:
        raise Refusal("Provider evidence changed")


def model_allowed(state, binding):
    if binding.get("provider_family") not in state.get("provider_policy", {}).get("allowed", []):
        raise Refusal("Unknown or prohibited provider")
    if binding.get("label") != LABELS.get(binding.get("role")):
        raise Refusal("Model substitution prohibited")
    if binding.get("entitlement") != "VERIFIED" or binding.get("paid_fallback") is not False:
        raise Refusal("Verified entitlement and disabled paid fallback required")
    retained(binding.get("evidence"))
    proof = workflow.read(binding["evidence"]["path"])
    current = time.time()
    if (proof.get("schema") != "9to1.native-model-entitlement/v1"
            or proof.get("verified_by") not in ("owner", "existing-coordinator")
            or not isinstance(proof.get("verified_at"), (int, float))
            or not isinstance(proof.get("expires_at"), (int, float))
            or not proof["verified_at"] <= current < proof["expires_at"]
            or proof["expires_at"] - proof["verified_at"] > 1800
            or any(proof.get(k) != binding.get(k) for k in ("label", "model", "provider_family", "entitlement", "paid_fallback"))
            or not proof.get("native_name")):
        raise Refusal("Fresh trusted exact label/provider/model entitlement mapping required")
    ref = binding.get("model", {})
    if not ref.get("id") or not ref.get("providerID"):
        raise Refusal("Actual native provider/model IDs required")


def native_model(api, binding):
    rows = api.request("GET", "/api/model")["data"]
    ref = binding["model"]
    proof = workflow.read(binding["evidence"]["path"])
    if not any(x.get("id") == ref["id"] and x.get("providerID") == ref["providerID"] and x.get("name") == proof["native_name"]
               and x.get("enabled") is True and x.get("status") == "active" for x in rows):
        raise Refusal("Exact model is unavailable; no fallback")


def binding_identity(binding):
    return hashlib.sha256(json.dumps(binding, sort_keys=True).encode()).hexdigest()


def native_session(api, binding):
    workspace_check(binding)
    info = api.request("GET", "/api/session/" + binding["session_id"])["data"]
    if (info.get("id") != binding["session_id"] or info.get("model") != binding["model"]
            or info.get("projectID") != binding["project_id"] or info.get("location") != binding["location"]
            or info.get("time", {}).get("archived")):
        raise Refusal("Live session model/project/location identity changed")
    if binding["role"] == "worker":
        metadata = info.get("metadata", {})
        if info.get("parentID") or metadata.get("9to1_team") != binding.get("team") or metadata.get("9to1_leader_session") != binding.get("leader_session_id"):
            raise Refusal("Worker requires genuine native team metadata and independent root session")
    return info


def workspace_check(binding):
    retained(binding.get("workspace_identity"))
    workspace = workflow.read(binding["workspace_identity"]["path"])
    if (workspace.get("project_id") != binding.get("project_id") or workspace.get("location") != binding.get("location")
            or not workspace.get("revision") or not workspace.get("generation") or not workspace.get("tree")
            or workspace.get("root_writer_handoff_ack") is not True
            or workspace.get("claimed_paths") != binding.get("allowed_paths")):
        raise Refusal("Pinned project/worktree generation required")
    directory = binding["location"]["directory"]
    stat = os.stat(directory)
    if workspace.get("directory_identity") != {"device": stat.st_dev, "inode": stat.st_ino}:
        raise Refusal("Worktree directory regenerated")
    if (workflow.git(directory, "rev-parse", "HEAD").decode().strip() != workspace["revision"]
            or workflow.git(directory, "rev-parse", "HEAD^{tree}").decode().strip() != workspace["tree"]):
        raise Refusal("Worktree revision/tree differ from exact handoff ACK")


def binding_preflight(state, name, binding, *, creating=False):
    """Pure local admission; run before any session-creation side effect."""
    catalogue = extension(state).get("catalogue", {})
    teams = catalogue.get("teams", {})
    if binding.get("role") not in LABELS or (name not in teams and name != "astra" and binding.get("role") != "worker"):
        raise Refusal("Unknown team or role")
    expected = "supervisor" if name == "astra" else ("leader" if name in teams else "worker")
    if binding["role"] != expected:
        raise Refusal("Role mismatch")
    sid = binding.get("session_id", "")
    if not sid.startswith("ses") or "/" in sid:
        raise Refusal("Valid native session ID required")
    paths = binding.get("allowed_paths")
    if not isinstance(paths, list) or not paths:
        raise Refusal("Explicit approved file scope required before session creation")
    for path in paths: workflow.scope_overlap(path, path)
    ext = extension(state)
    if any(r["owner"] == name and r["status"] in ("PREPARED", "UNKNOWN", "QUEUED") for r in ext["outbox"].values()):
        raise Refusal("Unresolved delivery prevents changing session binding")
    if expected == "worker":
        leader = ext["bindings"].get(binding.get("team"), {})
        if binding.get("team") not in teams or leader.get("role") != "leader" or binding.get("leader_session_id") != leader.get("session_id"):
            raise Refusal("Approved team and actual bound leader required")
        directory = os.path.realpath(binding.get("location", {}).get("directory", ""))
        if any(os.path.realpath(b["location"]["directory"]) == directory for key, b in ext["bindings"].items() if key != name):
            raise Refusal("Worker requires an independent isolated worktree")
    if any(b.get("session_id") == sid for key, b in ext["bindings"].items() if key != name):
        raise Refusal("Native session identity already assigned")
    if expected == "supervisor" and any(b.get("role") == "supervisor" for key, b in ext["bindings"].items() if key != name):
        raise Refusal("Only one Astra supervisor is permitted")
    if creating:
        if ext.get("paused") is not False:
            raise Refusal("New native session creation is paused or unknown")
        phase_allowed(state, binding)
        probe = copy.deepcopy(state)
        workflow.claim(probe, sid, paths, binding["role"])
        capacity = catalogue.get("capacity")
        if capacity is not None:
            if not isinstance(capacity, int) or isinstance(capacity, bool) or capacity < 1:
                raise Refusal("Invalid existing coordinator creation capacity")
            reserved = {b["session_id"] for b in ext["bindings"].values()}
            reserved.update(r["session_id"] for r in ext.get("bootstrap", {}).values())
            if sid not in reserved and len(reserved) >= capacity:
                raise Refusal("Existing coordinator session capacity exhausted")
    return expected


def bind(state, name, binding, api):
    expected = binding_preflight(state, name, binding)
    model_allowed(state, binding)
    native_model(api, binding)
    sid = binding.get("session_id", "")
    if not sid.startswith("ses") or "/" in sid:
        raise Refusal("Real native session ID required")
    ext = extension(state)
    if any(v["owner"] == name and v["status"] in ("PREPARED", "UNKNOWN", "QUEUED") for v in ext["outbox"].values()):
        raise Refusal("Unresolved delivery prevents rebinding")
    info = native_session(api, binding)
    if any(v["session_id"] == sid for k, v in ext["bindings"].items() if k != name):
        raise Refusal("A session cannot represent two owners")
    if expected == "worker":
        parent = ext["bindings"].get(binding.get("team"), {}).get("session_id")
        if not parent or binding.get("leader_session_id") != parent:
            raise Refusal("Worker must report to its actual bound team leader")
        directory = os.path.realpath(binding["location"]["directory"])
        if any(os.path.realpath(b["location"]["directory"]) == directory for key, b in ext["bindings"].items() if key != name):
            raise Refusal("Worker requires its own isolated Git worktree")
    elif info.get("parentID"):
        raise Refusal("Leaders/supervisor require root sessions for native tabs")
    ext["bindings"][name] = copy.deepcopy(binding)


def admit(state, name, paths, kind, capacity, api):
    ext = extension(state)
    b = ext["bindings"].get(name)
    if not b:
        raise Refusal("Native session is not bound")
    if b["role"] == "worker" and ext["bindings"].get(b.get("team"), {}).get("session_id") != b.get("leader_session_id"):
        raise Refusal("Team leader binding changed; reconcile worker association")
    if b["role"] == "worker" and any(os.path.realpath(other["location"]["directory"]) == os.path.realpath(b["location"]["directory"]) for key, other in ext["bindings"].items() if key != name):
        raise Refusal("Worker worktree is no longer isolated")
    model_allowed(state, b)
    phase_allowed(state, b)
    native_model(api, b)
    native_session(api, b)
    window = state.get("owner_implementation_window", {})
    if kind == "acceptance" and window.get("acceptance_paused") is not False:
        raise Refusal("Independent acceptance remains paused")
    if kind not in ("implementation", "compile", "smoke", "supervision", "acceptance"):
        raise Refusal("Unknown work kind")
    if ext.get("paused") is not False:
        raise Refusal("Team delivery paused or unknown")
    if not isinstance(capacity, int) or isinstance(capacity, bool) or capacity < 1:
        raise Refusal("Existing coordinator capacity required")
    live = api.request("GET", "/api/session/active")["data"]
    active = {x if isinstance(x, str) else x["id"] for x in live}
    if b["session_id"] in active:
        return False
    reserved = {r["session_id"] for r in ext["outbox"].values() if r["status"] in ("PREPARED", "UNKNOWN", "QUEUED") and r["session_id"] != b["session_id"]}
    if len(active | reserved) >= capacity:
        raise Refusal("Existing coordinator capacity exhausted")
    if not paths:
        raise Refusal("Exact ownership paths required")
    for p in paths:
        workflow.scope_overlap(p, p)
        allowed = b.get("allowed_paths", [])
        if not any(p == a or (a.endswith("/**") and p.startswith(a[:-3] + "/")) for a in allowed):
            raise Refusal("Path outside the coordinator-approved team scope")
    # Preview the existing exclusive ownership guard without stealing an owner.
    probe = copy.deepcopy(state)
    workflow.claim(probe, b["session_id"], paths, b["role"])
    return True


def tick(state, at):
    """Coalesce elapsed periods into one pending activation, including busy sessions."""
    ext = extension(state)
    for name in dict.fromkeys([*TEAMS, "astra", *ext["bindings"]]):
        period = 1800 if name == "astra" else 300
        slot = int(at) // period
        clock = ext["clocks"].setdefault(name, {"last_slot": slot - 1, "pending": False})
        if slot > clock["last_slot"]:
            clock.update(last_slot=slot, pending=True)
    return copy.deepcopy(ext["clocks"])


def prepare(state, name, text, paths, kind, capacity, api, at):
    ext = extension(state)
    clock = ext["clocks"].get(name, {})
    if not clock.get("pending"):
        return None
    existing = [v for v in ext["outbox"].values() if v["owner"] == name and v["status"] in ("PREPARED", "UNKNOWN", "QUEUED")]
    if existing:
        return None
    if not admit(state, name, paths, kind, capacity, api):
        return None
    sid = ext["bindings"][name]["session_id"]
    seed = json.dumps([sid, clock["last_slot"], text, paths, kind], sort_keys=True)
    mid = "msg_" + hashlib.sha256(seed.encode()).hexdigest()[:32]
    workflow.claim(state, sid, paths, ext["bindings"][name]["role"])
    ext["outbox"][mid] = {"owner": name, "session_id": sid, "status": "PREPARED", "paths": paths,
                           "kind": kind, "slot": clock["last_slot"], "created": at,
                           "capacity": capacity,
                           "binding_identity": binding_identity(ext["bindings"][name]),
                           "budget_seconds": 300 if name == "astra" else None,
                           "body": {"id": mid, "text": text, "delivery": "queue", "resume": True}}
    clock["pending"] = False
    return mid


def reconcile(record, api):
    sid, mid = record["session_id"], record["body"]["id"]
    items = api.request("GET", "/api/session/" + sid + "/inbox")["data"]
    if any(x.get("id") == mid and x.get("sessionID") == sid and x.get("type") == "user" and x.get("delivery") == "queue" for x in items):
        record["status"] = "QUEUED"
        return record["status"]
    # Absence from pending inbox is NOT completion or permission to retry.
    try:
        info = api.request("GET", "/api/session/" + sid + "/message/" + mid)["data"]
    except Exception:
        record["status"] = "UNKNOWN"
        return "UNKNOWN"
    if info.get("id") == mid and info.get("sessionID") == sid:
        record["status"] = "DELIVERED"
    else:
        record["status"] = "UNKNOWN"
    return record["status"]


def send_once(state_path, expected, mid, api):
    # Persist uncertainty BEFORE the side effect, so crashes never cause blind retry.
    with workflow.transaction(state_path, expected) as state:
        record = extension(state)["outbox"][mid]
        if record["status"] != "PREPARED":
            raise Refusal("Reconcile existing delivery; never blindly retry")
        binding = extension(state)["bindings"].get(record["owner"], {})
        if record["session_id"] != binding.get("session_id") or record.get("binding_identity") != binding_identity(binding):
            raise Refusal("Prepared binding identity changed")
        if not admit(state, record["owner"], record["paths"], record["kind"], record["capacity"], api):
            return {"id": mid, "status": "PREPARED", "reason": "busy; coalesced"}
        record["status"] = "UNKNOWN"
        record.setdefault("sent_at", int(time.time()))
        if record["budget_seconds"]:
            record.setdefault("checkpoint_due_at", record["sent_at"] + record["budget_seconds"])
        request = copy.deepcopy(record)
        if request["budget_seconds"]:
            request["body"]["text"] = ("Supervisor work budget: 300 seconds. Checkpoint and return by UTC epoch "
                + str(request["checkpoint_due_at"]) + ". Do not interrupt any other session.\n" + request["body"]["text"])
    try:
        receipt = api.request("POST", "/api/session/" + request["session_id"] + "/prompt", request["body"])["data"]
    except Exception:
        return {"id": mid, "status": "UNKNOWN"}
    with workflow.transaction(state_path, expected + 1) as state:
        record = extension(state)["outbox"][mid]
        if receipt.get("id") != mid or receipt.get("sessionID") != request["session_id"] or receipt.get("type") != "user" or receipt.get("delivery") != "queue":
            raise Refusal("Native receipt identity mismatch; remains UNKNOWN")
        record["status"] = "QUEUED"
        record["receipt"] = {k: receipt[k] for k in ("id", "sessionID", "type", "delivery") if k in receipt}
    return {"id": mid, "status": "QUEUED"}


def bootstrap(state_path, expected, name, binding, api):
    """Crash-safe native creation. An uncertain POST is reconciled, never repeated."""
    with workflow.transaction(state_path, expected) as state:
        ext = extension(state)
        model_allowed(state, binding); native_model(api, binding)
        workspace_check(binding)
        sid = binding.get("session_id", "")
        if not sid.startswith("ses") or "/" in sid:
            raise Refusal("A valid durable client session ID is required")
        intents = ext.setdefault("bootstrap", {})
        old = intents.get(name)
        if old and old["binding_identity"] != binding_identity(binding):
            raise Refusal("Bootstrap identity changed; reconcile original intent")
        try:
            api.request("GET", "/api/session/" + sid)
        except urllib.error.HTTPError as e:
            if e.code != 404: raise
            if old:
                raise Refusal("Uncertain session creation; no blind retry")
        else:
            bind(state, name, binding, api)
            intents[name] = {"status": "BOUND", "binding_identity": binding_identity(binding), "session_id": sid}
            return {"status": "BOUND", "session_id": sid}
        binding_preflight(state, name, binding, creating=True)
        body = {"id": sid, "title": "9to1 / " + name, "model": binding["model"], "location": binding["location"]}
        if binding["role"] == "worker":
            parent = ext["bindings"].get(binding.get("team"))
            if not parent or binding.get("leader_session_id") != parent["session_id"] or parent["location"] == binding["location"]:
                raise Refusal("Worker needs an independent worktree and actual team leader")
            body["metadata"] = {"9to1_team": binding["team"], "9to1_leader_session": parent["session_id"]}
        intents[name] = {"status": "UNKNOWN", "binding_identity": binding_identity(binding), "session_id": sid}
    try:
        api.request("POST", "/api/session", body)
    except Exception:
        return {"status": "UNKNOWN", "session_id": sid}
    with workflow.transaction(state_path, expected + 1) as state:
        bind(state, name, binding, api)
        extension(state)["bootstrap"][name]["status"] = "BOUND"
    return {"status": "BOUND", "session_id": sid}


def report(state, name, task_id, artifact, at):
    """Retain a task-linked result for the next leader/Astra cadence, never wake now."""
    ext = extension(state); binding = ext["bindings"][name]
    task = state["tasks"].get(task_id, {})
    if task.get("owner") != binding["session_id"]:
        raise Refusal("Report must reference the sender's existing task")
    retained(artifact)
    result = workflow.read(artifact["path"])
    if result.get("task_id") != task_id or result.get("source_revision") != task.get("source_revision") or not result.get("summary"):
        raise Refusal("Result artifact needs exact task/revision and factual summary")
    target = binding.get("team") if binding["role"] == "worker" else "astra"
    if target == name or target not in ext["bindings"]:
        raise Refusal("Actual upstream leader/supervisor binding required")
    period = 1800 if target == "astra" else 300
    rid = hashlib.sha256(json.dumps([name, task_id, artifact], sort_keys=True).encode()).hexdigest()
    ext.setdefault("reports", {}).setdefault(rid, {"sender": name, "target": target, "task_id": task_id,
        "artifact": artifact, "summary": result["summary"], "ready_slot": int(at) // period + 1, "status": "PENDING"})
    return rid


def dispatch(state_path, expected, jobs, capacity, api, at):
    """Existing coordinator invokes once per tick; all durable state stays in its ledger."""
    results = []
    with workflow.transaction(state_path, expected) as state:
        tick(state, at)
        ext = extension(state)
        for mid, record in ext["outbox"].items():
            if record["status"] in ("UNKNOWN", "QUEUED"):
                results.append({"id": mid, "status": reconcile(record, api)})
        active = api.request("GET", "/api/session/active")["data"]
        for record in ext["outbox"].values():
            if record.get("checkpoint_due_at") and at >= record["checkpoint_due_at"]:
                record["budget_observation"] = {"at": at, "native_active": record["session_id"] in active,
                    "status": "OVERRUN_CHECKPOINT_REQUIRED" if record["session_id"] in active else "NOT_ACTIVE_NO_COMPLETION_CLAIM"}
        ext["last_checkpoint"] = {"at": at, "event": "native-reconciliation", "inference_claim": False}
        prepared = [mid for mid, record in ext["outbox"].items() if record["status"] == "PREPARED"]
        jobs = copy.deepcopy(jobs)
        for target in dict.fromkeys(r["target"] for r in ext.get("reports", {}).values() if r["status"] == "PENDING"):
            period = 1800 if target == "astra" else 300
            ready = [(rid, r) for rid, r in ext["reports"].items() if r["target"] == target and r["status"] == "PENDING" and int(at) // period >= r["ready_slot"]]
            if not ready: continue
            binding = ext["bindings"][target]
            paths = binding.get("report_paths")
            if not paths: raise Refusal("Upstream report requires approved coordinator report paths")
            jobs.append({"name": target, "kind": "supervision", "paths": paths,
                "text": "Review factual upstream reports; preserve original task/evidence status.\n" + json.dumps([{ "report_id": rid, **r} for rid, r in ready], sort_keys=True),
                "report_ids": [rid for rid, r in ready]})
    generation = expected + 1
    for mid in prepared:
        results.append(send_once(state_path, generation, mid, api))
        generation = workflow.read(state_path)["generation"]
    for job in jobs:
        with workflow.transaction(state_path, generation) as state:
            mid = prepare(state, job["name"], job["text"], job["paths"], job["kind"], capacity, api, at)
            if mid:
                record = extension(state)["outbox"][mid]
                record["body"]["metadata"] = {"9to1_reports": job.get("report_ids", []), "9to1_task": job.get("task_id")}
                for rid in job.get("report_ids", []):
                    extension(state)["reports"][rid].update(status="OUTBOX", message_id=mid)
        generation += 1
        if mid:
            result = send_once(state_path, generation, mid, api)
            results.append(result)
            # send_once can persist one or two generations; reread rather than guess.
            generation = workflow.read(state_path)["generation"]
    return {"generation": generation, "deliveries": results, "status": "ONE_SHOT_COMPLETE"}


def tab_links(state, server_key):
    import base64
    if not server_key: raise Refusal("Actual native registered server key required")
    encoded = base64.b64encode(server_key.encode()).decode().rstrip("=").replace("+", "-").replace("/", "_")
    return {name: "/server/" + encoded + "/session/" + b["session_id"]
            for name, b in extension(state)["bindings"].items() if b["role"] != "worker"}


@contextlib.contextmanager
def invoker_lock(state_path):
    with Path(str(state_path) + ".opencode-teams-invoker.lock").open("a+b") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise Refusal("Another team watch/callback invoker already owns this state") from error
        try: yield
        finally: fcntl.flock(lock, fcntl.LOCK_UN)


def watch(state_path, jobs_path, capacity_path, api, *, interval=30, stop_path=None,
          max_cycles=None, clock=time.time, wait=time.sleep):
    """Optional foreground scheduling only; never starts/stops a service or worker."""
    if not 1 <= interval <= 60 or (max_cycles is not None and max_cycles < 1):
        raise Refusal("Watch interval must be1–60seconds and cycle bound positive")
    cycles = 0
    with invoker_lock(state_path):
        try:
            while True:
                state = workflow.read(state_path)
                if extension(state).get("paused") is not False:
                    return {"status": "PAUSED", "cycles": cycles}
                if stop_path and Path(stop_path).exists():
                    return {"status": "STOP_REQUESTED", "cycles": cycles}
                jobs = workflow.read(jobs_path)
                capacity = workflow.read(capacity_path)["capacity"]
                dispatch(state_path, state["generation"], jobs, capacity, api, int(clock()))
                cycles += 1
                if max_cycles is not None and cycles >= max_cycles:
                    return {"status": "CYCLE_BOUND", "cycles": cycles}
                wait(interval)
        except KeyboardInterrupt:
            return {"status": "INTERRUPTED", "cycles": cycles}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--state", required=True)
    p.add_argument("--expect", type=int, required=True)
    p.add_argument("--server", required=True)
    sub = p.add_subparsers(dest="action", required=True)
    q = sub.add_parser("bind"); q.add_argument("name"); q.add_argument("binding")
    q = sub.add_parser("tick"); q.add_argument("--at", type=int)
    q = sub.add_parser("prepare"); q.add_argument("name"); q.add_argument("--text-file", required=True); q.add_argument("--paths", nargs="+", required=True); q.add_argument("--kind", required=True); q.add_argument("--capacity", type=int, required=True)
    q = sub.add_parser("send"); q.add_argument("message_id")
    q = sub.add_parser("reconcile"); q.add_argument("message_id")
    q = sub.add_parser("pause"); q.add_argument("value", choices=["paused", "resumed"])
    q = sub.add_parser("bootstrap"); q.add_argument("name"); q.add_argument("binding")
    q = sub.add_parser("dispatch"); q.add_argument("--jobs", required=True); q.add_argument("--capacity", type=int, required=True)
    q = sub.add_parser("tabs"); q.add_argument("--server-key", required=True)
    q = sub.add_parser("configure"); q.add_argument("catalogue")
    q = sub.add_parser("report"); q.add_argument("name"); q.add_argument("task_id"); q.add_argument("artifact_identity")
    q = sub.add_parser("watch"); q.add_argument("--jobs", required=True); q.add_argument("--capacity-file", required=True); q.add_argument("--interval", type=int, default=30); q.add_argument("--stop-file"); q.add_argument("--max-cycles", type=int)
    a = p.parse_args(); api = NativeV2(a.server)
    try:
        if a.action == "send":
            result = send_once(a.state, a.expect, a.message_id, api)
        elif a.action == "bootstrap": result = bootstrap(a.state, a.expect, a.name, workflow.read(a.binding), api)
        elif a.action == "dispatch":
            with invoker_lock(a.state): result = dispatch(a.state, a.expect, workflow.read(a.jobs), a.capacity, api, int(time.time()))
        elif a.action == "watch": result = watch(a.state, a.jobs, a.capacity_file, api, interval=a.interval, stop_path=a.stop_file, max_cycles=a.max_cycles)
        elif a.action == "tabs": result = tab_links(workflow.read(a.state), a.server_key)
        else:
            with workflow.transaction(a.state, a.expect) as state:
                if a.action == "bind": result = bind(state, a.name, workflow.read(a.binding), api)
                elif a.action == "tick": result = tick(state, a.at if a.at is not None else int(time.time()))
                elif a.action == "prepare": result = prepare(state, a.name, Path(a.text_file).read_text(), a.paths, a.kind, a.capacity, api, int(time.time()))
                elif a.action == "pause": extension(state)["paused"] = a.value == "paused"; result = {"paused": extension(state)["paused"]}
                elif a.action == "configure": result = configure(state, workflow.read(a.catalogue))
                elif a.action == "report": result = report(state, a.name, a.task_id, workflow.read(a.artifact_identity), int(time.time()))
                else: result = reconcile(extension(state)["outbox"][a.message_id], api)
        print(json.dumps(result))
    except (Refusal, OSError, KeyError, ValueError) as e:
        p.exit(2, str(e) + "\n")


if __name__ == "__main__":
    main()
