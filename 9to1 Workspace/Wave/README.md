# HavenOS Wave

This is the bounded first standalone Wave app surface, with a versioned native project/timeline foundation.

## Functional journey

1. Launch Wave with a local `.wav` path.
2. Wave validates the RIFF/WAVE structure and accepts only 16-bit PCM in this first slice.
3. It reads real audio frames and produces 512 normalized waveform peaks using the same bounded sampling shape as the existing Imagine waveform reference.
4. The standalone surface prints duration, sample rate, channel count, and a compact waveform preview.
5. `--trim <input.wav> <start-seconds> <end-seconds> <new-output.wav>` exports the selected PCM frames to a new WAV file.
6. Missing, unsupported, or corrupt input fails closed with no fabricated waveform. Trim uses a half-open time range and never overwrites the source or an existing output file.
7. `--project-create` creates a persistent `.waveproject.json` with stable project/track IDs; `--project-import` adds a WAV as a non-destructive clip at a timeline time and advances the project revision.
8. `--project-export` mixes project clips into a new PCM16 WAV, preserving timeline offsets and leaving project/source files unchanged. This bounded exporter requires source sample rate/channel layout to match the project and rejects RIFF outputs above the format size limit.
9. Project save writes a temporary sibling file and replaces the project only after successful serialization. Open validates the schema and identities and rejects unknown schema versions rather than guessing.

Trim writes a canonical PCM `fmt` and `data` WAV file; unrelated RIFF metadata chunks are not copied. The native project stores stable `ProjectID`, `TrackID`, `ClipID` and app-local `SourceReferenceID`, source-frame range, timeline-frame placement, project audio configuration and revision. `SourceReferenceID` is a Wave-local reference only; it is not a Files `FileID` or a claim of Files identity. This standalone slice records the absolute source path plus a SHA-256 content fingerprint; export fails explicitly if the path is missing or the bytes/format no longer match. There is no relink workflow yet. Files-backed `FileID` resolution and shared media/audio primitives remain required integration work. The slice remains app-local under `9to1 Workspace/Wave` and does not change shared HUI, shell routing, platform services, or the legacy Imagine journey.

## Focused validation

```text
dotnet build "9to1 Workspace/Wave/HavenOS.Wave.csproj"
dotnet run --project "9to1 Workspace/Wave/HavenOS.Wave.csproj" -- --self-test
dotnet run --project "9to1 Workspace/Wave/HavenOS.Wave.csproj" -- --trim "input.wav" 0.25 0.75 "trimmed.wav"
dotnet run --project "9to1 Workspace/Wave/HavenOS.Wave.csproj" -- --project-create "session.waveproject.json" "Voice"
dotnet run --project "9to1 Workspace/Wave/HavenOS.Wave.csproj" -- --project-import "session.waveproject.json" "input.wav" 2.5
dotnet run --project "9to1 Workspace/Wave/HavenOS.Wave.csproj" -- --project-export "session.waveproject.json" "mix.wav"
```

The self-test writes a temporary one-second PCM tone, validates real bounded peaks and metadata, verifies a frame-exact half-second trim and source/output protection, confirms corrupt input and unknown project schema versions are rejected, then creates/imports/saves/reopens/exports a project. It verifies timeline silence and exact PCM frames in the mix, unchanged project revision/source bytes, and explicit failure after the imported source is removed or altered.
