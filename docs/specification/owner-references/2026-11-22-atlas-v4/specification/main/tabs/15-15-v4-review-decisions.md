# 15. v4 review & decisions

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.2dw4sn4oeuxw

<a id="h.m2c0droy0gkt"></a>

# 15. Atlas v4 review and decisions

<a id="h.4iewjn5ke5wg"></a>

Rapid alignment • 22 November 2026 • owner-editable review candidate

<a id="h.hoi3734jn8u3"></a>

## 15.1 Status and adoption

The owner requested rapid alignment of the remaining specification to the current Atlas, proactive ecosystem suggestions and an updated editable Atlas for a returned v4.1. This section records that review work; it does not silently adopt proposed features or certify production implementation.

The pinned v3.3 file is preserved as input provenance. The delivered v4 contains the 251 original screen identities plus 31 additive registrations, for 282 mapped routes. The owner edits v4 and returns v4.1; capture and compare that exact file, reconcile decisions, then use it as the design input for the next pass under AUTH-001 and ADOPT-001.

<a id="h.frhilhmt76e1"></a>

## 15.2 Approved alignment applied

Current labels are Decks, Frame, Flow, Virtual Device, Collection, Story Sequences and Voice & Vision. Code, Sites and Stacks remain separate destinations. Compatibility routes, requirement identifiers and historical provenance are retained. Connect remains the canonical communication app; Mail is its email surface/alias under the resolved Q2 decision. Sculpt is the full Blender fork, and Train's initial approved scope is fine-tuning, Dreaming, evaluation and checkpoint management.

Seven hubs are Coordinate, Create, Develop, Organise, Work, Experiment and Admin. Home remains a Haven management surface; Replay is a shared feature; Prototype belongs inside Develop. Customisation is off by default and preserves the saved arrangement when switched off.

Dashboard and New Tab retain separate routes, content and layout identities. The v3.3 Dashboard-to-New-Tab renderer alias is removed. Interaction-chapter persistence wording is corrected to agree with HOME-001/003/013.

The compact model picker and expanded settings share role selection and context state. Actual selected catalogue names are visible, including the supplied Qwen 4 27B fixture. Voice explicitly remains unconfigured in the absence of a named supplied voice model. Availability, hardware fit and runtime application are not inferred from fixture names.

Appearance retains the nine built-in theme identities and separate Mode (Light/Dark/Follow system), Brightness, Colour contrast and Off/global/per-app accent controls. Brightness defaults to neutral 50 only when missing; existing Contrast values are retained as Colour contrast. These adjustments affect chrome, preserve authored/guest content and remain subordinate to accessibility overrides. The owner-supplied image(20261122-134041).png and image(20261122-140356).png are the current default Glow Dark and Playful Dark material/layout references respectively; the Atlas retains both exact images as provenance and aligns their Dark previews and miniatures. Accent hues in those images are examples: app, host and supported model-control accents continue resolving dynamically under UI-004, without setting stored/global defaults, forcing a startup theme or fixing model identity/reasoning effort. These Dark screenshots do not invent new Light designs. The latest Glow correction uses image(20261122-152458).png as the intended depth/contrast reference and retains the earlier approved image; image(20261122-152414).png is defect evidence. Its ambient hue stays dynamic. Plain Settings category parents and a flat Settings brand surround replace conflicting inherited card/button fills. The extra Customisation & recovery shortcut is removed while canonical Layout/recovery access remains. The Appearance heading keeps Global Settings and Appearance and removes the redundant subtitle “Your theme, light and colour.” Custom/proprietary chrome must resolve the same theme roles as shared components; only the top application tab strip stays left-aligned, with centred in-page/secondary groups and accessible overflow. Rebuild Frosted Glass material definitions, component treatments and miniature from the soft frosted layering, true background blur, soft edges and floating rounded controls in the owner-selected pale/dark references Pasted image.png and Pasted image (2).png, replacing prominent borders and repeated rim/bevel treatments; 9to1 layouts and identity remain, and third-party flows, text, avatars and decorative imagery are not used as live 9to1 content. Rebuild Bubble material definitions, component treatments and miniature from the pale softly rounded raised matte panels, broad gentle shadows, quiet creamy near-white tiles and bold typography of Screenshot 2026-06-22 173831.png, replacing the mismatched gel/gloss, inset rims and saturated panel washes while retaining existing 9to1 layouts/content and dynamic accent tints; the reference adds no Maths dashboard features or newly invented Dark design. All first-party UI sliders use the earlier Professional Other diagram's thick rounded track and contrasting circular thumb, with both full-fill and progress/remainder forms under UI-013 and unchanged interaction contracts under IX-BASE-008. Other current theme materials and images remain review baselines while the owner designs them in the returned v4.1; no further theme designs are invented in this pass. The exact supplied Comic reference remains available, and fidelity or acceptance of its editable adaptation is not asserted.

New previews cover source-grounded Specialist work, Dreaming and Train candidates, sound assignments, shared task/plan/assumption/action records, compaction, mobile installation/navigation/development, device-specific Automation controls, Projector and independent customisation recovery.

The original Atlas specification links are replaced with mappings to this active native document. All 1,105 original requirement definitions remain; display-name changes do not rewrite identifiers or schemas.

<a id="h.9959nouhrg1l"></a>

## 15.3 Added Atlas screen register

These 31 entries supplement the original v3.3 screen register in section 9. Requirement links indicate design coverage and acceptance focus, not evidence that every clause has a connected production implementation. Original screens retain their identities; the file preserves the original embedded artwork/font assets and adds the exact Comic reference.

<a id="h.orbfx0fqp5ma"></a>

### Specialist · Knowledge workspace — specialist-knowledge

Requirements: KNOW-001–026, UIX-SET-015. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.motkoge0iv6i"></a>

### Write · Sourced Specialist note — specialist-sourced-note

Requirements: KNOW-019–020, PROD-004. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.jfyuqtoty3w6"></a>

### Settings · Dreaming & learning — settings-dreaming

Requirements: LEARN-001–012, UIX-SET-013. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.ahgajyibazre"></a>

### Train · Candidate review & rollback — training-candidate

Requirements: LEARN-010–012, PROD-010. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.kr5yuu7wzq2"></a>

### Settings · Sound bank & assignments — settings-sounds

Requirements: SOUND-001–012. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.3ajnc7sqzimv"></a>

### Home Launcher · Floating navigation — mobile-home

Requirements: MOB-004–008, MOB-016. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.mat5o68cva8c"></a>

### Mobile · More and pinned destinations — mobile-navigation

Requirements: MOB-004–008. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.7vhwjpui85v2"></a>

### Mobile · Hosted, standalone and hybrid — mobile-installation

Requirements: MOB-001–003. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.bn7o671wma0c"></a>

### Mobile · Code, Sites and Stacks — mobile-develop

Requirements: MOB-009–010, MOB-012–013. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.73e488pxohho"></a>

### Mobile · Games scene editor — mobile-games

Requirements: MOB-009, MOB-011–013. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.gyudso1c07im"></a>

### Mobile · Virtual Device viewer & input — mobile-virtual-device

Requirements: MOB-009, MOB-011–013. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.vemvl4ugxj84"></a>

### Automations · Desktop & mobile bindings — automation-device-bindings

Requirements: MOB-014–015. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.ch2khnj6ft07"></a>

### Projector · Experience gallery — projector-gallery

Requirements: ED-PROJECTOR-001–007, ED-PROJECTOR-009–011. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.je91ffrzm9om"></a>

### Projector · Phone controller & recovery — projector-session

Requirements: ED-PROJECTOR-008, ED-PROJECTOR-012–015. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.ja73sdovtakj"></a>

### Settings · Safe and unrestricted customisation — settings-customisation

Requirements: SHELL-009–015, HOME-015. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.q7q9merul48q"></a>

### Recovery · Independent customisation repair — customisation-recovery

Requirements: SHELL-011–012, SHELL-015. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.xsp6kx68kj11"></a>

### Actions · Task workspace — spaces-workbench

Requirements: RT-020–027, ACTSPACE-011–018, SPC-078–083. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.jcy0ybmdk7ga"></a>

### Spaces · Assumptions Bank — spaces-assumptions

Requirements: ASSUMP-001–014, RUN-212–215. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.mltstpp6c9a"></a>

### Spaces · Action Log — spaces-work-log

Requirements: SPC-078–083, RUN-206–208. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.azqzq65ixqjg"></a>

### Settings · Context compaction — settings-compaction

Requirements: RT-026, RUN-211–217, SPC-046–047. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.uytguk8tpwia"></a>

### Spaces · Response presentation — spaces-presentation

Requirements: NCONV-001–012. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.f0kalf5r5mjl"></a>

### Write · Linked task output — spaces-work-output

Requirements: ACTSPACE-014–015, SPC-080–082, PROD-004. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.qos9vg11crgt"></a>

### Home · Seven hubs — home-hubs

Requirements: PROD-010–013, HOME-007. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.y4xdexryz1tn"></a>

### Home · Customise hubs — home-hubs-customise

Requirements: PROD-012–013, HOME-007. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.qgvyvthsbfdp"></a>

### Hubs · Connected project — hub-project

Requirements: PROD-011, STORE-001, STORE-008. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.kfh7q2hy80xz"></a>

### 9to1 · Expanded model settings — host-model-settings

Requirements: PICK-001–009, RES-003. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.rgq18jxbgeyp"></a>

### Comic · Exact owner reference — theme-reference-comic

Requirements: UI-003, UI-005. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.9fvm2qu020dm"></a>

### Welcome · Choose your intelligence — onboarding-intelligence

Requirements: HOME-011, UI-003, UI-005. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.upm83hfs2fed"></a>

### v4 review · Start here — atlas-v4-review

Requirements: AUTH-001, ADOPT-001. Status: editable design preview; source/state checks completed where applicable; rendered and native-runtime acceptance remain open.

<a id="h.f8wq9yj6ngh"></a>

### Sculpt · Blender-derived 3D workspace — sculpt-workspace

Requirements: APP-SCULPT-001, ED-SCULPT-001–004, PROD-010. Status: editable Blender-derived workspace preview with local object/mode/panel state; actual Blender donor/runtime and visual acceptance remain separate.

<a id="h.ay8pu3gu1gyp"></a>

### Spaces · Chat with Temporary enabled (review alias) — spaces-temporary

Requirements: DATA-024, STORE-105, SPC-041–042, SPC-084–086, DEC-004. Status: fixed-fixture personalisation and conversion preview; no real messages, memory, plugin, history or training service is invoked.

<a id="h.y7ogxs2xcosu"></a>

## 15.4 Ecosystem proposals — pending owner review

P1–P7 are recommendations, not approved requirements. Marking a proposal Interested or adding notes in the Atlas expresses review direction; reconcile the returned v4.1 before changing normative scope. Suggested priority: P2 Use in…, P1 Related work, then P5 Shared review inbox, because they make existing canonical records easier to use across apps.

<a id="h.7cf4jpatykfz"></a>

### P1 · Related work and change impact

Show backlinks, owning apps, live/pinned revisions and the effect of changing a shared source. Begin with a Related work inspector, not a second resource store. Existing foundation: PROD-011; STORE-001/008; Flow source bindings. New scope is the shared user-facing workflow described here.

<a id="h.sdsyo0vbxg4f"></a>

### P2 · Use in…

One selection-aware chooser across Files, Spaces and editors. Offer Link live, Pin this revision or Create independent copy only where supported. Example: Data table → Present chart → linked Flow region, or notes → Specialist source. Existing foundation: RT-003; ARC-003; STORE-005. New scope is the shared user-facing workflow described here.

<a id="h.szl6k88c5b55"></a>

### P3 · Workspace recipes

Save a reusable set of app views, canonical project references, a Space/Assistant and pane arrangements. Adapt it to desktop, mobile or Projector. Opening a recipe does not start work or grant access. Existing foundation: PROD-011; ED-PROJECTOR-003/013. New scope is the shared user-facing workflow described here.

<a id="h.12jrht2k7cpt"></a>

### P4 · Prepare for offline

One dependency preflight for authorised files, linked sources, fonts/media, tools and eligible model routes. Readiness requires the chosen manifest to be verified; cloud-only dependencies remain explicitly unavailable. Existing foundation: PROD-007; STORE-202. New scope is the shared user-facing workflow described here.

<a id="h.10ez5aapeyh"></a>

### P5 · Shared review inbox

A Home view over pending AI changes, approvals, stale sources, failed exports and unresolved assumptions. Each entry opens its owning record; marking it read never approves the action. Existing foundation: SPC-072; Action Log; Assumptions Bank. New scope is the shared user-facing workflow described here.

<a id="h.o3gl74eimh12"></a>

### P6 · Continue on…

A deliberate device/display handoff preserving artifact identity, position and task context. Show receiving scope, capabilities and acknowledgement, and distinguish moving a view from moving execution. Existing foundation: STORE-201–204; Mesh; Projector. New scope is the shared user-facing workflow described here.

<a id="h.hja71le03d8f"></a>

### P7 · Make repeatable

Convert supported steps of completed Actions work into a disabled Automation draft with parameters, source references, budgets and approvals. Remove old receipts and one-time secrets; require preview/test before ordinary activation. Existing foundation: APP-FLOW-001; ED-AUTO-001. New scope is the shared user-facing workflow described here.

<a id="h.3viddtdpfk4o"></a>

## 15.5 Owner decisions — confirmed

<a id="h.vfkvqjpvt4rd"></a>

### Q1 · Sculpt scope — closed

Owner decision, 22 November 2026: Sculpt is the Blender fork, with the full compatible Blender-derived application scope. It is not limited to a focused Games asset workspace. APP-SCULPT-001 and ED-SCULPT-001–004 define the donor, canonical-source, control and acceptance boundaries.

Earlier review question and recommendation (superseded): Full 3D modelling/sculpting/animation app, or a focused asset workspace for Games? Recommendation for review: Start with a distinct 3D asset editor connected to Games; choose its detailed feature/donor contract explicitly. The approved hub slot alone does not define its features.

<a id="h.bilc7q51yyfx"></a>

### Q2 · Mail and Connect — closed

Owner decision, 22 November 2026: Mail and Connect are the same application. Retain Connect as the canonical app/package name; Mail is its email surface and compatible alias. Keep email, native chat, SMS/RCS and calls together under one Connect identity, with their correct protocol entities and existing capabilities. No separate Mail package or duplicated account/message store is introduced.

Earlier review question and recommendation (superseded): Separate email and messaging/calls apps, or one umbrella app? Recommendation for review: Keep Mail for email and Connect for messaging/calls. Preserve existing native chat, SMS/RCS and calls while the name and hub placement are settled.

<a id="h.6s5wz9wzvcdn"></a>

### Q3 · Temporary chat personalisation — confirmed

Confirmed by owner, 22 November 2026: Temporary is an on/off toggle within ordinary Chat, with the associated Personalised/Unpersonalised setting in the common Options panel. It is not a separate destination or response mode. The personalisation choice covers eligible existing memories, custom instructions and plugins; neither choice writes memory while temporary, and the choice locks after the first committed message. Before first send, Temporary can be toggled on or off within the same draft. After start, switching it off requests explicit local Save conversion; cancel or failure leaves it on. A committed regular chat cannot become Temporary. Successful saving applies normal account/workspace settings. See DEC-004 and SPC-084–086.

<a id="h.x1467iwwpkp2"></a>

### Q4 · Train breadth — closed

Owner decision, 22 November 2026: Approve fine-tuning, Dreaming, evaluation and checkpoint management as Train's initial scope. General pretraining and arbitrary direct manual weight editing are deferred beyond that initial scope and need a later separate contract. Required real parameter updates, capability checks, independent evaluation, promotion and rollback under LEARN-001–012 remain intact.

Earlier review question and recommendation (superseded): Fine-tuning, Dreaming, evaluation and checkpoint management initially; or also general pretraining and direct weight editing? Recommendation for review: Deliver the currently defined fine-tuning/Dreaming/evaluation/checkpoint scope first. Define the broader work separately before adding it to initial scope.

Existing decisions about custom material/accent composition (DEC-001), Study numeric scoring/evidence thresholds (DEC-005) and Industrial versus Metallic (DEC-007) remain visible in section 10. They do not block unrelated alignment. No answer is inferred from an unchanged or unreviewed screen.

<a id="h.9as5t3tb64f"></a>

## 15.6 Verification and next pass

All executable scripts in the compiled HTML pass JavaScript parsing; embedded JSON parses; the map covers all 282 screen identities and every mapped requirement resolves to an existing definition. Pure-state checks cover hub customisation, source and learning policy independence, candidate/rollback gates, mobile controls, trust narrowing, recovery gates, task revisions, assumptions, compaction, output history, Sculpt view state and Temporary personalisation/conversion gates. These are source/state checks, not rendered-browser evidence.

Local HTML preview was blocked in this environment. Visual geometry, full event ordering, keyboard/focus behaviour, accessibility measurements and an actual edited-file save/reopen remain unverified. Connected models, native app engines, sync, installation, permissions and hardware performance were not exercised. Existing screens also retain their documented fixture limitations.

The Atlas opens to an owner review desk with Open/Edit screen shortcuts, Reviewed checkboxes, four confirmed decisions with refinement-note fields and proposal-status selectors. These annotations are included in portable editor state. After edits, save the HTML as 9to1_Interface_Atlas_v4.1_Editable.html and return it. The next pass compares the complete file, including styles, images, authored screens and preferences, before adopting changes.
