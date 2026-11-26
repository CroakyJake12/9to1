"""Validate user-authorized Swarm configuration and mentor-produced plans."""

import json
import re
from pathlib import Path

from state import Fault
from swarm_placement import configured
from swarm_recipe_grants import recipe_digest

STRATEGIES = ("resident", "alternating", "partial")
PRESETS = {
    "balanced": {
        "seconds": 300,
        "tasks": 4,
        "tokens": 8192,
        "milestones": True,
        "on_error": True,
        "on_conflict": True,
    },
    "frequent": {
        "seconds": 90,
        "tasks": 2,
        "tokens": 2048,
        "milestones": True,
        "on_error": True,
        "on_conflict": True,
    },
    "milestones": {
        "seconds": 0,
        "tasks": 0,
        "tokens": 0,
        "milestones": True,
        "on_error": True,
        "on_conflict": True,
    },
}
ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]{0,63}")
MIN_CONTEXT = 16384


def text(value, name, limit=8192):
    if not isinstance(value, str) or not value.strip() or len(value) > limit:
        raise Fault(400, f"{name} must be nonempty text of at most {limit} characters")
    return value.strip()


def identifier(value, name):
    if not isinstance(value, str) or not ID.fullmatch(value):
        raise Fault(400, f"Invalid {name}")
    return value


def integer(value, name, lo, hi):
    if type(value) is not int or not lo <= value <= hi:
        raise Fault(400, f"{name} must be an integer from {lo} to {hi}")
    return value


def unique_strings(values, name, maximum=32):
    if not isinstance(values, list) or len(values) > maximum:
        raise Fault(400, f"Invalid {name} list")
    entries = [text(v, name, 1024) for v in values]
    if len(set(entries)) != len(entries):
        raise Fault(400, f"Duplicate {name}")
    return entries


def check_in(values):
    if not isinstance(values, dict) or set(values) - set(PRESETS["balanced"]):
        raise Fault(400, "Unsupported check-in scheduling fields")
    result = {**PRESETS["balanced"], **values}
    for k, maxv in (("seconds", 86400), ("tasks", 128), ("tokens", 10000000)):
        integer(result[k], k, 0, maxv)
    for k in ("milestones", "on_error", "on_conflict"):
        if not isinstance(result[k], bool):
            raise Fault(400, f"{k} must be a boolean")
    return result


def resolved_profile(ctx, mid, profile):
    model = ctx.model(mid)
    profiles = {p["id"]: p for p in ctx.profiles(mid)}
    if profile not in profiles:
        raise Fault(422, f"Unknown profile for {mid}")
    selected = profiles[profile].get("settings", {})
    validate = getattr(ctx.engines, "validate_settings", None)
    if validate:
        try:
            resolution = validate(mid, selected)
        except (ValueError, RuntimeError, KeyError) as error:
            raise Fault(422, f"Profile is unsupported for {mid}: {error}") from error
        if not isinstance(resolution, dict):
            raise Fault(422, "Engine did not confirm the selected profile")
        out = {**selected, **resolution}
        explicit = profiles[profile].get("placement_requested")
        if isinstance(explicit, dict) and "placement_requested" not in out:
            out["placement_requested"] = explicit
        return out
    return {
        "context": model.get("context") or model.get("context_length"),
        "engine": model.get("engine"),
        "gpu": model.get("gpu"),
        **selected,
    }


def reasoning_capabilities(ctx, mid, engine=None):
    row = ctx.model(mid)
    engine = engine or row.get("engine") or ""
    if (
        mid in ("qwen3.8-flash-next-unsloth-ud-iq4_xs", "unsloth-ud-iq4_xs")
        and "strata" in engine.lower()
    ):
        return {
            "modes": ["default", "enabled", "disabled"],
            "efforts": ["low", "medium", "xhigh"],
            "protocol": "native_strata",
        }
    known = mid in ("qwen3.8-27b-v100", "qwen3.8-27b-cpu")
    if not known and mid.startswith("local-") and getattr(ctx, "catalog", None):
        try:
            path = Path(ctx.catalog.get(mid)["path"])
        except (KeyError, TypeError):
            path = None
        # Limit the override to the actual 27B file whose embedded Jinja behavior was inspected.
        canonical = (
            Path.home() / "Documents/Default Project/models/Qwen3.8-27B-UD-IQ4_XS.gguf"
        )
        if path:
            try:
                known = path.samefile(canonical)
            except OSError:
                known = False
    if known and ("llama" in engine.lower() or "lightning" in engine.lower()):
        return {
            "modes": ["default", "enabled", "disabled"],
            "efforts": [],
            "protocol": "gguf_enable_thinking",
        }
    return {"modes": ["default"], "efforts": [], "protocol": None}


def reasoning_options(participant):
    mode = participant.get("reasoning", "default")
    if mode == "default":
        return {}
    if participant.get("reasoning_protocol") == "native_strata":
        return {
            "reasoning_effort": "none"
            if mode == "disabled"
            else participant.get("reasoning_effort") or "medium"
        }
    if participant.get("reasoning_protocol") == "gguf_enable_thinking":
        return {"chat_template_kwargs": {"enable_thinking": mode == "enabled"}}
    raise Fault(422, "The selected model does not have a verified reasoning control")


def participant(ctx, raw):
    if not isinstance(raw, dict):
        raise Fault(400, "A participant must be an object")
    mid = text(raw.get("model"), "model", 256)
    profile = raw.get("profile", "current")
    selected = resolved_profile(ctx, mid, profile)
    limit = selected.get("context")
    if not isinstance(limit, int) or limit < MIN_CONTEXT:
        raise Fault(
            422,
            f"{mid} needs a confirmed per-slot context of at least {MIN_CONTEXT} tokens",
        )
    context = integer(
        raw.get("context", min(32768, limit)), "context", MIN_CONTEXT, limit
    )
    engine = selected.get("engine")
    gpu = selected.get("gpu")
    if raw.get("engine") and raw["engine"] != engine:
        raise Fault(422, "Selected profile does not use the requested inference engine")
    if raw.get("gpu") and raw["gpu"] != gpu:
        raise Fault(422, "Selected profile does not use the requested device")
    caps = reasoning_capabilities(ctx, mid, engine)
    mode = raw.get("reasoning", "default")
    effort = raw.get("reasoning_effort")
    if mode not in caps["modes"]:
        raise Fault(422, "Unsupported reasoning mode for this model and engine")
    if effort is not None and (mode != "enabled" or effort not in caps["efforts"]):
        raise Fault(422, "Choose a verified effort with enabled reasoning")
    return {
        "model": mid,
        "profile": profile,
        "context": context,
        "engine": engine,
        "gpu": gpu,
        "reasoning": mode,
        "reasoning_effort": effort,
        "reasoning_protocol": caps["protocol"],
        "placement": configured(ctx.model(mid), selected),
    }


def project_snapshot(wd, references):
    """Capture the declared textual sources; remote references remain inert citations."""
    kinds = {
        ".md",
        ".rst",
        ".txt",
        ".cs",
        ".py",
        ".js",
        ".ts",
        ".tsx",
        ".csproj",
        ".props",
        ".sln",
        ".toml",
        ".json",
        ".yaml",
        ".yml",
    }
    visible = sorted(
        p.name
        for p in wd.iterdir()
        if not p.name.startswith(".") and (p.is_dir() or p.suffix.lower() in kinds)
    )
    inspected = []
    for ref in references:
        if "://" in ref:
            continue
        path = (wd / ref).resolve()
        if not path.is_relative_to(wd):
            raise Fault(
                403,
                "A local specification reference is outside the authorized workspace",
            )
        if not path.exists():
            continue
        if not path.is_file() or path.suffix.lower() not in kinds:
            raise Fault(
                422, "Choose a textual source or an inert specification identifier"
            )
        if path.stat().st_size > 65536:
            raise Fault(
                422,
                "Reference exceeds the 64 KiB admission limit; supply a narrower explicit excerpt",
            )
        with path.open("rb") as reader:
            data = reader.read(65537)
        if len(data) > 65536 or b"\x00" in data:
            raise Fault(422, "Reference is too large or is binary")
        inspected.append({"reference": ref, "text": data.decode("utf-8")})
    return {
        "root_entries": visible[:64],
        "additional_root_entries": max(0, len(visible) - 64),
        "explicit_text_sources": inspected,
    }


def configuration(ctx, raw):
    if not isinstance(raw, dict):
        raise Fault(400, "Configuration must be an object")
    wd = Path(
        text(raw.get("working_directory"), "working_directory", 2048)
    ).expanduser()
    if not wd.is_absolute() or not wd.is_dir():
        raise Fault(400, "An existing absolute working directory is required")
    wd = wd.resolve()
    workers = raw.get("workers")
    if not isinstance(workers, list) or not 1 <= len(workers) <= 12:
        raise Fault(400, "Choose one to twelve worker roles")
    roles = []
    for chosen in workers:
        w = participant(ctx, chosen)
        role = identifier(chosen.get("role"), "role")
        if any(r["role"] == role for r in roles):
            raise Fault(400, "Worker role identifiers must be unique")
        allowed = unique_strings(chosen.get("recipes", []), "recipe", 16)
        verify = chosen.get("verify_recipe")
        autonomy = chosen.get("autonomy", "draft")
        if autonomy not in ("draft", "approved_recipes") or (
            autonomy == "draft" and (allowed or verify)
        ):
            raise Fault(400, "Draft-only workers cannot run recipes")
        if verify and verify not in allowed:
            raise Fault(
                400, "The verification recipe must be in the explicit permission list"
            )
        recipe_hashes = {}
        for recipe in allowed:
            registered = ctx.jobs._recipe(recipe)
            if not Path(registered["cwd"]).resolve().is_relative_to(wd):
                raise Fault(
                    403, "Recipe working directory is outside the authorized workspace"
                )
            recipe_hashes[recipe] = recipe_digest(registered)
        roles.append(
            {
                **w,
                "role": role,
                "autonomy": autonomy,
                "recipes": allowed,
                "recipe_hashes": recipe_hashes,
                "verify_recipe": verify,
                "specialization": text(
                    chosen.get("specialization", role), "specialization", 512
                ),
            }
        )
    mentor = participant(ctx, raw.get("mentor"))
    strategy = raw.get("strategy", "resident")
    if strategy not in STRATEGIES:
        raise Fault(400, "Unknown residency strategy")
    partial = raw.get("partial_profile")
    if strategy == "partial" and (
        not partial or partial not in {p["id"] for p in ctx.profiles(mentor["model"])}
    ):
        raise Fault(
            422,
            "Partial residency needs an explicitly configured and measurable capped mentor profile",
        )
    if strategy == "partial":
        caps = next(p for p in ctx.profiles(mentor["model"]) if p["id"] == partial).get(
            "settings", {}
        )
        if (
            not mentor["model"].startswith("local-")
            or caps.get("gpu") != "Tesla V100"
            or type(caps.get("vram_ceiling_mib")) is not int
            or not 0 < caps["vram_ceiling_mib"] <= 4096
            or type(caps.get("gpu_layers")) is not int
            or caps["gpu_layers"] < 1
        ):
            raise Fault(
                422,
                "Choose an imported V100 GGUF mentor with explicit GPU layer count and a VRAM ceiling up to 4096 MiB",
            )
    same = {}
    for p in [mentor, *roles]:
        prior = same.setdefault(p["model"], (p["gpu"], p["profile"]))
        if prior != (p["gpu"], p["profile"]):
            raise Fault(
                409,
                "One shared loaded model cannot use conflicting simultaneous profiles",
            )
    if strategy != "resident" and mentor["model"] in {w["model"] for w in roles}:
        raise Fault(
            409, "Alternating or partial residency needs a distinct worker model"
        )
    values = raw.get("limits", {})
    if not isinstance(values, dict):
        raise Fault(400, "Limits must be an object")
    schema = {
        "max_tasks": (32, 1, 128),
        "max_seconds": (3600, 1, 86400),
        "max_output_tokens": (2048, 64, 8192),
        "max_check_ins": (12, 0, 64),
        "min_free_ram_mib": (1536, 1536, 1048576),
        "min_free_disk_gib": (60, 60, 65536),
        "max_gpu_used_mib": (0, 0, 1048576),
    }
    if set(values) - set(schema):
        raise Fault(400, "Unsupported resource or stop limit")
    limits = {
        k: integer(values.get(k, default), k, lo, hi)
        for k, (default, lo, hi) in schema.items()
    }
    return {
        "objective": text(raw.get("objective"), "objective"),
        "completion_criteria": text(
            raw.get("completion_criteria"), "completion criteria", 4096
        ),
        "working_directory": str(wd),
        "references": unique_strings(raw.get("references", []), "reference"),
        "mentor": mentor,
        "workers": roles,
        "strategy": strategy,
        "partial_profile": partial,
        "limits": limits,
        "max_workers": integer(
            raw.get("max_workers", min(4, len(roles))), "max_workers", 1, 12
        ),
        "check_in": check_in(raw.get("check_in", {})),
    }


def response_json(result):
    choices = result.get("choices") or []
    if not choices or choices[0].get("finish_reason") != "stop":
        raise Fault(422, "Model output did not finish normally")
    content = choices[0].get("message", {}).get("content")
    if not isinstance(content, str) or len(content) > 262144:
        raise Fault(422, "Missing or oversized structured model output")
    value = content.strip()
    match = re.fullmatch(
        r"```(?:json)?\s*\n(.*?)\n```", value, re.DOTALL | re.IGNORECASE
    )
    if match:
        value = match[1]
    try:
        obj = json.loads(value)
    except ValueError as e:
        raise Fault(422, "Model output must contain one JSON object") from e
    if not isinstance(obj, dict):
        raise Fault(422, "Model output must contain one JSON object")
    return obj


def validate_plan(config, existing, proposed):
    if not isinstance(proposed, list):
        raise Fault(422, "Mentor must return a task list")
    roles = {w["role"]: w for w in config["workers"]}
    combined = {k: {**v} for k, v in existing.items()}
    updated = set()
    replacements = {}
    for row in proposed:
        if not isinstance(row, dict) or set(row) - {
            "id",
            "role",
            "instruction",
            "acceptance",
            "depends_on",
            "milestone",
            "recipes",
            "supersedes",
        }:
            raise Fault(422, "Unsupported mentor task fields")
        tid = identifier(row.get("id"), "task id")
        if tid in updated or (tid in existing and existing[tid]["state"] != "pending"):
            raise Fault(
                422, "Only unstarted tasks may be revised and each needs a unique id"
            )
        role = row.get("role")
        if role not in roles:
            raise Fault(422, "Unknown worker role in mentor plan")
        deps = unique_strings(row.get("depends_on", []), "dependency", 128)
        allowed = unique_strings(row.get("recipes", []), "recipe", 16)
        if set(allowed) - set(roles[role]["recipes"]):
            raise Fault(
                403, "Mentor requested a recipe outside the role permission set"
            )
        milestone = row.get("milestone", False)
        if not isinstance(milestone, bool):
            raise Fault(422, "Milestone must be boolean")
        combined[tid] = {
            "id": tid,
            "role": role,
            "instruction": text(row.get("instruction"), "task instruction"),
            "acceptance": text(
                row.get("acceptance"), "task acceptance criterion", 4096
            ),
            "depends_on": deps,
            "recipes": allowed,
            "milestone": milestone,
            "state": "pending",
            "verified": False,
        }
        old = row.get("supersedes")
        if old is not None:
            previous = existing.get(old)
            if (
                old == tid
                or tid in existing
                or old in replacements
                or not previous
                or previous["state"] not in ("failed", "escalated", "conflict")
                or previous.get("stop_latched")
                or previous.get("requested_recipes")
                or previous.get("verification")
            ):
                raise Fault(
                    409,
                    "Only a settled unsuccessful generation without external actions may be superseded",
                )
            replacements[old] = tid
            combined[tid]["supersedes"] = old
        updated.add(tid)
    if not 1 <= len(combined) <= config["limits"]["max_tasks"]:
        raise Fault(422, "Mentor plan exceeds the authorized task count or is empty")
    for old, new in replacements.items():
        combined[old].update(state="superseded", superseded_by=new)
        for row in combined.values():
            if row["state"] == "pending" and old in row["depends_on"]:
                row["depends_on"] = [new if k == old else k for k in row["depends_on"]]
    seen = set()

    def visit(tid, path):
        if tid in seen:
            return
        if tid not in combined or tid in path:
            raise Fault(422, "Mentor dependencies contain an unknown task or cycle")
        for dep in combined[tid]["depends_on"]:
            visit(dep, path | {tid})
        seen.add(tid)

    for tid in combined:
        visit(tid, set())
    return combined
