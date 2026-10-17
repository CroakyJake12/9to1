# 9to1 Haven + Build — Weekend Direction (authoritative, 2026-10-17)

Owner amendment recorded 17 October 2026. Supersedes old stage order, separate
Dev/Sites/Stacks/Browse identities, M3-only remit and missing-signature stops.
Full compatible feature/data/donor/CUI/security requirements remain binding.

## Product names

- **9to1 Haven** — new canonical name for the former "Browse". Standalone desktop
  super-app and browser. Intentional. Does not rename the platform (9to1), the
  separate 9to1OS programme, or persisted IDs (compatibility aliases required).
- **Build** — ONE application replacing Dev + Sites + Stacks. Single logical
  backend; context-sensitive workspace. Preserve combined capabilities, mature
  engines and data; not a launcher for three old apps. Existing public sites keep
  their own addresses and source identity.

## Priority order (Weekend 17–18 Oct 2026, Europe/London)

Frameworks → Spaces → 9to1 Haven → Files → Build → Mail → Productivity Suite → everything else.
("Browse Files" = Browse, then Files — scheduling interpretation, not a merged app.)

## Delivery matrix

| Product | Standalone desktop | Embedded in Haven tab | Web (product.9to1.uk) |
|---|---|---|---|
| Ordinary apps (Build, Spaces, Files, Mail, productivity) | yes | yes | yes |
| Mini Computer | yes | yes | no |
| 9to1 Haven | yes | n/a (no self-embedding, no web edition) | no |

Required embedded entry: New Tab → sidebar → Apps → choose app → opens IN THAT SAME TAB.
Launching another desktop process/window or rendering an app-list card is not this journey.

## Interpretations (coordinator choices, reversible within approved behaviour)

- One backend = one authoritative project/service model with internal code, visual-site
  and collaboration modules. Not necessarily one process.
- Home Core stays shared service owner; Haven reuses Apps, Dashboard, Models/Discover.
- Preserve old identifiers/URLs/data/APIs via compatible aliases/adapters/migrations.
- product.9to1.uk is the supplied web destination; host/path mapping configurable;
  resolve literal-hostname vs pattern from existing authorised deployment config
  before any DNS mutation. Does not block app development.
- Existing public sites keep their own addresses and source identity.
- Full feature spec remains the backlog; lower-priority unfinished stays unfinished.

## Active owners / boundaries (do not interfere)

- UCML worker: owns Assistant memory, canonical write contracts, knowledge/retrieval
  services, Haven.Desktop App/UnifiedWorkspace host. In-flight edits in
  App.axaml.cs, UnifiedWorkspaceSession.cs, App.UnifiedWorkspaceHost.cs (3 compile
  errors — their area, not mine).
- M3 Chat worker (this agent): Spaces Chat CUI migration + framework primitives.

## Current integrated state (reconciled 2026-10-17)

- Branch: opencode/m3-shell-wiring-20261016, 13 commits ahead of origin/main.
- CUI Runtime: 366/366 (Markdown + FollowAtEnd included).
- Spaces (ChatSpace*): 49/49.
- ChatSpacePresentationAcceptanceTests: 5/5.
- CUI Compiler: 43 pass / 7 fail (unchanged baseline: 5 apps/Web missing files + MailWorkspace.cui + ModelPicker.cui).
- SpacesHomeNativeRenderingTests: 1 pass / 10 fail (pre-existing, worker area).
- SpacesHomeControllerTests: hangs (worker area).
- Haven.Desktop build: 3 errors, all in UCML-owned files (not mine).
- Chat chronology: tool activities now rendered as cards but still NESTED under
  messages (not globally interleaved). Treat as open.
- Add-menu/catalogue/search/mention: still not migrated (per delta doc §3.13).

## First safe implementation tasks (this pass)

1. Diagnose Haven.Desktop build errors (read-only, report to UCML owner).
2. Diagnose SpacesHome failures (read-only).
3. Fix tool-activity chronology to true mixed-event interleaving.
4. Update delta doc.

## Test environment

- Native ARM64 path found: worktree + conditional PlatformTarget (no repo change).
- x64 SDK 10.0.402 downloaded+verified at ~/.local/share/dotnet-x64.
- Haven.Desktop native suite is PE/IL-managed; needs X64 dotnet host for VSTest.
  Root-only binfmt/qemu still required; commands given to owner.
- Native capture route (NativeAssistantCanonicalVisualCaptureTests) in pre-existing
  failure band — visual evidence unavailable.