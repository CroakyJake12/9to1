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


def failures(local_only=False):
    data = json.loads(LOCK.read_text(encoding="utf-8"))
    upstreams = data["upstreams"]
    bindings = data["bindings"]
    errors = []
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
        if not path.startswith(owner + "/Source/"):
            errors.append(f"{app}/{donor}: source must be inside its owning Source/ tree")
            continue
        source = ROOT / path
        if not source.is_dir() or not source.resolve().is_relative_to(ROOT.resolve()):
            errors.append(f"{app}/{donor}: no app-local source tree at {path}")
            continue
        if not record.get("license") or not record.get("licenseFiles"):
            errors.append(f"{app}/{donor}: missing licence metadata")
        for file in record.get("licenseFiles", []):
            if not (source / file).is_file():
                errors.append(f"{app}/{donor}: upstream licence/notice missing: {file}")
        commit = record.get("commit", "")
        if not SHA.fullmatch(commit):
            errors.append(f"{app}/{donor}: no exact immutable upstream commit")
        if donor == "files":
            # Files was imported as source files, excluding only GitHub/attribute metadata.
            if git("rev-parse", "HEAD:9to1 Workspace/Files/Source/Files/src") != "d4a883424a2d11cb460774a9fb41debbe5c96363" or git("rev-parse", "HEAD:9to1 Workspace/Files/Source/Files/tests") != "be743d924b432091a7229d9f8b80091c9b1290b1":
                errors.append("Files: imported code/test trees disagree with the verified upstream snapshot")
            if git("diff", "--name-only", "--", path):
                errors.append("Files: working source differs from the pinned imported snapshot")
        else:
            if not record.get("repository", "").startswith("https://") or not record.get("fork", "").startswith("https://github.com/CroakyJake12/"):
                errors.append(f"{app}/{donor}: missing canonical upstream or controlled fork")
            if record.get("forkCommit") != commit:
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
    return errors, len(bindings)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--local-only", action="store_true", help="Inspect current materialisation; do NOT claim recursive-checkout acceptance")
    args = parser.parse_args()
    errors, count = failures(args.local_only)
    for issue in errors:
        print("FAIL:", issue)
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
