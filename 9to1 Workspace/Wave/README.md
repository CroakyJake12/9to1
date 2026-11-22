# Wave audio projects and native editing

Wave belongs to Create. The native workspace presents the existing multitrack project with clip, track, mixer and annotation controls. Keep all edits on the current project identity and sample-frame timeline. The project publisher, active Home attachment and Files services supply the original durable and media boundaries.

A clip move, trim, split or join should produce the same edit through keyboard and pointer. Check pending fields when changing selection, seeking or undoing. On close, preserve the user’s choice to save, discard or continue editing. Verify decoding, playback and export separately for the actual adapter and media source.

The project component and CUI sources identify the real entry. Record any missing platform or project-publisher dependency instead of substituting example media or fixture readiness.

```text
dotnet build "9to1 Workspace/Wave/HavenOS.Wave.csproj"
dotnet "<actual owning output>/HavenOS.Wave.dll" --native-workflow-test
```

## Timeline commands

The schema-6 project represents track and clip data. The workspace routes move, trim, split, join, duplicate, lift or ripple delete and gain/fade edits through the project operation layer. Marker and region navigation, sample-frame playhead values and mixer edits use the same timeline. Preserve pending fields or require an explicit apply/discard decision before a selection or undo can invalidate them.

[Wave component](HavenOS.Wave.csproj) · [Workspace UI](UI) · [Wave interactions](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
