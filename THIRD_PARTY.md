# Third-party software and donor provenance

This index does not replace the licence text distributed with each component.
Preserve notices, source offers and licence files when packaging or moving code.

| Component | Repository location | Licence/provenance status |
| --- | --- | --- |
| Avalonia-derived CUI platform | `framework/CUI/vendor/` | Import must record upstream URL and exact revision and retain the upstream MIT licence. Not present at the baseline audit. |
| llama.cpp-derived Dulche runtime | `9to1 Workspace/Home/Source/Dulche/llamacpp/` | Upstream revision is pinned by `upstream.lock.json`; package notice handling is referenced by OS packaging metadata. |
| Files donor | `9to1 Workspace/Files/Source/Files/` | Donor includes `LICENSE-MIT` and `LICENSE-MPL`; both must remain with donor-derived distributions as applicable. |
| Rnote-derived Canvas engine | Canvas and OS Canvas packaging trees | Provenance/licence files must be audited and included before package verification. |
| AppFlowy-derived Boards engine | Boards and OS Boards packaging trees | Provenance/licence files must be audited and included before package verification. |
| LibreOffice / LibreOfficeKit | Write, Data and Present engine trees | Distribution must comply with the exact linked/packaged LibreOffice components and include their notices. |
| GStreamer / GES | Wave/Motion platform work | Distribution obligations depend on the plugins and linkage actually shipped; scaffolding is not package evidence. |
| GNOME Shell and Mutter | `9to1 OS/platform/` submodules | Preserve upstream licences and exact submodule revisions in OS source and images. |
| Historical CakeOS/CakeAI material | `reference/` | Reference-only unless deliberately promoted with its original provenance and licence reviewed. |

## Packaging gate

No third-party-backed package is release-ready until the package contains the
required licence/notices, its source revision is immutable, and the parity record
identifies the exact included implementation. The repository's GPL-3.0 root
licence does not silently relicense independently licensed donor code.
