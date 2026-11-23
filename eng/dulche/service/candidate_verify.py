"""Verify the dated screen against immutable Hub manifests using metadata requests only."""

import argparse
import concurrent.futures
import json
from datetime import UTC, datetime
from pathlib import Path

from candidate_screen import EVIDENCE, read_report
from model_hub import COMMIT, REPO, Hub


def verify(evidence, hub=None):
    hub = hub or Hub()
    references = {}
    for candidate in evidence["items"]:
        references.setdefault((candidate["id"], candidate["revision"]), [])
        for option in candidate["quantizations"]:
            group = references.setdefault((option["repo"], option["revision"]), [])
            group.extend(option["files"])

    def lookup(pair):
        (repo, revision), files = pair
        if not REPO.fullmatch(repo) or not COMMIT.fullmatch(revision):
            raise ValueError("invalid immutable model reference")
        data = hub.metadata(
            "/api/models/" + repo + "/revision/" + revision + "?blobs=true"
        )
        if data.get("id") != repo or data.get("sha") != revision:
            raise ValueError("revision mismatch for " + repo)
        siblings = {x["rfilename"]: x for x in data.get("siblings", [])}
        for item in files:
            remote = siblings.get(item["path"], {})
            if (
                remote.get("size") != item["bytes"]
                or (remote.get("lfs") or {}).get("sha256") != item["sha256"]
            ):
                raise ValueError(
                    "file manifest mismatch for " + repo + "/" + item["path"]
                )
        return {"repo": repo, "revision": revision, "verified_file_hashes": len(files)}

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        verified = list(pool.map(lookup, references.items()))
    return {
        "verified_at_utc": datetime.now(UTC).isoformat(timespec="seconds"),
        "checkpoints": len(evidence["items"]),
        "immutable_references": verified,
        "verified_file_hashes": sum(v["verified_file_hashes"] for v in verified),
        "scope": "Hub metadata and pinned SHA-256 manifests. No weight bytes requested; source hashes do not certify local file integrity or GPU performance.",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, default=EVIDENCE)
    args = parser.parse_args()
    print(json.dumps(verify(read_report(args.evidence)), indent=2))
