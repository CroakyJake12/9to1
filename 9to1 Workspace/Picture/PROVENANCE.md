# Images reference provenance

Observed: 2026-08-30

| Field | Record |
| --- | --- |
| Reference | GNOME Image Viewer (Loupe) |
| Reference release | 50.0 (released 2026-03-13 according to the official Apps for GNOME listing) |
| Official app page | https://apps.gnome.org/Loupe/ |
| Official source repository | https://gitlab.gnome.org/GNOME/loupe |
| Upstream license | GPL-3.0-or-later (SPDX identifier present in upstream source) |
| Use in the historical first slice | Local file chooser → image view → browse adjacent images / inspect basic properties |
| Current donor materialisation | Both glycin/libglycin and Loupe actual upstream source are pinned in `Source/`; see `Source/DONOR-PROVENANCE.md` |
| Metadata reader | TagLib# `TagLibSharp` 2.3.0, package reference in `HavenOS.Images.csproj`; upstream https://github.com/mono/taglib-sharp; LGPL-2.1-or-later |

## Boundary

The roadmap names glycin/libglycin as the image-loader donor; Loupe is the viewer/interaction donor. Both actual donor trees are now present separately from this app's original C#/Avalonia implementation, with upstream licences intact. This does not establish loader integration or Loupe UI parity. The historical reference-only classification above describes the *first implementation slice*, not an exemption from donor-source obligations.

TagLib# is used only for supported image metadata reading and PNG metadata transfer/removal. Its format coverage is not treated as universal; unsupported metadata is reported as unavailable, and the location-removal export mode strips all metadata as a safer fallback.
