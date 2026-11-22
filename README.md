# 9to1

9to1 brings its apps, documents and Spaces into Haven, a native desktop host with a shared tab strip. Home owns app discovery, Dashboard and global settings. New Tab has its own start surface. Spaces owns conversations, Study and Space Builder. The product also includes web and device delivery targets.

Use the **22 November 2026 specification and Interface Atlas v4** for current behaviour and design. The four volumes cover the core product, Spaces, applications and editors. The screen register maps 282 Atlas routes to their requirements. [Product specification and editable Atlas](docs/specification/owner-references/2026-11-22-atlas-v4/README.md)

## Work on the desktop app

Read the current contributor instructions, then find the production route before editing. Haven composes the native Browse workspace, Home and app owners in `9to1 Workspace/shared/src/Haven.Desktop/`. The shared tab strip lives in `9to1 Workspace/Browse/NativeUI/UI/BrowseWorkspace.cui`. Home’s Appearance controls are authored in `9to1 Workspace/Home/UI/Home.cui` and use the Home preference service. [Contributor instructions](AGENTS.md)

The checkout’s root build entry is `eng/9to1.ps1`. Native desktop development on the owner’s PC uses a serial guarded build, an immutable self-contained publish and the managed development profile. Use the preview procedure to identify the exact candidate and inspect it on screen. [Development workflow](docs/development/workflow.md) · [Native preview](docs/development/live-preview.md)

## Source map

| Location | Responsibility |
| --- | --- |
| `framework/CUI/` | UI language, native runtime, material roles and accessibility |
| `9to1 Workspace/` | App implementations, native host, shared services and owning tests |
| `apps/` | Web and standalone app hosts |
| `cloud/` | Connected services and their deployment-specific configuration |
| `9to1 OS/` and `9-1 OS (Android)/` | Device and platform delivery |
| `docs/specification/owner-references/` | Dated owner materials and their provenance |
| `reference/` | Retained upstream and historical material |

A feature’s evidence belongs to the revision, host and interaction actually checked. Use the requirement register for outstanding work, the applicable build or interaction tests for implementation evidence, and native captures for visual comparisons. Preserve upstream licences and original attribution. [Third-party notices](THIRD_PARTY.md)
