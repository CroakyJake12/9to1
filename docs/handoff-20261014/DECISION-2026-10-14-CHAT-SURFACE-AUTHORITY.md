# Decision packet — Chat surface styling authority

**Raised:** 2026-10-14 · **Gate:** Phase B (WP01) · **Blocks:** Gate B visual correction of the
Spaces shell, Chat header, transcript and composer.

State: **OPEN — needs owner decision.** No workaround has been applied.
Engineering status of everything else remains independent.

---

## 1. What was observed

`CUI-PRO-201` (P:L00007) requires CUI to be the sole design authority for first-party
surfaces and forbids donor/native styling for them. `framework/CUI/Themes/CuiThemeResourceApplier.cs`
already honours that for CUI-hosted surfaces: it publishes `Cui*Brush` semantic resources and
aliases legacy `Haven*Brush` names onto them (`ApplyLegacyScopeAliases`, lines 349–379).

The **production Chat route does not consume that pipeline.** Evidence:

| Layer | Files | Fact |
|---|---|---|
| Production Chat scene | `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatHavenScene.cs` | imports `Haven.UI`, `Haven.Desktop.HavenUI.Tokens`; builds the visible surface in C# |
| Composer / attach markup | `…/Haven.Desktop/Prefabs/Chatbox.hui`, `ChatAddMenu.hui` | HUI `.hui` markup, token names like `SurfaceRaised`, `TextPrimary`, `Shadow:Card` |
| Token resolution for those names | `9to1 OS/HUI/vendor/Haven.UI/Tokens/HavenDesignTokens.cs`; `9to1 OS/HUI/HuiRenderer/CakeTheme.cs` | a **separate** token vocabulary |
| That vocabulary's colours | `9to1 OS/HUI/theme/tokens.json` | `background #0F1A1E`, `surface #16262C`, `foreground #EAF4F1`, `muted #8AA5A0`, `accent #3ED598` — a teal/mint palette with **no concept of CUI themes**; the whole vocabulary is derived by generic `Lighten(x, 0.07)` mixes |

`CakeTheme.Load` is referenced **nowhere** in `Haven.Desktop`, and no code path bridges
`CakeTheme`/`HavenDesignTokens` to `CuiThemeResourceApplier`.

**Consequence.** `CuiThemeCatalog.All` correctly contains Professional, Glow, Playful,
Frosted Glass, Bubble, Metallic, Retro, Western (+ Cinematic legacy), defaults to
Professional, and Montserrat is genuinely bundled and mapped. None of that reaches the Chat
chrome, because the Chat chrome resolves a different, static, teal palette.

Measured reference targets (`references/REFERENCE-MEASUREMENTS.md`) are neutral charcoal:
conversation `#14151A`, shell/sidebar `#1F2127`, rail `#1B1E23`, composer `#2D2D32`,
model pill `#2C2F38`.

### Corroboration from the repository's own tooling

`eng/cui/first-party-inventory.json` (regenerated at this baseline, `sourceHead 2c4d151b`)
classifies the Chat markup as `legacy-app-input`, not canonical CUI, and
`eng/cui/README.md` states:

> "Native legacy HUI hosts and the unconnected Data/Mail/Spaces/MiniComputer/Terminal
> declaration paths remain explicit conformance work until their real typed CUI host/renderer
> contracts are implemented and exercised."

Current conformance across all 146 first-party markup surfaces: **32 canonical-candidate,
91 legacy-app-input, 8 manual-runtime-adapter-required, 8 custom-definition-review, 4
review-test-intent, 2 retained, 1 legacy-vocabulary. `runtimeAcceptance` is `NOT RUN` for
146 of 146.**

---

## 2. Why this needs a decision rather than a fix

`9to1 Workspace/shared/HAVEN_UI_RULES.md` contains a binding **Theme Modification Lock**:

> "After that migration is recorded as complete, agents may repair conformance bugs and
> migrate newly discovered bypasses, but must not redesign HavenUI or change its approved
> primitives, geometry, motion, typography, four appearances or interaction language
> unless the user explicitly requests a design-system change."

HavenUI is `Haven.UI`/`.hui`. The lock therefore plausibly forbids the obvious fixes —
recolouring `tokens.json`, restyling `Chatbox.hui`, or moving HavenUI geometry — even though
those are exactly what the handoff's reference comparison asks for.

Under `9to1 Workspace/shared/AGENTS.md` precedence, "an ordinary feature request does not
silently override a rule", and only an *explicit* instruction that expressly changes the rule
sits above it. **No such explicit instruction has been given.** The handoff mandates the
Professional reference outcome but does not name the Theme Modification Lock or authorise
changing HavenUI's approved appearance system.

I will not silently pick an interpretation in either direction.

---

## 3. Options

**A — Point HavenUI's token resolution at the active CUI theme (recommended).**
Make `CakeTheme`/`HavenDesignTokens` resolve their names from the live CUI Professional
semantic resources instead of `tokens.json`, keeping the JSON only as the documented
fail-closed fallback for non-CUI hosts. This is the pattern `framework/CUI/Themes/README.md`
already describes ("Exact legacy brush names required by retained renderers alias these
resources").
- *For:* satisfies `CUI-PRO-201`; reuses correct framework capability; no per-app colour patch
  (explicitly rejected by D12); preserves all existing behaviour; smallest real owner.
- *Against:* changes what HavenUI surfaces render, so it arguably engages the Theme
  Modification Lock.
- *Risk:* needs runtime verification that no HavenUI surface depends on the teal palette
  specifically.

**B — Port the Chat surface to CUI templates.**
Migrate `ChatHavenScene`/`Chatbox.hui`/`ChatAddMenu.hui` to CUI elements and CUI templates.
- *For:* matches the handoff's stated preference most directly.
- *Against:* very large (`ChatPage.axaml.cs` is 115 KB, `ChatHavenScene.cs` 38 KB, `NewChatPage.cs`
  125 KB); risks the "existing working functionality must survive" requirement; the README says
  native legacy HUI hosts stay conformance work "until their real typed CUI host/renderer
  contracts are implemented" — i.e. it needs new framework work first.
- *Risk:* high regression surface.

**C — Recolour `tokens.json` to the reference hexes.**
- *Rejected in principle.* This is a per-surface colour patch producing "controls made to look
  professional only through application-local colour patches", which `CUI-PRO-202` forbids and
  D12 rejects. It would also leave Chat permanently outside theme switching. Recorded here only
  so it is visibly declined rather than silently skipped.

---

## 4. What is needed to proceed

An explicit instruction on one point:

> Does the approved Professional reference constitute an explicit design-system change that
> lifts the Theme Modification Lock for the Spaces/Chat surface?

- **If yes →** option A, implemented as a bounded first patch, with runtime comparison evidence
  against `references/intended/`.
- **If no →** option B, starting with the CUI host/renderer contract the README says is missing,
  and the Chat visual correction is explicitly deferred with a recorded blocker.

Until then, Gate B cannot start, and no Chat visual assertion can be marked anything but NOT RUN.

## 5. Independently unaffected work

This blocks **only** the Chat/Spaces visual correction. It does not block: CUI framework and
language conformance work, canonical Files/Dulche/Home/Accounts service work, Spaces data-model
and conversation-projection work, Spaces registration/identity work, or any non-Chat Space.
Those continue without this decision.