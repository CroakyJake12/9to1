# Wave — multitrack audio production — Wave audio timeline (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2306 · v2 lines 13445–13445

### UX27-OTHER-008 — Wave audio timeline

### V2-B2307 · v2 lines 13447–13447

**B/D.** Tracks, clips, waveform, ruler, playhead, selection and transport share actual timeline state. Clicking the ruler seeks; dragging a clip moves it with snap feedback; trimming changes its source range rather than resampling silently. Play/pause/stop and loop are visibly distinct, and meters reflect real signal rather than decorative animation.

### V2-B2308 · v2 lines 13449–13449

Edits remain undoable, with clear destructive/non-destructive boundaries. Export uses explicit range/format settings and real rendering status. Missing media retains a relinkable reference. Background waveform generation is lower priority than responsive transport/input and cannot silently omit audible material.
