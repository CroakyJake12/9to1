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

## Boundary

The roadmap names glycin/libglycin as the image-loader donor; Loupe is the viewer/interaction donor. Both actual donor trees are now present separately from this app's original C#/Avalonia implementation, with upstream licences intact. This does not establish loader integration or Loupe UI parity. The historical reference-only classification above describes the *first implementation slice*, not an exemption from donor-source obligations.
