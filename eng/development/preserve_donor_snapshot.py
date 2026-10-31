#!/usr/bin/env python3
"""Push a pinned donor snapshot to a remote, and prove the remote copy is byte-faithful.

Why this exists: the donor registry keeps some donors as extracted snapshots verified only by a
recorded content digest, and those trees were untracked working files. `git ls-files` returned zero
entries for them, so a clean checkout, a reset, or a different machine would have lost the donor UI
reference that the re-UI work depends on. "It is pinned in the registry" is not preservation.

The tool refuses rather than improvising:
- it re-computes the local digest with the gate's own `snapshot_digest` and stops if that disagrees
  with the registry, because pushing an edited tree and calling it faithful would be a false record;
- it stops on any file over GitHub's 100 MB per-file limit instead of silently pruning it;
- it only reports success after exporting the pushed commit with `git archive` into a clean directory
  and recomputing the digest there. Nothing is claimed from the push exit code alone.

Run one binding at a time. This tool never touches the working tree the gate checks.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def gate_digest_module():
    spec = importlib.util.spec_from_file_location("check_donor_sources", REPO / "eng/check-donor-sources.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run(argv: list[str], cwd: Path | None = None) -> str:
    result = subprocess.run(argv, cwd=str(cwd) if cwd else None, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"{' '.join(argv)} -> exit {result.returncode}: {(result.stderr or result.stdout).strip()[:400]}")
    return result.stdout.strip()


def binding_for(donor: str, app: str | None) -> dict:
    registry = json.loads((REPO / "eng/donor-sources.json").read_text())
    matches = [b for b in registry["bindings"]
               if b.get("donor") == donor and (app is None or b.get("app") == app)
               and b.get("scope") == "snapshot"]
    if len(matches) != 1:
        raise SystemExit(f"REFUSED: expected exactly one snapshot binding for donor={donor!r} app={app!r}, "
                         f"found {len(matches)}: {[(m.get('app'), m.get('path')) for m in matches]}")
    return matches[0]


def preserve(donor: str, app: str | None, repo_name: str, dry_run: bool = False) -> dict:
    module = gate_digest_module()
    binding = binding_for(donor, app)
    source = REPO / binding["path"]
    snap = binding.get("snapshot") or {}
    recorded = snap.get("contentDigest")
    if not recorded:
        raise SystemExit(f"REFUSED: {binding['path']} has no recorded contentDigest to preserve against")

    local = module.snapshot_digest(source)
    if local != recorded:
        raise SystemExit(f"REFUSED: the local tree no longer matches its pin "
                         f"(recorded {recorded[:12]}, present {str(local)[:12]}). Do not push a tree that "
                         "cannot be shown to be the pinned one; diagnose the difference first.")

    files = [p for p in source.rglob("*") if p.is_file()]
    if len(files) != int(snap.get("fileCount", -1)) and snap.get("fileCount") is not None:
        raise SystemExit(f"REFUSED: file count {len(files)} disagrees with the recorded {snap['fileCount']}")
    oversized = [p for p in files if p.stat().st_size > 100 * 1024 * 1024]
    if oversized:
        raise SystemExit("REFUSED: GitHub rejects files over 100 MB. These would have to be reported as "
                         "un-preserveable rather than pruned: "
                         + ", ".join(f"{p.relative_to(source)} ({p.stat().st_size // (1024*1024)} MB)"
                                     for p in oversized[:5]))

    work = Path(tempfile.mkdtemp(prefix="donor-push-"))
    try:
        tree = work / "tree"
        tree.mkdir()
        for f in files:
            dest = tree / f.relative_to(source)
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(f, dest)
        provenance = tree / "DONOR-PRESERVATION.md"
        provenance.write_text(
            "# Donor snapshot preservation push\n\n"
            "Byte-faithful copy of the 9to1 pinned donor snapshot tree, preserved so the source pull "
            "survives any single working copy. Not a fork with upstream history: the upstream revision "
            "is recorded here and used for the tag.\n\n"
            f"- 9to1 binding: app={binding.get('app')} donor={binding.get('donor')} path={binding['path']}\n"
            f"- upstream: {snap.get('ref')} @ {snap.get('commit')}\n"
            f"- snapshot fetched: {snap.get('fetched')}; scope {snap.get('modules')}\n"
            f"- files: {len(files)}; content digest (9to1 snapshot_digest): {recorded}\n"
            f"- licence: {snap.get('licence', 'see the LICENSE files retained in this tree')}\n"
            "- no file was edited, formatted or pruned for this push\n", encoding="utf-8")
        run(["git", "init", "-q"], tree)
        # Measured defect of the first version: a donor tree's own .gitattributes let git normalise line
        # endings on commit, so the exported tree no longer reproduced the recorded digest (Gitea: exported
        # 67f22f23ae92 against recorded 756481adff65). The attributes file below lives inside .git, so it is
        # not part of the tree, and it turns off every conversion that would alter bytes.
        (tree / ".git" / "info" / "attributes").write_text(
            "* -text -filter -working-tree-encoding -export-subst -export-ignore\n", encoding="utf-8")
        # -f matters: some donor trees ship a .gitignore that would drop files the recorded snapshot has.
        run(["git", "add", "-A", "-f"], tree)
        run(["git", "-c", "core.hooksPath=/dev/null", "commit", "-q", "-m",
             f"{binding.get('donor')} donor snapshot: {snap.get('ref')} @ {snap.get('commit')}\n\n"
             f"Byte-faithful {len(files)}-file tree exactly as pinned by the 9to1 donor registry "
             f"(content digest {recorded}, verified by exporting this commit with git archive and "
             "recomputing eng/check-donor-sources.py snapshot_digest). Nothing edited, formatted or "
             "pruned; files the donor tree's own .gitignore would skip are included because the "
             "recorded snapshot contains them."], tree)
        run(["git", "branch", "-M", "main"], tree)
        commit = run(["git", "rev-parse", "HEAD"], tree)
        ref = str(snap.get("ref") or "")
        for prefix in ("refs/tags/", "refs/heads/", "refs/remotes/"):
            if ref.startswith(prefix):
                ref = ref[len(prefix):]
        safe_ref = ref.replace("/", "-") or "rev"
        tag = f"upstream-{safe_ref}-{str(snap.get('commit'))[:12]}"
        run(["git", "tag", "-f", tag], tree)

        # Prove faithfulness from the commit object itself before anything is pushed.
        exported = work / "exported"
        exported.mkdir()
        archive = subprocess.run(["git", "archive", commit], cwd=str(tree), capture_output=True)
        if archive.returncode != 0:
            raise SystemExit("REFUSED: could not export the prepared commit")
        tar = subprocess.run(["tar", "-x", "-C", str(exported)], input=archive.stdout, capture_output=True)
        if tar.returncode != 0:
            raise SystemExit(f"REFUSED: could not unpack the export: {tar.stderr.decode()[:200]}")
        (exported / "DONOR-PRESERVATION.md").unlink(missing_ok=True)
        exported_digest = module.snapshot_digest(exported)
        if exported_digest != recorded:
            raise SystemExit(f"REFUSED: the prepared commit does not reproduce the recorded digest "
                             f"(exported {str(exported_digest)[:12]}, recorded {recorded[:12]})")

        receipt = {"app": binding.get("app"), "donor": binding.get("donor"), "path": binding["path"],
                   "local_digest_matches_pin": True, "exported_commit_digest_matches": True,
                   "upstream": {"ref": snap.get("ref"), "commit": snap.get("commit")},
                   "files": len(files), "contentDigest": recorded,
                   "preservationCommit": commit, "tag": tag,
                   "repository": f"https://github.com/CroakyJake12/{repo_name}", "pushed": False}
        if dry_run:
            receipt["note"] = "DRY RUN: verified locally, nothing created or pushed"
            return receipt

        run(["gh", "repo", "create", repo_name, "--public",
             "--description", f"Pinned 9to1 donor snapshot: {binding.get('donor')} "
                              f"{snap.get('ref')} @ {str(snap.get('commit'))[:12]} (byte-faithful, "
                              f"digest {recorded[:12]}…). Preservation copy, not a fork with history."])
        run(["git", "remote", "add", "origin", f"https://github.com/CroakyJake12/{repo_name}.git"], tree)
        run(["git", "push", "-q", "-u", "origin", "main"], tree)
        run(["git", "push", "-q", "origin", f"refs/tags/{tag}"], tree)
        refs = run(["git", "ls-remote", "origin"], tree)
        if commit not in refs or tag not in refs:
            raise SystemExit(f"REFUSED: remote refs do not show the pushed commit and tag: {refs[:300]}")
        receipt["pushed"] = True
        receipt["remoteRefsVerified"] = True
        return receipt
    finally:
        shutil.rmtree(work, ignore_errors=True)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--donor", required=True)
    ap.add_argument("--app", default=None, help="disambiguate a donor used by several apps")
    ap.add_argument("--repo-name", required=True)
    ap.add_argument("--dry-run", action="store_true", help="verify and prepare, create and push nothing")
    args = ap.parse_args(argv)
    print(json.dumps(preserve(args.donor, args.app, args.repo_name, args.dry_run), indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
