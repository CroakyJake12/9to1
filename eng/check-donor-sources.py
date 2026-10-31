"""Audit declared external donors, app-local source, provenance and recursive checkout pins.

Default mode is the release gate: an app-local nested clone without a committed
gitlink is NOT sufficient. --local-only verifies the unstaged working-tree
materialisation while explicitly withholding recursive-checkout acceptance.
"""

import argparse
import configparser
import hashlib
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


def snapshot_verification_problems(entry, record, path, source):
    """Checks for a donor deliberately materialised as an extracted snapshot, not a submodule.

    Some donors are needed only as a UI reference for the Stage A mirrors. Vendoring them as a loose
    snapshot at an exact upstream revision is honest as long as three things are recorded and re-checked:
    the revision the snapshot came from, a content digest that detects silent edits, and the licence files
    read from that tree. It is never presented as a recursive checkout: the snapshot is local, and the
    fork/submodule conversion is a separate unit.
    """
    problems = []
    # Each app has its own donor tree, so each tree has its own digest. A block recorded on the shared
    # upstream entry cannot describe three different trees: the first binding would overwrite the second.
    snap = entry.get("snapshot") or {}
    if not snap and record.get("snapshot"):
        problems.append("snapshot pin is recorded on the shared upstream entry rather than on this app's "
                        "binding, so it cannot be true for every consumer of that donor")
        snap = record["snapshot"]
    for field in ("ref", "commit", "fetched"):
        if not snap.get(field):
            problems.append(f"snapshot record is missing {field}")
    if not snap.get("reason"):
        problems.append("snapshot record has no reason for not being a pinned submodule")
    digest = snapshot_digest(source)
    if digest is None:
        problems.append("snapshot source cannot be read")
        return problems
    if snap.get("contentDigest") and snap["contentDigest"] != digest:
        problems.append(f"snapshot content digest disagrees with the tree on disk "
                        f"({str(snap['contentDigest'])[:12]} recorded, {digest[:12]} present) - "
                        "a donor snapshot must not be edited in place")
    if snap.get("fileCount") is not None:
        counted = sum(1 for _ in Path(source).rglob("*") if _.is_file())
        if counted != int(snap["fileCount"]):
            problems.append(f"snapshot file count disagrees: recorded {snap['fileCount']}, present {counted}")
    return problems


def snapshot_digest(source):
    """sha256 over every file path and content digest in the snapshot, order-stable."""
    try:
        entries = []
        for path in sorted(Path(source).rglob("*")):
            if not path.is_file():
                continue
            try:
                digest = hashlib.sha256(path.read_bytes()).hexdigest()
            except OSError:
                return None
            entries.append(f"{path.relative_to(source)}\t{digest}")
        return hashlib.sha256("\n".join(entries).encode("utf-8")).hexdigest()
    except OSError:
        return None


def dependency_lock_problems(entry, record, app, donor):
    """Checks for a donor consumed as a pinned build from a lock file rather than as vendored source.

    llama.cpp is the real case: the inference host FetchContents an exact upstream commit and the lock
    records its tree. That is honest only while the lock, the registry and the licence say the same thing,
    so those are what get checked - an absent lock or a disagreeing revision is a failure, not a waiver.
    """
    problems = []
    lock_rel = entry.get("binaryLock")
    if not lock_rel:
        return [f"{app}/{donor}: pinned-binary verification claims a lock file but none is recorded"]
    lock_path = ROOT / lock_rel
    if not lock_path.is_file():
        return [f"{app}/{donor}: recorded dependency lock does not exist at {lock_rel}"]
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        return [f"{app}/{donor}: dependency lock cannot be read: {exc}"]
    commit, _unused, pin_problems = resolved_pin(entry, record)
    problems.extend(f"{app}/{donor}: {x}" for x in pin_problems)
    if lock.get("pinnedCommit") != commit:
        problems.append(f"{app}/{donor}: dependency lock pins {lock.get('pinnedCommit')} but the donor "
                        f"registry pins {commit} - one of them is wrong, and the build follows the lock")
    if not lock.get("license"):
        problems.append(f"{app}/{donor}: dependency lock records no licence")
    elif record.get("license") and lock.get("license") and lock["license"] not in record["license"]:
        problems.append(f"{app}/{donor}: licence disagrees between dependency lock ({lock['license']}) and "
                        f"donor registry ({record['license']})")
    if not lock.get("tree"):
        problems.append(f"{app}/{donor}: dependency lock records no upstream tree digest")
    if not entry.get("consumer") or "runtimeRequirement" not in entry:
        problems.append(f"{app}/{donor}: integration boundary or runtime requirement not recorded")
    return problems


def snapshot_gitlink_problems(stage_line, recorded_commit):
    """Cross-checks a committed gitlink against a recorded snapshot pin.

    A snapshot donor is deliberately not a submodule today, which is honest only while it is described
    that way. If someone later converts the tree into a submodule without updating the pin, gitlink and
    recorded pin would silently disagree, so a gitlink that exists has to match the recorded commit.
    """
    if not stage_line:
        return []            # no gitlink: the caller already reports that recursive checkout cannot work
    if not stage_line.startswith("160000 "):
        return ["path carries loose committed files rather than a gitlink, so the snapshot record and "
                "the committed tree disagree"]
    # `git ls-files --stage` prints: mode SP sha SP stage TAB path
    parts = stage_line.replace("\t", " ").split()
    linked = parts[1] if len(parts) > 1 else ""
    if linked != recorded_commit:
        return [f"committed gitlink {linked[:12]} disagrees with the recorded snapshot pin "
                f"{str(recorded_commit)[:12]}: a converted submodule must update its pin, not keep it"]
    return []


def vendored_source_lock_problems(entry, record, path, source):
    """Checks a donor engine vendored as a pinned SOURCE LOCK rather than an app-local checkout.

    Strata is the real case: the maintained fork lives with the model/engine it drives, and its upstream
    identity is bound by a manifest (origin, commit, commit tree, archive digest, and one row per upstream
    blob with its sha256). That is a stronger claim than a loose tree, so it gets checked as such: the pin
    must agree everywhere, the manifest must be internally consistent, and the licence must be present.

    It does NOT claim that every upstream blob is vendored in this repository. The lock binds them; the
    repository carries the fork's own bridge files. The gate says which of the two it verified.
    """
    problems = []
    if not path.startswith("9to1 Models/"):
        problems.append("a source-lock engine must sit in the model/engine tree it drives (9to1 Models/...), "
                        "not an app Source tree, and that location must be recorded deliberately")
    evidence = record.get("provenanceEvidence") or {}
    doc_rel = evidence.get("document")
    if not doc_rel:
        problems.append("no provenance document recorded for the source-lock engine")
        return problems
    doc = ROOT / doc_rel
    if not doc.is_file():
        problems.append(f"provenance document absent: {doc_rel}")
        return problems
    text = doc.read_text(encoding="utf-8", errors="ignore")
    commit = record.get("commit", "")
    if commit and commit not in text:
        problems.append(f"provenance document does not name the recorded commit {commit[:12]}")
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        problems.append("source-lock engine has no exact 40-character upstream commit")

    lock_rel = evidence.get("sourceLock")
    lock_path = (source / lock_rel) if lock_rel else None
    if not lock_rel or lock_path is None or not lock_path.is_file():
        problems.append(f"source lock file absent: {lock_rel or '(none recorded)'}")
        return problems
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        problems.append(f"source lock cannot be read: {exc}")
        return problems
    for field, expected in (("commit", commit), ("origin", record.get("repository"))):
        value = lock.get(field)
        if expected and value != expected:
            problems.append(f"source lock {field} '{str(value)[:16]}' disagrees with the donor registry "
                            f"'{str(expected)[:16]}'")
    rows = lock.get("rows") or []
    if lock.get("blobCount") != len(rows):
        problems.append(f"source lock declares {lock.get('blobCount')} blobs but contains {len(rows)} rows")
    if evidence.get("sourceLockBlobs") not in (None, lock.get("blobCount")):
        problems.append(f"registry records {evidence.get('sourceLockBlobs')} locked blobs, the lock itself "
                        f"declares {lock.get('blobCount')}")
    if lock.get("sourceBytes") != sum(int(row.get("bytes", 0)) for row in rows):
        problems.append("source lock byte total does not equal the sum of its rows")
    bad = [row.get("path") for row in rows
           if not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", "")))
           or not re.fullmatch(r"[0-9a-f]{40}", str(row.get("gitBlobSha", "")))]
    if bad:
        problems.append(f"{len(bad)} lock row(s) lack a sha256 or git blob id, first: {bad[0]}")
    for name in record.get("licenseFiles", []):
        if not (source / name).is_file():
            problems.append(f"licence notice absent from the engine tree: {name}")
    return problems


def failures(local_only=False):
    data = json.loads(LOCK.read_text(encoding="utf-8"))
    upstreams = data["upstreams"]
    bindings = data["bindings"]
    errors = []
    # Aspirational donors are reported separately and never counted as issues: they are named future
    # work, not a gate failure over product that has not been built.
    aspirational = []
    # Donors consumed as a pinned build from a lock file (llama.cpp): legitimate without vendored
    # source, but only while the lock and the registry name the same revision, so they are reported
    # here and cross-checked rather than waved through.
    pinned_binary = []
    # Snapshots are recorded as deliberately-not-submodules, so the gate must never claim that a fresh
    # recursive checkout can materialise them - the old summary line did exactly that.
    unconverted_snapshots = []
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
        elif scope == "vendored-source-locked":
            # A maintained engine fork lives with the model directory it drives, and its pin is enforced
            # through a source lock instead of an app-local checkout. The dedicated check below validates
            # that layout, including where the source is allowed to sit.
            pass
        elif not path.startswith(owner + "/Source/"):
            errors.append(f"{app}/{donor}: source must be inside its owning Source/ tree")
            continue
        source = ROOT / path
        if entry.get("verification") == "dependency-lock-pinned-binary":
            problems = dependency_lock_problems(entry, record, app, donor)
            if problems:
                errors.extend(problems)
            else:
                pinned_binary.append(
                    f"{app}/{donor}: consumed as a pinned build from "
                    f"{entry.get('binaryLock')} at {record.get('commit', '')[:12]} "
                    f"({record.get('license')}). No vendored source, no source build here; the runtime "
                    "binary identity still has to match this commit, which this gate cannot observe.")
            continue
        if not source.is_dir() or not source.resolve().is_relative_to(ROOT.resolve()):
            status = str(entry.get("status", ""))
            missing_fields = aspirational_binding(entry) if status else ["status"]
            if status and not missing_fields:
                aspirational.append(
                    f"{app}/{donor}: declared donor with no materialised source "
                    f"(status {status}; planned {entry['plannedVerdict']}; requirement reference "
                    f"{entry['specLine']} [{entry.get('requirementKind', 'specification line')}]) - "
                    f"{entry['reason']}")
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
        elif scope == "vendored-source-locked":
            errors.extend(f"{app}/{donor}: {x}" for x in
                          vendored_source_lock_problems(entry, record, path, source))
            notes_ok = True
        elif scope == "snapshot":
            # An extracted donor snapshot at an exact revision: no gitlink, so what is enforceable is
            # the recorded revision, a content digest that catches silent in-place edits, and the licence
            # files read from that tree. Recursive-checkout acceptance is explicitly withheld.
            errors.extend(snapshot_verification_problems(entry, record, path, source))
            stage = git("ls-files", "--stage", "--", path)
            errors.extend(f"{app}/{donor}: {x}" for x in
                          snapshot_gitlink_problems(stage, (entry.get("snapshot") or {}).get("commit")))
            if not stage:
                unconverted_snapshots.append(f"{app}/{donor} ({path})")
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
    consumers: dict[str, list] = {}
    for entry in bindings:
        if (ROOT / entry.get("path", "")).is_dir():
            consumers.setdefault(entry.get("donor", ""), []).append(entry.get("app", ""))
    for record_name, record in data.get("upstreams", {}).items():
        if record.get("snapshot") and len(consumers.get(record_name, [])) > 1:
            errors.append(f"{record_name}: one shared snapshot block serves "
                          f"{len(consumers[record_name])} app bindings "
                          f"({', '.join(sorted(consumers[record_name]))}); record the pin per binding")
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
    return errors, len(bindings), aspirational, pinned_binary, unconverted_snapshots


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--local-only", action="store_true", help="Inspect current materialisation; do NOT claim recursive-checkout acceptance")
    args = parser.parse_args()
    errors, count, aspirational, pinned_binary, unconverted = failures(args.local_only)
    for issue in errors:
        print("FAIL:", issue)
    for note in pinned_binary:
        print("PINNED BINARY:", note)
    for note in aspirational:
        print("ASPIRATIONAL:", note)
    if pinned_binary:
        print(f"Donors consumed as pinned builds from lock files (no vendored source): "
              f"{len(pinned_binary)}")
    if aspirational:
        print(f"Recorded aspirational donors (named future work, not gate failures): {len(aspirational)}")
    if errors:
        print(f"Donor-source audit FAIL: {len(errors)} issue(s) across {count} bindings")
        return 1
    if unconverted:
        print(f"SNAPSHOT DONORS WITHOUT A GITLINK ({len(unconverted)}): extracted at an exact revision "
              "and verified by recorded digest, but a fresh recursive checkout CANNOT materialise them. "
              "Converting each into a controlled-fork submodule pin is outstanding work: "
              + "; ".join(unconverted))
    if args.local_only:
        print(f"LOCAL SOURCE ONLY: {count} donor bindings materialised and pinned; committed gitlinks NOT checked")
    elif unconverted:
        print(f"PASS EXCEPT RECURSIVE CHECKOUT: {count} donor bindings carry source, provenance and "
              f"licence custody, but {len(unconverted)} are extracted snapshots with no committed "
              "gitlink, so a fresh recursive checkout would not reproduce them")
    else:
        print(f"PASS: {count} donor bindings, source, provenance and committed recursive-checkout gitlinks")
    return 0


if __name__ == "__main__":
    sys.exit(main())
