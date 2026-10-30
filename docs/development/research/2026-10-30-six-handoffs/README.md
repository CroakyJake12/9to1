# 9to1 — six research and design hand-offs
**Prepared:** 30 October 2026  
**Repository inspected:** `CroakyJake12/9to1` at `4e82ef6871c5071af34624fd767dd123cf79d5f3`  
**Scope:** research, source inspection, design proposals and a release-notes draft. No repository edits, worker instructions, builds, installation, deployment or release were performed.

## What this answers

The supplied OpenCode session explicitly proposed six PC-free hand-offs. This pack answers each, rather than assigning fresh implementation work to another agent. The initiating message is `msg_18cb3b2a6001unsfyPy2AQ7Pxv`, message index 2171 in the supplied JSON export. Its checkpoint was `96454af6`; the subsequently inspected repository revision is the longer hash above. Do not confuse those two snapshots. [T1, R1]

| File | Deliverable | Status |
|---|---|---|
| A-unified-devtools.md | Neutral inspection model, adapters, F12 behaviour and acceptance plan | Design proposal grounded in inspected code |
| B-licensing-security.md | Donor licence decisions, private-service boundary and packaging notices | Research/engineering assessment; not legal clearance |
| C-engines-and-duckdb.md | Firefox ARM64 availability, protocols, embedding limits and Python dependency pinning | Upstream research and a proposed packaging plan |
| D-model-browser-parity.md | Donor-derived features, real UX27 mappings and reusable-code evidence requirements | Initial manifest for the supplied assessment's scope |
| E-files-storage.md | Three-root Files navigation and safe workspace lifecycle | Owner requirements plus explicitly marked engineering proposals |
| F-arm64-release-notes-DRAFT.md | Held-package release-note text and release gates | Draft; no new binary or acceptance evidence |
| SOURCES.md | Source catalogue, fixed revisions and evidence boundaries | References, not a software bill of materials |

## Evidence vocabulary

**OWNER REQUIREMENT:** a user statement in the supplied session/current conversation. **SOURCE-INSPECTED:** actual repository content was read at the identified revision; this does not prove it runs. **PROJECT-REPORTED:** a project note or agent claims a result that was not independently rerun here. **UPSTREAM-DOCUMENTED:** an official external source documents a capability. **PROPOSED:** a design or test recommended in this pack. **NOT RUN:** no execution evidence from this task.

These memos do not supersede the canonical product specification, CUI-only requirement, real-donor requirement, resource guards, owner stop instruction or package hold. New interfaces, local MB-/DT-/FS- identifiers, paths and tests are proposals, not existing APIs or silently approved product requirements.

## Principal outcomes

The open-app/private-service choice is already recorded in the repository. The remaining issue is the precise code and distribution boundary, not whether all of 9to1 must be closed. AGPL does not automatically open unrelated services, and a process boundary is not an automatic licence exemption. Existing GPL-compatible donor work should not be discarded merely because some services are private. See B.

A browser binary, a debug protocol and an embeddable rendering surface are different deliverables. Official ARM64 Firefox artifacts exist, but native CUI integration still needs a real prototype. Current Firefox is not a CDP-compatible drop-in. See A and C.

The model-browser manifest preserves the requirement to reuse actual donor logic where appropriate. It is not authorisation to replace donor integration with a list of matching feature names. Files auto-provisioning and the Boards destination picker must coexist. See D and E.

## Outstanding verification

No native UI, target ARM64 installation, whole-app regression, Gecko/CUI rendering integration, exact pinned Gecko toolchain build, wheel download/hash verification or legal clearance was performed. The installed custom Unsloth source was not independently available here; its detailed findings remain attributed to the repository assessment. The release-note draft must not be published as an accepted release.

---

# A — Unified DevTools design memo
**Status:** PROPOSED DESIGN, with specific source-inspected foundations. No implementation or acceptance claim.

## 1. Recovered goal and current foundations

The requested experience is one F12 inspector across CUI application surfaces and ordinary web pages, with engine details behind adapters rather than separate user experiences. The session also asks for live appearance editing for mock-ups. This does not require misrepresenting CUI as actual HTML. [T1]

The current `CuiLiveTreeInspector` captures the real control tree and exposes layout, accessibility, source locations, bindings and actions. Its capture assigns path-based element IDs such as `root/0/1`, and includes a tree revision. That means a path alone must not be treated as a durable identity across insertions/reorders. This is an inference from the inspected implementation, not a reproduced defect. The DevTools source directory contains hot-reload, linter, theme and performance components; their presence is not proof that the current Haven launch path wires them correctly. [R4]

The inspected Linux Chromium implementation of `OpenDeveloperToolsAsync` throws `NotSupportedException`. The Windows search result instead shows a call to `OpenDevToolsWindow`; the Linux gap must not be generalised to every platform. [R5]

## 2. Proposed architecture

```text
Active Haven tab / standalone CUI surface
                    |
              target resolver
                    |
        one CUI-authored DevTools shell
                    |
        versioned inspection contract
           /            |            \
    CUI adapter    Chromium adapter    Gecko adapter
    real tree           CDP            RDP; BiDi where appropriate
```

Keep the existing CUI diagnostics and engine transports. Add adaptation and host routing rather than a second disconnected inspection tree. F12 resolves the active target; switching tabs switches target identity, subscriptions and selection atomically. Closing/restarting a target invalidates its handles. A pinned-target mode may be provided, but its target must remain visible and explicit.

### Neutral records (proposed, not existing classes)

| Record | Minimum fields and purpose |
|---|---|
| TargetDescriptor | Target ID, profile/workspace scope, surface kind, engine/runtime version, session epoch, available capabilities |
| NodeHandle | Target ID + session epoch + backend node ID; associated document/tree revision; separate authored/source identity when available |
| NodeSnapshot | Handle, parent, ordered children or lazy-child token, kind, name, text summary, typed attributes, source reference |
| LayoutSnapshot | Bounds, margin/padding/border where meaningful, coordinate-space label, viewport/scroll/zoom/DPI transform, clipping |
| StyleSnapshot | Property, authored value, effective value, origin, inheritance/priority information, source location and editability |
| DiagnosticSnapshot | Accessibility, action/binding status, logs and performance data, with measured timestamps and explicit unavailable fields |
| MutationRequest/Result | Target and revision preconditions, requested operation, permission scope, preview/persist mode, result revision, inverse/undo support or truthful non-reversibility |

Use actual semantic types. For web nodes retain CSS cascade and computed-style facts; for CUI retain tokens, bindings, instance properties and source definitions. Do not fabricate CSS specificity for a CUI binding, or silently flatten richer engine information to fit the neutral layer. Engine-specific extensions remain accessible alongside the shared panels.

### Proposed adapter operations

The initial read contract covers attach/detach, capability discovery, paged tree capture, child expansion, layout/style/source/accessibility inspection, hit testing, highlighting, and subscriptions to tree/layout/navigation/lifecycle changes. Each operation is target-scoped and cancellable.

Writes are separate operations: validate a proposed text/attribute/style edit; apply it under current target/revision and permission checks; report the real result; revert a supported preview. Runtime evaluation is a separately granted capability, never an implicit part of “read styles”. The frontend must not execute arbitrary returned strings as host commands.

These are behavioural interfaces, not a claim that the three backends have identical APIs or can implement every operation. Adapter capability negotiation and typed errors are mandatory.

## 3. Shared F12 experience

Elements, appearance/layout, source location, accessibility, logs and performance have common navigation and selection conventions. CUI adds bindings/actions/themes without replacing the common shell. Web-specific network, JavaScript debugging and storage remain available where the adapter implements them; a missing panel is labelled unavailable rather than filled with illustrative data.

For mock-up editing, default to a **temporary preview** in the selected target. Clearly distinguish it from modifying a source file or saving a document. Persisting an edit requires an explicit supported operation, source mapping, a reviewable diff and the usual write authority. CUI document edits use canonical application commands; inspecting a control does not grant billing, account or document privileges. Preserve author content and selection where possible; report unsupported undo rather than promising to reverse arbitrary script side effects.

The renderer's coordinate transformation must be used once and labelled. A highlight rectangle and a hit-test point must agree at the same viewport/zoom/DPI. Keyboard focus belongs to the active target or inspector deliberately; hidden tabs cannot receive input by mistake. [R6]

## 4. Protocol choice

Mozilla removed its experimental CDP implementation in Firefox 141. A new adapter must not assume modern Firefox speaks Chromium CDP. Mozilla documents RDP for its developer tools and WebDriver BiDi through its remote-agent ecosystem. Use RDP for the Firefox-specific inspector integration, and assess BiDi operation by operation for shared automation. “CDP or JWP” was not the right framing. [W1, W2]

Do not promise a universal stable wire format. Pin the engine and adapter compatibility profile; capability-test it; invalidate old sessions on navigation/crash. Reuse protocol/front-end donor code only with its applicable licences and recorded provenance. A neutral shell does not imply rewriting the browser's DOM, CSS or debugging implementation.

## 5. Implementation sequence and acceptance gate

First wire the existing CUI inspector to the real current host. Then prove one Chromium page and one Gecko page through read-only adapters. Only after those targets are correct, add guarded preview edits and persistent source/document operations. Keep advanced debugging features as an explicit incremental matrix, not a silent reduction in promised parity.

Proposed tests, all NOT RUN here:

| ID | Scenario | Required observable outcome |
|---|---|---|
| DT-01 | F12 on CUI, Chromium and Gecko targets | Same shell; correct target identity and capabilities; no static/demo tree |
| DT-02 | Insert/reorder a node after capturing a handle | Stale edit is rejected or explicitly rebound to proven identity, never applied to a different node |
| DT-03 | Resize, scroll and change scaling | Highlight, bounds and hit tests agree with the actual surface |
| DT-04 | Navigate or crash the browser while inspector is open | Old epoch becomes invalid; no writes to reused node IDs |
| DT-05 | Preview a style, cancel, reopen | Preview state and persistence behaviour match the labelled mode |
| DT-06 | Switch profile/target with pending requests | No cross-profile logs, source, cookies or mutations leak into the new target |
| DT-07 | Unsupported capability and interrupted transport | Truthful unavailable/error state; existing application remains usable |
| DT-08 | Real native keyboard/pointer and accessibility checks | Independent native evidence, not just serialized snapshot tests |

**Ready to hand off:** contract and routing design. **Not established:** complete native wiring, full RDP feature coverage for the chosen engine revision, or tested runtime behaviour.

---

# B — Licence posture and protected-service boundary
**Status:** researched engineering decision memo, not a binding legal opinion or approval to ship.

## 1. The distribution decision is already made

The later project note records the owner's choice: main applications are free/open source; monetisation, identity and subscription services may contain private components. It also records “embed what we aren't modifying, fork what we are.” The root repository contains the GPL version 3 licence text. This is not permission to relabel all existing code Apache, nor does the licence text alone resolve every file's version-or-later status or copyright ownership. [R2, R3, T1]

The note's blanket exclusion of Unsloth Studio on the assumption that it would open the paid/gated server side is too broad. The right investigation is which code constitutes the covered or combined work, what is distributed, and which modified AGPL program users interact with—not whether there happens to be another private server on the network.

AGPL section 13 adds corresponding-source access for users interacting remotely with a modified covered program. It does not simply say “the network must be open source”. GPL and AGPL also have executable-distribution obligations. For a combined GPLv3/AGPLv3 work, the licences provide a combination mechanism; it is not equivalent to relabelling all components under whichever licence is preferred. [W3, W4, W5]

## 2. Component decisions

| Component | Evidence and immediate conclusion |
|---|---|
| Rnote Canvas donor | Its pinned workspace metadata declares GPL-3.0-or-later and version 0.14.2; the project reports the Canvas bridge links it. Preserve this actual donor integration in the open application rather than replacing it solely because separate paid services exist. Complete distribution/source compliance before release. [R2, R9] |
| Gecko/Firefox donor | The pinned root licence redirects to a detailed licence inventory and explicitly excludes trademark rights. Apply the licences governing the actual files, preserve notices and publish required modified/covered source. Do not infer from a list of GPL/MPL/LGPL texts that every file permits any global election. [R10, W6] |
| AppFlowy Board library | The exact inspected component licence offers AGPLv3 and MPL2. Record an MPL2 election for covered library code where used. This is the Kanban Board component, not proof that the full AppFlowy application has the same licence. The project currently reports no actual linked Board donor code. [R2, R11] |
| Unsloth core | The project assessment and upstream distinguish the Apache-licensed core from AGPL Studio. Permissive core APIs may be reusable, but availability of a particular download/quantisation function must be demonstrated rather than inferred from the root licence. [R7, W7] |
| Unsloth Studio/model browser | The local custom-source assessment reports AGPL-3.0-only Studio and hub code. Keep adoption conditional on a concrete source/linkage/distribution review. It is not categorically incompatible with an open-source 9to1 app plus genuinely independent services. Rewriting the UI does not remove obligations from copied backend code. [R7, W3] |
| DuckDB | Project records MIT for the pinned donor; preserve applicable notices. Source pinning and the runtime Python wheel are distinct supply-chain records. [R2, R8] |
| Private service code | Establish independent ownership and boundaries; do not ship a proprietary combined derivative merely by putting it in another folder. Non-distributed internal GPL use is different from conveying a combined executable, and AGPL network use adds its own condition. [W3, W5] |

### What a boundary can and cannot establish

The FSF's FAQ describes the distinction between an aggregate of independent programs and a combined program as a fact-sensitive legal question. Pipes, sockets or separate processes often connect independent programs, but tightly coupled exchange of internal structures can point the other way. Therefore “separate process = always safe” and “uses the same network = all source must open” are both unsound shortcuts. [W4]

For 9to1, document the actual imports, linkage, copied code, generated interfaces, runtime communication and distribution units. Have the precise arrangement reviewed before a public release where proprietary licensing depends on that boundary. This memo does not decide a disputed derivative-work question from a diagram.

## 3. Security architecture that matches the owner's goal

**Proposed arrangement:** open CUI applications and appropriately licensed local donor components call independently implemented identity, entitlement and billing services through narrowly scoped APIs. Keep payment/provider secrets and account authority on the trusted service side, not inside a desktop binary or published source archive.

OWASP recommends authorisation checks on every request; hiding the client implementation is not a substitute. [W8] Applying that principle here: the service decides the permitted account/resource/action, permitted spend and real entitlement from authoritative server state. A request is a request, not proof of permission. Client-side “paid=true”, arbitrary account IDs or user-supplied prices must not confer privileges. Use request reconciliation to avoid duplicate consequential operations and retain auditable outcomes.

Keep source-code availability, secrets and customer data separate. Publishing source need not publish production credentials or user records; however, “security-sensitive” is not an automatic exception allowing required corresponding source to be withheld. Likewise, an owner-controlled local application cannot provide a trustworthy remote entitlement boundary merely through hidden local checks.

These are proposed defensive requirements, not an audit of the current services. No bypass testing or production account operations were performed.

## 4. Notices and corresponding source for an ARM64 .deb

Debian policy specifies copyright/licence information under `/usr/share/doc/PACKAGE/copyright`. Use the actual binary package name, not an assumed `9to1-desktop` name. [W9]

For a digital release, the proposed packaging gate is a version-matched binary and corresponding-source offering with preserved donor notices, required licence texts, patches, build scripts and dependency information sufficient for the applicable licences. An upstream homepage or moving `main` link is not a reliable substitute for the exact released source. The precise delivery route must satisfy the licences; this memo does not substitute a casual “email us” sentence for a compliant source offer. [W5]

**Draft notice text — placeholders must be resolved before use:**

> This package contains 9to1 and third-party components identified in the accompanying licence manifest. Package version: [exact version]; source revision: [exact revision]; architecture: [actual architecture]. The corresponding source for this build, including applicable modifications and build instructions, is available at [verified release-specific source location]. Component licences and copyright notices are available in [actual installed notice location]. Licence-specific rights and warranty terms are set out in those accompanying licences.

For a modified AGPL service that supports remote interaction, add a prominent, functioning source-access mechanism for users of that version where section 13 requires it. Do not describe a merely planned source endpoint as available. Keep Firefox branding permissions separate from software-licence compliance. [W3, R10]

## 5. Release-blocking checks

Confirm per-component/file scope, exact binary/source correspondence, actual linkage to private components, notices and third-party obligations, accessible source delivery, and any applicable installation-information requirements. Record exceptions only when supported by licence text or permission from the rights holder.

**Decision recommendation:** preserve open-app GPL-compatible donor use; reconsider the categorical Studio rejection; keep private services genuinely independent; resolve obligations through a concrete integration plan rather than either indiscriminate rewriting or blanket claims of safety.

---

# C — Browser engines, ARM64 artifacts and DuckDB pinning
**Status:** upstream research plus a proposed integration/packaging plan. No binary download, signature verification, compiler run or ARM installation was performed.

## 1. Does upstream publish pinned ARM64 Linux Firefox artifacts?

**Yes.** An inspected Mozilla candidate directory for `153.0-candidates/build1` contains `linux-aarch64/`, source artifacts, SHA-256/SHA-512 checksum manifests and detached signatures. This is a concrete historical availability example, not a recommendation to ship Firefox 153 or a claim that it is the latest secure release on 30 October 2026. [W10]

Candidate availability does not establish that any selected binary matches the repository's Gecko source pin. Nor does a candidate directory imply production acceptance. Select a security-appropriate supported release in the implementation/release process, then record the exact version, candidate build number where applicable, locale, architecture, artifact path, source changeset and verification evidence.

**Proposed digest discipline:** retain the complete original manifest and detached signature, validate using an independently trusted Mozilla signing-key identity, select the exact artifact entry and verify downloaded bytes, then record the result in a versioned engine-artifact manifest. A checksum fetched alongside an attacker-controlled binary is not by itself publisher authentication. Never invent a digest or mark a verification complete merely because a directory lists `.asc` files.

Updates should create an explicit new manifest and rerun adapter/rendering tests; do not follow an unversioned “latest” URL silently. Signed metadata and local digests serve different purposes and both outcomes should be recorded.

## 2. Is Firefox CDP-compatible, or is JWP required?

That framing needs correction. Mozilla removed its experimental CDP support in Firefox 141. Its own DevTools uses RDP; WebDriver BiDi supplies modern automation functionality. Choose APIs by operation and demonstrated capability, not by treating the older JSON Wire Protocol as a full replacement for browser developer tools. [W1, W2]

Proposed division: the Chromium adapter uses CDP; the Gecko inspector uses RDP for needed inspector/debugger operations, with BiDi where useful for supported automation. Pin adapter compatibility to an actual engine revision. A BiDi connection is not proof that every CSS, DOM, source, debugger, network or performance panel has parity. Test the real operation matrix.

## 3. Can Gecko build for the ARM laptop?

Mozilla documents Linux/AArch64 as a supported target and describes cross-compilation using an AArch64 target and bootstrap-managed sysroot. A supported target and a native build host are separate concepts. The existing x86-64 development machine is a reasonable candidate cross-build host; whether the exact pinned tree builds with the installed toolchain remains unverified. [W11, W12]

Before a build, record source changeset, supported build configuration, actual compiler/Rust/Python/toolchain versions, sysroot/ABI, resources and target requirements. Follow the repository's serial SDK/resource guards. Do not start installing global toolchains, stopping Strata, changing engine versions or consuming the native build slot on the strength of this research memo.

## 4. Does a published Firefox binary solve CUI embedding?

**No—not by itself.** Artifact availability, process control and a native rendering integration are different gates. Debug protocols can inspect/control a browser; that alone does not provide a CUI-hostable compositor surface, input path, accessibility integration or lifecycle bridge. GeckoView is an Android library, not proof of a drop-in desktop Linux embedding SDK. [W13]

The proposed next experiment is a deliberately narrow Gecko/CUI proof: load a real page into the actual host surface; demonstrate resizing, DPI transforms, wheel/trackpad input, text/IME, focus, popups, accessibility, profile isolation, crash recovery and packaging on the target. No screenshot-card or offscreen image stretched into the UI counts as a completed interactive embed. Preserve the owner's Gecko requirement; do not silently switch engines to make the experiment easier. [R6]

Prefer an isolated browser-process lifecycle as a design starting point, but treat the surface-sharing/embedding technique as an unresolved engineering choice until tested. The owner rule “embed unchanged, fork modified” describes source use; it does not dictate that all code shares one process.

### Two additional corrections to the original discussion

Google's inspected Chrome for Testing platform list includes Linux x64, macOS ARM64/x64 and Windows variants, but does not list Linux ARM64. Do not base Linux ARM packaging on a claimed official CfT ARM64 artifact or assume its version/download JSON is a checksum manifest. [W14]

CEF explicitly provides stand-alone binary distributions that do not require downloading CEF/Chromium source to build an application against them. Therefore the blanket claim that Chromium embedding necessarily requires building Chromium is incorrect. Exact target/package availability and the appropriateness of CEF for 9to1 remain separate checks. This is a factual correction, not permission to replace the chosen Gecko route. [W15]

## 5. Where should the real DuckDB dependency be pinned?

The inspected worker at `9to1 Workspace/Data/workers/duckdb_worker.py` requires exactly `1.5.5` and imports the ambient Python package. That prevents a different reported version from running, but neither installs the dependency nor authenticates its wheel. The inspected workers directory contains the two worker scripts; no adjacent lockfile was shown. This is not a claim that no dependency tooling exists anywhere else in the entire repository. [R8]

Keep the donor source pin in the donor registry for provenance. Separately define the artifact actually loaded by Python.

**Proposed ownership:** a runtime requirements declaration beside the Data workers, resolved to platform/interpreter-specific hash-locked requirements under that directory (or the existing shared lock convention, if the implementer finds one). The package build consumes that lock into an isolated runtime/wheelhouse. The donor registry points to it rather than pretending its C++ source gitlink installs the Python wheel. Add a check that the runtime declaration and worker-required version agree.

A useful proposed layout is:

```text
9to1 Workspace/Data/workers/
    duckdb_worker.py
    requirements.in                # proposed version declaration
    locks/<platform>-<python>.txt   # proposed resolved, hash-locked artifacts
eng/donor-sources.json              # provenance/reference; not an installer
```

Do not overwrite a discovered existing lock authority, choose a different Python ABI silently, or auto-upgrade the dependency. A future upgrade must update the worker contract and its compatibility tests together.

### Concrete artifact evidence

PyPI lists DuckDB 1.5.5 Linux AArch64 wheels, including the following reference entry for CPython 3.12. This establishes availability for that ABI, not that the laptop/package uses CPython 3.12. [W16]

```text
Filename: duckdb-1.5.5-cp312-cp312-manylinux_2_26_aarch64.manylinux_2_28_aarch64.whl
Published SHA-256: f316eae2323d9a851883fdf2dee91c1f9efe251ab33e14a2272f82a913422ed6
Evidence: PyPI metadata read; file NOT downloaded or locally hash-verified here.
```

pip documents hash-checking mode and binary-only installation. Apply those controls to every relevant resolved artifact, using the correct target ABI and platform; prepare artifacts at build time rather than fetching arbitrary code on first application launch. [W17]

### Proposed tests, all NOT RUN

Test offline startup from the packaged environment; missing dependency; wrong version; wrong CPU/ABI; corrupt/mismatched wheel bytes; dependency leakage from the user's global Python; worker launch on x86-64 and ARM64; database open/query/close; and preservation of the worker's existing read-only and external-access restrictions. Source availability, successful import and a successful user-facing Data workflow are three separate evidence states.

## Research closure

Availability, protocol direction and a packaging design are answered. Exact supported-release selection, real binary verification, compatibility of the current pinned Gecko tree/toolchain, the CUI rendering bridge and target-package execution remain implementation/acceptance gates—not unanswered web searches disguised as completion.

---

# D — Model-browser donor/parity manifest
**Status:** initial researched manifest covering features named in the supplied assessment. Not a complete inventory of every Unsloth Studio feature; not implemented or accepted.

## 1. Scope and evidence

The requested donor assessment identifies model-repository search, GGUF listing, disk-budget checks, quantisation method/size estimates, and adapter/merge operations as useful backend reuse targets. It reports that Studio is SvelteKit/Tauri while 9to1 requires CUI. Upstream documentation also describes local model use, training and export, but this manifest does not import that whole product scope by default. [R7, W7]

The local assessment is of a specific custom installation, not a fresh independent inspection of every current upstream file. Its “no support for PLE GGUFs” finding concerns a named model path. It does not, by itself, establish that searching for or downloading a PLE file is impossible. Treat catalogue/download, import/validation, conversion and inference support as separate capabilities. The resident Strata host must not be stopped, replaced or commandeered by donor code. [R7, T1]

**Real reuse remains the goal.** Each adopted row must eventually record the donor revision and files, retained logic, the typed adapter, the production call path, applicable licences and tests. A C# class named after a donor, a licence notice or a parity checklist alone is not donor integration. Conversely, a CUI frontend rewrite does not justify discarding reusable backend logic. [R6]

## 2. Identifier discipline

The MB- identifiers below are new local manifest row IDs, not existing UX27 requirements. The UX27 references are verified governing contracts: SHARED-001/002/008, HOST-009, SPACES-003/005/007/009/011. Where no feature-specific UX27 clause was identified, say so instead of inventing one. A general state/permission requirement does not by itself authorise a new training product. [R6]

| Local ID | Capability to preserve/adapt | Verified governing contract | Required integration evidence / proposed acceptance |
|---|---|---|---|
| MB-01 | Search and browse model repositories | SHARED-002/008; HOST-009 | Trace an actual donor search path or explicitly justified alternative. Loading/error/empty/offline states must be real; public browsing cannot require an unrelated 9to1 paid plan. |
| MB-02 | Inspect repository revision, model details, licence and supported files | SHARED-001; SPACES-005 | Show exact revision/file metadata and capability basis. Do not infer runnable support from a familiar model name. |
| MB-03 | List/select GGUF quantisations and complete artifact sets | SPACES-005/009 | Record selected repository revision and files. Treat split files and required companions as a cohort; validate completeness before installation. PLE runtime support is a separate row. |
| MB-04 | Estimate disk need before a download or conversion | SHARED-008; HOST-009 | Account for destination, temporary output and retained input as applicable. Label estimates; refuse safely when capacity is insufficient, without deleting unrelated models. |
| MB-05 | Download, cancel/retry and verify artifacts | SHARED-001/002/008 | Stable job identity; honest byte progress; integrity/size verification; incomplete data cannot appear Installed. Resume semantics depend on the actual backend and verified remote identity. |
| MB-06 | Gated repository authentication | SHARED-001; SPACES-003 | Respect the user's real provider access/licence acceptance. Token presence is not permission to bypass a gated repository. Protect tokens and redact logs. |
| MB-07 | Register installed artifacts and discover existing local models | SPACES-007/009; HOST-009 | Keep downloaded, verified, installed, selected, loaded and inference-ready distinct. Install must not silently start or replace a running model. |
| MB-08 | Select quantisation method and show size/compatibility estimates | SHARED-001/008; SPACES-005 | Tie each method to a verified backend/version and model architecture. Do not offer unsupported transformations merely because a generic option exists. Feature-specific UX27 quantisation clause not identified. |
| MB-09 | Execute a supported quantisation/export job | SHARED-001/002/008 | Preserve immutable source input; stage output separately; validate completed output before publishing it; retain cancellation/error information. Schedule resources without displacing resident inference. Detailed UX27 job contract requires a source mapping. |
| MB-10 | Adapter discovery, compatibility and configuration | SPACES-005/009 | Record compatible base identity/revision, adapter format and required backend. Unsupported combinations stay unavailable with a reason. No feature-specific UX27 adapter clause identified. |
| MB-11 | Merge/export supported base-plus-adapter combinations | SHARED-001/008; SPACES-005 | New derived output with provenance; no silent overwrite of the base; truthful precision/format semantics; incomplete output not registered as runnable. Exact operation scope requires donor API verification. |
| MB-12 | Shared model groups and lazy runtime selection | SPACES-009 | One canonical Den-backed identity across Haven/Spaces. Selecting a group member does not load it; an active run remains pinned; future Send resolves the permitted route. |
| MB-13 | Replace donor proprietary AI/subscription integration | SPACES-003/011; owner donor rule | Use explicit 9to1 provider/account/entitlement adapters. No silent switch to a paid route; no invented quota for local inference; preserve protected server-side authority. |

All rows are **NOT ACCEPTED**. Existing 9to1 interfaces and CUI strings confirm aspects of the installation/selection contract, but those observations do not prove the full workflows or donor integration. [R12]

## 3. Proposed shared states and service boundary

A single catalogue/download service can serve both Haven's model browser and Spaces without duplicating storage and readiness truth. Suggested state progression is:

```text
Discovered -> SelectedForDownload -> Downloading -> Verifying -> Installed
Installed -> CompatibilityChecked -> SelectedResponder
SelectedResponder --explicit eligible Send--> Loading -> Ready
```

These are proposed UI/domain states, not existing enum names. Failure/cancellation belongs to each operation. An existing external Strata route can already be Ready without passing through a new local download. Keep provider route state distinct from artifact state.

A donor extraction should expose typed catalogue, artifact-manifest, download, transformation and adapter operations. Do not import Studio's entire user/tunnel/router/model-host stack merely to reuse a catalogue. Equally, do not assume a small wrapper proves that those modules can be separated: inspect imports, state, callbacks, authentication, subprocess control and licences first. The result may be a bounded donor subsystem rather than the whole backend.

## 4. Adoption decision and verification plan

Licence work is conditional rather than a blanket rejection; refer to B. Identify the exact custom/upstream revision and buildable source, then audit the intended donor files. Implement the CUI shell against those extracted operations. Where replacement is genuinely necessary, preserve the reason, feature parity and owner's integration intent in the manifest; do not silently downgrade the requirement to “inspired by”.

Test no-network catalogue behaviour, interrupted downloads, changed remote revisions, absent shards/companions, stale checksums, disk exhaustion, revoked access, unsupported transformations, process interruption, duplicate registration and model selection during a live run. Validate each transformation against a supported fixture and inspect the actual produced artifact. PLE inference/conversion remains a named compatibility gate until an appropriate engine proves it.

**Deliverable limit:** this manifest covers the named assessment features and governing contracts. Exact Studio dependency extraction, legal boundary acceptance, binary transformation correctness and live Strata coexistence have not been established by this task.

---

# E — Files three-root navigation and storage rules
**Status:** recovered owner requirements plus labelled implementation proposals. No filesystem migration or provisioning was performed.

## 1. Owner requirements, not inferred replacements

The session explicitly specifies three top-level roots: Cloud, 9to1 Local Workspace and This Machine. The local workspace should be created/detected automatically under Documents without a Files > Connect prerequisite; another save destination remains available. Existing directories must be inspected and preserved, not deleted or overwritten. For user-populated folders that are not an app-directory layout, put app directories under Apps. A previously registered missing workspace has a relocation/recreation flow. The owner later explicitly corrected the agent: this automatic provisioning does **not** cancel the destination-selection flow when creating a board. [T2, T3, T4]

| Root | Intended contents | Required independence |
|---|---|---|
| Cloud | The signed-in user's legitimately connected 9to1 cloud resources and permitted providers | Sign-in may connect the relevant service; it cannot confer access to unrelated provider accounts. Failures stay scoped to Cloud. |
| 9to1 Local Workspace | The registered local 9to1 folder, wherever it currently resides | Local creation/editing/saving works without a cloud account or Files connection wizard. |
| This Machine | The OS-visible filesystem and volumes available to the user | Respect OS permissions; local browsing is not a cloud operation. |

The user's Windows example has This Machine expand to C: and F:. **Proposed Linux presentation:** use actual filesystem/volume labels and mount locations, such as System (`/`) or a removable volume with its real mount point. Do not manufacture drive letters on Linux or display labels as proof of access. Preserve readable names without confusing mount identity with a path string.

## 2. Workspace lifecycle table

| Situation | Required action / proposed safeguard | User-visible result |
|---|---|---|
| Fresh user; no registered root and no existing default folder | Automatically create the workspace and required app directories in the user's resolved Documents location, under that user's permissions | Ready local workspace; no missing-directory warning and no Connect requirement |
| Fresh user; default folder already exists | Inspect and adopt without deleting, renaming or overwriting user content; add only genuinely missing safe directories | Existing data remains intact |
| Existing folder has ordinary user-created subfolders | Use an Apps grouping for app-managed directories, following the owner's rule; do not move the user's unrelated content | User layout preserved; applications organised beneath Apps |
| A required directory name is occupied by a file or conflicting object | Stop that specific creation; preserve the object and explain the conflict rather than replacing it | Scoped repair choice, not data loss |
| Registered root remains available | Reuse its identity and current path; verify access | Normal workspace |
| Previously registered root moved, and exactly one authorised matching candidate is found | Rebind the root location after validating identity and access; preserve document identities and relative references | Workspace follows its move |
| Multiple plausible matches or clones | Do not guess; present a location choice with useful details | No automatic adoption of the wrong copy |
| Previously registered root absent and not rediscovered | Present “9to1 directory missing — Re-locate or create new” with both actions | Owner-selected recovery; no silent replacement workspace |
| Volume offline, permission denied or transient I/O failure | Distinguish unavailable/inaccessible from confirmed missing; retain the registration | Do not create a competing empty workspace to hide a recoverable failure |
| User chooses another save destination | Use the destination under normal OS permissions | Workspace default does not override the chosen path |
| User creates a board | Retain the requested destination-selection flow, with a useful default | Automatic provisioning and destination choice both work |

Rows on fresh/existing/moved roots and Apps grouping reflect the owner statements. Collision, offline, permission and clone handling are explicit engineering safeguards proposed here, not claims that those behaviours already exist. [T2, T3, T4]

## 3. Proposed registration and rediscovery model

The hand-off calls for `.9to1-files/drive.json` as the root marker. Verify the current implementation/schema before changing it. A proposed record includes schema version, stable workspace/root identity and suitable creation/migration metadata; maintain the current location separately in the user-scoped registration. An absolute path alone is not the identity. The marker is a discovery aid, **not an account credential or an authorisation grant**. [T1]

Rediscovery should use permitted registered locations, known parents/volumes and available OS information first. Avoid unrestricted scans or assuming a single Documents path. A same-volume identity aid may not survive copying across volumes, and cloned folders may duplicate markers; those cases require disambiguation rather than a nearest-name guess. Update registration only after a successful validation; do not rewrite all document references before the new root is usable.

For a relocated workspace, resolve managed relative paths against the new root. External user-selected paths remain external. Preserve canonical board/document IDs and recovery drafts. Never infer that removing a workspace registration authorises deleting its content.

## 4. Installation timing caveat

The owner explicitly asked for provisioning when the `.deb` is installed. This memo does not silently convert that into a new manual setup flow. System-package installation and per-user workspace creation still need an implementation design that identifies the intended user safely; blindly creating the package manager's home folder or a root-owned Documents/9to1 would not meet the requirement.

A per-user provisioning routine should be automatically invoked by the appropriate authorised installation/session path and also idempotently checked before first local use. The exact trigger, multiple-user semantics and supported OS Documents-location API remain implementation decisions to resolve, not a claim that an install hook already works. Any proposed first-launch-only timing is a departure from the literal installation timing and must be recorded as such.

## 5. Proposed acceptance checks — all NOT RUN

Test a new profile with no folder, an existing empty folder, a populated user folder, an existing Apps directory, filename collisions, repeated provisioning, normal root relocation, copy/clone ambiguity, inaccessible permissions, an offline removable volume, interrupted registration updates and user-selected external save paths. Use disposable fixtures, never real notes.

Separately test creating a board through its destination picker, cancelling without mutation, saving, closing and reopening in a fresh process without cloud sign-in. Verify the same content and IDs, not just the displayed path or a Saved label. Exercise This Machine with real Linux mounts and, on Windows, the actual drive-letter view. No acceptance is implied by the table alone.

---

# F — ARM64 package release notes
**DRAFT — HELD / NOT RELEASE-ACCEPTED**  
**Prepared:** 30 October 2026  
**This document is not a new package and does not lift the owner's package hold.**

## Build identity

| Field | Value |
|---|---|
| Intended target | Linux ARM64; exact supported distribution/ABI to be recorded from the produced package |
| Package name/version/filename | NOT ASSIGNED OR VERIFIED IN THIS TASK |
| Package SHA-256 / signature | NOT GENERATED OR VERIFIED IN THIS TASK |
| Package source revision | Must be supplied by the packaging worker; not inferred from research HEAD |
| Research repository snapshot | `4e82ef6871c5071af34624fd767dd123cf79d5f3` |
| Native owner-instance retest | NOT RUN HERE |
| ARM installation and execution | NOT RUN HERE |
| Release decision | HELD pending the real evidence gates below |

The earlier attached 28 October `.deb` is not evidence that a new 30 October held package has been built, installed or accepted. Do not reuse its identity or checksum for this draft.

## Candidate release-note wording

This candidate is intended to continue the Boards menu and editing corrections while preserving local-first storage, CUI interaction contracts and existing content. It is not a declaration that all Boards requirements, donor integrations or browser engines are complete.

The project report describes corrections to block-menu opening/dismissal and focus, guarded duplicate/undo behaviour, paragraph/heading conversion, and host-specific title theme repaint. These are **project-reported changes**, not independently rerun results from this research task. Unsupported Design conversions remain explicitly unavailable rather than replacing content with blank objects. [R13]

The report's final owning-test result is **249 passed, 0 failed, 0 skipped**. It also explicitly labels native application/ARM verification NOT RUN and keeps the package hold until real menu behaviour is observed. The record preserves earlier failures; the final count must not be confused with full application or release acceptance, nor assumed to certify every later repository change. [R13]

## Known limitations and open gates

| Area | Honest statement for this draft |
|---|---|
| Boards Design conversion | The cited report supports the paragraph/heading unit, not completion of every conversion destination. Lossless mappings and legacy paths still need the owning requirements and evidence. |
| Real menu behaviour | Native owner-instance evidence is still required by the cited hold. Headless component captures do not satisfy it. |
| Gecko/CUI rendering | Research confirms upstream artifacts/protocols, not successful embedding in this package. |
| Unified DevTools | A design memo exists; the inspected Linux Chromium DevTools method still throws unsupported. Do not list unified F12 as shipped. |
| Files three roots | A recovered requirements/design table exists; this task does not establish its implementation. |
| DuckDB runtime | The source worker requires 1.5.5. The exact shipped wheel, interpreter, integrity and ARM operation need package evidence. |
| Model browser / Unsloth | The manifest and licence assessment are research, not proof of donor adoption, PLE transformation support or inference compatibility. |
| Licensing/source distribution | The exact package component inventory, notices and corresponding source must be completed for that build. |

## Required acceptance evidence before publishing a release

1. **Identity:** exact package version/architecture, source commit and dirty state, source/build receipts, binary digest, donor/runtime artifact manifest and a reproducible launch path.
2. **Native behaviour:** the actual installed/development target opens; the grip menu works, dismisses and reopens; focus/keyboard/drag remain correct. Record platform and observations.
3. **Data preservation:** local create/open/destination selection/save/reopen, undo, stale-save and recovery workflows pass with fixture content. No account/Files-connect dependency and no destructive migration.
4. **Target execution:** install and launch on the ARM target, exercising required native libraries and Python/engine runtimes. A cross-build alone is not this gate.
5. **Regression:** changed owning tests and affected shared consumers run at the documented source state. Keep failed, skipped, blocked and unrun results distinct.
6. **Distribution:** licence notices and exact corresponding source are actually accessible; release-specific limitations match the binary. Only the authorised owner/worker process can change the hold.

All six gates are requirements for later evidence, not results of this task. Do not publish placeholders, claim screenshots were reviewed when unavailable, or translate “source-inspected” into “verified on ARM”.

---

# Source catalogue and evidence boundaries

Checked during this research on 30 October 2026. Web pages can be cached and may change; versioned artifact examples below are not assertions about the latest release. Repository reads use the fixed revision where stated. Source excerpts have been paraphrased; this bundle does not redistribute the original session, third-party source or binaries.

## Supplied session and owner instructions

**T1 — OpenCode session.** `ses_e7d17e2d7ffd3anbdelpo5hi3s.json`, supplied by the user. The six hand-offs are visible assistant message `msg_18cb3b2a6001unsfyPy2AQ7Pxv` (array index 2171; rendered file line 152597). Later user index 2173 records open-app/private-services and embed/fork decisions. Older agent claims are evidence of what was said, not independent proof of repository state.

**T2 — Storage and navigation owner statements.** Same session: user indices 1300, 1305 and 1306. Automatic Documents/9to1 provisioning; Cloud / 9to1 Local Workspace / This Machine; Windows drive examples. Original rendered lines around 94669/94682 contain the three-root statement.

**T3 — Existing-content protection.** Same session: user index 1313, rendered lines 94968/94981. Preserve an existing folder, add missing directories, use Apps for a user-populated layout, distinguish a previously lost root from a new install.

**T4 — Destination-choice correction.** Same session: user index 1586, rendered lines 114844/114857. Automatic provisioning does not remove the board-creation destination-selection flow.

## Repository sources

All R1–R8 and R12–R13 references to 9to1 use revision `4e82ef6871c5071af34624fd767dd123cf79d5f3`. A repository note is identified as such rather than treated as independently executed evidence.

**R1 — Research revision.** GitHub branch metadata read at that SHA; commit message “Stop Goal continuation on explicit owner instruction”.
`https://github.com/CroakyJake12/9to1/commit/4e82ef6871c5071af34624fd767dd123cf79d5f3`

**R2 — Later licence posture note.** `docs/development/donor-assessments/licence-posture-2026-10-30.md`. Records owner decisions and local audit claims; its categorical AGPL/private-server inference is qualified in memo B.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/docs/development/donor-assessments/licence-posture-2026-10-30.md`

**R3 — Root licence.** `LICENSE`, opening lines read directly; GPL version 3 text. This is not a complete per-file licence audit.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/LICENSE`

**R4 — Actual CUI inspection implementation.** `framework/CUI/DevTools/src/CuiLiveTreeInspector.cs`, lines 1–160, plus the containing directory listing. Confirms capture/path IDs and inspected methods, not native wiring or live performance.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/framework/CUI/DevTools/src/CuiLiveTreeInspector.cs`

**R5 — DevTools engine methods.** Repository search excerpts for `OpenDeveloperToolsAsync`, including Linux `ChromiumEngineTab.cs` throwing and Windows `WindowsChromiumEngineFactory.cs` opening its DevTools window. Search excerpts were sufficient for these narrow claims; no complete engine audit was performed.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/9to1%20Workspace/Browse/NativeLinux/ChromiumEngineTab.cs`

**R6 — UX27 contracts.** `docs/specification/owner-references/2026-10-27-expanded-spec/behavioural-expansion.md`, source lines 520–680; portions of `requirements.json` including authority/shared contracts. These are specifications, not implementation evidence. More specific later owner decisions remain controlling.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/docs/specification/owner-references/2026-10-27-expanded-spec/behavioural-expansion.md`

**R7 — Local Unsloth assessment.** `docs/development/donor-assessments/unsloth-studio-2026-10-30.md`, read in full. Its local custom-source header counts, PLE limitation and host findings were not independently repeated here.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/docs/development/donor-assessments/unsloth-studio-2026-10-30.md`

**R8 — Actual DuckDB worker and directory.** `9to1 Workspace/Data/workers/duckdb_worker.py`, lines 1–70; directory listing. Confirms exact runtime version check, ambient import and adjacent listing, not whole-repository lockfile absence.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/9to1%20Workspace/Data/workers/duckdb_worker.py`

**R9 — Pinned Rnote metadata.** `CroakyJake12/rnote` at `8cac558f`, root `Cargo.toml` lines 1–45 and `crates/rnote-engine/Cargo.toml` lines 1–60. Workspace GPL-3.0-or-later, version 0.14.2; engine inherits licence. Read-only verification, not a build.
`https://github.com/CroakyJake12/rnote/blob/8cac558f/Cargo.toml`

**R10 — Pinned Gecko licence pointer.** `CroakyJake12/gecko-dev` at `5836a062726f715fda621338a17b51aff30d0a8c`, root `LICENSE`. Redirects to detailed inventory; trademark rights not granted. The detailed inventory was not exhaustively audited.
`https://github.com/CroakyJake12/gecko-dev/blob/5836a062726f715fda621338a17b51aff30d0a8c/LICENSE`

**R11 — Pinned AppFlowy Board licence.** `CroakyJake12/appflowy-board` at `804d7898ac0becabf73e45527baf5d5c573cd6bb`, `LICENSE` opening declaration of AGPLv3/MPL2 licensing. Applies to that component, not automatically the full AppFlowy application.
`https://github.com/CroakyJake12/appflowy-board/blob/804d7898ac0becabf73e45527baf5d5c573cd6bb/LICENSE`

**R12 — Existing model-browser contracts.** Repository search excerpts: `9to1 Workspace/shared/src/Haven.Application/HuggingFaceModels.cs`, `HomeHuggingFace.cui`, and `docs/specification/owner-references/2026-10-26-desktop-recovery/requirements.md`. Installation is distinct from selection/readiness; public browsing and gated-provider access rules. Not end-to-end testing.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/9to1%20Workspace/shared/src/Haven.Application/HuggingFaceModels.cs`

**R13 — Boards owning-test and hold report.** `docs/development/boards-menu-repair-2026-10-30.md`, read in full. Its 249/249 final result is project-reported; logs, runtime screenshots and ARM execution were not rerun/inspected here. The report preserves intermediate failures and explicitly retains the package hold.
`https://github.com/CroakyJake12/9to1/blob/4e82ef6871c5071af34624fd767dd123cf79d5f3/docs/development/boards-menu-repair-2026-10-30.md`

## Official external sources

**W1 — Firefox 141 CDP removal.** Mozilla Firefox WebDriver newsletter and Remote Agent preferences.
`https://fxdx.dev/firefox-webdriver-newsletter-141/`
`https://firefox-source-docs.mozilla.org/remote/Prefs.html`

**W2 — Mozilla debugging/automation protocols.** RDP protocol documentation and Remote Agent overview. Operation coverage still requires compatibility testing against the selected engine.
`https://firefox-source-docs.mozilla.org/devtools/backend/protocol.html`
`https://firefox-source-docs.mozilla.org/remote/index.html`

**W3 — AGPLv3 text.** Especially section 13, remote network interaction. The fetched page presents the licence text.
`https://www.gnu.org/licenses/agpl-3.0.de.html`

**W4 — GNU licence FAQ.** Independent programs/aggregates, communication semantics and GPLv3/AGPLv3 combination. The FSF's explanatory interpretation is not a ruling on 9to1's exact facts.
`https://www.gnu.org/licenses/gpl-faq.en.html`

**W5 — GPLv3 and FSF AGPL explanation.** Distribution/source obligations, conveying versus network interaction, and combined-work treatment.
`https://www.gnu.org/licenses/gpl.en.html`
`https://www.fsf.org/bulletin/2021/fall/the-fundamentals-of-the-agplv3`

**W6 — MPL 2.0 FAQ.** Mozilla's explanation of file-level copyleft and combining with separate proprietary files.
`https://www.mozilla.org/en-US/MPL/2.0/FAQ/`

**W7 — Unsloth upstream.** Official repository and Studio documentation. General core/Studio licensing split and current documented capabilities; not a verification of the user's custom installed tree.
`https://github.com/unslothai/unsloth`
`https://unsloth.ai/docs/new/studio`

**W8 — OWASP Authorisation Cheat Sheet.** Server-side authorisation, least privilege and checking requests. No service security audit performed.
`https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html`

**W9 — Debian policy, copyright information.** Packaging notice location and requirements.
`https://www.debian.org/doc/debian-policy/ch-docs.html#copyright-information`

**W10 — Mozilla concrete candidate archive.** Historical Firefox 153.0 candidate build 1, with Linux AArch64 directory, source and signed digest manifests. Listed artifacts were not downloaded or cryptographically verified here; not recommended as the latest release.
`https://ftp.mozilla.org/pub/firefox/candidates/153.0-candidates/build1/`

**W11 — Mozilla supported configurations.** Target/host distinctions.
`https://firefox-source-docs.mozilla.org/build/buildsystem/supported-configurations.html`

**W12 — Mozilla cross-compilation guide.** Documents Linux/AArch64 target and bootstrap sysroot; not proof of the local pinned toolchain.
`https://firefox-source-docs.mozilla.org/build/buildsystem/cross-compile.html`

**W13 — GeckoView.** Official Android embedding project; not a Linux desktop embedding contract.
`https://mozilla.github.io/geckoview/`

**W14 — Chrome for Testing.** Official supported-platform list; Linux ARM64 was not listed in the inspected README. No implication that all Chromium ARM64 builds are unavailable.
`https://github.com/GoogleChromeLabs/chrome-for-testing`

**W15 — Chromium Embedded Framework.** Official README states binary distributions are stand-alone and do not require CEF/Chromium source downloads to build an application against them.
`https://github.com/chromiumembedded/cef`

**W16 — DuckDB 1.5.5 PyPI release.** File listing, exact CPython/AArch64 wheel and published SHA-256 metadata. No wheel downloaded or verified locally in this task.
`https://pypi.org/project/duckdb/1.5.5/`

**W17 — pip secure installs.** Hash-checking and binary-only installation controls.
`https://pip.pypa.io/en/stable/topics/secure-installs/`
