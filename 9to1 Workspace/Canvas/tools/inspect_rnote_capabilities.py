#!/usr/bin/env python3
"""Inspect the controlled donor's actions and authored UI; never certify runtime parity."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

DONOR_REVISION = "1a728d6a85db3528f9c79dc0990700e91b22696f"
ACTION = re.compile(r"(?:SimpleAction|PropertyAction)::new(?:_stateful)?\s*\(\s*\"([^\"]+)\"", re.MULTILINE)
DYNAMIC_ACTION = re.compile(r"(?:SimpleAction|PropertyAction)::new(?:_stateful)?\s*\(\s*([^\"\s][^\n]+)")
REPLACEMENTS = {
    "new-doc": "Canonical Canvas artifact creation and Files registration",
    "save-doc": "Canonical Files immutable revision publication and autosave",
    "save-doc-as": "Explicit canonical Files artifact copy/export",
    "open-doc": "Canonical Files artifact open",
    "open-file": "Canonical Files artifact open",
    "rename-file": "Canonical Files rename preserving artifact identity",
    "trash-file": "Canonical Files delete/recovery",
    "duplicate": "Explicit canonical Files artifact duplication",
    "create-folder": "Canonical Files folder creation",
    "open-folder": "Canonical Files folder browsing",
    "add-workspace": "Canonical Files storage/workspace binding",
    "remove-selected-workspace": "Canonical Files storage/workspace binding",
    "edit-selected-workspace": "Canonical Files storage/workspace binding",
    "move-selected-workspace-up": "Canonical Files workspace navigation ordering",
    "move-selected-workspace-down": "Canonical Files workspace navigation ordering",
    "open-settings": "Canonical CUI settings and Home user preferences",
    "open-appmenu": "Canonical CUI application menu",
    "open-canvasmenu": "Canonical CUI Canvas menu",
    "keyboard-shortcuts": "Canonical CUI accessible keyboard command catalogue",
    "about": "Home application identity and retained donor provenance/licences",
}


def source_ref(root: Path, path: Path, line: int | None = None) -> dict:
    result = {"path": path.relative_to(root).as_posix()}
    if line is not None:
        result["line"] = line
    return result


def inspect(root: Path) -> dict:
    actual = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"], check=True,
                            capture_output=True, text=True).stdout.strip()
    if actual != DONOR_REVISION:
        raise ValueError(f"Controlled donor revision changed: expected {DONOR_REVISION}, found {actual}")
    ui = root / "crates/rnote-ui"
    files = sorted([*ui.rglob("*.rs"), *ui.rglob("*.ui")])
    if not files:
        raise ValueError("Controlled donor UI/action sources are absent")
    entries = []
    dynamic = []
    hashes = []
    for path in files:
        content = path.read_bytes()
        hashes.append({**source_ref(root, path), "sha256": hashlib.sha256(content).hexdigest()})
        text = content.decode("utf-8")
        if path.suffix == ".rs":
            for occurrence, match in enumerate(ACTION.finditer(text), 1):
                name = match[1]
                native = REPLACEMENTS.get(name)
                entries.append({
                    "id": f"action:{path.relative_to(root).as_posix()}:{name}:{occurrence}",
                    "kind": "donor-action", "name": name,
                    "classification": "REPLACE_WITH_9TO1_NATIVE_EQUIVALENT" if native else "PRESERVE",
                    "rationale": native or "Retain applicable donor behaviour; source presence does not prove working Canvas integration.",
                    "source": source_ref(root, path, text.count("\n", 0, match.start()) + 1),
                    "parityEvidence": "UNVERIFIED", "releaseBlocking": True,
                })
            for match in DYNAMIC_ACTION.finditer(text):
                expression = match[1].strip()
                names = []
                if expression == '&format!("set-color-{}", i + 1), None);':
                    color_setters = re.search(r"let color_setters = \{(.*?)\n        \};", text, re.DOTALL)
                    setters = [] if color_setters is None else re.findall(r"p\.setter_(\d+)\(\)", color_setters[1])
                    if sorted(map(int, setters)) != list(range(1, 10)):
                        raise ValueError("Inspected color-setter expansion changed; re-audit the donor source")
                    names = [f"set-color-{number}" for number in range(1, len(setters) + 1)]
                dynamic.append({"expression": expression,
                                "classification": "PRESERVE",
                                "inspectedNames": names,
                                "source": source_ref(root, path, text.count("\n", 0, match.start()) + 1),
                                "releaseBlocking": True,
                                "reason": "Expanded names require native behaviour evidence." if names else "Dynamic action names require inspected expansion and native behaviour evidence."})
        else:
            document = ET.fromstring(text)
            for occurrence, element in enumerate(document.iter(), 1):
                if element.tag not in {"object", "template", "item"}:
                    continue
                properties = {child.attrib.get("name", ""): (child.text or "").strip()
                              for child in element if child.tag in {"property", "attribute"}}
                labels = {key: value for key, value in properties.items()
                          if key in {"label", "title", "subtitle", "tooltip-text", "tooltip_text", "action-name", "action"} and value}
                name = element.attrib.get("id") or element.attrib.get("class")
                if not name and not labels:
                    continue
                entries.append({
                    "id": f"ui:{path.relative_to(root).as_posix()}:{name or 'item'}:{occurrence}",
                    "kind": "donor-ui-surface", "name": name, "class": element.attrib.get("class"),
                    "authoredProperties": labels,
                    "classification": "REPLACE_WITH_9TO1_NATIVE_EQUIVALENT",
                    "rationale": "Ordinary GTK shell/control surface requires accessible canonical CUI equivalent; underlying editing behaviour remains a preservation requirement.",
                    "source": source_ref(root, path),
                    "parityEvidence": "UNVERIFIED", "releaseBlocking": True,
                })
    return {
        "schemaVersion": 1, "donorRevision": actual,
        "coverage": "Source-discovered literal actions, dynamic action declarations and authored UI surfaces. Not an accepted exhaustive behavioural audit.",
        "releaseConformance": False,
        "releaseGates": [
            "Every discovered entry requires actual native Canvas behaviour/accessibility/permission evidence.",
            "Every dynamic action's expanded names require runtime parity evidence; any family without inspectedNames also requires source expansion.",
            "Non-action donor engine capabilities, import/export variants, gesture/input behaviour and platform settings require exhaustive manual audit and acceptance evidence.",
            "No NOT_APPLICABLE exception is accepted by this generated inventory.",
        ],
        "sourceFiles": hashes,
        "entries": entries,
        "dynamicActionFamilies": dynamic,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--donor", type=Path, default=Path(__file__).resolve().parents[1] / "Source/Rnote")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "RnoteCapabilityInventory.json")
    parser.add_argument("--check", action="store_true", help="Fail if the checked-in source inventory differs; does not test runtime parity.")
    args = parser.parse_args()
    inventory = inspect(args.donor.resolve())
    serialized = json.dumps(inventory, indent=2, ensure_ascii=False) + "\n"
    if args.check:
        if args.output.read_text(encoding="utf-8") != serialized:
            raise SystemExit("Source inventory is stale; regenerate and inspect the changed donor capabilities.")
    else:
        args.output.write_text(serialized, encoding="utf-8")
    actions = sum(entry["kind"] == "donor-action" for entry in inventory["entries"])
    print(f"Inspected {len(inventory['sourceFiles'])} source files: {actions} literal actions, "
          f"{len(inventory['entries']) - actions} UI surfaces, {len(inventory['dynamicActionFamilies'])} dynamic families. "
          "Runtime parity remains UNVERIFIED; release conformance is false.")


if __name__ == "__main__":
    main()
