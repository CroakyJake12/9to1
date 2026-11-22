# Impress engine integration for Present

Present’s document and UI remain under 9to1 ownership. The engine worker provides bounded LibreOffice Impress operations, including the documented slide API and typed worker events. Keep worker protocol identity separate from a native slide or document ID.

Verify the worker and its libraries on the actual target platform. Check opening, edit application, output, errors, handle cleanup and retry behavior. Run the native editor route separately to show that its selection and document state survive the engine interaction.

The following protocol and build details identify the supported foundation. Their historical execution evidence stays limited to the recorded baseline.

## Semantic slide API

HUI and generative UI code should use CakeOS operations, not LibreOffice command strings. The current semantic layer exposes:

- `addSlideAfter(slideIndex)`;
- `duplicateSlide(slideIndex)`;
- `deleteSlide(slideIndex)`;
- `undo()`;
- `redo()`.

The legacy/donor Present model contains a broader semantic vocabulary for title, text, image, shape, media, notes, and element removal. Those operations should only be added here once each one has a stable LibreOffice object-selection/identity strategy and an end-to-end runtime test.

## Worker protocol v1

`cakeos-present-worker` communicates over stdin/stdout. Each frame is:

```text
uint32 little-endian JSON byte length
uint32 little-endian binary payload byte length
UTF-8 JSON metadata
optional binary payload
```

Limits:

- JSON metadata: 1 MiB maximum;
- binary payload: 64 MiB maximum;
- render width/height: 1..4096 pixels;
- render pixel budget: 16 Mi pixels;
- request-side binary payloads: not supported in v1.

Every request carries an `id` and `op`. Responses repeat the request `id`, include `ok`, and include `protocolVersion`. Render responses carry raw RGBA or BGRA bytes after the metadata frame.

Current protocol operations:

- `hello`
- `open`
- `close`
- `listSlides`
- `slideExtent`
- `renderSlide`
- `addSlideAfter`
- `duplicateSlide`
- `deleteSlide`
- `undo`
- `redo`
- `saveAs`
- `quit`

There is intentionally no arbitrary UNO-command operation in the HUI protocol.

## Typed worker events

Protocol v1 exposes only this CakeOS-owned event vocabulary:

- `canvasInvalidated`
- `documentChanged`
- `slideChanged`
- `editingContextChanged`
- `engineError`

Semantic mutations always emit `documentChanged` plus a conservative full-slide `canvasInvalidated` event. Selected LibreOffice callbacks are additionally translated into typed events, including rectangle tile invalidations where available. Raw LibreOffice callback IDs, theme payloads, command-state strings, and UNO result payloads are not exposed.

The event queue is bounded to 256 entries. If upstream event volume exceeds the bound, the worker drops excess detail and emits a full repaint invalidation rather than allowing unbounded memory growth.

Responses and events share the framed stdout stream, so clients must demultiplex frames by request `id` versus `event`.

## Linux dependencies

Runtime:

- `libreoffice-impress-nogui` and its Draw/core/UNO dependencies.

Build/test:

- `libreofficekit-dev`;
- `nlohmann-json3-dev` for the worker's protocol JSON handling;
- CMake and a C++20 compiler.

No GTK embedding is used.

## Build

```sh
cmake -S apps/present-engine -B build/present-engine -DCMAKE_BUILD_TYPE=Release
cmake --build build/present-engine --parallel
ctest --test-dir build/present-engine --output-on-failure
```

The CI build enables `-Wall -Wextra -Wpedantic -Werror` for the engine, worker, and smoke-test executables.

## Version strategy

Tiled LibreOfficeKit APIs require `LOK_USE_UNSTABLE_API`. That dependency stops at the worker/engine boundary; HUI must never consume the LOK ABI directly. Newer slideshow-layer APIs should likewise be added behind this boundary and version-gated rather than raising HUI's dependency on LibreOffice internals.

The next UI-facing integration should consume the worker protocol, not instantiate `PresentEngine` inside HUI.

[Public interface](include) · [Engine checks](tests) · [Present host](../README.md)
