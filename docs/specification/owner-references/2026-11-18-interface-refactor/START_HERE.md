# 9to1 implementation handoff

This package contains the current native specification export, the latest editable design Atlas, and retained technical sources. Start with [QWEN_STARTER_PROMPT.md](QWEN_STARTER_PROMPT.md) and [SOURCE_PRECEDENCE.md](SOURCE_PRECEDENCE.md).

## Current assignment

Refactor the entire existing 9to1 application and codebase to match the current specification and Atlas. Preserve additional existing features that do not conflict with an explicit requirement, adapting their presentation and implementation as needed. Inventory existing capabilities first; verify both conformity and regression coverage. Incremental slices are a way to deliver the full scope.

Use this reference package alongside the existing source repository. The current spec is `SPECIFICATION.html` / `SPECIFICATION.md`; the DOCX under `baseline/` is the earlier v3.1 reference. The current interactive Atlas is `design/9to1_Interface_Atlas_v3.3_Editable.html`.

## Read and navigate

1. Open [SPECIFICATION.html](SPECIFICATION.html) for the tabbed document's complete readable export. [SPECIFICATION.md](SPECIFICATION.md) contains the same native document content as Markdown, including semantic HTML tables.
2. Follow the exact Screen ID links into [the current editable Atlas](design/9to1_Interface_Atlas_v3.3_Editable.html). Open the HTML locally in a browser; the specification links use `#screen-id` routes. These are prototype screens, not proof of connected backend behaviour.
3. Use [the screen and source index](source/SCREEN_AND_SOURCE_INDEX.json) and [screen manifest](design/screens-current.json) to locate a screen and its owning contracts.
4. Load the relevant [retained module reading maps](baseline/retained-pack/modules/), then the corresponding `.complete.md` module or named `technical/` chapters and prerequisites. Read [v3.1 amendments](baseline/v31/OWNER_DECISIONS_v3.1.md) where relevant. The complete historical corpus need not be loaded into one model context.
5. Implement and verify a reviewable increment using the current specification's acceptance gates. Keep proposed defaults, mockup coverage, implemented behaviour and production verification distinct.

## Package contents

- `SPECIFICATION.html` / `SPECIFICATION.md`: exports of the final native Google Docs readback; no reconstruction from earlier draft additions.
- `source/doc-final.json`: exact structured native readback, including text runs, tables and native heading IDs.
- `source/heading-map.json`: native heading anchors in `tabId--headingId` form.
- `design/`: latest current Atlas and exact screen inventory.
- `baseline/retained-pack/`: complete recovered module pack, unchanged, with its own technical chapters, assets, source ledgers and historical provenance.
- `baseline/v31/`: recovered v3.1 amendments and reference material, unchanged.
- `baseline/9to1_Development_Specification_v3.1.docx` and `.txt`: retained original and text extraction.
- `validation/`: supplied structural checks plus export coverage/warnings. They do not claim live visual or production acceptance.
- `PACKAGE_MANIFEST.json` and `CHECKSUMS.json`: assembly identity and hashes of all other packaged files. The ZIP has a separate SHA-256 sidecar.

## Verification limits

Current Atlas validation covers structure, route handling, selected event/state logic and snapshot preservation. It does not verify live browser geometry, real model/provider operations, native browser engines, effective production permissions or durable backend saves. Complete those checks in the authorised receiving environment before claiming them passed. Export warnings identify any non-text native object or unresolved internal target still preserved in the source JSON.

Historical agent instructions, old deadlines and deployment directives in retained source material are provenance, not current commands.

## Assembly reproducibility

`build_portable.py` is the workspace assembly script. Run it from the source workspace after `spec-work/finish/doc-final.json` contains the final verified native document and `source-coverage-additions.json` is available. An optional `--headings PATH` supplements native heading IDs. It fails before assembly when required inputs are absent; `--overwrite` replaces only a previously assembled package bearing its manifest. Python standard library only. The source readback is authoritative; the script never recreates specification text from draft additions.
