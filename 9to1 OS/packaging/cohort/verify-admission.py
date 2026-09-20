#!/usr/bin/env python3
"""Validate the immutable package-backed cohort admission record.

This validates metadata only. Package bytes must still be verified and extracted
with packaging/llamacpp/stage-cohort.sh before any installation decision.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
LOCK = ROOT / "packaging" / "llamacpp" / "cohort-artifact.lock.json"
EXPECTED = {
    "llamacpp-runtime": {
        "source": ("main", "11be1b700022095e4b598768b5286adfe966f85a"),
        "workflow": (10031771129, 10031771129, "haven-llamacpp-runtime-amd64"),
        "package": (
            "haven-llamacpp-runtime_0.4.0+haven0.1_amd64.deb",
            "13ea16e4ffa92d1e4b2f31a1a32ba00dc8802ec9077733d7db96a68dad92142d",
            "amd64",
            ["python3", "libc6", "libstdc++6", "libgcc-s1", "libgomp1"],
            "/usr/bin/haven-modelctl",
        ),
    },
    "hui-linux-graphical-preview": {
        "source": ("platform/hui-linux-graphical-preview", "2a578502c31973fbefa3b3f82efa22d7ad11db53"),
        "workflow": (34157071097, 10031358716, "hui-linux-graphical-package"),
        "package": (
            "haven-hui-preview_0.1.0+git2a578502c319_amd64.deb",
            "104f8ff3929b8b0e1c90673804d7cfbe79a31ea5c2763dc040246a7ead53575a",
            "amd64",
            ["libc6", "libgcc-s1", "libstdc++6", "zlib1g"],
            "/usr/bin/cakeos-hui-preview",
        ),
    },
}


def fail(message: str) -> None:
    raise SystemExit(f"cohort admission failed: {message}")


def main() -> int:
    try:
        document = json.loads(LOCK.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read immutable artifact lock: {error}")

    if document.get("schemaVersion") != 1:
        fail("unsupported artifact lock schema")
    artifacts = {item.get("id"): item for item in document.get("artifacts", [])}
    if set(artifacts) != set(EXPECTED):
        fail("only the two package-backed artifacts may be admitted")

    for artifact_id, expected in EXPECTED.items():
        artifact = artifacts[artifact_id]
        source = artifact.get("source", {})
        workflow = artifact.get("workflow", {})
        package = artifact.get("package", {})
        if (source.get("ref"), source.get("revision")) != expected["source"]:
            fail(f"{artifact_id} source provenance changed")
        if (
            workflow.get("runId"),
            workflow.get("artifactId"),
            workflow.get("artifactName"),
        ) != expected["workflow"]:
            fail(f"{artifact_id} workflow provenance changed")
        if (
            package.get("filename"),
            package.get("sha256"),
            package.get("architecture"),
            package.get("dependencies"),
            package.get("launcher"),
        ) != expected["package"]:
            fail(f"{artifact_id} package metadata changed")

    for retired_builder in ("build-debs.sh", "verify-debs.sh"):
        if (Path(__file__).parent / retired_builder).exists():
            fail(f"source-only cohort builder remains: {retired_builder}")

    print("Cohort admission metadata passed; verify package bytes before installation.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
