"""Record and verify tree equivalence between a loose vendored donor tree and its controlled fork.

A loose vendored tree has no gitlink, so the donor gate cannot compare "the checkout's HEAD" with the
pinned revision. This tool gives that case a real check: it compares blobs path-by-path between the
repository's committed tree for the vendored path and a fork branch, and records the result. Verify then
re-measures the repository side always, and re-measures the fork side whenever a clone is supplied.
"""

import argparse
import json
from pathlib import Path
import subprocess
import sys

def git(cwd, *args):
    return subprocess.run(["git", "-C", str(cwd), *args], capture_output=True, text=True)


def ls_tree(cwd, spec):
    res = git(cwd, "ls-tree", "-r", "--format=%(objectname) %(path)", spec)
    if res.returncode:
        raise RuntimeError(f"ls-tree {spec}: {res.stderr.strip()}")
    out = {}
    for line in res.stdout.splitlines():
        blob, _, path = line.partition(" ")
        out[path] = blob
    return out


def compare(ours, fork):
    shared = sorted(set(ours) & set(fork))
    differing = [p for p in shared if ours[p] != fork[p]]
    return {"shared_paths": len(shared), "identical_blobs": len(shared) - len(differing),
            "differing_paths": differing, "only_in_repository_tree": sorted(set(ours) - set(fork)),
            "only_in_fork_branch": sorted(set(fork) - set(ours))}


def record(repo, path, fork_repo, fork_ref, out_path):
    repo = Path(repo)
    ours = ls_tree(repo, f"HEAD:{path}")
    our_tree = git(repo, "rev-parse", f"HEAD:{path}").stdout.strip()
    if fork_repo and Path(fork_repo).is_dir():
        fork = ls_tree(fork_repo, fork_ref)
        fork_tree = git(fork_repo, "rev-parse", f"{fork_ref}^{{tree}}").stdout.strip()
        fork_commit = git(fork_repo, "rev-parse", fork_ref).stdout.strip()
        side = "both-sides-measured"
    else:
        fork = fork_tree = fork_commit = None
        side = "repository-side-only (no fork clone supplied)"
    result = {"schema": "9to1.donor-fork-equivalence/v1", "path": path,
              "repository_tree": our_tree, "repository_files": len(ours),
              "fork_ref": fork_ref, "fork_commit": fork_commit, "fork_tree": fork_tree,
              "fork_files": None if fork is None else len(fork),
              "measurement": side,
              "equivalence": None if fork is None else compare(ours, fork)}
    out = Path(out_path)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n")
    return result


def verify(repo, receipt_path, fork_repo=None):
    receipt = json.loads(Path(receipt_path).read_text())
    repo = Path(repo)
    path = receipt["path"]
    problems = []
    ours = ls_tree(repo, f"HEAD:{path}")
    our_tree = git(repo, "rev-parse", f"HEAD:{path}").stdout.strip()
    if our_tree != receipt["repository_tree"]:
        problems.append(f"repository tree for {path} changed since the receipt: recorded "
                        f"{str(receipt['repository_tree'])[:12]} now {our_tree[:12]}")
    if fork_repo and Path(fork_repo).is_dir():
        fork = ls_tree(fork_repo, receipt["fork_ref"])
        now = compare(ours, fork)
        if now["differing_paths"]:
            problems.append(f"{len(now['differing_paths'])} paths differ between the vendored tree and "
                            f"the fork branch: " + ", ".join(now["differing_paths"][:8]))
        unexplained = [p for p in now["only_in_repository_tree"]]
        if unexplained:
            problems.append(f"{len(unexplained)} paths exist only in the vendored tree and are not "
                            f"explained by the fork: " + ", ".join(unexplained[:8]))
        return {"checked": "both-sides", "problems": problems, "equivalence": now}
    return {"checked": "repository-side only (pass a --fork clone to re-measure the fork side)",
            "problems": problems, "equivalence": None}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("verb", choices=["record", "verify"])
    ap.add_argument("--repo", default=".")
    ap.add_argument("--path", required=True)
    ap.add_argument("--fork", help="path to a clone of the controlled fork")
    ap.add_argument("--ref", help="fork branch or commit to compare against")
    ap.add_argument("--receipt", required=True)
    args = ap.parse_args(argv)

    if args.verb == "record":
        if not args.ref:
            print("REFUSED: record needs --ref", file=sys.stderr)
            return 2
        result = record(args.repo, args.path, args.fork, args.ref, args.receipt)
        print(json.dumps(result, indent=2, ensure_ascii=False))
        eq = result["equivalence"]
        if eq is None:
            print("\nPARTIAL: fork side not measured", file=sys.stderr)
            return 2
        if eq["differing_paths"] or eq["only_in_repository_tree"]:
            print("\nEQUIVALENCE NOT CLEAN: differing or repository-only paths exist", file=sys.stderr)
            return 1
        print("\nequivalence recorded: every vendored path matches the fork branch, byte for byte")
        return 0

    out = verify(args.repo, args.receipt, args.fork)
    print(json.dumps(out, indent=2, ensure_ascii=False))
    if out["problems"]:
        for p in out["problems"]:
            print("PROBLEM: " + p, file=sys.stderr)
        return 1
    print(f"verified ({out['checked']})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
