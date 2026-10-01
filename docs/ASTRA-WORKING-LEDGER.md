# Project Astra la Vista — working evidence

Authority: [9to1 Development Specification](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit), the user's replacement launch instructions, and the configurable subscription amendment. This evidence ledger does not replace or reduce the specification. All requirements before **Future Apps & Features** remain in scope; Future is explicitly excluded.

The complete 19:06 source contains one tab, 9,726 elements and 9,725 paragraphs. Comparison with the earlier complete retrieval proves every byte before the excluded Future boundary at index 1,238,669 is unchanged; 90 added lines are within Future or trailing whitespace. Metadata checked again at 00:41 UTC on 1 October still reports modification `2026-09-30T19:06:02.779Z`. Complete source, indexed assignments and comparison evidence are retained under `/workspace/astra-source/`.

Current delivery: [draft PR #1](https://github.com/CroakyJake12/9to1/pull/1), branch `astra/launch-integration-20260930`. Main remains `98a08827c9fbc486fe987da73f4aee8398120d31`. This is continuing implementation, not a release or a whole-specification completion claim. Historical evidence remains in Git and the immutable checkpoint records; the previous longer ledger is additionally preserved as `checkpoint7-prior-ledger.md`.

## Ownership and canonical contracts

The user authorised the maximum separate workers and explicitly requested Astra. Six accepted `gpt-6-astra` workers plus the coordinator occupy all seven concurrency slots, without nested delegation. No separate fast-tier or effective-model verification is exposed. Bounded source deliveries release files for further work; they do not close a worker's assignment. The coordinator allocates, reviews, integrates and records evidence.

| Owner | Full product responsibility |
| --- | --- |
| 1 | CUI, Home, Models, Vision & Voice, Dulche/external integrations; generic Core/Application/Infrastructure/UI/Connector/Browser and shared platform contracts. |
| 2 | Files, Connect, Mail, Spaces, Write, Present, Boards, Planner, Automations; shared Desktop and its tests except explicitly transferred paths. |
| 3 | Games, Play, Wave, Motion, Data, Forms, Browse, Maps, Shelf; product-specific domain/service paths and the exact 22 Data Desktop paths recorded in `worker3-data-desktop-ownership-paths.json`. |
| 4 | Accounts/subscriptions, Profiles, Business, Admin, Studio, Dev, Stacks, Sites, Terminal, Mini Computer and Web; sole owner of project/dependency/build/workflow metadata. |
| 5 | OS, Go/Android, Universal Model Picker, Wine/WinBoat compatibility and Launcher. |
| 6 | Canvas, Picture and their native Rnote/Glycin product integration. |

Canonical entities, IDs, revisions, stores and typed owner APIs must be reused. Home owns actor/resource admission and non-serializable one-use execution handles; product owners enforce current authority and canonical commit boundaries. Read-only AI cannot mutate. Data actions require individual approval; live database changes additionally require meaningful preview, validation and a recoverable backup. Computer Use requires explicit invocation and excludes games. A copied token, request ID, local-store existence or source checkout is not an authority grant.

Desktop `Services/HomePluginSidebarAuthority.cs` is the explicit service-only transfer to Worker 1; Desktop renderers remain Worker 2. The shared motion preference extraction preserves its existing path/schema/facade; the single Desktop facade returns to Worker 2 after capture. The two canonical Home storage setup files temporarily authored by Worker 2 return to Worker 1 after capture. All metadata remains Worker 4.

## Latest integrated evidence

The seventh independent source-only snapshot is `/workspace/astra-checkpoints/20260930-2348`, based on published sixth commit `034b071f2550eeeccd5c676f66bb51a018236d2f`. Its baseline verifies 8,868 Git blobs and modes and materialises only the exact pinned XamlX, Avalonia.DBus and libvterm source. It shares no writable build output with the main worker checkout or earlier snapshots.

Final program tree `62d59491468b8fb0ed715fe47bc9b7c2e57f76ce` selects 208 immutable SHA-verified source files across 126 changed paths. All 8,921 tracked snapshot blobs/modes match the staged tree, with no uncaptured staged paths. `checkpoint7-program-index-audit-v4.json`, `checkpoint7-selected-source-v4.json`, `checkpoint7-results.json` and `checkpoint7-final-evidence.json` retain exact selection, commands, logs, failures and source trees.

| Checks | Observed result and source qualification |
| --- | --- |
| Final program tree | Desktop 853, Infrastructure 482, Canvas 51, Picture 81, OS 49 and the Accounts executable all pass. Named test suites have zero skips. Native Canvas uses genuine pinned Rnote; Picture uses genuine Glycin with mandatory bubblewrap. |
| Earlier seventh tree `7d3b7198904487d55e06a3c83d3546ed6ebab936` | Keyboard/Go 23, Admin 7, Terminal native UI 5, Launcher 35, Forms CUI 28, Studio 33, AI 24, Home 208, Core 718, UI 163 and Runtime 110 pass with zero skips. OS application build passes with zero warnings/errors; portable Terminal and actual Linux PTY/Home-owned signal executables pass. Only the Desktop plugin service and its tests were subsequently added; these fourteen results retain their exact earlier source qualification. |
| Notice collector | Five offline Python regressions pass on the seventh snapshot, covering cached texts, source hashes, path confinement, tampering and unresolved obligations. This verifies the collector, not complete package notice closure. |
| Previous published head | All 13 remote CI jobs pass on exact sixth commit `034b071f2550eeeccd5c676f66bb51a018236d2f`; the seventh head requires its own remote run. |

The first AI gate failed because forcing a Linux RID added a directory to the fixture's defined portable source-asset path; the corrected harness passes with the same source. The first Desktop compile identified the omitted `IPluginSidebarSpaceAccess` service dependency. Its exact service/test pair was captured, reviewed and added; the final full Desktop gate passes. Both original failures remain recorded.

| Seventh behavior | Bounded evidence and limits |
| --- | --- |
| Home/CUI and Launcher | Issuer-bound rejected claims and store-import audit recovery preserve negative/committed outcomes without issuing another grant or repeating a write. Shared owner-approval routing uses the actual Home resource broker; Launcher exposes canonical layout context and typed actions, with tested Preview/Keep and current actor/revision guards. Complete device/installed widget transport and OS parity remain open. |
| Data and Forms | Canonical table/field/record identity and typed record updates reuse the existing workbook. Bounded receipts commit with cells and allow exact operation recovery. Form-origin fields in this seventh increment are caller-declared metadata, **not authenticated Forms provenance**. Production Forms-to-Data binding remains disabled; subsequent retained-response admission and journal sources require separate integration. |
| Spaces and shared media | Explicit storage setup, current profile/store UUID authority, shared vector objects/rendering, canonical Canvas owning-surface delegation and canonical motion preferences pass the named gates. The plugin service filters enabled per-Space package manifests and rechecks current access. Controlled fixtures do not establish genuine installed package/signature trust or target activation. SQL/settings composite deletion is not claimed atomic. |
| Studio, Picture and Terminal | Studio has a native Home/CUI configuration host, guarded Den import and audit-only recovery. Picture has genuine decode cancellation, owner-bound prepared playback and bounded native scene hosting. Creator encoding only supports cancellation checks before/after its pinned ABI call. Terminal verifies real PTY bytes, current Linux environment/session authority, queue/lifecycle rules and owned signals. Production Desktop Terminal navigation, full metadata editing and complete donor parity remain separately assigned. |

## Earlier verified foundations

Historical checkpoints retain the following bounded evidence; they are not fresh runs on the seventh tree.

| Area | Evidence retained |
| --- | --- |
| Canonical ownership/persistence | Actual Linux principal/private files, explicit legacy-store import, durable SQLite/settings UUIDs, cancellation and corruption recovery. Real two-process Files CAS yields exactly one success and one revision conflict; persistent writer leases cover final publication without unlinking the lock inode. Revoked actors/claims under held resource leases deny publication. |
| Native donors | Real LibreOffice Calc query/edit/recalculate/save/reopen and Writer semantic edit/bold/save/reopen; actual GStreamer/GES decode/timeline/export/seek/cancel; controlled Godot and Blender bridges; native Rnote stroke identity/history/save/reopen and bounded expanded-input checks; genuine sandboxed Glycin decode/export/animation. These do not establish complete upstream donor feature parity. |
| OS and Picture input | Actual X11/Xvfb OS Preview leaves persisted revisions unchanged, Keep advances canonical revisions, layer/wheel/keyboard navigation works, personal Models renders, and a real unsigned peer is denied using kernel-observed PID. Owner Picture X11 startup/focus is observed. Production controlled-launch/installed-process authority, compositor and full device acceptance remain open. |
| Models remote proof | Sixth CI installs genuine Debian `haven-llamacpp-runtime 0.4.0+haven0.1`, verifies package-owned paths/import/inventory and runs actual pinned-model CPU streamed completion/unload through a mode-0600 worker Unix socket. Evidence explicitly states `systemdManaged=false`, `approvedVM=false`, `GPU=false`, `productionbenchmark=false`, and no model packaging/upload. A single run is not a production benchmark or complete installed OS package. |
| Sixth remote CI | Android, Data canonical UI/LibreOffice, Linux publish, Models runtime/package/installed proof/real CPU/pinned llama CPU, Writer native/runtime-only, headless Rnote, Ubuntu 24/26 worker gates all pass. Fifth failures and sixth repairs are preserved. Linux host archive mechanics pass; portable native relocation and complete notices/source obligations remain open. |
| Android artifacts | Root independently verifies Android23 development APK, 115,805,750 bytes, SHA256 `5a7c84f94fbe84470a134f11f4c5498ed39d7b1ce47d4bc3b3ac975efa24bae3`, ZIP CRC, manifest and arm64-v8a/x86_64 entries. Its dependency closure is recorded separately; no exact seventh-snapshot, device/Lawnchair, widget transport or inference acceptance follows. Older APK/native pins remain preserved. |

## Configurable subscriptions

The builder selects monthly AI usage first: 0.5x, 1x, 3x, 5x, 15x, 25x, 50x, 100x or Custom; then storage: 20/100/250/500 GB, 1/3/5/15 TB or Custom. It displays monthly option prices, actual AI Dust quantity and a live combined breakdown. One server-authoritative calculation supplies option prices, custom selections, combined quotes and checkout.

Target is a **15% margin on tax-exclusive combined revenue**, after approved full-use AI/storage, attributable infrastructure/operating and final-charge-dependent payment/billing costs; shared costs count once. This is not a 15% markup. Service limits and all cost/fee bases are explicit inputs; pricing does not assume unused allowances.

Retained Accounts evidence covers 192 full-use fee/rounding combinations plus custom, missing-input, quote-owner, overflow and denominator cases. Actual Chromium verifies all nine AI and nine storage options, Dust labels, live custom edits, stale request suppression, invalid-input denial and a 390px layout using explicitly fictional approved fixture costs. Durable quotes, quote-expiry checks inside the account lease, settlement-verifier/monthly-policy ports and idempotent receipt activation are bounded existing evidence.

Production provider costs, the 1x AI Dust definition, full-use service bounds, applicable payment/billing/tax/FX inputs and monthly policy remain unset. No invented production offer is displayed. The user's 11:03 reset was the agent usage reset and defines no subscription reset policy. Free/local functionality, model-access rules and hosted-site allowances retain their contracts. Production provider verification, renewal/refund/organisation purchase behavior and the full billing lifecycle remain assigned.

## Following cohorts and remaining acceptance

SHA-verified following source is independently preserved under `ready-20260930-2348/next-*`, released to its owners, and excluded from the seventh cutoff. It includes real conversation SQL admission and separate Home binding, production Terminal navigation, authenticated retained Forms-origin admission/journaling, Sites publication/audit/hierarchy repairs, Picture projection/metadata, genuine Canvas pointer input, further Launcher/Android flows, and actual equation typesetting with exact managed-package/font provenance and notices. Owner checks alone do not establish their integrated acceptance.

Full CUI/tooling and shared object families; model/remote/entitlement lifecycle; Vision/Voice capture/providers/accessibility; complete owning workflows for every assigned app; live Forms/publication and Data designers; full Canvas/Picture/media/game donor parity; OS compositor/boot/compatibility and Android device/Lawnchair behavior; installed process authority; complete native/package relocation, notices and source obligations; performance/accessibility/platform acceptance; and production identity/billing remain open. Every applicable source requirement stays assigned. `VERIFIED` always names an observed bounded check; `UNVERIFIED` means missing evidence; `BLOCKED` requires a concrete missing prerequisite. No percentage is inferred from test counts.

## Environment, capacity and external boundaries

Prepared tools include .NET SDK 10.0.301/runtime 10.0.9, PowerShell 7.5.3, Android workload 36.1.2/API 36/build tools 36.0.0/OpenJDK 21.0.12.1, retained Rust/native/donor sources and pinned outputs. Task-specific writable paths avoid changing HOME. All managed build/test/host lifetimes use `flock --close /tmp/astra-dotnet-build.lock`, disabled build servers and single-node builds. Source work remains parallel. Builds stop below their explicit workspace/tmp headroom guards.

Recovery preserves tracked source, immutable evidence/logs, actual native pins, model/module artifacts and every APK. Only owned closed generated output or exact duplicates with retained hashes are eligible. Root's sixth managed-only cleanup measured 586,379,264 bytes recovered; Worker 6's six stale package-matched PDB copies measured 318,136,320 bytes; Worker 5's 222 closed non-RID package duplicates measured 660,787,852 bytes. The last operation's process scan occurred afterward and is explicitly timestamped as such; it is not represented as prior inspection. Earlier per-file receipts remain retained. No active output or canonical artifact is intentionally removed.

Cloudflare credentials are unavailable, blocking live deployment only; Sites authoring/capability/package work continues. Production DNS changes, live-site replacement, WordPress retirement and purchases need specific final approval of concrete artifacts. Primary-branch merge, force push and destructive reset are outside this pass. Existing content and provenance must be preserved. Delivery targets remain 30 September evening progress and 2 October night completion, Europe/London; they do not relax scope or verification.
