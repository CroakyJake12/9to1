#!/usr/bin/env python3
"""Exercise the Present worker's framed HUI boundary end to end."""

from __future__ import annotations

import json
import struct
import subprocess
import sys
from pathlib import Path
from typing import Any, BinaryIO

MAX_FRAME = 64 * 1024 * 1024
ALLOWED_EVENTS = {
    "canvasInvalidated",
    "documentChanged",
    "slideChanged",
    "editingContextChanged",
    "elementSelectionChanged",
    "engineError",
}


def read_exact(stream: BinaryIO, size: int) -> bytes:
    data = bytearray()
    while len(data) < size:
        chunk = stream.read(size - len(data))
        if not chunk:
            raise RuntimeError(f"worker stream ended after {len(data)} of {size} bytes")
        data.extend(chunk)
    return bytes(data)


def send_frame(stream: BinaryIO, metadata: dict[str, Any]) -> None:
    encoded = json.dumps(metadata, separators=(",", ":")).encode("utf-8")
    stream.write(struct.pack("<II", len(encoded), 0))
    stream.write(encoded)
    stream.flush()


def read_frame(stream: BinaryIO) -> tuple[dict[str, Any], bytes]:
    metadata_size, payload_size = struct.unpack("<II", read_exact(stream, 8))
    if metadata_size <= 0 or metadata_size > 1024 * 1024:
        raise RuntimeError(f"invalid worker metadata size: {metadata_size}")
    if payload_size > MAX_FRAME:
        raise RuntimeError(f"invalid worker payload size: {payload_size}")
    metadata = json.loads(read_exact(stream, metadata_size))
    payload = read_exact(stream, payload_size)
    return metadata, payload


class WorkerClient:
    def __init__(self, worker: Path) -> None:
        self.process = subprocess.Popen(
            [str(worker)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        if self.process.stdin is None or self.process.stdout is None:
            raise RuntimeError("failed to create Present worker pipes")
        self.stdin = self.process.stdin
        self.stdout = self.process.stdout
        self.next_id = 1
        self.events: list[dict[str, Any]] = []

    def request(self, op: str, **fields: Any) -> tuple[dict[str, Any], bytes]:
        request_id = self.next_id
        self.next_id += 1
        send_frame(self.stdin, {"id": request_id, "op": op, **fields})

        while True:
            response, payload = read_frame(self.stdout)
            event_name = response.get("event")
            if event_name is not None:
                if payload:
                    raise RuntimeError("worker event unexpectedly contained binary data")
                if response.get("protocolVersion") != 1:
                    raise RuntimeError(f"event used an unexpected protocol version: {response}")
                if event_name not in ALLOWED_EVENTS:
                    raise RuntimeError(f"worker leaked an unknown/raw event type: {event_name}")
                if not isinstance(response.get("data"), dict):
                    raise RuntimeError(f"worker event data is not an object: {response}")
                self.events.append(response)
                continue

            if response.get("id") != request_id:
                raise RuntimeError(
                    f"response id mismatch: expected {request_id}, got {response.get('id')}"
                )
            return response, payload

    def require_ok(self, op: str, **fields: Any) -> tuple[dict[str, Any], bytes]:
        response, payload = self.request(op, **fields)
        if not response.get("ok"):
            raise RuntimeError(f"{op} failed: {response.get('error')}")
        if response.get("protocolVersion") != 1:
            raise RuntimeError(f"unexpected protocol version: {response}")
        return response, payload

    def take_events(self) -> list[dict[str, Any]]:
        events = self.events
        self.events = []
        return events

    def close(self) -> None:
        if self.process.poll() is None:
            try:
                self.require_ok("quit")
            finally:
                self.stdin.close()
        stderr = self.process.stderr.read().decode("utf-8", errors="replace") if self.process.stderr else ""
        code = self.process.wait(timeout=10)
        if code != 0:
            raise RuntimeError(f"worker exited with {code}: {stderr}")


def slides(response: dict[str, Any]) -> list[dict[str, Any]]:
    return response["result"]["slides"]


def slide_count(response: dict[str, Any]) -> int:
    return len(slides(response))


def slide_names(response: dict[str, Any]) -> list[str]:
    return [str(slide["name"]) for slide in slides(response)]


def event_names(events: list[dict[str, Any]]) -> set[str]:
    return {str(event["event"]) for event in events}


def require_semantic_mutation_events(events: list[dict[str, Any]], reason: str) -> None:
    names = event_names(events)
    if "documentChanged" not in names or "canvasInvalidated" not in names:
        raise RuntimeError(f"missing {reason} mutation events: {sorted(names)}")

    changed = [
        event for event in events
        if event["event"] == "documentChanged"
        and event["data"].get("reason") == reason
    ]
    repaints = [
        event for event in events
        if event["event"] == "canvasInvalidated"
        and event["data"].get("source") == "semantic"
        and event["data"].get("reason") == reason
        and event["data"].get("all") is True
    ]
    if not changed or not repaints:
        raise RuntimeError(f"{reason} did not emit the stable semantic mutation contract")


def element_text(result: dict[str, Any]) -> list[str]:
    output: list[str] = []
    for element in result["elements"]:
        output.extend(str(value) for value in element.get("text", []))
    return output


def require_element_inventory(
    response: dict[str, Any],
    *,
    slide_index: int,
    expected_text: str,
) -> None:
    result = response["result"]
    if result.get("slideIndex") != slide_index or result.get("snapshotFresh") is not True:
        raise RuntimeError(f"element inventory has invalid snapshot metadata: {result}")
    if result.get("referenceStability") != "snapshot-only":
        raise RuntimeError(f"element inventory overstates identity stability: {result}")
    elements = result.get("elements")
    if not isinstance(elements, list) or not elements:
        raise RuntimeError(f"element inventory is empty: {result}")
    for element in elements:
        if element.get("referenceStability") != "snapshot-only":
            raise RuntimeError(f"element overstates identity stability: {element}")
        expected_ref = f"slide:{element['slideIndex']}/object:{element['objectIndex']}"
        if element.get("ref") != expected_ref:
            raise RuntimeError(f"element ref is not the normalized CakeOS form: {element}")
    if expected_text not in element_text(result):
        raise RuntimeError(
            f"element inventory did not expose {expected_text!r}: {element_text(result)}"
        )


def main() -> int:
    if len(sys.argv) != 5:
        print(
            "usage: worker_protocol_smoke.py WORKER INPUT.odp OUTPUT.odp REORDER_INPUT.odp",
            file=sys.stderr,
        )
        return 2

    worker = Path(sys.argv[1]).resolve()
    source = Path(sys.argv[2]).resolve()
    output = Path(sys.argv[3]).resolve()
    reorder_source = Path(sys.argv[4]).resolve()
    reorder_output = output.with_name(f"{output.stem}-reordered.odp")
    output.unlink(missing_ok=True)
    reorder_output.unlink(missing_ok=True)

    client = WorkerClient(worker)
    total_events = 0
    try:
        hello, payload = client.require_ok("hello")
        if payload:
            raise RuntimeError("hello unexpectedly returned binary data")
        result = hello["result"]
        required_capabilities = {
            "open",
            "listSlides",
            "listElements",
            "slideExtent",
            "renderSlide",
            "addSlideAfter",
            "duplicateSlide",
            "deleteSlide",
            "moveSlide",
            "undo",
            "redo",
            "saveAs",
        }
        missing = required_capabilities.difference(result["capabilities"])
        if missing:
            raise RuntimeError(f"worker is missing capabilities: {sorted(missing)}")
        if result["pixelTransport"] != "inline-binary-v1":
            raise RuntimeError(f"unexpected pixel transport: {result['pixelTransport']}")
        if result["eventTransport"] != "framed-json-v1":
            raise RuntimeError(f"unexpected event transport: {result['eventTransport']}")
        if set(result["events"]) != ALLOWED_EVENTS:
            raise RuntimeError(f"unexpected event vocabulary: {result['events']}")

        opened, _ = client.require_ok("open", path=str(source))
        if slide_count(opened) != 1:
            raise RuntimeError("worker did not open the one-slide fixture")

        extent, _ = client.require_ok("slideExtent", slideIndex=0)
        width = extent["result"]["widthTwips"]
        height = extent["result"]["heightTwips"]
        if width < 1000 or height < 1000:
            raise RuntimeError(f"worker returned unrealistic slide extent {width}x{height}")

        rendered, pixels = client.require_ok(
            "renderSlide", slideIndex=0, pixelWidth=320, pixelHeight=180
        )
        render_result = rendered["result"]
        if render_result["pixelWidth"] != 320 or render_result["pixelHeight"] != 180:
            raise RuntimeError(f"unexpected render dimensions: {render_result}")
        if render_result["pixelFormat"] not in {"RGBA", "BGRA"}:
            raise RuntimeError(f"unexpected pixel format: {render_result}")
        if len(pixels) != 320 * 180 * 4:
            raise RuntimeError(f"unexpected binary render size: {len(pixels)}")
        if len(set(pixels)) < 2:
            raise RuntimeError("worker render payload is uniform")

        rejected, rejected_payload = client.request(
            "renderSlide", slideIndex=0, pixelWidth=5000, pixelHeight=1
        )
        if rejected.get("ok") or rejected_payload:
            raise RuntimeError("worker did not reject an oversized render request")

        # Clear typed events emitted by open/render before testing the
        # deterministic semantic-mutation contract.
        client.require_ok("listSlides")
        total_events += len(client.take_events())

        added, _ = client.require_ok("addSlideAfter", slideIndex=0)
        if slide_count(added) != 2:
            raise RuntimeError("worker addSlideAfter did not create a slide")

        # Events are emitted after their originating response. The next request
        # proves a HUI client can demultiplex queued events before its response.
        listed, _ = client.require_ok("listSlides")
        if slide_count(listed) != 2:
            raise RuntimeError("worker listSlides changed state unexpectedly")
        mutation_events = client.take_events()
        total_events += len(mutation_events)
        require_semantic_mutation_events(mutation_events, "addSlideAfter")

        undone, _ = client.require_ok("undo")
        if slide_count(undone) != 1:
            raise RuntimeError("worker undo did not restore one slide")

        redone, _ = client.require_ok("redo")
        if slide_count(redone) != 2:
            raise RuntimeError("worker redo did not restore the added slide")

        duplicated, _ = client.require_ok("duplicateSlide", slideIndex=0)
        if slide_count(duplicated) != 3:
            raise RuntimeError("worker duplicateSlide did not create a slide")

        deleted, _ = client.require_ok("deleteSlide", slideIndex=2)
        if slide_count(deleted) != 2:
            raise RuntimeError("worker deleteSlide did not remove a slide")

        client.require_ok("saveAs", path=str(output), format="odp")
        if not output.exists() or output.stat().st_size == 0:
            raise RuntimeError("worker saveAs did not create the output presentation")

        client.require_ok("close")
        reopened, _ = client.require_ok("open", path=str(output))
        if slide_count(reopened) != 2:
            raise RuntimeError("worker output did not persist two slides")

        # Switch to a three-slide fixture with stable names so worker-level
        # reordering proves identity/order, not just the slide count.
        client.require_ok("close")
        reorder_opened, _ = client.require_ok("open", path=str(reorder_source))
        if slide_names(reorder_opened) != ["Alpha", "Beta", "Gamma"]:
            raise RuntimeError(f"unexpected reorder fixture: {slide_names(reorder_opened)}")

        inventory, inventory_payload = client.require_ok("listElements", slideIndex=0)
        if inventory_payload:
            raise RuntimeError("listElements unexpectedly returned binary data")
        require_element_inventory(inventory, slide_index=0, expected_text="Alpha")

        client.require_ok("listSlides")
        total_events += len(client.take_events())

        moved_down, _ = client.require_ok("moveSlide", fromIndex=0, toIndex=2)
        if slide_names(moved_down) != ["Beta", "Gamma", "Alpha"]:
            raise RuntimeError(f"worker downward reorder failed: {slide_names(moved_down)}")

        # The next element query both pumps the move events and proves HUI can
        # never unknowingly consume the old on-disk inventory after an edit.
        stale_inventory, stale_payload = client.request("listElements", slideIndex=0)
        if stale_inventory.get("ok") or stale_payload:
            raise RuntimeError("worker returned an element inventory after unsaved mutation")
        stale_message = str(stale_inventory.get("error", {}).get("message", ""))
        if "stale" not in stale_message or "save" not in stale_message:
            raise RuntimeError(f"worker did not explain stale element inventory: {stale_inventory}")
        move_events = client.take_events()
        total_events += len(move_events)
        require_semantic_mutation_events(move_events, "moveSlide")

        moved_up, _ = client.require_ok("moveSlide", fromIndex=2, toIndex=0)
        if slide_names(moved_up) != ["Alpha", "Beta", "Gamma"]:
            raise RuntimeError(f"worker upward reorder failed: {slide_names(moved_up)}")

        moved_final, _ = client.require_ok("moveSlide", fromIndex=0, toIndex=2)
        if slide_names(moved_final) != ["Beta", "Gamma", "Alpha"]:
            raise RuntimeError(f"worker final reorder failed: {slide_names(moved_final)}")

        client.require_ok("saveAs", path=str(reorder_output), format="odp")
        if not reorder_output.exists() or reorder_output.stat().st_size == 0:
            raise RuntimeError("worker reorder save did not create an output presentation")

        client.require_ok("close")
        reorder_reopened, _ = client.require_ok("open", path=str(reorder_output))
        if slide_names(reorder_reopened) != ["Beta", "Gamma", "Alpha"]:
            raise RuntimeError(
                f"worker reorder did not persist: {slide_names(reorder_reopened)}"
            )

        persisted_inventory, persisted_payload = client.require_ok("listElements", slideIndex=0)
        if persisted_payload:
            raise RuntimeError("persisted listElements unexpectedly returned binary data")
        require_element_inventory(persisted_inventory, slide_index=0, expected_text="Beta")

        # Pump any events left after the final open and confirm none escaped the
        # stable worker event vocabulary.
        client.require_ok("listSlides")
        final_events = client.take_events()
        total_events += len(final_events)
        if not all(event["event"] in ALLOWED_EVENTS for event in final_events):
            raise RuntimeError("raw LibreOffice callback escaped the worker boundary")

        print("worker_protocol=passed")
        print("worker_events=passed")
        print("worker_element_inventory=passed")
        print("worker_element_snapshot_staleness=passed")
        print("worker_slide_reorder=passed")
        print("worker_slide_order=Beta,Gamma,Alpha")
        print(f"worker_event_count={total_events}")
        print(f"worker_extent_twips={width}x{height}")
        print(f"worker_render_bytes={len(pixels)}")
        print("worker_saved_slides=2")
        return 0
    finally:
        client.close()


if __name__ == "__main__":
    raise SystemExit(main())
