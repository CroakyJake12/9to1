# Governing directive — merge HUI into CUI, spec takes precedence

**Recorded:** 2026-10-14 · **Authority:** explicit owner instruction
**Status:** binding. Supersedes earlier working assumptions in this delivery.

## 1. The directive, verbatim intent

1. *"CUI is literally just a renamed HUI."*
2. *"I didn't have a HUI spec as detailed as CUI. Merge the 2 platforms."*
3. *"Spec takes priority/precedence over how the UI framework behaves, etc."*

Operative consequences:

- **CUI is the target implementation.** `.cui` markup, `CuiTheme`, the CUI property cascade,
  the CUI control-template catalogue and the CUI compiler/runtime are authoritative.
- **HUI is legacy input to be merged into CUI**, not a peer framework to be preserved.
- **Where current framework behaviour conflicts with the specification, the specification
  wins.** Existing tests, defaults and rendering that encode the older behaviour are wrong
  against spec and may be corrected — with the clause and reason retained.

This retracts two positions I recorded earlier:

- The decision packet `DECISION-2026-10-14-CHAT-SURFACE-AUTHORITY.md`, which framed HUI vs CUI
  as a live architectural fork requiring an owner choice between porting Chat to CUI and
  keeping HUI as composition layer. **There is no fork; there is a merge, with CUI winning.**
- The framing that the Chat surface is "not CUI at all". It is HUI-authored — that is the
  migration work, not an architectural dead end.

## 2. Why the instruction is correct, with evidence

`framework/CUI` is the only implementation that matches the specification:

| Requirement | `framework/CUI` | HUI / `Haven.UI` |
|---|---|---|
| Built-in themes (`CUI-PREF-201`: 8 + Create Theme) | `CuiThemeCatalog.All` = 9 incl. Cinematic legacy, **Professional default** | `HavenUiTheme` = **5** (Glow, Bubble, Retro, Playful, Cinematic), **Glow default** |
| CUI sole design authority (`CUI-PRO-201`) | owns tokens, templates, cascade, themes | separate `CakeTheme` + `HavenUiResourceApplier` palette authority |
| Public markup vocabulary (`CuiLanguageVocabulary`) | Page, Container, Text, Image, Audio, Video, Object, Button, Input, Layer (+ nonvisual) | `.hui` Container/Button/Text/Icon/Prefab/DynamicUI |
| Typed property cascade | `CuiEffectivePropertyStore` with BuiltIn→Theme→Definition→Instance→Region→Overlay | parallel `HavenProperties` store |
| Conformance tooling | `eng/cui/migrate_legacy.py`, 146-surface inventory | none |

The HUI `HavenUiTheme` enum is exactly the pre-Professional five-theme set the specification
superseded. This is a direct, measured conflict where **spec precedence decides in CUI's favour**.

## 3. Consequence for work already done

The palette work is aligned and stands:

- `SurfacePaletteCatalog` now resolves the **neutral shell for Chat/Go/Spaces from
  `CuiSurfacePaletteCatalog`**, i.e. from CUI's spec-compliant Professional palette, instead of
  blending a per-app accent into the surfaces (`CUI-PRO-202` forbids that "application-local
  colour patch"). Scoped so owner-approved per-app mockups (Tasks' warm orange background) are
  not silently overridden.
- This also **restored** `Glow_palette_matches_the_pre_theme_baseline`, whose "identity
  transform" guarantee was failing at HEAD.

## 4. What "merge the two platforms" actually requires here

Measured, not estimated:

| Asset | Count | Note |
|---|---|---|
| `.hui` markup files | 30 | `Haven.UI` prefab/DynamicUI markup |
| `Haven*` token names aliased to CUI | ~35 | `CuiThemeResourceApplier.ApplyLegacyScopeAliases` already bridges these |
| Parallel theme enums | 2 | `CuiTheme` (spec) vs `HavenUiTheme` (legacy) |
| Parallel palette owners | 2+ | `CuiSurfacePaletteCatalog` vs `SurfacePaletteCatalog`/`CakeTheme`/`HavenUiResourceApplier` |
| `Haven` identifier occurrences | **37,783** across **9,302** files | includes assembly names, namespaces, persisted keys, user-facing copy |

The merge is therefore **not one change**. It is a staged programme:

- **M1 — Theme authority.** Retire `HavenUiTheme` as an independent axis; resolve theme from
  `CuiTheme` with the legacy five identities preserved for existing saved settings
  (`CUI-PREF-201` requires truthful legacy handling for Cinematic and older saved values).
- **M2 — Palette ownership.** Collapse `SurfacePaletteCatalog` / `HavenUiResourceApplier` /
  `CakeTheme` onto `CuiSurfacePaletteCatalog` as the single semantic owner, keeping the
  existing `Haven*Brush` alias names working.
- **M3 — Prefab/DynamicUI into CUI.** Express HUI Prefab and DynamicUI as CUI constructs
  (`CuiLanguageVocabulary` already reserves `Prefab`). This is the largest piece: CUI has no
  equivalent of the HUI prefab/dynamic-UI runtime today.
- **M4 — Markup migration.** Convert `.hui` to `.cui` under `eng/cui/migrate_legacy.py`.
- **M5 — Identifier consolidation.** `Haven` → `9to1`, `HavenUI` → `CakeUI`, `HUI` → `CUI`,
  separating branding from internal symbols, persisted IDs, protocol contracts and provenance.
  Existing `CuiThemeResourceApplier` aliasing and assembly-scanned prefab/template discovery
  (`HavenPrefabCatalog.FromAssembly`, `HavenDynamicUITemplateCatalog.FromAssembly`) are the
  specific reflection/resource dependencies that make this non-mechanical.

Each stage needs build + the native suite (now runnable via the x64 runtime) before the next.

## 5. Correction made to my own plan

I began a `SpaceRail` in `Haven.UI`/HUI before this directive landed. It **did not compile**
(guessed property/event names against the real `HavenProperties` API) and I removed it rather
than leave the tree broken or ship a guessed implementation.

That was the right call under the new directive anyway: the rail required by `S:L00014` should
be a **CUI** construct once M3/M4 land, not another HUI control added to the legacy surface.

## 6. Standing constraints unchanged

- Spec precedence resolves *appearance and behaviour* conflicts. It does **not** waive
  persistence, security or the standing requirements in `CURRENT-REQUIREMENTS.md`.
- Stable IDs are never renumbered or recycled; `SpaceID` survives renames.
- Retained functionality is not removed to win a screenshot comparison. Where an existing test
  encodes superseded behaviour, it is corrected **with the clause and reason retained** — not
  deleted.
- Legacy surfaces without a verified successor are listed, not removed.

## 7. Not claimed

No part of the merge is complete. The palette corrections are the only work that currently
matches this directive, and they are corrections **inside** the legacy palette owner, not a
merge of the frameworks.