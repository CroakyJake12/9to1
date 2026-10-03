# B1 browser foundations evidence

Source: AHj4eMRBcI3x5FFYbDM2m-hiWUCk_SbZphbsfCtNVrsp0bqkXxEmCGQMUSBbZxWMB76lDNvgrMwyepTjeCqVIpuSFZx5BKabswqtngHgOTo; canonical snapshot modified 2026-10-03T16:54:11.626Z. Baseline 98a08827c9fbc486fe987da73f4aee8398120d31 is inspected source, not an accepted release.

The JSON ledger preserves 550 quoted normative/acceptance paragraphs from General Rules, the full Agentic Contract, CUI, common web, Home and Sol Happy. These paragraphs are coverage records, not a completion percentage. Clause lists inherit their paragraph headings and require independent review for mapping granularity. Every mapped runtime procedure is NOT-RUN.

Architecture: reuse the canonical Home.cui and renderer-neutral Home feature/navigation contracts through the pinned CUI/Avalonia browser runtime. No second parser, Home surface, HTTP service authority, local entitlement calculation or private Home background service is introduced. A browser fragment encodes existing route request identities and does not change server routing.

| Capability | Contract | State | Restriction |
|---|---|---|---|
| Browser CUI rendering | Pinned CUI/Avalonia Browser runtime | IMPLEMENTED-UNVERIFIED | Build/runtime dependencies missing; no replacement renderer. |
| Home navigation and route URLs | HomeFeatureNavigationRequest/HomeRouteIds | IMPLEMENTED-UNVERIFIED | URL fragment codec planned; direct context preserved without invented HTTP endpoints. |
| Dashboard/Library/Events | Canonical Home.cui and Home-owned providers | BLOCKED | Surface can render; server data and persistence unavailable, mutating commands disabled. |
| Apps/install/update/repair/rollback | Home package authority | BLOCKED | Local package management is capability-negotiated native-only; browser app launch needs actual registered surface. |
| Discover/Model Picker | Home/Dulche provider registry | BLOCKED | No browser catalogue/provider adapter; no local model success claimed. |
| Settings/Permissions/Notifications | Home service owners | BLOCKED | Canonical shared surfaces/authenticated services absent. |
| Mesh/device/local model operations | Enrolled device and Home providers | BLOCKED | No privileged browser device access or fake Mesh execution. |
| CAKE ID/account/org restoration | Team C authenticated services | BLOCKED | No invented auth endpoints or token storage. |
| Shared AI bar/compose/Action Graph | Home/shared AI contracts | BLOCKED | No private compose or entitlement authority. |
| Desktop continuity/persistence/history | Files and owning app services | BLOCKED | Requires real staging plus Team A clients. |
| Browser compatibility/accessibility/performance | Declared browser/capacity matrix | BLOCKED | SH-13 launch decisions and real browser runtime evidence pending. |

Open release decisions: authenticated server Home transport and environment ownership; supported browser versions; measurable performance/capacity/recovery targets. Missing vendor imports block compilation until shared framework ownership resolves them. Existing Home markup supplies Dashboard/Library/Events branches, with other full feature surfaces still owned by Team A.

The worker will implement browser bootstrap, URL context/history plumbing and explicit unavailable handling under apps/Web. Implemented source is provisional; rendered shell, successful compilation or unit tests will not establish application/browser parity.

Provisional subsystem results: production route/registry unit16/16 PASS, mocked Node7/7 PASS, real Chromium151 DOM/history7/7 PASS. A controlled isolated response mutation proves history assertions reject duplicate-event regression. Full CUI browser host build is BLOCKED (MSB4006 vendor target cycle; direct evaluation MSB4019 absent AvaloniaPublicKey.props); renderer, canonical Home interaction, authenticated BFCache restore, deployed service/continuity and complete app parity remain unverified. Initial failing logs are retained separately. Exact source hashes, commands and evidence scopes are in implementation-manifest.json.
