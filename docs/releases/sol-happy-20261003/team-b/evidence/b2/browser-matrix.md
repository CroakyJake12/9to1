# B2 browser capability matrix

Canonical document: `1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg`, live revision `AHj4eMRBcI3x5FFYbDM2m-hiWUCk_SbZphbsfCtNVrsp0bqkXxEmCGQMUSBbZxWMB76lDNvgrMwyepTjeCqVIpuSFZx5BKabswqtngHgOTo`, modified 2026-10-03T16:54:11.626Z. Repository baseline: `98a08827c9fbc486fe987da73f4aee8398120d31`. These files are evidence and mappings, not replacement product authority.

The ledger preserves every nonempty app clause, API line and schema line conservatively. Clause counts are **not atomic requirement totals**. Compound statements require multiple assertions before acceptance; optional/conditional prose is explicitly marked for applicability review. No browser requirement is VERIFIED. A manual procedure is authored coverage, not a test result.

## Per-app operations and implementation seams

| App | Required operation groups | Actual reusable domain source inspected | Browser implementation/validation |
| --- | --- | --- | --- |
| Write | Launch/backstage; true paragraph/run/style layout; rich selection/IME/clipboard; contextual +/Eye/overflow/pinned actions; nested Sections; Continuous/Pages/Book; Locked/Unlocked objects; custom styles/tables; citations/review; digital/scanned/mixed PDF editing; native/DOCX/ODT/Markdown/TXT/PDF; search/recovery; Variants | `shared/src/Haven.Application/Write/WriteDocumentEditor*.cs`, `IWriteDocumentEngine.cs`, Core Notes structures; actual production selection/formatting/comment domain probe | MISSING production browser editor. BLOCKED on B1 CUI/editor host, approved shared engine contract, Writer/PDF renderer and authenticated services. A DOM contenteditable substitute is expressly nonconformant. |
| Present | Slide navigator/sorter/outline; stable slide/object/notes IDs; object geometry/tree; master/layout/theme inheritance; six text-statistic scopes; search/proof/review; Actions runtime/Transitions; built-in/custom components; Generative UI; sandboxed code; media; GLB/glTF; structured ink; Presenter/Audience privacy; rehearsal/recording; route-based video/audio/podcast; PPTX/ODP compatibility; static outputs; Variants | `shared/src/Haven.Application/Present/PresentEditor*.cs`, `PresentPlaybackSession.cs`, Core Present, native `Present/present-engine` | MISSING production browser authoring/runtime. BLOCKED on shared renderer/Actions/components/media/code providers and service session contracts. No independent animation engine permitted. |
| Canvas | Infinite/paged creation and confirmed reversible conversion; bottom Select/Pan/Pen/Eraser/Insert/Tools/AI; presets/custom pens; natural/quick eraser; full registered insertion catalogue; quick settings/Smart Pen; stylus fidelity; layers/object geometry; live Boards embeds; semantic AI session restoration; history/recovery; donor imports/exports; Variants | `shared/src/Haven.Application/Canvas/CanvasArtifactSession.cs`, `CanvasArtifactCodec.cs`, interaction controller; Canvas app and Rnote donor integration | MISSING browser drawing surface. BLOCKED on shared Rnote browser/runtime capability and canonical authenticated artifact service. Device pressure/tilt absence may narrow input controls only; existing stored samples must survive. |
| Boards | Real launch/board picker/nested hierarchy; Native/Write/Canvas/Chat/ScopedChat pages and source subpages; Edit/View independent of Locked/Unlocked; drawing in both layout modes; contextual island; H1–H6/custom styles; Graph and table/image editing; Data-backed views; same-artifact embedded editing; ownership-safe deletion; concurrent/recovery behavior | `Boards/contract/RichBoardSession.cs`, `HavenRichBoardV3.cs`, contract collaboration; CUI app/HUI projection; shared objects remain Home-owned | MISSING full browser workspace. BLOCKED on registered CUI runtime, embedded shared editors, Data and Dulche/source services. AppFlowy-board alone does not establish full AppFlowy workspace parity. |
| Data | Four data paradigms; mature grid/formulas; rich/nested values; cube slicing; stable records/schema/keys/relations/ER/Normalise; native SQL target and typed execution; protected live mutations; pipelines/refresh; Eye→Visualise catalogue and multi-sheet mappings; charts/pivots; source conversion and provenance; Forms bindings; per-action AI approval; Variants | `Data/App/DataGridSession.cs`, `DataQuerySession.cs`, Calc/DuckDB interfaces/workers; `Data/Cui/DataCuiWorkspaceController.cs`; Forms storage integration | MISSING production browser analytical editor. Existing 10×8 slice cannot be claimed complete. BLOCKED on hosted donor execution and approved shared Data contracts. No mini spreadsheet engine or browser SQL simulation. |
| Forms | Canonical builder/preview/publish; complete typed field catalogue/TableInput/repeating groups; Form/Test/Quiz/custom mode; deterministic/regex/math/AI/hybrid marking; grading; visual LaTeX and graph responses; question banks/timers; bidirectional Data schema; shared logic graph; layout/themes; versioned responses; multiplayer/team/buzzer/knockout/reconnect; anti-cheat/fairness/privacy; AI/events | `shared/src/Haven.Application/Forms/IFormsSubmissionStore.cs`, Infrastructure Forms store, existing Desktop `FormsPage.cs`, Data Forms runtime test | MISSING browser builder and published runtime. Existing feedback submission UI does not establish specified Forms product depth. BLOCKED on Team A FormProject/runtime contract and Team C server-authoritative response/session/marking/Data services. |

Paths above are relative to `/workspace/team-b-worktree/9to1 Workspace/` unless explicitly prefixed.

## Every app inherits these browser gates

`BP-ALL-*` and `shared-requirements.json` retain General Rules, Agentic Contract, common Apps/Web, Home Shared Productivity Engine, applicable Terra-form and all Sol Happy clauses. They require:

- Real CAKE ID/organisation sessions and shared permission brokerage; fresh sign-in/out/account switch, expiry/revocation and private-cache separation.
- Create/open/edit/save/close/reopen/history/import/export/share/recovery with actual durable revisions, expected IDs/content, backend inspection and a second client.
- Direct artifact URLs, refresh/Back/Forward, restored selection/branch context; source rename/move/delete and independent source permissions.
- Browser→desktop→browser edits of the same artifact; independent concurrent changes preserve both edits; same-entity conflicts remain explicit.
- Shared object/API semantics and full donor manifests; typed UI/API parity; contextual AI Read-only versus Write, provenance and permitted scope.
- Keyboard/screen-reader/200% scaling, desktop-equivalent and narrow/touch layouts, themes/contrast/reduced motion, focus return and populated failure states.
- Measured large populated workloads and bounded caches/subscription cleanup. Supported browser versions and quantitative baselines remain an explicit SH-13 decision; local Chromium availability does not establish the support matrix.
- Exact frontend/backend/deployment/source identities and final integrated-candidate rerun. Dev-server, model/domain, mock, static screenshot and source inspection results remain distinct from deployed acceptance.

## Exact applicability and exclusions

No B2 application has a web exemption. The only current product exemption is Mini Computer in the global Web Apps section; it is outside B2 and does not relax productivity requirements.

Native Install Home bootstrap does not apply to ordinary browser operation. Exact basis: paragraph **103827**: “Browser-hosted web surfaces cannot require a local Home installation; they instead use the corresponding authenticated 9to1 shared web services and capability negotiation defined for web clients.” This substitutes authenticated shared-service negotiation, not private replacement services.

Canvas has a specific shared AI-bar placement rule, paragraph **510914**: “The bottom-toolbar AI item is the sole normal Canvas entry point for expanding the shared Dulche contextual AI bar. Canvas MUST NOT show a second permanently expanded AI bar while the bottom toolbar is present.” Hidden sessions retain identity/lifecycle and the sparkle restoration behavior.

Boards donor proprietary AI/accounts/billing/paid services are excluded from direct copying by paragraph **526911**. Dulche/native replacements or concrete non-applicability decisions remain required. This is not a shortened nonproprietary AppFlowy feature allowance.

Variants TF-V01 names Write, Data, Present and Canvas in B2; Boards/Forms are not named in that seven-app mandate. Their existing history/conflict/recovery requirements remain mandatory. No invented exemption is applied to other requirements.

The Android/Go temporary Terra-form limited-authoring rule does not narrow browser requirements or carry automatically into Sol Happy.

## Coverage files and limits

`requirements.json` maps all six app sections; `shared-requirements.json` retains common obligations; `procedures.json` contains paragraph-specific expected oracles and family journeys; `candidate-domain-tests.json` inventories actual existing test symbols and source hashes. Every procedure remains **NOT_RUN** until genuinely executed. Coverage retains all compound clauses but fixture/action/assertion expansion is still needed for full executable acceptance. This incompleteness remains visible and is not counted as passed coverage.

No Team A model or Team C service is replaced. New production browser controls are not approved until B1 exposes the existing compatible CUI seam. No app source was edited by B2.
