# G02-T01 — Model Picker / Reasoning Control: Current-State Inventory

Repo: `/home/jacob/Documents/GitHub/9to1` (read-only investigation; no builds, no writes to repo).
All paths below are relative to that repo root unless stated. Line numbers are from the working tree at investigation time.

---

## 1. Where the model picker UI lives TODAY

### 1a. The trigger button (top rail)

- **`9to1 Workspace/shared/src/Haven.Desktop/Views/Shell/TopRail/TopRail.axaml:27`** — `<hui:HavenModelPickerButton x:Name="UniversalModelButton" ...>` in the top rail. Its inline content is: cpu icon + `UniversalModelName` (e.g. "Local model") + `·` + `UniversalReasoningValue` (e.g. "60%") + chevron-down icon. So the rail pill shows **model name + reasoning percent only — no Main/Voice/Vision, no role concept**.
- **`.../TopRail/TopRail.axaml.cs`**
  - `:50` `public event EventHandler? ModelRequested;`
  - `:72` registers `TopRail.Actions.Model` on the event bus
  - `:108-115` `SetModelSummary(modelName, reasoningPercent)` → updates name text, `N%` text, and `UniversalModelButton.EffortPercentage`
  - `:129` `SetModelSelectorEnabled`, `:131` `ShowModelFlyout(Flyout)` (the flyout content is owned by the shell, not the rail)
  - `:235` `UniversalModelButton.Click += OnModelClicked` → `InvokeModelAction` (partial in `TopRail.SemanticActions.cs:50`)
- **`.../TopRail/TopRail.HavenScene.cs`** — a scene-based redraw of the rail overlays the XAML anchors: `:84-86` subscribes to `UniversalModelButton/UniversalModelName/UniversalReasoningValue` property changes, `:98` `scene.SetModelSummary(UniversalModelName.Text, ParseEffort(UniversalReasoningValue.Text))`.
- **`.../TopRail/TopRailFinalScene.cs`** (the Haven-scene "final rail", currently the live visual)
  - `:99`, `:181` `ModelButton` (a `HavenButton`, not `HavenModelPickerButton`)
  - `:145` `ModelButton.Invoked += ... ModelRequested`
  - `:282-303` `SetModelSummary` sets content `"{model} · {effort}%"`, accessible name "Model {name}, reasoning {effort}%", and picks background key by effort band: `<35 → "HavenModelReasoningLowBrush"`, `<70 → Balanced`, `<95 → High`, `≥95 → Max` (`:298-301`).

### 1b. The button class + reasoning style classes

- **`.../Haven.Desktop/HavenUI/Components/Buttons/HavenButtons.cs:174-201`** — `HavenModelPickerButton : HavenButtonBase`, class `havenModelPicker`, styled property `EffortPercentage` (default 60). `ApplyEffort` (`:194-200`) toggles classes:
  - `reasoningLow` <35, `reasoningBalanced` 35–69, `reasoningHigh` 70–94, `reasoningMax` ≥95.
- **`.../Haven.Desktop/Styles/DefaultTheme.axaml:857-867`** — the `havenModelPicker.reasoningLow/Balanced/High/Max` styles. NOTE: all four set the **same** `HavenAccentPrimaryBrush` background (`:863-866`), i.e. the class-based styling is palette-neutral today. The actual per-level gradients are the *brush resources* `HavenModelReasoningLow/Balanced/High/MaxBrush` at **`DefaultTheme.axaml:81-84`** (amber→orange→red→purple linear gradients), consumed by `TopRailFinalScene.cs:296-301` (Haven scene path), not by the `.reasoning*` class styles.
- Registry: **`.../HavenUI/Registry/HavenUiComponentRegistry.cs:64`** — `"HavenModelPicker"` component registered, note "ReasoningOverride".
- Where `HavenModelPickerButton` is instantiated:
  - `TopRail.axaml:27` (desktop rail XAML anchor)
  - Android: **`9-1 OS (Android)/src/Haven.Android/MainView.Mobile.Layout.cs:166-182`** — circular 54px pill; `EffortPercentage` from `_preferences.DefaultEffort` (Low→20, Medium→55, High→80, else 100); `model.Click += ... ShowModelSelector()` (definition of `ShowModelSelector` on Android not found in `9-1 OS (Android)/src` — **unverified**, likely compiled from a shared file).
- A third legacy control: **`.../Haven.Desktop/Controls/ModelConfigurationControl.cs`** (see 1d).

### 1c. The flyout that opens on click (the actual "picker")

- **`.../Haven.Desktop/Interface/Shell/MainView.axaml.cs`**
  - `:3320` `TopRail.ModelRequested += OnTopRailModelRequested`; `:3351` → `ShowModelSelectorAsync()`
  - `:3401-3638` `ShowModelSelectorAsync()` builds the flyout **entirely in code**:
    - Loads models from `_ollama.GetModelsAsync` (`:3407`) — **local/Ollama catalogue only** in this flyout.
    - "Model options" title (`:3411-3417`)
    - **Model**: a single `HavenSelect` dropdown of model names (`:3428-3462`); on change (`:3437-3446`): `NewChatPage.SelectModel` / `TrainingPageViewModel.SelectedModel` / `CurrentChat.SelectedModel = model`, then `_preferences.SetModelDefaults(...)`, then `TopRail.SetModelSummary(...)`.
    - **Allow Actions**: permissions `HavenSelect` (`:3477-3521`) — extra control not in the spec picker.
    - **Voice**: a `HavenSelect` of TTS voices bound to `_globalCallViewModel.SelectedVoice` (`:3523-3557`) — this is a call/TTS voice list, **not** the spec's "Voice role model".
    - **Reasoning**: header + percent (`:3559-3574`) and a `HavenSlider` 20–100, TickFrequency 20, snap-to-tick (`:3576-3599`); on change maps through local `EffortForPercentage` (`:3649-3655`: Low ≤20, Medium ≤60, High ≤80, Max else) and persists via `_preferences.SetModelDefaults`, live-updates `TopRail.SetModelSummary`, and sets `CurrentChat.SelectedEffort` (`:3585-3598`). Helper note: this flyout's 20/60/80/100 mapping (`EffortPercentage`, `:3640-3647`) **disagrees** with canonical `ReasoningScale` (25/50/75/100, `9to1 Workspace/shared/src/Haven.Core/DomainLogic/ReasoningScale.cs:11-24`) and with `ModelConfigurationControl`'s text.
    - Flyout container: `HavenDropdown` + `HavenDropdownCard` width 420 (`:3609-3626`), then `TopRail.ShowModelFlyout(_modelSelectorFlyout)`.
  - **No Main/Voice/Vision role tabs. No collapsible Context/Model/Assistant-Specialist headings. No pending-context "Apply and reload". No source distinction (local/remote/cloud) beyond the Ollama list. No model search list with source labels.** The flyout is a flat stack: Model, Allow Actions, Voice, Reasoning.

### 1d. Legacy/unused second picker: `ModelConfigurationControl`

- **`.../Haven.Desktop/Controls/ModelConfigurationControl.cs:32-847`** — instruction-bar control with submenu flyout: Advanced configurations (temperature/context-limit/action-limit, `:342-399`), Resolve-errors recovery buttons (`:404-437`), Model catalogue with search + recents + recommended (`:467-583`), and an effort slider fixed to the four-step `ReasoningScale` 25/50/75/100 (`:174-198`, `:296-303`, `:813-820`).
- **Not instantiated anywhere in production code**: `new ModelConfigurationControl` appears only in `ModelConfigurationControlTests.cs:41`; the only src reference is `TopRail.axaml.cs:111` calling the static `SimplifyModelName`. Effectively dead UI today (retained + tested).

### 1e. Settings copy of the defaults

- **`.../Haven.Desktop/Views/Pages/Settings/SettingsHavenPage.axaml.cs:202-212`** — `SaveModelDefaults()` → `_preferences.SetModelDefaults(model, effort)`. A second write path into the same persisted pair.

**Answer to "is there Main/Voice/Vision today?"** — No. Grep across `Views/Shell/TopRail` and `Interface/Shell` finds only the unrelated Vision *workspace* (`MainView.Imagine.cs:20-66`, a creative app surface) and "Vision & Voice" app naming. The only role-tab code exists in the Atlas prototype (see §3), not in the product.

---

## 2. Persistence and propagation of model selection

- **Service**: `9to1 Workspace/shared/src/Haven.Desktop/Services/UserPreferencesService.cs`
  - `:106` persisted at `Path.Combine(paths.DataDirectory, "preferences.json")`; `Save()` at `:864`.
  - `:470` `DefaultModel`, `:471` `DefaultEffort` (parse-fallback Medium); record fields `:932`, `:938`.
  - `:670-674` `SetModelDefaults(model, effort)` → `with { DefaultModel, DefaultEffort }` + `Save()` — **global default, written immediately (no pending state)**.
  - `:736` `SetAdvancedModelOptions(temperature, contextLimit, actionLimit)` — the only persisted context-limit knob (`GenerationOptions.ContextLimit`, `:519`), reachable today from ModelConfigurationControl's Advanced panel and Settings, **not** from the picker flyout. There is **no `modelContextTokens`-equivalent pending/applied mechanism** (grep "modelContextTokens" in `9to1 Workspace` / Android src: 0 hits).
- **Writers** (all funnel to `SetModelDefaults`):
  - `MainView.axaml.cs:3444` (picker flyout model select), `:3595` (picker flyout reasoning slider)
  - `Views/Pages/Chat/ChatPage.axaml.cs:359` (`SelectedModel` setter), `:382` (`SelectedEffort` setter)
  - `ViewModels/ChatPageViewModel.cs:688`, `:713` (same pattern)
  - `Views/Pages/Chat/NewChatPage.cs:351` (`SelectModel`)
  - `ViewModels/UtilityPagesViewModels.cs:1086`; `Views/Pages/Settings/SettingsHavenPage.axaml.cs:209`
- **Propagation to the rail**: `TopRail.SetModelSummary` (`TopRail.axaml.cs:108-115`) → `HavenModelPickerButton.EffortPercentage` → `reasoning*` classes; and `TopRail.HavenScene.cs:93-104` mirrors the anchors into `TopRailFinalScene.SetModelSummary` (`TopRailFinalScene.cs:282-309`) which applies the `HavenModelReasoning*Brush`.
- **Does choosing a model change document/session identity?** No evidence of identity churn:
  - `ChatPage.SelectedModel` setter (`ChatPage.axaml.cs:354-361`) only raises `SendCommand.RaiseCanExecuteChanged()` and writes prefs.
  - `NewChatPage.SelectModel` (`NewChatPage.cs:340-360`) explicitly documents "Choosing a working model changes only the model-backed send gate: the transcript is unchanged"; it re-binds to the current catalogue snapshot (`AdmittedCatalogueEntry`) but never recreates `_conversation`.
  - `MainView.axaml.cs:3437-3446` reuses `CurrentChat`/`CurrentPage`, no new tab/conversation created.
  - Whether individual persisted messages record the model name used — **unverified** (not investigated).

---

## 3. Spec + Atlas requirements (quoted)

From `docs/specification/owner-references/2026-11-18-interface-refactor/SPECIFICATION.md`:

- Section **`## 2.3.3 - Model picker`** (line 193). No "GATE" string exists in SPECIFICATION.md (`grep -c GATE` = 0); the picker requirement ids are `MODEL-001`…`MODEL-006`:
  - **MODEL-001** (line 195): "The picker contains centred Main, Voice and Vision tabs. This alignment is scoped to the picker; Haven's own tabs remain left aligned."
  - **MODEL-002** (line 197): "Within a role, Reasoning stays visible. Context, Model and Assistant/Specialist are separate collapsible headings that show the current selection or status while closed; Context can show usage or Pending changes. Use one compact searchable list of model rows with name, short source label and selected state."
  - **MODEL-003** (line 199): "Distinguish local device, remote app/device and cloud provider sources without repeating the same source description in large stacked cards. Browse models opens the model catalogue. Preserve a role's current choice when another section opens or the role tab changes."
  - **MODEL-004** (line 201): "Changing Context creates a pending value. Show Apply and reload only when it differs from the applied value. Typing, Enter, dismissing a section or switching roles must not silently apply it. Invalid values leave the applied value unchanged and show an inline reason."
  - **MODEL-005** (line 203): "The production implementation must validate a context limit against the selected model/provider and available resources. Do not infer a maximum from sample numbers. Applying starts an explicit reload operation; show loading, success or failure, and retain recoverable input if reload fails."
  - **MODEL-006** (line 205): "Assistant/Specialist selection references canonical identities shared with Home and Spaces. Selecting None removes that participant choice for the role; it does not delete the underlying Assistant or Specialist."
  - Mockups (line 207): `host-model-picker`, `home-models`, `home-models-installed`, `home-model-details`, `home-assistants`, `home-specialists` (in `design/9to1_Interface_Atlas_v3.3_Editable.html`).
  - Preview boundary (line 209): "…The preview shares one pending/applied context value across Main, Voice, Vision and the host context popup, saved as uiPreferences.modelContextTokens. Model, reasoning and participant selections are per-role in-session state and are not saved-file preferences. Production provider limits still need implementation."
- Related ids that constrain the picker:
  - **THEME-004** (line 121): "…only the Main/Voice/Vision selector is centred within its own tab segments."
  - **UX-002** (line 47): "Navigation does not silently reset the selected model, accent, layout or conversation scope."
  - **CUI-LANG-002** (line 103) / **CUI-NEW-004** (line 775): theme/accent change must not alter "model selection or action scope".
  - Owner-override paragraph (line 1039, §7.10): "the compact picker uses Main, Voice and Vision; Reasoning stays visible and Context, Model and Assistant/Specialist collapse. This presentation replaces the old picker modality grouping… A changed context value is pending until Apply and reload."
  - **RUNTIME-007** (line 1031): "Changing a model revalidates inherited context and reasoning settings."
  - **RUNTIME-008** (line 1035): "…Retain the documented discrete effort mapping: three levels 35/70/100, four 25/50/75/100, five 20/40/60/80/100, mapped to real provider level IDs rather than a floor formula."
  - Line 771 (owner override): "Haven tabs remain left aligned; only Main/Voice/Vision picker labels are centred."
  - **QA-03** (line 695): "Model picker: centre only its role tabs; expand each heading; change context; dismiss and return; apply explicitly; test invalid input and a reload failure in the production implementation. No other interaction silently applies the pending value."
  - Context-menu **CTX-10 · Model row** (line 649, table): "Select; Details; Manage local model / provider as applicable… Reference: MODEL-002–006."
  - Coverage table (line 663): §2.3.3 → screen ids `host-model-picker; home-models; home-models-installed; home-model-details`, "Compact model rows and pending context apply; no real model reload."

**Atlas** (`design/9to1_Interface_Atlas_v3.3_Editable.html`, JS embedded):
- Registration `host-model-picker` (~char 743490): "Anchored dropdown… Universal Model Picker… The host model control opens an anchored picker… Selecting a member stays lazy until Send… Discover models opens the catalogue" — this is the older overlay body (`modelPickerMenu`, char ~3693960: All/Local/Cloud/Groups tabs + "Thinking effort" select).
- The **v2 role picker** (char ~3682700–3701800) is the current reference: `roles = [['main','Main'],['voice','Voice'],['vision','Vision']]`; per-role `reasoningByRole`/`roleSelections`/`personSelections`/`sectionOpen`; `modelPickerMenu = function refreshedModelPicker()` emits `hr-role-tabs` (tablist), a **visible Reasoning range** (`data-hr-reasoning`, Quick–Deep), and three collapsible `<details>` sections: **Context** (usage ring + "Pending changes" + "Apply and reload" only while pending), **Model** (search + compact rows + "Browse models" → `home-models`), **Assistant/Specialist** (search, None option). Context applies to `uiPreferences.modelContextTokens`. Notes override at char ~3709797 restates: "The compact picker has Main, Voice and Vision role tabs. Reasoning is always visible… Keyboard users can move between tabs with Left/Right or Home/End…"
- Spec sources (line 1051): "Owner instructions: 18 November picker/context changes; Atlas v12: host-model-picker notes."

---

## 4. Existing tests

- `9to1 Workspace/shared/tests/Haven.Desktop.Tests/HavenUiComponentSystemTests.cs:100-112` — `Model_picker_is_the_documented_reasoning_palette_exception`: `HavenModelPickerButton` EffortPercentage 20 → `reasoningLow`; 80 → `reasoningHigh`, not low.
- `.../TopRailRegressionCorrectionTests.cs:12-33` — final rail test pinning `scene.ModelButton` background to `HavenModelReasoningBalancedBrush` (60%), `…LowBrush` (20), `…HighBrush` (80), `…MaxBrush` (100) via `rail.SetModelSummary`.
- `.../SharedControlStateParityContractTests.cs:12-32` — `havenModelPicker` included in `InteractiveClasses`; asserts `:pointerover` style exists in DefaultTheme.axaml.
- `.../ModelConfigurationControlTests.cs:26-52` — `SimplifyModelName` theory + "one compact button and one flyout" AvaloniaFact for the legacy control.
- `9to1 Workspace/shared/tests/Haven.Core.Tests/ReasoningScaleTests.cs:8-77` — 4-step mapping 25/50/75/100, slider snapping, 3/4/5-tier bijective scales (matches RUNTIME-008), per-effort context limits.
- Related but not picker UI: `Haven.Core.Tests/LlamaCppRuntimeTests.cs`, `Haven.Infrastructure.Tests/NamedReasoningBridgeTests.cs`, `ResilientRoutingReasoningGuardTests.cs`, `ModelUsageRepositoryTests.cs` (runtime/routing, unexamined in detail).
- **No test covers `MainView.ShowModelSelectorAsync` flyout contents, role tabs, pending-context behavior, or the flyout's 20/60/80/100 effort mapping.**

---

## 5. Gap table

| # | Requirement (spec id) | Current state | Best place to implement | Risks |
|---|---|---|---|---|
| 1 | MODEL-001 centred Main/Voice/Vision role tabs | **Missing** — flyout has flat Model/Voice(TTS)/Reasoning rows (`MainView.axaml.cs:3401-3638`); no role concept in product code | Rebuild flyout content in `MainView.ShowModelSelectorAsync` using HavenUI components (`HavenDropdown`/`HavenDropdownCard`, `TopRail.ShowModelFlyout`, `TopRail.axaml.cs:131`); centring styles in `DefaultTheme.axaml` near `:857` | Two render paths (XAML anchor + `TopRailFinalScene`) must stay in sync; `TopRail.HavenScene.cs` mirrors anchors only; centering must not bleed into Haven tabs (THEME-004) |
| 2 | MODEL-002 Reasoning always visible; collapsible Context/Model/Assistant-Specialist headings showing current value | **Partial** — Reasoning slider exists and is visible (`:3559-3607`) but uses non-canonical 20/60/80/100 bands (`:3640-3655`) vs `ReasoningScale` 25/50/75/100 (`Haven.Core/DomainLogic/ReasoningScale.cs:11-24`); no collapsible sections anywhere | New picker panel class under `Views/Shell/TopRail` or `HavenUI/Components`; unify all effort math onto `ReasoningScale` (ModelConfigurationControl `:174-198` already does) | Changing band thresholds breaks `TopRailRegressionCorrectionTests.cs:26` (Balanced@60) and `HavenUiComponentSystemTests` (Low@20/High@80 class pins in `HavenButtons.cs:194-200`); prefs persist `DefaultEffort` as enum string — safe — but `MainView` EffortForPercentage thresholds differ from ModelConfigurationControl's, unify carefully |
| 3 | MODEL-002 compact searchable model rows (name, short source, selected) | **Partial** — `HavenSelect` dropdown (`:3428-3436`); search+recents list exists only in unused `ModelConfigurationControl.BuildModelPanel` (`:467-583`); catalogue is `_ollama.GetModelsAsync` only | Reuse/adapt `ModelConfigurationControl.BuildModelPanel` pattern inside the new flyout; source the catalogue from the Dulche/provider catalogue rather than Ollama only | Ollama-only list contradicts MODEL-003 sources and §7.10 "one Dulche catalogue… picker choices" (line 1021); `ModelDescriptor` carries capabilities (`ToolCapability.Vision/Audio…`, `:600-608`) — role filtering needs a capability→role map (none exists yet; unverified whether one exists deeper in Haven.Core) |
| 4 | MODEL-003 local/remote/cloud distinction + "Browse models" opens catalogue; per-role choice preserved | **Missing** — no source labels in flyout; no browse link; no per-role state | Source labels from provider metadata on `ModelDescriptor`/route (RUNTIME-007 filtering, spec line 1031); per-role state lives in new picker state object (spec: in-session, not persisted, line 209) | "Browse models" target = Home `home-models` surface; navigation from flyout must not reset model selection (UX-002, line 47); Home models screen state unexamined (unverified) |
| 5 | MODEL-004 pending context value + explicit "Apply and reload"; invalid input inline reason | **Missing** — no context control in the flyout at all; nearest is Settings/Advanced `SetAdvancedModelOptions` (`UserPreferencesService.cs:736`) which applies immediately; `modelContextTokens` equivalent: 0 grep hits | Add Context section in new flyout; persist applied value via `SetAdvancedModelOptions`/`GenerationOptions.ContextLimit` (`:519`); pending value held in picker state only | Spec named preference is `uiPreferences.modelContextTokens` shared with host context popup (line 209) — product schema for that key doesn't exist; inventing storage shape risks diverging from later spec work |
| 6 | MODEL-005 validate limit vs model/provider; reload op with loading/success/failure | **Missing** — no provider-limit validation found (unverified beyond grep) | New picker + backend validation call into model/runtime service (`IProviderModelClient`-adjacent services; exact seam unverified) | Requires real runtime reload semantics; Atlas explicitly says preview does not reload a runtime (line 209) — implementer must not fake success (UX-004, line 51) |
| 7 | MODEL-006 Assistant/Specialist selection per role, canonical ids, None option | **Missing** in picker — `ChatPage.SelectedAgent` exists (`ChatPage.axaml.cs:363-373`) but is not in the flyout and not per-role | Add Assistant/Specialist section; reuse canonical assistant catalogue used by Home/Spaces (identity source unverified) | Must reference canonical ids, not names (spec line 1021: display-name match ≠ identity); SelectedAgent currently writes only a UI notice, no persistence |
| 8 | Trigger pill shows model + reasoning with level palette | **Exists** — `HavenModelPickerButton` + `reasoning*` classes (`HavenButtons.cs:174-201`), live visuals via `TopRailFinalScene.cs:282-309` + brushes (`DefaultTheme.axaml:81-84`); tests pin it | Keep; if role tabs arrive, pill may need role summary later (spec silent — unverified) | Class styles `:863-866` are identical colors today; palette lives in the Haven-scene brush path — styling changes must update both |
| 9 | Identity stability on selection (CTX-10 "no implied install"; UX-002) | **Exists (verified for desktop paths)** — selection never creates/changes conversation (`NewChatPage.cs:340-360` comment + setter bodies) | Preserve in rewrite | `MainView:3443` sets `CurrentChat.SelectedModel` on a shared chat; per-message model recording unverified |
| 10 | Test coverage for picker requirements (QA-03) | **Missing** — no tests for flyout contents/behavior (§4) | Add Haven headless tests beside `TopRailRegressionCorrectionTests.cs`; extend `HavenUiComponentSystemTests` | Existing palette tests assume current thresholds; expect to update them alongside MODEL/effort unification |

### Notes for the implementer
- The single biggest structural decision: `ShowModelSelectorAsync` builds the entire picker inline in the 4,000-line `MainView.axaml.cs`; extract a dedicated control (mirroring `AppLauncherControl`/`NotificationCentre` ownership in `TopRail.axaml.cs:174-222`) so the Haven scene, tests, and Android (`MainView.Mobile.Layout.cs:166-182`) can share it.
- Two conflicting effort↔percentage maps exist (`MainView.axaml.cs:3640-3655` vs `Haven.Core/DomainLogic/ReasoningScale.cs`); RUNTIME-008 (line 1035) mandates the discrete documented mappings — reconcile first to avoid the rail pill, flyout slider and persisted `DefaultEffort` disagreeing.
- `ModelConfigurationControl` is currently unwired; it already contains the search/recents/recommended model list and `ReasoningScale` slider the spec asks for — candidate for revival rather than rewrite.
