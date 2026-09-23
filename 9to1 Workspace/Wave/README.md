# HavenOS Wave

This is the bounded first standalone Wave app surface.

## Functional journey

1. Launch Wave with a local `.wav` path.
2. Wave validates the RIFF/WAVE structure and accepts only 16-bit PCM in this first slice.
3. It reads real audio frames and produces 512 normalized waveform peaks using the same bounded sampling shape as the existing Imagine waveform reference.
4. The standalone surface prints duration, sample rate, channel count, and a compact waveform preview.
5. `--trim <input.wav> <start-seconds> <end-seconds> <new-output.wav>` exports the selected PCM frames to a new WAV file.
6. Missing, unsupported, or corrupt input fails closed with no fabricated waveform. Trim uses a half-open time range and never overwrites the source or an existing output file.

Trim writes a canonical PCM `fmt` and `data` WAV file; unrelated RIFF metadata chunks are not copied. The slice remains app-local under `9to1 Workspace/Wave` and does not change shared HUI, shell routing, platform services, or the legacy Imagine journey.

## Focused validation

```text
dotnet build "9to1 Workspace/Wave/HavenOS.Wave.csproj"
dotnet run --project "9to1 Workspace/Wave/HavenOS.Wave.csproj" -- --self-test
dotnet run --project "9to1 Workspace/Wave/HavenOS.Wave.csproj" -- --trim "input.wav" 0.25 0.75 "trimmed.wav"
```

The self-test writes a temporary one-second PCM tone, validates real bounded peaks and metadata, verifies a frame-exact half-second trim and source/output protection, then confirms corrupt input is rejected.
