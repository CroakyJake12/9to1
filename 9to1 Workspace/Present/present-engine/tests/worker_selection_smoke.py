#!/usr/bin/env python3
"""Prove snapshot-ref selection maps to stable live HUI geometry events."""

from __future__ import annotations

import sys
import time
from pathlib import Path
from typing import Any

from worker_protocol_smoke import WorkerClient, require_semantic_mutation_events


def checkpoint(name: str) -> None:
    print(f"selection_checkpoint={name}", flush=True)


def find_ref(response: dict[str, Any], expected_text: str) -> str:
    result = response.get("result", {})
    if result.get("referenceStability") != "snapshot-only":
        raise RuntimeError(f"element inventory overstates identity stability: {result}")
    for element in result.get("elements", []):
        if expected_text in [str(value) for value in element.get("text", [])]:
            ref = element.get("ref")
            if not isinstance(ref, str):
                raise RuntimeError(f"element ref is not a string: {element}")
            return ref
    raise RuntimeError(f"could not find element containing {expected_text!r}: {result}")


def selected_event(
    events: list[dict[str, Any]], ref: str, generation: int
) -> dict[str, Any]:
    matches = [
        event for event in events
        if event.get("event") == "elementSelectionChanged"
        and event.get("data", {}).get("selected") is True
        and event.get("data", {}).get("ref") == ref
    ]
    if not matches:
        raise RuntimeError(f"missing selected geometry event for {ref}: {events}")
    event = matches[-1]
    data = event["data"]
    if data.get("referenceStability") != "snapshot-only":
        raise RuntimeError(f"selection event overstates reference stability: {event}")
    if data.get("snapshotGeneration") != generation:
        raise RuntimeError(f"selection event has the wrong snapshot generation: {event}")
    rect = data.get("rectTwips")
    if not isinstance(rect, dict):
        raise RuntimeError(f"selection event has no rectTwips: {event}")
    if int(rect.get("width", 0)) <= 0 or int(rect.get("height", 0)) <= 0:
        raise RuntimeError(f"selection event has non-positive bounds: {event}")
    if not isinstance(data.get("angleHundredthDegrees"), int):
        raise RuntimeError(f"selection event has no normalized angle: {event}")
    return event


def wait_for_selected_event(
    client: WorkerClient, ref: str, generation: int
) -> dict[str, Any]:
    events: list[dict[str, Any]] = []
    for attempt in range(50):
        client.require_ok("listSlides")
        events.extend(client.take_events())
        try:
            return selected_event(events, ref, generation)
        except RuntimeError:
            if attempt == 49:
                raise
            time.sleep(0.02)
    raise RuntimeError(f"selection callback wait exhausted for {ref}")


def require_cleared(
    events: list[dict[str, Any]],
    ref: str,
    reason: str,
    generation: int | None = None,
) -> None:
    matches = [
        event for event in events
        if event.get("event") == "elementSelectionChanged"
        and event.get("data", {}).get("selected") is False
        and event.get("data", {}).get("ref") == ref
        and event.get("data", {}).get("reason") == reason
    ]
    if not matches:
        raise RuntimeError(
            f"missing deselection event for {ref} with reason {reason!r}: {events}"
        )
    if generation is not None and matches[-1]["data"].get("snapshotGeneration") != generation:
        raise RuntimeError(f"deselection event has the wrong snapshot generation: {matches[-1]}")


def worker_exit_details(client: WorkerClient) -> str:
    code = client.process.poll()
    if code is None:
        try:
            code = client.process.wait(timeout=2)
        except TimeoutError:
            code = None
    stderr = ""
    if client.process.stderr is not None and code is not None:
        stderr = client.process.stderr.read().decode("utf-8", errors="replace")
    return f"returncode={code}, stderr={stderr!r}"


def main() -> int:
    if len(sys.argv) != 4:
        print(
            "usage: worker_selection_smoke.py WORKER INPUT.odp OUTPUT.odp",
            file=sys.stderr,
        )
        return 2

    worker = Path(sys.argv[1]).resolve()
    source = Path(sys.argv[2]).resolve()
    output = Path(sys.argv[3]).resolve()
    save_as_output = output.with_name(f"{output.stem}-save-as.odp")
    two_slide_output = output.with_name(f"{output.stem}-two-slides.odp")
    one_slide_output = output.with_name(f"{output.stem}-one-slide.odp")
    output.unlink(missing_ok=True)
    for path in (save_as_output, two_slide_output, one_slide_output):
        if path.exists():
            raise RuntimeError(f"save-as output path unexpectedly exists: {path}")

    client = WorkerClient(worker)
    try:
        hello, payload = client.require_ok("hello")
        if payload:
            raise RuntimeError("hello unexpectedly returned binary data")
        capabilities = set(hello["result"]["capabilities"])
        for capability in (
            "listElements",
            "selectElement",
            "clearElementSelection",
            "replaceElementText",
            "moveSlide",
            "saveAs",
            "snapshotGeneration",
            "snapshotGenerationRequiredAfterTransition",
        ):
            if capability not in capabilities:
                raise RuntimeError(f"worker is missing selection capability {capability}")
        if "elementSelectionChanged" not in hello["result"]["events"]:
            raise RuntimeError("worker does not advertise the stable selection event")
        checkpoint("hello")

        opened, _ = client.require_ok("open", path=str(source))
        if [slide["name"] for slide in opened["result"]["slides"]] != ["Alpha", "Beta", "Gamma"]:
            raise RuntimeError("selection fixture did not open with the expected slide order")
        checkpoint("fixture_open")

        inventory, payload = client.require_ok("listElements", slideIndex=0)
        if payload:
            raise RuntimeError("listElements unexpectedly returned binary data")
        alpha_ref = find_ref(inventory, "Alpha")
        snapshot_generation = inventory["result"]["snapshotGeneration"]
        if not isinstance(snapshot_generation, int) or snapshot_generation < 1:
            raise RuntimeError(f"element inventory has no valid generation: {inventory}")
        if any(
            element.get("snapshotGeneration") != snapshot_generation
            for element in inventory["result"].get("elements", [])
        ):
            raise RuntimeError("element refs do not share the inventory generation")
        checkpoint("inventory")

        # Clear any open/render events before testing selection correlation.
        client.require_ok("listSlides")
        client.take_events()

        # The generation token is additive: a legacy v1 request without it
        # still selects the current snapshot, with generation echoed in output.
        legacy_selected, _ = client.require_ok("selectElement", ref=alpha_ref)
        if legacy_selected["result"].get("snapshotGeneration") != snapshot_generation:
            raise RuntimeError(f"legacy selection did not report its current generation: {legacy_selected}")
        wait_for_selected_event(client, alpha_ref, snapshot_generation)
        client.require_ok("clearElementSelection")
        client.require_ok("listSlides")
        require_cleared(client.take_events(), alpha_ref, "explicitClear", snapshot_generation)

        selected, payload = client.require_ok(
            "selectElement", ref=alpha_ref, snapshotGeneration=snapshot_generation
        )
        if payload:
            raise RuntimeError("selectElement unexpectedly returned binary data")
        if (
            selected["result"].get("ref") != alpha_ref
            or selected["result"].get("selected") is not True
            or selected["result"].get("snapshotGeneration") != snapshot_generation
        ):
            raise RuntimeError(f"selectElement returned invalid metadata: {selected}")
        checkpoint("select_response")

        try:
            first_geometry = wait_for_selected_event(client, alpha_ref, snapshot_generation)
        except Exception as error:
            if client.process.poll() is not None:
                raise RuntimeError(
                    "worker failed after the successful selectElement response: "
                    + worker_exit_details(client)
                ) from error
            raise
        checkpoint("selection_geometry")

        unchanged_text, unchanged_text_payload = client.require_ok(
            "replaceElementText",
            ref=alpha_ref,
            snapshotGeneration=snapshot_generation,
            text="Alpha",
        )
        if (
            unchanged_text_payload
            or unchanged_text["result"].get("changed") is not False
            or unchanged_text["result"].get("snapshotFresh") is not True
            or unchanged_text["result"].get("snapshotGeneration") != snapshot_generation
        ):
            raise RuntimeError(f"no-op semantic text edit changed snapshot state: {unchanged_text}")
        client.require_ok("listSlides")
        unchanged_text_events = client.take_events()
        if any(
            event.get("event") == "documentChanged"
            or (
                event.get("event") == "elementSelectionChanged"
                and event.get("data", {}).get("selected") is False
                and event.get("data", {}).get("ref") == alpha_ref
            )
            for event in unchanged_text_events
        ):
            raise RuntimeError("no-op semantic text edit invalidated document or selection state")
        retained_text_selection, _ = client.require_ok("clearElementSelection")
        if (
            retained_text_selection["result"].get("ref") != alpha_ref
            or retained_text_selection["result"].get("snapshotGeneration") != snapshot_generation
        ):
            raise RuntimeError("no-op semantic text edit consumed the active selection")
        client.require_ok("listSlides")
        require_cleared(client.take_events(), alpha_ref, "explicitClear", snapshot_generation)
        client.require_ok(
            "selectElement", ref=alpha_ref, snapshotGeneration=snapshot_generation
        )
        wait_for_selected_event(client, alpha_ref, snapshot_generation)

        client.take_events()
        invalid_move, invalid_move_payload = client.request(
            "moveSlide", fromIndex=0, toIndex=99
        )
        if (
            invalid_move.get("ok")
            or invalid_move_payload
            or invalid_move.get("error", {}).get("code") != "NOT_FOUND"
        ):
            raise RuntimeError("worker accepted an out-of-range slide move")
        preserved_after_invalid_move, _ = client.require_ok("listSlides")
        if [slide["name"] for slide in preserved_after_invalid_move["result"]["slides"]] != [
            "Alpha", "Beta", "Gamma"
        ]:
            raise RuntimeError("invalid move changed the active slide order")
        invalid_move_events = client.take_events()
        if any(
            event.get("event") == "documentChanged"
            or (
                event.get("event") == "elementSelectionChanged"
                and event.get("data", {}).get("selected") is False
                and event.get("data", {}).get("ref") == alpha_ref
            )
            for event in invalid_move_events
        ):
            raise RuntimeError("invalid move changed the document or cleared the selection")
        retained_selection, _ = client.require_ok("clearElementSelection")
        if retained_selection["result"].get("ref") != alpha_ref:
            raise RuntimeError("invalid move discarded the active selection state")
        client.require_ok("listSlides")
        require_cleared(client.take_events(), alpha_ref, "explicitClear", snapshot_generation)
        checkpoint("invalid_move_preserves_selection")
        client.require_ok(
            "selectElement", ref=alpha_ref, snapshotGeneration=snapshot_generation
        )
        wait_for_selected_event(client, alpha_ref, snapshot_generation)

        no_op_move, _ = client.require_ok("moveSlide", fromIndex=0, toIndex=0)
        if [slide["name"] for slide in no_op_move["result"]["slides"]] != [
            "Alpha", "Beta", "Gamma"
        ]:
            raise RuntimeError("no-op move changed slide order")
        client.require_ok("listSlides")
        no_op_events = client.take_events()
        if any(
            event.get("event") == "documentChanged"
            or (
                event.get("event") == "elementSelectionChanged"
                and event.get("data", {}).get("selected") is False
                and event.get("data", {}).get("ref") == alpha_ref
            )
            for event in no_op_events
        ):
            raise RuntimeError("no-op move invalidated document or selection state")
        no_op_selection, _ = client.require_ok("clearElementSelection")
        if no_op_selection["result"].get("ref") != alpha_ref:
            raise RuntimeError("no-op move discarded the active selection")
        client.require_ok("listSlides")
        require_cleared(client.take_events(), alpha_ref, "explicitClear", snapshot_generation)
        checkpoint("noop_move_preserves_state")
        client.require_ok(
            "selectElement", ref=alpha_ref, snapshotGeneration=snapshot_generation
        )
        wait_for_selected_event(client, alpha_ref, snapshot_generation)

        client.take_events()
        missing_source = source.with_name(f"{source.name}.luna-missing")
        if missing_source.exists():
            raise RuntimeError(f"failed-open fixture path unexpectedly exists: {missing_source}")
        failed_open, failed_open_payload = client.request(
            "open", path=str(missing_source)
        )
        if (
            failed_open.get("ok")
            or failed_open_payload
            or failed_open.get("error", {}).get("code") != "RUNTIME_ERROR"
        ):
            raise RuntimeError(f"worker unexpectedly opened the missing presentation: {failed_open}")
        preserved, _ = client.require_ok("listSlides")
        if [slide["name"] for slide in preserved["result"]["slides"]] != ["Alpha", "Beta", "Gamma"]:
            raise RuntimeError("failed open did not preserve the active presentation")
        failed_open_events = client.take_events()
        if any(
            event.get("event") == "elementSelectionChanged"
            and event.get("data", {}).get("selected") is False
            and event.get("data", {}).get("ref") == alpha_ref
            for event in failed_open_events
        ):
            raise RuntimeError("failed open cleared the active selection")
        checkpoint("failed_open_preserves_selection")

        cleared, payload = client.require_ok("clearElementSelection")
        if payload or cleared["result"].get("selected") is not False:
            raise RuntimeError(f"clearElementSelection returned invalid metadata: {cleared}")
        client.require_ok("listSlides")
        clear_events = client.take_events()
        require_cleared(clear_events, alpha_ref, "explicitClear", snapshot_generation)
        checkpoint("explicit_clear")

        client.require_ok(
            "selectElement", ref=alpha_ref, snapshotGeneration=snapshot_generation
        )
        wait_for_selected_event(client, alpha_ref, snapshot_generation)
        client.require_ok("open", path=str(source))
        client.require_ok("listSlides")
        reopened_events = client.take_events()
        require_cleared(reopened_events, alpha_ref, "documentChanged", snapshot_generation)
        checkpoint("successful_open_clears_old_selection")

        stale_same_path_legacy, stale_same_path_legacy_payload = client.request(
            "selectElement", ref=alpha_ref
        )
        if (
            stale_same_path_legacy.get("ok")
            or stale_same_path_legacy_payload
            or stale_same_path_legacy.get("error", {}).get("code")
            != "INVALID_ARGUMENT"
            or stale_same_path_legacy.get("error", {}).get("reason")
            != "snapshot_generation_required"
        ):
            raise RuntimeError(
                f"tokenless stale reference did not enter safe migration state: {stale_same_path_legacy}"
            )

        stale_same_path, stale_same_path_payload = client.request(
            "selectElement",
            ref=alpha_ref,
            snapshotGeneration=snapshot_generation,
        )
        stale_same_path_message = str(stale_same_path.get("error", {}).get("message", ""))
        if (
            stale_same_path.get("ok")
            or stale_same_path_payload
            or stale_same_path.get("error", {}).get("code") != "REVISION_CONFLICT"
            or stale_same_path.get("error", {}).get("reason") != "stale_snapshot_generation"
            or "generation" not in stale_same_path_message
        ):
            raise RuntimeError(f"old snapshot generation survived reopening the same path: {stale_same_path}")

        reopened_inventory, _ = client.require_ok("listElements", slideIndex=0)
        reopened_generation = reopened_inventory["result"]["snapshotGeneration"]
        if reopened_generation == snapshot_generation:
            raise RuntimeError("reopening the same path did not advance snapshot generation")
        reopened_alpha_ref = find_ref(reopened_inventory, "Alpha")
        if reopened_alpha_ref != alpha_ref:
            raise RuntimeError("reopening the same document changed its stable slide/object ref")
        alpha_ref = reopened_alpha_ref

        # Re-select, then mutate the document. The worker must invalidate both
        # the saved snapshot and its snapshot-scoped live selection.
        client.require_ok(
            "selectElement", ref=alpha_ref, snapshotGeneration=reopened_generation
        )
        wait_for_selected_event(client, alpha_ref, reopened_generation)
        checkpoint("reselection_geometry")

        moved, _ = client.require_ok("moveSlide", fromIndex=0, toIndex=2)
        if [slide["name"] for slide in moved["result"]["slides"]] != ["Beta", "Gamma", "Alpha"]:
            raise RuntimeError("selection mutation did not reorder the fixture")
        checkpoint("move")

        stale, stale_payload = client.request("selectElement", ref=alpha_ref)
        if (
            stale.get("ok")
            or stale_payload
            or stale.get("error", {}).get("code") != "REVISION_CONFLICT"
            or stale.get("error", {}).get("reason") != "snapshot_not_fresh"
        ):
            raise RuntimeError("worker accepted a snapshot ref after document mutation")
        stale_message = str(stale.get("error", {}).get("message", ""))
        if "stale" not in stale_message or "save" not in stale_message:
            raise RuntimeError(f"worker did not explain stale selection ref: {stale}")
        checkpoint("stale_rejected")

        mutation_events = client.take_events()
        require_cleared(mutation_events, alpha_ref, "documentMutation", reopened_generation)
        require_semantic_mutation_events(mutation_events, "moveSlide")
        checkpoint("mutation_events")

        client.require_ok("saveAs", path=str(output), format="odp")
        if not output.exists() or output.stat().st_size == 0:
            raise RuntimeError("selection test did not persist reordered output")
        checkpoint("save")

        stale_after_save, stale_after_save_payload = client.request(
            "selectElement",
            ref=alpha_ref,
            snapshotGeneration=reopened_generation,
        )
        stale_after_save_message = str(stale_after_save.get("error", {}).get("message", ""))
        if (
            stale_after_save.get("ok")
            or stale_after_save_payload
            or stale_after_save.get("error", {}).get("code") != "REVISION_CONFLICT"
            or stale_after_save.get("error", {}).get("reason") != "stale_snapshot_generation"
            or "generation" not in stale_after_save_message
        ):
            raise RuntimeError(f"old generation survived an incompatible mutation and save-as: {stale_after_save}")

        stale_after_save_legacy, stale_after_save_legacy_payload = client.request(
            "selectElement", ref=alpha_ref
        )
        if (
            stale_after_save_legacy.get("ok")
            or stale_after_save_legacy_payload
            or stale_after_save_legacy.get("error", {}).get("code")
            != "INVALID_ARGUMENT"
            or stale_after_save_legacy.get("error", {}).get("reason")
            != "snapshot_generation_required"
        ):
            raise RuntimeError(
                f"tokenless reference was not refused after save-as: {stale_after_save_legacy}"
            )

        refreshed, _ = client.require_ok("listElements", slideIndex=0)
        beta_ref = find_ref(refreshed, "Beta")
        refreshed_generation = refreshed["result"]["snapshotGeneration"]
        if refreshed_generation == reopened_generation:
            raise RuntimeError("save-as did not advance snapshot generation after slide mutation")
        checkpoint("refreshed_inventory")

        client.require_ok(
            "selectElement", ref=beta_ref, snapshotGeneration=refreshed_generation
        )
        wait_for_selected_event(client, beta_ref, refreshed_generation)
        checkpoint("refreshed_geometry")

        failed_save, failed_save_payload = client.request(
            "saveAs", path="", format="odp"
        )
        if (
            failed_save.get("ok")
            or failed_save_payload
            or failed_save.get("error", {}).get("code") != "INVALID_ARGUMENT"
        ):
            raise RuntimeError("worker accepted save-as with an empty destination")
        client.require_ok("listSlides")
        failed_save_events = client.take_events()
        if any(
            event.get("event") == "documentChanged"
            or (
                event.get("event") == "elementSelectionChanged"
                and event.get("data", {}).get("selected") is False
                and event.get("data", {}).get("ref") == beta_ref
            )
            for event in failed_save_events
        ):
            raise RuntimeError("failed save-as changed the document or cleared selection")
        retained_after_failed_save, _ = client.require_ok("clearElementSelection")
        if (
            retained_after_failed_save["result"].get("ref") != beta_ref
            or retained_after_failed_save["result"].get("snapshotGeneration")
            != refreshed_generation
        ):
            raise RuntimeError("failed save-as consumed active selection or generation")
        client.require_ok("listSlides")
        require_cleared(client.take_events(), beta_ref, "explicitClear", refreshed_generation)
        client.require_ok(
            "selectElement", ref=beta_ref, snapshotGeneration=refreshed_generation
        )
        wait_for_selected_event(client, beta_ref, refreshed_generation)

        saved_again, _ = client.require_ok(
            "saveAs", path=str(save_as_output), format="odp"
        )
        if not save_as_output.exists() or save_as_output.stat().st_size == 0:
            raise RuntimeError("save-as with an active selection did not persist output")
        saved_generation = saved_again["result"]["snapshotGeneration"]
        if saved_generation == refreshed_generation:
            raise RuntimeError("save-as did not advance the selected snapshot generation")
        stale_after_save_as, _ = client.request(
            "selectElement", ref=beta_ref, snapshotGeneration=refreshed_generation
        )
        if stale_after_save_as.get("ok") or "generation" not in str(
            stale_after_save_as.get("error", {}).get("message", "")
        ):
            raise RuntimeError(f"save-as left an old selected reference valid: {stale_after_save_as}")
        cleared_after_save, _ = client.require_ok("clearElementSelection")
        if (
            cleared_after_save["result"].get("selected") is not False
            or cleared_after_save["result"].get("snapshotGeneration") != saved_generation
        ):
            raise RuntimeError("save-as did not refresh the active selection generation")

        # Reach a one-slide persisted deck, then prove the worker's final-slide
        # guard refuses deletion without consuming the active snapshot selection.
        deleted_alpha, _ = client.require_ok("deleteSlide", slideIndex=2)
        if [slide["name"] for slide in deleted_alpha["result"]["slides"]] != ["Beta", "Gamma"]:
            raise RuntimeError("failed to remove the expected final slide before guard test")
        client.require_ok("saveAs", path=str(two_slide_output), format="odp")
        deleted_gamma, _ = client.require_ok("deleteSlide", slideIndex=1)
        if [slide["name"] for slide in deleted_gamma["result"]["slides"]] != ["Beta"]:
            raise RuntimeError("failed to retain the expected slide for final-slide guard test")
        client.require_ok("saveAs", path=str(one_slide_output), format="odp")
        if not one_slide_output.exists() or one_slide_output.stat().st_size == 0:
            raise RuntimeError("one-slide fixture was not persisted")
        final_inventory, _ = client.require_ok("listElements", slideIndex=0)
        beta_ref = find_ref(final_inventory, "Beta")
        final_generation = final_inventory["result"]["snapshotGeneration"]
        client.require_ok(
            "selectElement", ref=beta_ref, snapshotGeneration=final_generation
        )
        wait_for_selected_event(client, beta_ref, final_generation)

        final_delete, final_delete_payload = client.request("deleteSlide", slideIndex=0)
        if final_delete.get("ok") or final_delete_payload:
            raise RuntimeError("worker deleted the presentation's final slide")
        if (
            final_delete.get("error", {}).get("code") != "INVALID_ARGUMENT"
            or "at least one slide" not in str(final_delete.get("error", {}).get("message", ""))
        ):
            raise RuntimeError(f"worker did not explain final-slide protection: {final_delete}")
        preserved_final, _ = client.require_ok("listSlides")
        if [slide["name"] for slide in preserved_final["result"]["slides"]] != ["Beta"]:
            raise RuntimeError("failed final-slide deletion changed the presentation")
        final_delete_events = client.take_events()
        if any(
            event.get("event") == "documentChanged"
            or (
                event.get("event") == "elementSelectionChanged"
                and event.get("data", {}).get("selected") is False
                and event.get("data", {}).get("ref") == beta_ref
            )
            for event in final_delete_events
        ):
            raise RuntimeError("failed final-slide deletion changed the document or selection")
        retained_final_selection, _ = client.require_ok("clearElementSelection")
        if (
            retained_final_selection["result"].get("ref") != beta_ref
            or retained_final_selection["result"].get("snapshotGeneration") != final_generation
        ):
            raise RuntimeError("failed final-slide deletion consumed selection generation")

        rect = first_geometry["data"]["rectTwips"]
        print("worker_element_selection=passed")
        print("worker_element_geometry=passed")
        print("worker_selection_staleness=passed")
        print(f"worker_selection_ref={alpha_ref}")
        print(
            "worker_selection_rect_twips="
            f"{rect['x']},{rect['y']},{rect['width']},{rect['height']}"
        )
        checkpoint("complete")
        return 0
    finally:
        if client.process.poll() is None:
            client.close()


if __name__ == "__main__":
    raise SystemExit(main())
