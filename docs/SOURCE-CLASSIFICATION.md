# Source Classification

This inventory describes the role of the major checked-out trees. It is an
ownership aid, not a deletion list. A legacy or donor implementation remains
until its replacement is verified.

| Path | Classification | Rationale |
| --- | --- | --- |
| `framework/CUI/` | ACTIVE | Authoritative CUI framework, compiler, tooling and platform integration under construction. |
| `9to1 Workspace/Home/` | ACTIVE | Home domain, OS-hub work and Dulche runtime source. |
| `9to1 Workspace/Spaces/` | ACTIVE | Spaces identities, navigation and built-in/custom Space implementation. |
| `9to1 Workspace/{Boards,Browse,Canvas,Data,Dev,Files,Motion,Picture,Present,Studio,Terminal,Wave,Write}/` | ACTIVE | Canonical standalone application and engine source. Some nested donor trees remain classified separately below. |
| `9to1 Workspace/shared/src/Haven.Core/` | ACTIVE | Stable entities, value objects and contracts. Persisted IDs must be preserved. |
| `9to1 Workspace/shared/src/Haven.Application/` | ACTIVE | Use cases, routing and orchestration. |
| `9to1 Workspace/shared/src/Haven.Infrastructure/` | ACTIVE | Persistence, providers and external integration. |
| `9to1 Workspace/shared/src/Haven.Desktop/` | LEGACY | Current AXAML/HUI desktop product and migration source. It remains runnable evidence, but is not the final CUI UI architecture. |
| `9to1 Workspace/shared/src/Haven.UI/` | LEGACY | Existing HUI parser/scene/runtime. Reuse is allowed during migration; HUI is not the final public framework identity. |
| `9to1 Workspace/Files/Source/Files/` | DONOR | Upstream Files/WinUI implementation used for behaviour and 1:1 surface parity. |
| `9to1 Workspace/Home/Source/Dulche/llamacpp/` | ACTIVE | Pinned llama.cpp-derived Dulche runtime with retained upstream provenance. |
| `9to1 OS/HUI/Cui/` | ACTIVE | Initial CUI parser/tests; expected to converge into the framework tree. |
| `9to1 OS/HUI/{HuiRenderer,LinuxHost,WindowsHost,vendor}/` | LEGACY | Existing HUI/Avalonia-package host and vendored Haven.UI migration input. |
| `9to1 OS/release/` and `9to1 OS/packaging/` | ACTIVE | Package admission, preload and OS release tooling. |
| `9to1 OS/apps/` | OBSOLETE_CANDIDATE | Must not become a second app source tree; inspect each entry before removal. |
| `9to1 OS/platform/` | ACTIVE | OS platform components and pinned GNOME/Mutter submodules. |
| `9-1 OS (Android)/` | ACTIVE | Android platform host under CUI migration. |
| `reference/` | REFERENCE | Historical CakeOS/CakeAI source and audit evidence; never loaded as active product input. |
| `**/bin/`, `**/obj/`, `**/__pycache__/`, `*.pyc` | GENERATED | Build/runtime output; ignored and not source. |

## Removal gate

Use the sequence `inventory → classify → replace → verify → mark obsolete →
remove`. In particular, an AXAML/HUI file is not removable merely because a
`.cui` file with a similar name exists. Rendering, interaction, platform and
parity evidence must exist first.
