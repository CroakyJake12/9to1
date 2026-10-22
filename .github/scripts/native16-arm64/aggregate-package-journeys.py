#!/usr/bin/env python3
"""Fail-closed ARM checkpoint from original step OUTCOMES and exact raw receipts.

Actions may label a continue-on-error step's conclusion 'success' after exit 1.
Only outcome plus raw evidence is meaningful. Passing this focused checkpoint
never grants independent visual review, owner-laptop or full-release acceptance.
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import os

GATES = {
    "startup": ("native-startup-receipt.json", "STARTUP_OUTCOME",
                "PASS_BOUNDED_STARTUP_ONBOARDING_AND_HOME_CLOSE_CHECKPOINT_REQUIRES_SCREENSHOT_REVIEW"),
    "chat": ("package-ui-journey-receipt.json", "CHAT_OUTCOME",
             "CHECKPOINT_ACTUAL_UI_JOURNEY_REQUIRES_INDEPENDENT_SCREENSHOT_REVIEW"),
    "browser": ("package-browser-journey-receipt.json", "BROWSER_OUTCOME",
                "PASS_FOCUSED_NATIVE_FIRST_ASCII_ENTER_SUBMIT_RENDER_AND_NORMAL_CLOSE_ONLY"),
    "removal": ("package-removal-receipt.json", "REMOVAL_OUTCOME",
                "PASS_PACKAGE_REMOVAL_AND_PROFILE_PRESERVATION"),
}


def aggregate(evidence, source_head, outcomes):
    gates = []
    if len(source_head) != 40 or any(c not in "0123456789abcdef" for c in source_head):
        raise ValueError("An exact immutable source revision is required")
    for name, (filename, outcome_variable, expected_status) in GATES.items():
        path = evidence / filename
        row = {"gate": name, "step_outcome": outcomes.get(outcome_variable, "NOT RUN"),
               "evidence": filename, "status": "BLOCKED", "failures": []}
        try:
            data = path.read_bytes()
            row["evidence_sha256"] = hashlib.sha256(data).hexdigest()
            raw = json.loads(data)
            if not isinstance(raw, dict):
                raise ValueError("Receipt must be an object")
            row["raw_status"] = raw.get("status", "NOT RUN")
            row["raw_error"] = raw.get("error")
            row["required_phase_failures"] = raw.get("required_phase_failures", [])
            provenance = raw.get("installed_build_provenance", {})
            if not isinstance(provenance, dict) or not isinstance(row["required_phase_failures"], list):
                raise ValueError("Invalid provenance or required-phase failure shape")
            if provenance.get("source_head") != source_head or raw.get("acceptance_tools_head") != source_head:
                row["failures"].append({"kind": "INFRASTRUCTURE", "code": "SOURCE_OR_TEST_REVISION_MISMATCH"})
            if name != "startup" and raw.get("source_head") != source_head:
                row["failures"].append({"kind": "INFRASTRUCTURE", "code": "RAW_SOURCE_REVISION_MISMATCH"})
            if row["step_outcome"] != "success":
                row["failures"].append({"kind": "UNCLASSIFIED_REQUIRES_TRIAGE", "code": "STEP_NOT_SUCCESSFUL"})
            if raw.get("status") != expected_status:
                row["failures"].append({"kind": "UNCLASSIFIED_REQUIRES_TRIAGE", "code": "RAW_JOURNEY_NOT_SUCCESSFUL"})
            if row["required_phase_failures"]:
                row["failures"].append({"kind": "UNCLASSIFIED_REQUIRES_TRIAGE", "code": "RETAINED_REQUIRED_PHASE_FAILURE"})
            row["status"] = "FAIL" if row["failures"] else "CHECKPOINT"
        except (OSError, ValueError, TypeError) as error:
            row["failures"].append({"kind": "INFRASTRUCTURE", "code": "MISSING_OR_INVALID_RAW_RECEIPT",
                                    "detail": str(error)})
        gates.append(row)
    checkpoint = all(row["status"] == "CHECKPOINT" for row in gates)
    return {"schema": "9to1.arm-package-journey-aggregate/v1",
            "at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "source_head": source_head, "status": "CHECKPOINT" if checkpoint else "BLOCKED",
            "gates": gates, "independent_screenshot_review": "REQUIRED",
            "owner_laptop": "NOT RUN", "automatic_full_application_acceptance": False,
            "release_ready": False}


def write_report(receipt, evidence, summary=None):
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence / "combined-package-journey-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    lines = ["## ARM installed-package checkpoint", "",
             "| Journey | Actual outcome | Raw status | Result |", "|---|---|---|---|"]
    for row in receipt["gates"]:
        values = [row["gate"], row["step_outcome"], row.get("raw_status", "NOT RUN"), row["status"]]
        lines.append("| " + " | ".join(str(value).replace("|", "\\|").replace("\n", " ") for value in values) + " |")
    lines += ["", "Independent visual review: REQUIRED. Owner laptop: NOT RUN. Release ready: false.", ""]
    report = "\n".join(lines)
    (evidence / "package-checkpoint-summary.md").write_text(report)
    if summary:
        with Path(summary).open("a") as stream:
            stream.write(report)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, default=Path("evidence"))
    args = parser.parse_args()
    receipt = aggregate(args.evidence, os.environ["GITHUB_SHA"], os.environ)
    write_report(receipt, args.evidence, os.environ.get("GITHUB_STEP_SUMMARY"))
    return 0 if receipt["status"] == "CHECKPOINT" else 1


if __name__ == "__main__":
    raise SystemExit(main())
