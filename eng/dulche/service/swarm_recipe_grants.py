"""Bind swarm recipe grants to the operator-registered action at admission."""

import hashlib
import json
from pathlib import Path

from state import Fault


def recipe_digest(recipe):
    # Match the scheduler's existing queued-recipe digest.
    return hashlib.sha256(json.dumps(recipe, sort_keys=True).encode()).hexdigest()


def authorize_recipe(state, request, name, recipe):
    """Recheck a swarm task's delegated grant before enqueue and execution.

    The persisted swarm configuration, rather than a model response or a queued
    request, is the authority for the originally admitted recipe definition.
    """
    sid = request.get("swarm_id")
    tid = request.get("task_id")
    try:
        if not isinstance(sid, str) or not isinstance(tid, str):
            raise Fault(403, "A swarm recipe needs a swarm and task identity")
        if request.get("swarm_worker") != tid:
            raise Fault(403, "Swarm worker and task identities do not match")
        with state.lock:
            row = state.db.execute("SELECT doc FROM swarms WHERE id=?", (sid,)).fetchone()
            if not row:
                raise Fault(403, "Unknown swarm recipe grant")
            doc = json.loads(row[0])
        task = doc.get("tasks", {}).get(tid)
        if not isinstance(task, dict) or task.get("stop_latched"):
            raise Fault(403, "Swarm task has no active recipe grant")
        if task.get("state") not in ("needs_tools", "tools", "tools_parked"):
            raise Fault(403, "Swarm task is not authorized to run tools")
        config = doc["config"]
        role = next(
            (item for item in config["workers"] if item.get("role") == task.get("role")),
            None,
        )
        if (
            not role
            or role.get("autonomy") != "approved_recipes"
            or name not in role.get("recipes", [])
            or name not in task.get("requested_recipes", [])
            or request.get("model") != role.get("model")
        ):
            raise Fault(403, "Recipe is outside the delegated swarm task grant")
        hashes = role.get("recipe_hashes")
        if not isinstance(hashes, dict) or not isinstance(hashes.get(name), str):
            raise Fault(409, "Swarm recipe grant has no admission fingerprint; start a newly authorized swarm")
        if recipe_digest(recipe) != hashes[name]:
            raise Fault(409, "Registered recipe changed since swarm authorization")
        root = Path(config["working_directory"])
        if not Path(recipe["cwd"]).resolve().is_relative_to(root):
            raise Fault(403, "Recipe working directory is outside the swarm grant")
    except Fault as error:
        # Do not copy command arguments, generated content or private reasoning into audit.
        state.audit(
            "swarm_recipe_denied",
            sid if isinstance(sid, str) else "",
            {"task": tid if isinstance(tid, str) else None, "recipe": name, "reason": str(error)},
        )
        raise
