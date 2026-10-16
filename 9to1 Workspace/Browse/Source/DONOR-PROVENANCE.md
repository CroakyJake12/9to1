# Browse Firefox/Gecko source

Canonical Mozilla repository: https://hg.mozilla.org/mozilla-central . Its GitHub Git-SHA mirror https://github.com/mozilla/gecko-dev (`master`) and controlled fork https://github.com/CroakyJake12/gecko-dev are pinned to mirror commit `5836a062726f715fda621338a17b51aff30d0a8c` (selected 2026-09-24 UTC).

On 16 October 2026, the declared `Source/FirefoxGecko/` directory and tracked gitlink were missing. The controlled fork's HEAD and master were verified at the recorded revision, and its full working source was restored as a shallow submodule at that exact revision. The root `LICENSE` points to `toolkit/content/license.html`, which retains the donor licensing notices. The donor source was not modified during restoration.

Source availability does **not** establish Firefox parity or a working engine. A maintained CUI integration patch set, registered Gecko host, compiled runtime and rendered-page acceptance remain outstanding. `docs/architecture/browse.md` records that integration boundary; do not treat the gitlink as engine or product acceptance.
