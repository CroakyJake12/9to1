"""Classify donor engines and applications as FORK or EMBED from observed modification, not roles.

Owner rule, 30 October 2026: what decides forking is *what is actually being modified*. A donor that
is an application whose features we extend and whose UI we replace in CUI is forked (Files, Firefox).
A donor that is an engine used unmodified inside our app is embedded, preferably as upstream's
prebuilt signed binary (Gecko, DuckDB). The same donor can split either way: our Canvas work patches
Rnote's engine crates, so there the engine is the forked artifact.

Read-only. It never clones, fetches, builds or promotes anything, and grants no acceptance.
"""

import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

REGISTRY = "eng/donor-sources.json"
NOT_RUN = ("Classification is source-inspected evidence about modification scope. It proves no "
           "compile, native run, package, install or acceptance, and authorises no clone or build.")

Verdict = ("FORK", "EMBED", "NOT CONSUMED", "UNRESOLVED")


def _git(cwd, *args):
    res = subprocess.run(["git", "-C", str(cwd), *args], capture_output=True, text=True)
    return res


def declared_submodules(repo):
    """Return {name: {path, url}} from .gitmodules, tolerating an absent file."""
    cfg = _git(repo, "config", "--file", ".gitmodules",
               "--get-regexp", r"submodule\..*\.(path|url)").stdout
    out = {}
    for line in cfg.splitlines():
        m = re.match(r"submodule\.(.*?)\.(path|url)\s+(.*)$", line.strip())
        if m:
            name, key, value = m.groups()
            out.setdefault(name, {})[key] = value
    return {name: {"path": v.get("path", ""), "url": v.get("url", "")}
            for name, v in out.items() if v.get("path")}


def head_gitlinks(repo):
    """Return {path: commit} for gitlinks recorded in HEAD."""
    out = {}
    raw = _git(repo, "ls-tree", "-r", "HEAD").stdout
    for line in raw.splitlines():
        meta, _, path = line.partition("\t")
        fields = meta.split()
        if len(fields) >= 3 and fields[0] == "160000":
            out[path] = fields[2]
    return out


def _porcelain(line):
    """Split a porcelain v1 record into (kind, path), tolerating renames."""
    if line.startswith("??"):
        return "untracked", line[3:]
    path = line[3:]
    if " -> " in path:
        path = path.split(" -> ", 1)[1]
    return "modified", path.strip('"')


def checkout_state(repo, path):
    """Measurable state of one donor working tree: present, checked-out commit, local dirt."""
    sub = Path(repo) / path
    if not sub.is_dir() or not ((sub / ".git").exists() or (sub / ".git").is_file()):
        return {"present": False, "commit": None, "modified": [], "untracked": []}
    res = _git(sub, "status", "--porcelain")
    modified, untracked = [], []
    for line in res.stdout.splitlines():
        if not line.strip():
            continue
        kind, file = _porcelain(line)
        (untracked if kind == "untracked" else modified).append(file)
    return {"present": True,
            "commit": _git(sub, "rev-parse", "HEAD").stdout.strip() or None,
            "modified": modified, "untracked": untracked}


def patch_touched_paths(patch_text):
    """Files a unified diff modifies, taken from its +++ headers."""
    got = set()
    for m in re.finditer(r"^\+\+\+\s+(\S+)", patch_text, re.M):
        path = m.group(1)
        if path.startswith(("a/", "b/")):
            path = path[2:]
        if path and path != "dev/null":
            got.add(path)
    return sorted(got)


def commit_touched_paths(repo, commit):
    """Files a commit in a donor checkout modifies."""
    raw = _git(repo, "show", "--name-only", "--format=", str(commit)).stdout
    return sorted({l.strip() for l in raw.splitlines() if l.strip()})


def classify(policy, state):
    """Decide the verdict from what is actually modified; the role label is never evidence.

    Precedence is deliberate: observed or recorded modification outranks everything else, because
    that is the owner's test. Absence of a gitlink is not evidence of non-use either.
    """
    modified = list(policy.get("modified_artifacts") or [])
    consumed = bool(policy.get("consumed_by"))
    dirty = list(state.get("modified") or [])

    scopes = [dict(entry, path=None) for entry in modified if not entry.get("paths")]
    for entry in modified:
        for p in entry.get("paths") or []:
            scopes.append({"artifact": entry.get("artifact"), "path": p, "kind": entry.get("kind")})

    unclassified = [p for p in dirty
                    if not any(str(p).startswith(s.get("path") or "") for s in scopes if s.get("path"))]

    if dirty and not scopes:
        return {"verdict": "UNRESOLVED",
                "reason": "donor working tree is modified but the policy records no modification, "
                          "so the fork scope cannot be established",
                "modified_scopes": [], "unclassified_changes": unclassified}
    if scopes or dirty:
        pristine = list(policy.get("pristine_scopes") or [])
        clash = sorted({str(s.get("path")) for s in scopes if s.get("path")
                        and any(str(s["path"]).startswith(p) for p in pristine)})
        if clash:
            return {"verdict": "UNRESOLVED",
                    "reason": "recorded modifications land inside scopes declared pristine: "
                              + ", ".join(clash),
                    "modified_scopes": scopes, "unclassified_changes": unclassified}
        return {"verdict": "FORK",
                "reason": "9to1 modifies this donor, so it is forked; the fork scope is the modified "
                          "artifact, which can be narrower than the whole project",
                "modified_scopes": scopes, "unclassified_changes": unclassified}
    if consumed:
        return {"verdict": "EMBED",
                "reason": "consumed by 9to1 with no recorded or observed modification, so it is "
                          "embedded prebuilt from a pinned, signature- and digest-verified build",
                "modified_scopes": [], "unclassified_changes": unclassified}
    return {"verdict": "NOT CONSUMED",
            "reason": "declared for provenance/intent only: no consumer and no modification",
            "modified_scopes": [], "unclassified_changes": unclassified}


def modifications_present_at_pin(repo, donor, pin):
    """Do the donor's recorded modifications actually exist at the pinned commit?

    This is the check that stops a pin and a build drifting apart: pinning an untouched upstream
    commit while the build patches a fetched copy is the "depends on whatever copy the user happens
    to have" failure mode, so it is reported rather than hidden.
    """
    found = []
    sub = Path(repo) / donor["path"]
    patch_root = Path(repo) / donor.get("patch_root", donor["path"])
    for entry in donor.get("modified_artifacts") or []:
        artifact, kind = entry.get("artifact"), entry.get("kind")
        if kind == "commit" and entry.get("commit") and pin and sub.is_dir():
            if _git(sub, "merge-base", "--is-ancestor", str(entry["commit"]), str(pin)).returncode:
                found.append(f"{artifact}: modification commit {str(entry['commit'])[:12]} is not an "
                             f"ancestor of pin {str(pin)[:12]}")
        if kind == "patch":
            for name in entry.get("patch_files") or []:
                pf = patch_root / name
                if not pf.exists():
                    found.append(f"{artifact}: patch file {name} is missing")
                elif sub.is_dir() and _git(sub, "apply", "--check", "--reverse",
                                           str(pf)).returncode:
                    found.append(f"{artifact}: patch {name} is not applied at pin {str(pin)[:12]} "
                                 f"(it exists only as a downstream build-time patch)")
    return found


def consumed_refs(repo, donor):
    """The revision the build actually fetches, read out of the project's own lock or manifest.

    A gitlink pin only says what the checkout records. What the *build* consumes can be a different
    revision fetched from a different remote, which is how a donor ends up depending on whatever copy
    the build environment happens to have. Reading the lock closes that gap.
    """
    out = []
    for spec in donor.get("consumed_refs") or []:
        f = Path(repo) / spec["file"]
        if not f.is_file():
            out.append({"file": spec["file"], "revisions": [], "error": "declared lock/manifest file missing"})
            continue
        text = f.read_text(errors="replace")
        revs = sorted({m.group("rev") for m in re.finditer(spec["pattern"], text, re.M)
                       if m.groupdict().get("rev")})
        out.append({"file": spec["file"], "revisions": revs, "error": None})
    return out


def census(repo, registry_path=REGISTRY):
    """Read-only census of declared donors against HEAD gitlinks, working trees and policy."""
    repo = Path(repo)
    registry = json.loads((repo / registry_path).read_text())
    block = registry.get("fork_policy_20261030")
    if not block:
        raise RuntimeError(f"{registry_path} has no fork_policy_20261030 block")

    declared_paths = {d["path"] for d in declared_submodules(repo).values()}
    gitlinks = head_gitlinks(repo)
    rows, violations, findings = [], [], []

    for donor in block["donors"]:
        name, path = donor["donor"], donor["path"]
        state = checkout_state(repo, path)
        got = classify(donor, state)
        verdict = donor.get("verdict")
        if verdict not in Verdict:
            violations.append(f"{name}: policy verdict {verdict!r} is not one of {Verdict}")
        if got["verdict"] != verdict:
            violations.append(f"{name}: evidence says {got['verdict']} but policy says {verdict} "
                              f"({got['reason']})")
        violations += [f"{name}: {m}" for m in got["unclassified_changes"]]
        pin = gitlinks.get(path) or state["commit"]
        if donor.get("pinned_at") and pin and not pin.startswith(donor["pinned_at"]):
            violations.append(f"{name}: pin {pin[:12]} does not match the policy pin "
                              f"{str(donor['pinned_at'])[:12]}")
        fork_tip = donor.get("fork_tip")
        if fork_tip and pin and not str(pin).startswith(str(fork_tip)[:12]):
            message = (f"{name}: our fork branch {donor.get('fork_branch', '(unnamed)')} carries the "
                       f"modification at {str(fork_tip)[:12]}, but the pinned revision is "
                       f"{str(pin)[:12]} - consumption has not moved onto the fork")
            (findings if donor.get("fork_not_consumed_yet") else violations).append(message)
        findings += [f"{name}: {m}" for m in modifications_present_at_pin(repo, donor, pin)]
        refs = consumed_refs(repo, donor)
        acknowledged = bool(donor.get("modification_absent_from_consumed_ref"))
        for ref in refs:
            if ref["error"]:
                findings.append(f"{name}: {ref['error']} ({ref['file']})")
            elif ref["revisions"] and not any(
                    str(pin).startswith(r) or r.startswith(str(pin)[:12])
                    for r in ref["revisions"]):
                message = (f"{name}: the build fetches "
                           f"{', '.join(ref['revisions'])[:48]} from {ref['file']} but the recorded "
                           f"pin is {str(pin)[:12]} - pin and build input are different revisions")
                (findings if acknowledged else violations).append(message)
        if not donor.get("declared_submodule", True) and path not in declared_paths:
            findings.append(f"{name}: vendored as a loose tree, not a pinned submodule - provenance "
                            "gap (no upstream base commit or commit-level modification history)")
        rows.append({
            "donor": name, "path": path, "role": donor.get("role"),
            "declared_submodule": path in declared_paths,
            "gitlink_in_head": gitlinks.get(path, ""), "checked_out": state["present"],
            "consumed_by": donor.get("consumed_by") or [],
            "verdict": verdict, "evidence_verdict": got["verdict"], "reason": got["reason"],
            "fork_scope": sorted({str(s.get("path") or s.get("artifact"))
                                  for s in got["modified_scopes"]}),
            "pristine_scopes": donor.get("pristine_scopes") or [],
            "planned_verdict": donor.get("planned_verdict"),
            "consumed_refs": [{"file": r["file"], "revisions": r["revisions"], "error": r["error"]}
                              for r in refs],
        })

    orphans = sorted(declared_paths - {d["path"] for d in block["donors"]})
    if orphans:
        findings.append("declared submodules with no policy entry (not yet classified): "
                        + ", ".join(orphans))
    return {"generated_from": str(repo), "donors": rows, "violations": violations,
            "findings": findings, "note": NOT_RUN}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("verb", choices=["census"])
    ap.add_argument("--repo", default=".")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--out", help="fresh private output; never a reviewer evidence directory")
    args = ap.parse_args(argv)

    out = Path(args.out) if args.out else None
    if out is not None and out.exists():
        # Refuse before doing any work: a census must never damage an existing report.
        print(f"REFUSED: {out} already exists; census never overwrites", file=sys.stderr)
        return 2

    result = census(args.repo)
    if args.json:
        text = json.dumps(result, indent=2, ensure_ascii=False)
    else:
        text = "\n".join(f"{r['verdict']:<13}{r['planned_verdict'] or '':<14}"
                         f"{', '.join(r['fork_scope'])[:52]:<53}{r['donor']}"
                         for r in sorted(result["donors"], key=lambda x: (x["verdict"], x["donor"])))
    if out is not None:
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text + "\n")
    print(text)
    for label, items in (("FINDINGS", result["findings"]), ("VIOLATIONS", result["violations"])):
        if items:
            print(f"\n{label}:", file=sys.stderr if label == "VIOLATIONS" else sys.stdout)
            for v in items:
                print(f"  - {v}", file=sys.stderr if label == "VIOLATIONS" else sys.stdout)
    if result["violations"]:
        return 1
    print("\nno violations; " + NOT_RUN)
    return 0


if __name__ == "__main__":
    sys.exit(main())
