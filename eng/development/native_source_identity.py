"""Generate native16 input identity from committed Git objects, without promotion.

The API is read-only. CLI writes only an explicitly supplied fresh private --out;
Root must review/compose the result and refresh its normal source manifests.
Original minimum base, prior pins and historical acceptance remain provenance.
Current native/package/release acceptance is always NOT RUN.
"""

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

import workflow

IDENTITY = ".github/scripts/native16-arm64/native16-source-identity.json"
REGULAR = {"100644", "100755"}
NOT_RUN = ("Fresh combined build, install, onboarding, chat, browser, persistence, "
           "normal close, removal and owner Ubuntu26/GNOME ARM acceptance NOT RUN. "
           "Mechanical identity generation grants no acceptance.")
Refusal = workflow.Refusal
DIFF_OPTIONS = ["--binary", "--full-index", "--no-renames", "--no-ext-diff",
                "--no-textconv", "--no-color", "--no-relative", "--unified=3",
                "--inter-hunk-context=0", "--diff-algorithm=myers",
                "--indent-heuristic", "--src-prefix=a/", "--dst-prefix=b/",
                "--ignore-submodules=none", "--submodule=short", "-O", os.devnull,
                "--output-indicator-new=+", "--output-indicator-old=-",
                "--output-indicator-context= ", "--word-diff=none"]


def git_environment():
    # workflow.git has no environment argument. Keep its no-optional-locks
    # convention while removing repository redirects and diff environment knobs.
    environment = dict(os.environ)
    for name in list(environment):
        if name in {"GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE",
                    "GIT_PREFIX", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES",
                    "GIT_SHALLOW_FILE", "GIT_REPLACE_REF_BASE", "GIT_DIFF_OPTS",
                    "GIT_EXTERNAL_DIFF", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT",
                    "GIT_ATTR_SOURCE", "GIT_NAMESPACE"} or name.startswith(("GIT_CONFIG_KEY_", "GIT_CONFIG_VALUE_")):
            environment.pop(name)
    environment.update(GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1",
                       GIT_ATTR_NOSYSTEM="1", GIT_NO_REPLACE_OBJECTS="1",
                       GIT_NO_LAZY_FETCH="1", GIT_GRAFT_FILE=os.devnull, LC_ALL="C")
    return environment


def git(repo, *args):
    try:
        return subprocess.check_output(["git", "--no-optional-locks", "-C", str(repo), *args],
                                       stderr=subprocess.PIPE, env=git_environment())
    except subprocess.CalledProcessError as error:
        if args[:2] == ("merge-base", "--is-ancestor") and error.returncode == 1:
            raise Refusal("Original minimum source base is not an ancestor of requested commit") from error
        raise Refusal("Git read failed: " + error.stderr.decode(errors="replace")[-1000:]) from error


def commit(repo, revision):
    if not isinstance(revision, str) or re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", revision) is None:
        raise Refusal("An exact lowercase committed revision is required")
    actual = git(repo, "rev-parse", "--verify", revision + "^{commit}").decode().strip()
    if actual != revision:
        raise Refusal("Revision must identify the commit itself")
    return actual


def tree(repo, revision):
    entries = {}
    for row in git(repo, "ls-tree", "-r", "-z", "--full-tree", revision).split(b"\0"):
        if not row:
            continue
        metadata, name = row.split(b"\t", 1)
        name = name.decode("utf-8")
        workflow.safe_relative(name)
        mode, kind, oid = metadata.decode("ascii").split()
        entries[name] = (mode, kind, oid)
    return entries


def stream_digest(repo, arguments):
    """Hash output incrementally: do not materialise large blobs/product patches."""
    process = subprocess.Popen(["git", "--no-optional-locks", "-C", str(repo), *arguments],
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               env=git_environment())
    digest = hashlib.sha256()
    try:
        while chunk := process.stdout.read(1024 * 1024):
            digest.update(chunk)
        error = process.stderr.read()
        if process.wait():
            raise Refusal("Git stream failed: " + error.decode(errors="replace")[-1000:])
        return digest.hexdigest()
    finally:
        process.stdout.close()
        process.stderr.close()
        if process.poll() is None:
            process.kill()
            process.wait()


def _object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise Refusal("Duplicate identity JSON key: " + key)
        result[key] = value
    return result


def _invalid_constant(value):
    raise Refusal("Invalid identity JSON constant: " + value)


def generate(repo, revision):
    """Return deterministic metadata for this exact commit; never read dirty files."""
    revision = commit(repo, revision)
    current = tree(repo, revision)
    identity_entry = current.get(IDENTITY)
    if identity_entry is None or identity_entry[0] not in REGULAR or identity_entry[1] != "blob":
        raise Refusal("Committed prior identity must be a regular blob")
    if int(git(repo, "cat-file", "-s", identity_entry[2])) > 2 * 1024 * 1024:
        raise Refusal("Committed prior identity exceeds2MiB")
    prior = json.loads(git(repo, "cat-file", "blob", identity_entry[2]),
                       object_pairs_hook=_object, parse_constant=_invalid_constant)
    if not isinstance(prior, dict):
        raise Refusal("Prior identity must be an object")
    base = commit(repo, prior.get("source_commit"))
    original = prior.get("original_native16_provenance", {})
    if not isinstance(original, dict) or original.get("source_commit", base) != base:
        raise Refusal("Original minimum source base changed")
    git(repo, "merge-base", "--is-ancestor", base, revision)
    pins = prior.get("changed_files_sha256")
    if not isinstance(pins, dict):
        raise Refusal("Prior input pins must be a map")
    for name, digest in pins.items():
        workflow.safe_relative(name)
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            raise Refusal("Malformed prior input SHA256: " + name)
    previous = tree(repo, base)
    changed = {name for name in set(current) | set(previous)
               if current.get(name) != previous.get(name)}
    selected = set(pins) - {IDENTITY}
    deleted, links = [], {}
    for name in sorted(changed - {IDENTITY}):
        before, after = previous.get(name), current.get(name)
        if after is None:
            deleted.append(name)
        elif after[0] in REGULAR and after[1] == "blob":
            if before is not None and before[0] not in REGULAR:
                raise Refusal("Unsupported tracked type change: " + name)
            selected.add(name)
        elif after[0] != "160000" or after[1] != "commit":
            raise Refusal("Changed input is not a regular blob or gitlink: " + name)
        if (before and before[0] == "160000") or (after and after[0] == "160000"):
            if before and after and before[0] != after[0]:
                raise Refusal("Unsupported tracked type change: " + name)
            links[name] = {"before": before[2] if before else None,
                           "after": after[2] if after else None}
    hashes = {}
    for name in sorted(selected):
        entry = current.get(name)
        if entry is None or entry[0] not in REGULAR or entry[1] != "blob":
            raise Refusal("Missing/deleted/nonregular prior input pin: " + name)
        hashes[name] = stream_digest(repo, ["cat-file", "blob", entry[2]])
    # Attribute source is the committed target, never dirty .gitattributes.
    # Refuse local overrides that could defeat this committed-tree digest.
    attributes = Path(git(repo, "rev-parse", "--path-format=absolute", "--git-path", "info/attributes").decode().strip())
    if attributes.is_symlink() or attributes.exists() and attributes.stat().st_size:
        raise Refusal("Local info/attributes override prevents deterministic product diff")
    configured = subprocess.run(["git", "--no-optional-locks", "-C", str(repo),
        "config", "--name-only", "--get-regexp", r"^diff\..*\."],
        capture_output=True, env=git_environment())
    if configured.returncode not in (0, 1):
        raise Refusal("Unable to check custom diff driver configuration")
    if configured.stdout.strip():
        raise Refusal("Custom diff driver configuration prevents deterministic product diff")
    product_hash = stream_digest(repo, ["-c", "core.quotePath=true", "-c", "core.attributesFile=/dev/null",
        "-c", "core.bigFileThreshold=512m",
        "-c", "diff.mnemonicPrefix=false", "-c", "diff.suppressBlankEmpty=false",
        "-c", "diff.autoRefreshIndex=false", "--attr-source=" + revision,
        "diff", *DIFF_OPTIONS, base, revision, "--", ".", ":(exclude).github"])
    result = copy.deepcopy(prior)
    result["changed_files_sha256"] = hashes
    result["patch_sha256"] = product_hash
    result["patch_digest_scope"] = (
        "SHA256 of canonical git diff --binary --full-index --no-renames " + base +
        " INPUT_COMMIT -- . :(exclude).github; external diff/textconv disabled; "
        "committed attributes, myers/indent-heuristic, context3 and a/b prefixes. "
        "Excludes all .github metadata and native identity to avoid circular hashes.")
    acceptance = result.setdefault("acceptance", {})
    if not isinstance(acceptance, dict):
        raise Refusal("Historical acceptance must be an object")
    acceptance["current_combined_candidate"] = NOT_RUN
    contract = result.setdefault("current_candidate_input_contract", {})
    if not isinstance(contract, dict):
        raise Refusal("Prior current candidate contract must be an object")
    contract.update(base=base, source_input_count=len(hashes), changed_gitlinks=links,
                    changed_deleted_unpinned_paths=deleted, native_self_hash_exclusion=IDENTITY,
                    regeneration_is_source_review_or_acceptance=False,
                    generation={"schema": "9to1.native-source-identity-generation/v1",
                                "input_commit": revision, "prior_identity_blob": identity_entry[2],
                                "git_version": git(repo, "--version").decode().strip(),
                                "product_diff_excludes": [".github"],
                                "product_diff_options": DIFF_OPTIONS})
    return result


def write_new(repo, destination, value):
    """Write only a new private file; no directory creation or repo mutation."""
    destination = Path(destination)
    if not destination.is_absolute() or str(destination).startswith("//") or ".." in destination.parts:
        raise Refusal("--out must be an absolute new private path")
    protected = {Path(repo).resolve(), Path(git(repo, "rev-parse", "--path-format=absolute", "--git-common-dir").decode().strip())}
    for row in git(repo, "worktree", "list", "--porcelain", "-z").split(b"\0"):
        if row.startswith(b"worktree "):
            protected.add(Path(os.fsdecode(row[9:])).resolve())
    if any(destination == root or root in destination.parents for root in protected):
        raise Refusal("--out must be outside repository, worktrees and common Git directory")
    payload = workflow.encoded(value)
    descriptor = os.open(destination.anchor, os.O_RDONLY | os.O_DIRECTORY)
    try:
        for part in destination.parts[1:-1]:
            child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=descriptor)
            os.close(descriptor)
            descriptor = child
        try:
            fd = os.open(destination.name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                         0o600, dir_fd=descriptor)
        except FileExistsError as error:
            raise Refusal("Existing output preserved; choose a fresh --out") from error
        with os.fdopen(fd, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, required=True, help="Existing Git repository; read only")
    parser.add_argument("--revision", required=True, help="Exact committed source SHA")
    parser.add_argument("--out", type=Path, required=True, help="Fresh absolute private JSON path outside Git/worktrees")
    args = parser.parse_args(argv)
    try:
        value = generate(args.repo, args.revision)
        write_new(args.repo, args.out, value)
        print(json.dumps({"status": "GENERATED_SOURCE_METADATA_ONLY", "out": str(args.out),
                          "sha256": workflow.digest(workflow.encoded(value)),
                          "source_inputs": len(value["changed_files_sha256"]),
                          "acceptance": "NOT RUN", "release_ready": False}))
        return 0
    except (Refusal, OSError, ValueError, UnicodeError) as error:
        print(json.dumps({"status": "REFUSED", "reason": str(error)}), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
