#!/usr/bin/env python3
"""Retain byte-identical existing Actions archives; never build or promote them.

Only the four explicitly pinned artifacts from the failed ARM candidate are
accepted. Authentication is confined to api.github.com and stripped on redirect.
The retained receipt records the original failure and original immutable identity.
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import os
import shutil
import urllib.parse
import urllib.request

REPOSITORY = "CroakyJake12/9to1"
RUN = 39698883180
HEAD = "37ba63a92a509013435d61af73cbe17e246167bf"
ARTIFACTS = {
    12393340411: ("9to1-native16-arm64-internal-package", 689354024,
                  "cc666315e974c12313d9613e8a26c84ab8c60d78fe5a5ff2da9ed9157e7428a0"),
    12393400325: ("9to1-native16-arm64-source-and-startup-evidence", 41761116,
                  "b39a41c167e395205364c6c808337447ff9fd72c8b11754e81c14e43de856e32"),
    12393480169: ("9to1-browser-source-sidecar", 819639181,
                  "6aa529c4db9de7ea3c9260150b07b518609e25747025771eb876764e7ca44c2d"),
    12393058751: ("9to1-pinned-llamacpp-upstream-source", 36756708,
                  "0fb6b9ba0d1d65abe16994496aca78e1f11b09058d6aa8b5853c54d8e02dbc82"),
}


def validate_metadata(metadata, artifact_id):
    name, size, sha = ARTIFACTS[artifact_id]
    run = metadata.get("workflow_run", {})
    if (metadata.get("id"), metadata.get("name"), metadata.get("size_in_bytes"),
            metadata.get("digest"), metadata.get("expired"), run.get("id"),
            run.get("head_sha")) != (artifact_id, name, size, "sha256:" + sha,
                                    False, RUN, HEAD):
        raise ValueError("Original artifact metadata does not match its pinned identity")


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, new_url):
        target = urllib.parse.urlsplit(new_url)
        if target.scheme != "https" or target.username or target.password:
            raise ValueError("Artifact redirect must be HTTPS without URL credentials")
        redirected = super().redirect_request(request, fp, code, msg, headers, new_url)
        if urllib.parse.urlsplit(request.full_url).netloc != target.netloc:
            redirected.remove_header("Authorization")
        return redirected


def request(path):
    token = os.environ.get("GH_TOKEN")
    if not token:
        raise ValueError("A supported ephemeral same-repository Actions token is required")
    return urllib.request.Request("https://api.github.com/repos/" + REPOSITORY + path,
                                  headers={"Authorization": "Bearer " + token,
                                           "Accept": "application/vnd.github+json",
                                           "X-GitHub-Api-Version": "2022-11-28",
                                           "User-Agent": "9to1-immutable-artifact-retention"})


def retain(output):
    if os.environ.get("GITHUB_REPOSITORY") != REPOSITORY:
        raise ValueError("Retention is restricted to the original repository")
    if output.exists() or output.is_symlink():
        raise ValueError("Choose a fresh output directory")
    output.mkdir(parents=True)
    if shutil.disk_usage(output).free < sum(row[1] for row in ARTIFACTS.values()) + 256 * 1024 * 1024:
        raise ValueError("Insufficient free space for all pinned original archives")
    opener = urllib.request.build_opener(SafeRedirect())
    with opener.open(request("/actions/runs/" + str(RUN)), timeout=60) as response:
        run = json.load(response)
    if (run.get("id"), run.get("head_sha"), run.get("conclusion"), run.get("status")) != (
            RUN, HEAD, "failure", "completed"):
        raise ValueError("Original run identity or recorded failure changed")
    receipt = {"schema": "9to1.immutable-artifact-retention/v1",
               "observed_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
               "repository": REPOSITORY, "original_run": RUN, "source_head": HEAD,
               "original_conclusion": "failure", "release_readiness": "FAIL_OR_BLOCKED",
               "owner_laptop": "NOT RUN", "build_or_product_execution": "NOT RUN",
               "artifacts": []}
    receipt_path = output / "original-artifacts-receipt.json"
    for artifact_id, (name, size, expected_sha) in ARTIFACTS.items():
        api_path = "/actions/artifacts/" + str(artifact_id)
        with opener.open(request(api_path), timeout=60) as response:
            metadata = json.load(response)
        validate_metadata(metadata, artifact_id)
        target = output / (str(artifact_id) + ".zip")
        partial = target.with_suffix(".partial")
        sha, written = hashlib.sha256(), 0
        with opener.open(request(api_path + "/zip"), timeout=120) as source, partial.open("xb") as destination:
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                written += len(chunk)
                if written > size:
                    raise ValueError("Artifact exceeds its pinned byte count")
                destination.write(chunk)
                sha.update(chunk)
        if written != size or sha.hexdigest() != expected_sha:
            raise ValueError("Original archive byte identity mismatch")
        partial.rename(target)
        receipt["artifacts"].append({"original_id": artifact_id, "name": name,
                                      "file": target.name, "bytes": written,
                                      "sha256": sha.hexdigest(), "metadata": metadata})
        receipt_path.write_text(json.dumps(receipt, indent=2) + "\n")
        print("Retained", artifact_id, written, expected_sha, flush=True)
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    retain(parser.parse_args().output)
