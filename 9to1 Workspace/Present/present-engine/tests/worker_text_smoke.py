#!/usr/bin/env python3
"""Prove snapshot-referenced text replacement and CakeOS text history."""

from __future__ import annotations

import sys
from pathlib import Path

from worker_protocol_smoke import WorkerClient, require_semantic_mutation_events


def element_ref_with_text(response: dict, expected_text: str) -> str:
    result = response.get("result", {})
    if result.get("referenceStability") != "snapshot-only":
        raise RuntimeError(f"element response overstates reference stability: {result}")
    for element in result.get("elements", []):
        if expected_text in [str(value) for value in element.get("text", [])]:
            if element.get("referenceStability") != "snapshot-only":
                raise RuntimeError(f"element overstates reference stability: {element}")
            ref = element.get("ref")
            if not isinstance(ref, str) or not ref.startswith("slide:") or "/object:" not in ref:
                raise RuntimeError(f"element did not expose a CakeOS snapshot ref: {element}")
            return ref
    raise RuntimeError(f"could not find element containing {expected_text!r}: {result}")


def require_text(response: dict, expected_text: str) -> None:
    element_ref_with_text(response, expected_text)


def slide_count(response: dict) -> int:
    return len(response["result"]["slides"])


def main() -> int:
    if len(sys.argv) != 4:
        print(
            "usage: worker_text_smoke.py WORKER INPUT.odp OUTPUT_PREFIX",
            file=sys.stderr,
        )
        return 2

    worker = Path(sys.argv[1]).resolve()
    source = Path(sys.argv[2]).resolve()
    output_prefix = Path(sys.argv[3]).resolve()
    updated = output_prefix.with_name(f"{output_prefix.name}-updated.odp")
    undone = output_prefix.with_name(f"{output_prefix.name}-undo.odp")
    redone = output_prefix.with_name(f"{output_prefix.name}-redo.odp")
    mixed_base = output_prefix.with_name(f"{output_prefix.name}-mixed-base.odp")
    mixed_undo = output_prefix.with_name(f"{output_prefix.name}-mixed-undo.odp")
    mixed_redo = output_prefix.with_name(f"{output_prefix.name}-mixed-redo.odp")
    for path in (updated, undone, redone, mixed_base, mixed_undo, mixed_redo):
        path.unlink(missing_ok=True)

    client = WorkerClient(worker)
    try:
        hello, payload = client.require_ok("hello")
        if payload:
            raise RuntimeError("hello unexpectedly returned binary data")
        capabilities = set(hello["result"]["capabilities"])
        for capability in ("listElements", "replaceElementText", "undo", "redo", "saveAs"):
            if capability not in capabilities:
                raise RuntimeError(f"worker is missing text capability {capability}")

        opened, _ = client.require_ok("open", path=str(source))
        if slide_count(opened) != 3:
            raise RuntimeError("text fixture did not open as a three-slide presentation")

        inventory, payload = client.require_ok("listElements", slideIndex=0)
        if payload:
            raise RuntimeError("listElements unexpectedly returned binary data")
        ref = element_ref_with_text(inventory, "Alpha")

        multiline, multiline_payload = client.request(
            "replaceElementText",
            ref=ref,
            text="Alpha\nBeta",
        )
        if multiline.get("ok") or multiline_payload:
            raise RuntimeError("worker accepted multiline text in the single-line semantic write slice")
        multiline_message = str(multiline.get("error", {}).get("message", ""))
        if "single-line" not in multiline_message:
            raise RuntimeError(f"worker did not explain multiline rejection: {multiline}")
        still_fresh, _ = client.require_ok("listElements", slideIndex=0)
        require_text(still_fresh, "Alpha")

        replaced, payload = client.require_ok(
            "replaceElementText",
            ref=ref,
            text="CakeOS Typed",
        )
        if payload:
            raise RuntimeError("replaceElementText unexpectedly returned binary data")
        if replaced["result"].get("ref") != ref or replaced["result"].get("snapshotFresh") is not False:
            raise RuntimeError(f"replaceElementText returned invalid metadata: {replaced}")

        stale, stale_payload = client.request("listElements", slideIndex=0)
        if stale.get("ok") or stale_payload:
            raise RuntimeError("worker returned element data from a stale post-text snapshot")
        stale_message = str(stale.get("error", {}).get("message", ""))
        if "stale" not in stale_message or "save" not in stale_message:
            raise RuntimeError(f"worker did not explain stale text snapshot: {stale}")
        replace_events = client.take_events()
        require_semantic_mutation_events(replace_events, "replaceElementText")

        client.require_ok("saveAs", path=str(updated), format="odp")
        if not updated.exists() or updated.stat().st_size == 0:
            raise RuntimeError("text replacement did not persist to ODP")
        updated_inventory, _ = client.require_ok("listElements", slideIndex=0)
        require_text(updated_inventory, "CakeOS Typed")

        client.require_ok("undo")
        stale_after_undo, payload = client.request("listElements", slideIndex=0)
        if stale_after_undo.get("ok") or payload:
            raise RuntimeError("worker returned element data before saving text undo")
        client.require_ok("saveAs", path=str(undone), format="odp")
        undo_inventory, _ = client.require_ok("listElements", slideIndex=0)
        require_text(undo_inventory, "Alpha")

        client.require_ok("redo")
        stale_after_redo, payload = client.request("listElements", slideIndex=0)
        if stale_after_redo.get("ok") or payload:
            raise RuntimeError("worker returned element data before saving text redo")
        client.require_ok("saveAs", path=str(redone), format="odp")
        redo_inventory, _ = client.require_ok("listElements", slideIndex=0)
        require_text(redo_inventory, "CakeOS Typed")

        client.require_ok("close")
        client.require_ok("open", path=str(redone))
        reopened_inventory, _ = client.require_ok("listElements", slideIndex=0)
        require_text(reopened_inventory, "CakeOS Typed")

        malformed, malformed_payload = client.request(
            "replaceElementText",
            ref="object:0",
            text="bad",
        )
        if malformed.get("ok") or malformed_payload:
            raise RuntimeError("worker accepted a malformed semantic element ref")

        # Prove that a CakeOS-managed text history entry does not obstruct an
        # earlier LibreOffice-native slide history entry. The worker must undo
        # text first, then the preceding slide add, and redo both in order.
        client.require_ok("close")
        mixed_opened, _ = client.require_ok("open", path=str(source))
        if slide_count(mixed_opened) != 3:
            raise RuntimeError("mixed-history fixture did not reopen with three slides")

        mixed_added, _ = client.require_ok("addSlideAfter", slideIndex=0)
        if slide_count(mixed_added) != 4:
            raise RuntimeError("mixed-history native slide add failed")
        client.require_ok("saveAs", path=str(mixed_base), format="odp")

        mixed_inventory, _ = client.require_ok("listElements", slideIndex=0)
        mixed_ref = element_ref_with_text(mixed_inventory, "Alpha")
        client.require_ok("replaceElementText", ref=mixed_ref, text="Mixed Text")

        client.require_ok("undo")
        mixed_native_undo, _ = client.require_ok("undo")
        if slide_count(mixed_native_undo) != 3:
            raise RuntimeError(
                "CakeOS text history blocked the preceding native slide undo"
            )
        client.require_ok("saveAs", path=str(mixed_undo), format="odp")
        mixed_undo_inventory, _ = client.require_ok("listElements", slideIndex=0)
        require_text(mixed_undo_inventory, "Alpha")

        mixed_native_redo, _ = client.require_ok("redo")
        if slide_count(mixed_native_redo) != 4:
            raise RuntimeError(
                "CakeOS text history blocked the native slide redo"
            )
        client.require_ok("redo")
        client.require_ok("saveAs", path=str(mixed_redo), format="odp")
        mixed_redo_inventory, _ = client.require_ok("listElements", slideIndex=0)
        require_text(mixed_redo_inventory, "Mixed Text")

        print("worker_text_replace=passed")
        print("worker_text_history=passed")
        print("worker_text_persistence=passed")
        print("worker_text_constraints=passed")
        print("worker_mixed_text_native_history=passed")
        print(f"worker_text_ref={ref}")
        return 0
    finally:
        client.close()


if __name__ == "__main__":
    raise SystemExit(main())
