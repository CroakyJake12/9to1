# Unsloth Studio as a Spaces / model-browser donor — measured assessment

30 October 2026. Owner asked whether Unsloth Studio could be a donor for Spaces (heavily modified,
because "it already contains a lot of useful logic") and whether its model browser could be taken for
the Haven model browser, with the licence checked first. Every claim below was measured against the
source tree at `~/.local/share/unsloth-custom-20261014/source`; nothing is inferred from reputation.

## Licence (checked first, as asked)

Unsloth is **dual-licensed**, and the split is the opposite of what "just take the browser" needs:

| part | licence | how verified |
| --- | --- | --- |
| core `unsloth` package | **Apache-2.0** | `pyproject.toml:16` `license = "Apache-2.0"`; README §License states the core stays Apache-2.0 while "certain optional components, such as the Unsloth Studio UI" are AGPL-3.0 |
| `source/studio/` (the Studio app) | **AGPL-3.0-only** | 5,659 `SPDX-License-Identifier: AGPL-3.0-only` headers versus 1 Apache-2.0; `studio/LICENSE.AGPL-3.0` |
| `source/studio/backend/hub` — **the model browser** | **AGPL-3.0-only** | 88 AGPL SPDX headers, 0 others |

So the model browser is inside the AGPL half. Taking it verbatim puts an AGPL-3.0 network-copyleft
obligation on the distributed build, which is why it is queued as
`OPEN-2026-10-30-DONOR-LICENCES` rather than adopted or rejected in code.

## Technical fit, measured

- **UI cannot be adopted at all.** Studio's frontend is SvelteKit and its shell is Tauri/Rust. Our
  CUI rule means application structure, styling, state and behaviour must be authored in CUI, so the
  whole visual half — most of the value of a literal fork — has to be rewritten anyway. Only backend
  logic can cross, behind a typed infrastructure boundary.
- **Their browser cannot serve our actual model.** `studio/backend/models/tabs/hugging_face_tab.py`
  states the chat template is hard-coded and there is **"no support for PLE GGUFs"**. The model this
  PC actually serves is a PLE GGUF: the resident Strata host is launched with
  `--ple-gguf …/IQ3_S/Qwen3.8-Flash-Next-GSQ-RCO-IQ3_S-00002-of-00002.gguf`. Their download/quantise
  path would have to grow PLE support before it could replace a working path.
- **It brings a competing model host.** `studio/backend/` has 19 top-level subsystems (`models`,
  `router`, `system`, `tools`, `training`, `tunnel`, `users`, `work`, `web`, `vision`, `tts`, `video`,
  `tuning`, `inference`, `hooks`, …), an `endpoint_proxy.py`, and its own llama.cpp/b18071 prebuilt
  installers. Adopting it means reconciling two inference hosts and two service routers on one
  machine, against the invariant that the resident Strata host is never stopped or swapped.
- **As installed it cannot be rebuilt.** Our own `Linux-migration/Unsloth-Linux-port.md` records that
  the installed package lacks frontend/Rust source and Cargo/Tauri configuration. Adoption therefore
  requires vendoring the source tree, not the app.
- **Where a donor would genuinely pay**: browsing/download (repo search, GGUF file listing, disk-budget
  checks), quantisation (quant-method/size estimation), adapter and merge operations. Our equivalents
  today are thin — 646 lines across Hugging Face search (251), catalogue (217) and local GGUF
  discovery (178) — so this is the highest-value donor target in Spaces if the licence question
  resolves in favour of use.

## Position

Not adopted, and not rejected in code. Two useful things can be taken regardless of the licence
answer, and both are the pattern already used successfully for Boards:

1. **Their feature list as a parity checklist** for our CUI model browser (the same shape as
   `Boards/DonorParityManifest.md`, where no AppFlowy code is linked but the behaviour target is).
2. **The non-AGPL core** (Apache-2.0) for quantisation/download logic behind a typed boundary, if the
   owner wants that rather than writing it against `llama.cpp`/`gguf` directly.

The blocking variable is the owner's distribution answer, not engineering effort.
