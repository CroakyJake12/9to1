# CakeOS Present engine foundation

This directory contains the first working foundation for migrating Present onto LibreOffice Impress while keeping HUI as the only application UI.

## Architecture boundary

```text
HUI / Present / GenUI
        |
        | CakeOS Present protocol v1
        | framed JSON metadata + raw pixel payloads
        v
cakeos-present-worker
        |
        | CakeOS C++ API
        v
cakeos::present::PresentEngine
        |
        | LibreOfficeKit / semantic UNO commands
        v
LibreOffice Impress + Draw no-GUI runtime
```

HUI does not link to LibreOfficeKit and does not embed LibreOffice, GTK, or GNOME UI. The worker process is the compatibility and crash-isolation seam. LibreOffice objects, UNO types, callback IDs, and command names do not cross the worker protocol.

The engine uses a private LibreOffice user profile by default so Present does not share the user's normal desktop LibreOffice profile.

## Implemented and runtime-proven on the CakeOS Ubuntu baseline

The Ubuntu 26.04 CI gate currently proves all of the following against a real no-GUI LibreOffice runtime:

- initialize LibreOfficeKit;
- open an ODP presentation and reject non-presentation document types in the engine;
- enumerate and select slides;
- read exact per-slide geometry from Impress page metadata;
- render slides into RGBA/BGRA four-channel pixel buffers;
- add a slide;
- duplicate a slide;
- delete a slide while preventing deletion of the final slide;
- undo and redo slide operations;
- save and reopen ODP;
- export to PPTX, reopen the generated PPTX, and render it;
- run the engine out-of-process through `cakeos-present-worker`;
- negotiate worker capabilities and protocol version;
- transfer render pixels as raw binary rather than base64;
- reject oversized render requests while keeping the worker usable;
- persist semantic edits through the worker;
- emit a stable typed event vocabulary for document/repaint state.

The PPTX gate is an engine-generated round-trip proof, not a claim of general Microsoft PowerPoint fidelity.

## Deliberately not claimed yet

- a finished HUI Present visual surface;
- text, shape, image, media, or notes semantic editing through the worker;
- stable object identity for individual slide elements;
- drag/resize/selection parity through HUI;
- presenter-mode transitions or object animations;
- embedded audio/video playback;
- a complete accessibility semantic tree;
- arbitrary PPT/PPTX fidelity parity with Microsoft PowerPoint;
- production sandbox policy, service supervision, or crash restart integration;
- shared-memory render transport;
- runtime proof on the approved CakeOS VM itself.

These remain explicit later acceptance gates.

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

## Automated evidence

The workflow deliberately separates compatibility from runtime proof.

### Ubuntu 24.04 compatibility gate

Proves:

- configure;
- strict compilation against the older LibreOfficeKit 24.2 API family;
- contract tests.

It does **not** claim reliable headless slide geometry on LibreOffice 24.2. That runtime can return insufficient page metadata in this path.

### Ubuntu 26.04 runtime gate

This is the current CakeOS baseline acceptance runner. A green run proves the full smoke sequence using the distro no-GUI LibreOffice stack, including geometry, render, semantic slide lifecycle, ODP persistence, engine-generated PPTX round trip, and the out-of-process worker protocol.

One observed green runtime used:

- Ubuntu 26.04.1 LTS;
- LibreOffice 26.2.5.2;
- GCC 15.2;
- `libreoffice-core-nogui` / `libreoffice-impress-nogui` 26.2.5.2.

The deterministic fixture produced an exact `11906 x 16838` twip slide extent and a non-uniform 1280x720 render. The worker also returned a 320x180x4 binary render payload and persisted a two-slide edited presentation.

## Evidence labels

- **Inspected**: source/API/package metadata reviewed.
- **Implemented**: code exists on the migration branch.
- **Built**: strict compilation succeeded in the named environment.
- **Tested**: automated tests executed successfully.
- **Runtime-proven**: the operation executed through a real LibreOfficeKit/Impress runtime.
- **VM-proven**: reserved for direct execution on the approved CakeOS VM; this has not yet been claimed for this slice.

## Version strategy

Tiled LibreOfficeKit APIs require `LOK_USE_UNSTABLE_API`. That dependency stops at the worker/engine boundary; HUI must never consume the LOK ABI directly. Newer slideshow-layer APIs should likewise be added behind this boundary and version-gated rather than raising HUI's dependency on LibreOffice internals.

The next UI-facing integration should consume the worker protocol, not instantiate `PresentEngine` inside HUI.
