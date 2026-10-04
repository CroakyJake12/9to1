# Motion capability provenance

Authoritative HavenOS base: `CroakyJake12/HavenAI` branch `havenos-main`, base commit `7b2acae6175e5c380a3812b531b90ca82dbf85c3`.

## Capability boundary

The assigned baseline exposes the stable `motion` application route through the generic shell route contract, but the repository capability scan found no dedicated Motion editing, timeline, rendering, export, or Motion persistence engine that can be reused truthfully.

This standalone slice therefore adds only an independent `HavenOS Apps/Motion` capability surface. It preserves the route identity `motion` and fails closed: all engine-dependent capabilities remain explicitly unavailable.

The surface supports read-only header inspection for MP4, M4V, MOV, 3GP, WebM, Matroska, and AVI sources. It reports the recognized container and file size; it does not decode or play the media.

The slice does **not** claim or simulate timeline editing, keyframes, rendering, export, media encoding, or persistence. Those capabilities must remain disabled until a real implementation is present and validated.

## Donor / licence status

At the time this first slice was implemented, no external donor was present. GStreamer and GES actual source is now materialised under `Source/GStreamer/` and `Source/GES/` at immutable revisions; see `Source/DONOR-PROVENANCE.md` and `eng/donor-sources.json`. The standalone GES latest default-branch HEAD is historical, while the GStreamer monorepo contains the newer GES implementation. Neither source is integrated with Motion's current read-only header inspector; playback, editing, rendering and export remain unavailable.
