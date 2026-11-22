# Motion sequence and caption editing

Motion belongs to Create. The project store tracks timeline and caption edits using integral frames on the sequence’s rational clock. Source media remains in Files. Preserve media references, project identity, frame positions and the editor’s undo history.

Trace a user action through the native timeline, then through the project edit and durable store. Verify that a save/reopen retains track order, selections where specified, caption timing and original media references. Record a missing codec, source or provider as a distinct state.

The retained host and test notes describe bounded integrations. Validate playback and rendering separately from the correctness of stored timeline edits.

## Host composition

The original Home/Desktop owner creates a `MotionEditSession` for a user-authorised project, then constructs `MotionCuiWorkspace` with its existing action-admission callback. Optional callbacks select media through Files and supply an authorised render destination. `MotionNativeSurface.CreateScene(workspace, originalReadiness)` returns the actual CUI scene for `CuiSceneHost`/`CuiNativeHost`. The host owns scene retirement and must dispose the workspace when closing it. No callback grants itself permission.

`MotionMediaService` consumes the existing `IMediaAssetSourceResolver` (preferring retained-revision resolution), `IMediaEngine` and optional `IMediaTimelineRenderer`. Resolved leases must match the existing canonical asset, FileID and retained revision. Source playback holds its lease until disposal. Relinking preserves the existing AssetId and every clip identity. A render snapshot uses original assets, holds all source leases, and pins ProjectID/revision/SequenceID. The current shared render contract cannot represent captions; sequence rendering rejects projects with cues rather than silently omitting them. Sidecar subtitle text is available independently.

The standalone `--native` entry displays Home connection/install guidance because this app does not yet have an original authenticated Home startup attachment. It never constructs a replacement runtime. The primary coordinator still needs to wire the scene into the canonical installed application route and provide real Files pick/export callbacks. No installed-launch readiness is claimed.

## Bounded checks

`--self-test` runs the inherited project/inspection checks plus rational caption interchange and native edit-session/CUI-dispatch workflows. `--ui-self-test` mounts the actual authored CUI with the timeline Object under Avalonia headless/Skia and saves a desktop PNG. All test fixtures are retained during this implementation lane.

The next gaps include composed sequence preview, native titles/transforms/effects/keyframes/graphs, source-length probing, proxies, shared audio-track integration, full subtitle styling/burn-in, persistent render-queue UI, and ordinary installed Home/Windows/web route composition. Source playback is not a composed sequence viewer. These bounded checks do not establish complete Motion acceptance or packaging.


## Media and edit-session ownership

`MotionCuiWorkspace` forwards UI actions to the original `MotionEditSession`. The media service receives the existing asset resolver and media engine; a retained revision is preferable when available. A standalone launch without an authenticated Home attachment should present setup guidance and preserve the project. Studio/export and decoding capability require actual observation on the target.

[Timeline UI](UI/MotionWorkspace.cui) · [Project](HavenOS.Motion.csproj) · [Motion requirements](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
