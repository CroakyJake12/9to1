# Motion editing candidate

This app owns native structured timeline and caption state. Source media stays in Files. `MotionProjectStore` edits integral frames on the sequence's rational clock and saves through its existing revision compare-and-swap. `MotionEditSession` persists each accepted edit and persists undo/redo as new revisions; undo history belongs to the current editing session.

`MotionCuiWorkspace` is the common typed action dispatcher for the authored `UI/MotionWorkspace.cui` surface. The graphical timeline is a specialised CUI Object; keyboard clip selectors and numeric frame fields address the same records. The surface includes track controls, insert/overwrite, source slip/trim/move, split/duplicate, clip lift/extract, all-track range lift/extract, multiple caption tracks, cue editing/split/merge and plain-text SRT/WebVTT interchange. Importing a second subtitle document adds a separate native caption track. Extraction remaps captions with the same frame deletion as visual tracks.

SRT/WebVTT parsing currently supports the plain-text subset; unsupported styling/settings are rejected rather than discarded. Frame and millisecond conversion uses exact integer arithmetic. WebVTT output preserves native cue GUIDs. SRT has no equivalent native identity field. Cue timing/content remains editable after reopen.

## Host composition

The original Home/Desktop owner creates a `MotionEditSession` for a user-authorised project, then constructs `MotionCuiWorkspace` with its existing action-admission callback. Optional callbacks select media through Files and supply an authorised render destination. `MotionNativeSurface.CreateScene(workspace, originalReadiness)` returns the actual CUI scene for `CuiSceneHost`/`CuiNativeHost`. The host owns scene retirement and must dispose the workspace when closing it. No callback grants itself permission.

`MotionMediaService` consumes the existing `IMediaAssetSourceResolver` (preferring retained-revision resolution), `IMediaEngine` and optional `IMediaTimelineRenderer`. Resolved leases must match the existing canonical asset, FileID and retained revision. Source playback holds its lease until disposal. Relinking preserves the existing AssetId and every clip identity. A render snapshot uses original assets, holds all source leases, and pins ProjectID/revision/SequenceID. The current shared render contract cannot represent captions; sequence rendering rejects projects with cues rather than silently omitting them. Sidecar subtitle text is available independently.

The standalone `--native` entry displays Home connection/install guidance because this app does not yet have an original authenticated Home startup attachment. It never constructs a replacement runtime. The primary coordinator still needs to wire the scene into the canonical installed application route and provide real Files pick/export callbacks. No installed-launch readiness is claimed.

## Bounded checks

`--self-test` runs the inherited project/inspection checks plus rational caption interchange and native edit-session/CUI-dispatch workflows. `--ui-self-test` mounts the actual authored CUI with the timeline Object under Avalonia headless/Skia and saves a desktop PNG. All test fixtures are retained during this implementation lane.

The next gaps include composed sequence preview, native titles/transforms/effects/keyframes/graphs, source-length probing, proxies, shared audio-track integration, full subtitle styling/burn-in, persistent render-queue UI, and ordinary installed Home/Windows/web route composition. Source playback is not a composed sequence viewer. These bounded checks do not establish complete Motion acceptance or packaging.

Authority consulted: the checkpoint's canonical DEVELOPMENT-SPECIFICATION Motion section; POST-RELEASE-UPDATES; shared AGENTS, HAVEN_UI_RULES, architecture/platform/security/validation rules; state-and-persistence and building-ui documentation. Existing CUI resources and runtime are reused; no shared theme or host source was modified.

## First checkpoint evidence (7 October 2026)

- Actual owning project Debug build: `build-06.log`, 0 warnings/errors. The vendored source graph requires `-p:AvaloniaBuildTasksLocation=/workspace/c50-app-builds/motion/bin/Avalonia.Build.Tasks/debug/Avalonia.Build.Tasks.dll` when using the isolated artifacts path. This points at the actual freshly built source task assembly; no source filtering or replacement dependencies were used.
- `--self-test`: natural exit 0; inherited project checks, caption interchange and edit/undo/CUI persistence checks passed. Fixture project `/tmp/motion-editing-evidence-rzQDzK/edit.motion.json` retained.
- `--ui-self-test`: second run natural exit 0 with 0 CUI diagnostics and visible native controls/timeline. Screenshot `/workspace/c50-lane-evidence/motion/motion-desktop-02-controls.png` retained. The first, incomplete test bootstrap screenshot is retained separately as `motion-desktop-01-untemplated.png`.
- Observed remaining visual issue: disabled controls render with poor contrast/white artifacts under this shared headless theme. Enabled inputs and controls render. This is not whole-app visual acceptance; shared theme source was not changed.
- Actual Windows GUI, installed Home route, real Files-backed source playback, and end-to-end shared render: UNRUN. Separate shared FFmpeg renderer implementation was delivered as a proposal outside this app root, not silently installed into the shared source.

## Follow-up changes

Native documents now declare schema 2. Loading schema 1 migrates only the in-memory representation (visible/unlocked visual tracks and no caption tracks by default), retaining all existing identity/timing/revision fields. The first save of an old document retains an exact unique `.schema1.*.backup` before atomic replacement. Future schemas are rejected. Reload saved project recovers from a detected compare-and-swap conflict and clears obsolete session undo/redo state.

Additional typed edits include adjacent-cut roll, clip slide between touching neighbors, inward ripple trim of either edge across all tracks/captions, and cross-track move with stable clip/source identity. Lock state is checked on both sides of a track transfer. Source handle duration beyond the selected range still requires future media-duration probing; these operations preserve editable references and the rendering backend must reject unavailable source frames. The existing Slip operation has the same source-length limitation.

The workspace now uses visible frame-field labels and ordinary source/track/range names, and exposes user-facing preview/export availability. It no longer places implementation architecture in the editing instructions.

Follow-up validation: owning Debug `build-07.log` passed with 0 warnings/errors. `self-test-02.log` passed with natural exit 0, including v1 migration/backup/future-schema/CAS recovery, roll/slide/ripple trim/cross-track preservation, existing caption and project workflows, transport draft retention and caption reload. `ui-self-test-03.log` rendered the actual CUI with 0 diagnostics; screenshot `motion-desktop-03-schema2.png` shows visible frame labels and user-facing capability text. The shared disabled-control contrast issue remains visible. Windows and installed-host/media integration remain UNRUN.
