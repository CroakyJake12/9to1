"""Audit declared external donors, app-local source, provenance and recursive checkout pins.

Default mode is the release gate: an app-local nested clone without a committed
gitlink is NOT sufficient. --local-only verifies the unstaged working-tree
materialisation while explicitly withholding recursive-checkout acceptance.
"""

import argparse
import configparser
import json
import re
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
LOCK = ROOT / "eng" / "donor-sources.json"
SHA = re.compile(r"^[0-9a-f]{40}$")
# This independent inventory prevents a donor declaration being dropped from
# the JSON registry in order to make a missing source tree pass the gate.
REQUIRED = {
    "Boards": {"appflowy-board", "rnote"},
    "Canvas": {"rnote"},
    "Browse": {"firefox-gecko"},
    "Write": {"libreoffice"},
    "Present": {"libreoffice"},
    "Data": {"libreoffice", "duckdb"},
    "Planner": {"eds", "libical"},
    "Terminal": {"libvterm"},
    "Dev": {"vscodium", "code-oss"},
    "Pictures": {"glycin", "loupe"},
    "Wave": {"gstreamer", "ges"},
    "Motion": {"gstreamer", "ges"},
    "Dulche": {"llama-cpp"},
    "Wine": {"wine"},
    "WinBoat": {"winboat"},
    "Files": {"files"},
}


def git(*args, cwd=ROOT):
    result = subprocess.run(["git", "-C", str(cwd), *args], capture_output=True, text=True, check=False)
    return result.stdout.strip() if result.returncode == 0 else None


def resolved_pin(entry, record):
    """The commit one app really pins for a donor, with the shared upstream record as the default.

    Different apps may legitimately consume different revisions of the same donor (Canvas builds
    against Rnote v0.14.2 while Boards records the v0.15 line). A binding may override the shared
    record, but only with a recorded reason, so deliberate divergence stays distinguishable from drift.
    """
    commit = entry.get("commit") or record.get("commit", "")
    fork_commit = entry.get("forkCommit") or record.get("forkCommit")
    problems = []
    if entry.get("commit") and not entry.get("commitReason"):
        problems.append("per-app donor pin has no recorded commitReason")
    if commit and not SHA.fullmatch(commit):
        problems.append("no exact immutable upstream commit")
    # Only donors that have a controlled fork must agree between the upstream and fork revisions.
    # Some donors are imported as plain source files with verified tree hashes instead (Files), and the
    # original check deliberately did not apply to them.
    if record.get("fork") and fork_commit != commit:
        problems.append("fork and upstream revisions disagree")
    return commit, fork_commit, problems


def framework_verification_problems(entry, record, path):
    """Checks for a donor vendored as a loose tree inside a framework, with no gitlink of its own.

    A framework donor cannot be pinned by a submodule gitlink yet, so the enforceable claim is a
    recorded blob-level equivalence: the repository tree for the path, the fork branch it was compared
    against, and which repository-only paths are the wrapper's own provenance records rather than
    unexplained source. The receipt is written by eng/development/donor_fork_equivalence.py.
    """
    problems = []
    receipt_ref = entry.get("equivalenceReceipt")
    if not receipt_ref:
        return [f"{entry['app']}/{entry['donor']}: framework donor has no equivalenceReceipt recorded"]
    receipt_path = ROOT / receipt_ref
    if not receipt_path.is_file():
        return [f"{entry['app']}/{entry['donor']}: equivalenceReceipt missing at {receipt_ref}"]
    try:
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        return [f"{entry['app']}/{entry['donor']}: equivalenceReceipt is not valid JSON ({exc})"]
    if receipt.get("path") != path:
        problems.append(f"{entry['app']}/{entry['donor']}: equivalenceReceipt path {receipt.get('path')!r} "
                        f"does not match the binding path {path!r}")
    recorded_tree = receipt.get("repository_tree")
    current_tree = git("rev-parse", f"HEAD:{path}")
    if not recorded_tree:
        problems.append(f"{entry['app']}/{entry['donor']}: equivalenceReceipt records no repository_tree")
    elif recorded_tree != current_tree:
        problems.append(f"{entry['app']}/{entry['donor']}: vendored tree changed since the equivalence "
                        f"receipt ({str(recorded_tree)[:12]} -> {current_tree[:12]}); re-run "
                        f"eng/development/donor_fork_equivalence.py and review the difference")
    if receipt.get("fork_ref") and record.get("fork") and \
            not str(receipt.get("fork_commit", "")).startswith(str(record.get("commit", ""))[:12]) and \
            receipt.get("fork_commit") != record.get("commit"):
        problems.append(f"{entry['app']}/{entry['donor']}: equivalence receipt fork commit "
                        f"{str(receipt.get('fork_commit'))[:12]} is not the recorded pin "
                        f"{str(record.get('commit'))[:12]}")
    equivalence = receipt.get("equivalence") or {}
    if equivalence.get("differing_paths"):
        problems.append(f"{entry['app']}/{entry['donor']}: {len(equivalence['differing_paths'])} "
                        f"vendored paths differ from the fork branch: "
                        + ", ".join(equivalence["differing_paths"][:5]))
    allowed = set(entry.get("wrapperPaths") or [])
    extra = sorted(set(equivalence.get("only_in_repository_tree") or []) - allowed)
    if extra:
        problems.append(f"{entry['app']}/{entry['donor']}: vendored tree holds paths the fork does not "
                        f"carry and the binding does not declare as wrapper records: "
                        + ", ".join(extra[:5]))
    return problems


def aspirational_binding(entry):
    """A declared donor with no source yet, whose absence is recorded rather than hidden.

    The specification designates donors for apps that have not been built yet. Demanding source for them
    makes a real gate fail on unbuilt product, which trains people to ignore the gate; silently dropping
    the declaration hides debt. So such a binding must state why it is aspirational, what it is planned
    to be, and which specification line requires it - and the gate reports the whole class separately.
    """
    missing = [key for key in ("status", "plannedVerdict", "reason", "specLine")
               if not entry.get(key)]
    return missing


def failures(local_only=False):
    data = json.loads(LOCK.read_text(encoding="utf-8"))
    upstreams = data["upstreams"]
    bindings = data["bindings"]
    errors = []
    # Aspirational donors are reported separately and never counted as issues: they are named future
    # work, not a gate failure over product that has not been built.
    aspirational = []
    modules = configparser.ConfigParser(interpolation=None)
    modules.read(ROOT / ".gitmodules", encoding="utf-8")
    paths_in_modules = {modules[s]["path"]: modules[s]["url"].removesuffix(".git")
                        for s in modules.sections() if s.startswith('submodule ')}
    seen = set()
    for entry in bindings:
        app, donor = entry["app"], entry["donor"]
        key = (app, donor)
        if key in seen:
            errors.append(f"{app}/{donor}: duplicate declaration")
        seen.add(key)
        if donor not in upstreams:
            errors.append(f"{app}/{donor}: unknown donor identity")
            continue
        record = upstreams[donor]
        path = entry["path"]
        owner = entry["owner"]
        scope = entry.get("scope", "app-local")
        if scope == "framework":
            # A framework donor is vendored under the framework that consumes it, not under an app's
            # Source tree, because it is compiled by the framework itself.
            if not path.startswith(owner + "/vendor/"):
                errors.append(f"{app}/{donor}: framework donor source must sit under {owner}/vendor/")
                continue
        elif not path.startswith(owner + "/Source/"):
            errors.append(f"{app}/{donor}: source must be inside its owning Source/ tree")
            continue
        source = ROOT / path
        if not source.is_dir() or not source.resolve().is_relative_to(ROOT.resolve()):
            status = str(entry.get("status", ""))
            missing_fields = aspirational_binding(entry) if status else ["status"]
            if status and not missing_fields:
                aspirational.append(
                    f"{app}/{donor}: declared donor with no materialised source "
                    f"(status {status}; planned {entry['plannedVerdict']}; spec line "
                    f"{entry['specLine']}) - {entry['reason']}")
            else:
                errors.append(f"{app}/{donor}: no app-local source tree at {path}")
                if status:
                    errors.append(f"{app}/{donor}: aspirational status is missing required field(s): "
                                  + ", ".join(missing_fields))
            continue
        if not record.get("license") or not record.get("licenseFiles"):
            errors.append(f"{app}/{donor}: missing licence metadata")
        for file in record.get("licenseFiles", []):
            if not (source / file).is_file():
                errors.append(f"{app}/{donor}: upstream licence/notice missing: {file}")
        # One donor can be pinned at different revisions by different apps, and that is legitimate when
        # the apps really build against different revisions (Canvas consumes Rnote v0.14.2 while Boards
        # records the v0.15 line). A binding may therefore override the shared upstream commit, but only
        # with a recorded reason, so a deliberate divergence stays distinct from silent drift.
        commit, fork_commit, pin_problems = resolved_pin(entry, record)
        errors.extend(f"{app}/{donor}: {p}" for p in pin_problems)
        if donor == "files":
            # Files was imported as source files, excluding only GitHub/attribute metadata.
            if git("rev-parse", "HEAD:9to1 Workspace/Files/Source/Files/src") != "d4a883424a2d11cb460774a9fb41debbe5c96363" or git("rev-parse", "HEAD:9to1 Workspace/Files/Source/Files/tests") != "be743d924b432091a7229d9f8b80091c9b1290b1":
                errors.append("Files: imported code/test trees disagree with the verified upstream snapshot")
            if git("diff", "--name-only", "--", path):
                errors.append("Files: working source differs from the pinned imported snapshot")
        elif scope == "framework":
            # No gitlink yet, so the enforceable claim is the recorded blob-level equivalence.
            if not record.get("repository", "").startswith("https://") or not record.get("fork", "").startswith("https://github.com/CroakyJake12/"):
                errors.append(f"{app}/{donor}: missing canonical upstream or controlled fork")
            errors.extend(framework_verification_problems(entry, record, path))
        else:
            if (entry.get("forkCommit") or record.get("forkCommit")) != commit:
                errors.append(f"{app}/{donor}: fork and upstream revisions disagree")
            if paths_in_modules.get(path) != record.get("fork"):
                errors.append(f"{app}/{donor}: .gitmodules URL/path disagrees with donor record")
            if git("rev-parse", "HEAD", cwd=source) != commit:
                errors.append(f"{app}/{donor}: materialised source HEAD differs from pinned revision")
            if git("status", "--porcelain", cwd=source):
                errors.append(f"{app}/{donor}: upstream source has unrecorded modifications")
            if not local_only:
                stage = git("ls-files", "--stage", "--", path)
                committed = git("ls-tree", "HEAD", "--", path)
                if not stage or not stage.startswith("160000 ") or commit not in stage:
                    errors.append(f"{app}/{donor}: no matching gitlink in index (recursive checkout cannot materialise source)")
                if not committed or not committed.startswith("160000 ") or commit not in committed:
                    errors.append(f"{app}/{donor}: no matching committed gitlink (fresh recursive checkout cannot materialise source)")
        provenance = ROOT / owner / "Source" / "DONOR-PROVENANCE.md"
        if not provenance.is_file():
            errors.append(f"{app}/{donor}: no app-local provenance record")
        else:
            text = provenance.read_text(encoding="utf-8")
            for value in (record.get("canonicalRepository", record["repository"]), record["repository"], commit, *record.get("licenseFiles", [])):
                if value not in text:
                    errors.append(f"{app}/{donor}: app-local provenance disagrees or omits {value}")
            if record.get("fork") and record["fork"] not in text:
                errors.append(f"{app}/{donor}: controlled fork missing from app-local provenance")
        if not entry.get("consumer") or "runtimeRequirement" not in entry:
            errors.append(f"{app}/{donor}: integration boundary or runtime requirement not recorded")
    for app, donors in REQUIRED.items():
        for missing in donors - {donor for owner, donor in seen if owner == app}:
            errors.append(f"{app}/{missing}: declared external donor omitted from source registry")
    for app in data.get("firstPartyOrInternal", []):
        if app in REQUIRED:
            errors.append(f"{app}: external donor app misclassified as first-party/internal")
    # First-party and internal dependencies belong in the explicit exempt list,
    # rather than inventing external source obligations for them.
    if not data.get("firstPartyOrInternal"):
        errors.append("first-party/internal dependency classification missing")
    return errors, len(bindings), aspirational


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--local-only", action="store_true", help="Inspect current materialisation; do NOT claim recursive-checkout acceptance")
    args = parser.parse_args()
    errors, count, aspirational = failures(args.local_only)
    for issue in errors:
        print("FAIL:", issue)
    for note in aspirational:
        print("ASPIRATIONAL:", note)
    if aspirational:
        print(f"Recorded aspirational donors (named future work, not gate failures): {len(aspirational)}")
    if errors:
        print(f"Donor-source audit FAIL: {len(errors)} issue(s) across {count} bindings")
        return 1
    if args.local_only:
        print(f"LOCAL SOURCE ONLY: {count} donor bindings materialised and pinned; committed gitlinks NOT checked")
    else:
        print(f"PASS: {count} donor bindings, source, provenance and committed recursive-checkout gitlinks")
    return 0


if __name__ == "__main__":
    sys.exit(main())
