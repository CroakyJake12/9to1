# HavenOS Dev app surface

This directory owns the independent HavenOS Dev domain surface. It currently provides versioned multi-root workspace persistence, provider-driven project-template discovery with preflight toolchain readiness, a stage-oriented task executor, and a code-intelligence adapter over Haven's maintained language service.

The canonical workspace path also connects the maintained source editor, reviewed text edits, saved build stages, tests, Git observations and terminal commands to the existing Task/Run. The native workbench is composed by the Desktop owner over the same Dev, Files and canonical task services; its presence does not register a separate Dev App launch or grant workspace execution.

## Current capabilities

- Workspace and project records use stable IDs; workspace files are versioned, atomically saved, and protected from stale writers.
- Project templates can be registered by providers and filtered by language, framework, platform, product surface, search text, and toolchain readiness.
- Task execution validates dependencies, requires workspace trust where declared, requests Home authorization before executing, enforces stage timeouts, and returns stage-level states and diagnostics.
- Code intelligence delegates semantic operations to the existing advanced service. Code-action edits require a cached exact proposal and an explicit Home grant; edits are rejected when workspace identity/revision/path changes. Rename remains unavailable until the shared service can provide a reviewable preview before mutation.
- Structured results carry stable error codes, target IDs, retry metadata, and request IDs.
- A Space can persist a reference to the original workspace/project/root and canonical Task/Run. Fresh reopening reads the existing stores and current task observation; it does not create a workspace, task or attempt. A revision conflict requires an explicit current-reference attachment through the owning service.

## Ownership and integration limits

Dev does not replace Files, Stack, Terminal, Sites, AI Studio, Home, or extension hosting. Providers must connect the domain seams here to those owning services. The native workbench has source editing and reviewed per-pass inverse preparation through the maintained owner. A separate registered Dev App launch, full project wizard, toolchain installer, DAP debugger, Test Explorer, package providers, Stack review UI, CUI builder, remote environment adapters and exhaustive first-party API/event catalogue remain open implementation requirements.

Canonical project tree and text search currently refuse: the physical original workspace owner has no safe per-child traversal port. The workbench's tree/search commands do not establish that capability. Source reads and edits still require the actual configured Files identity resolver, original task admission and current native fences. Process acceptance is distinct from an observed successful build or test exit.

The source editor and shared UI/runtime are owned by their existing workspace and platform layers. Dev's domain APIs must be wired into those layers through their owners; this project adds no alternate editor runtime or platform-specific dependency. Visual Studio extension provenance remains outside this package.

## Focused validation

Run from the repository root:

```powershell
dotnet build "9to1 Workspace/Dev/HavenOS.Dev.csproj"
dotnet test "9to1 Workspace/Dev/Tests/HavenOS.Dev.Tests.csproj"
```

Run the owning Spaces tests for persisted attachment/reopening controls as well:

```powershell
dotnet test "9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj" --filter "FullyQualifiedName~SpaceDevelopmentWorkspaceTests"
```

These commands describe the normal owning projects. The current source changes require fresh build/test evidence; this page makes no test-pass claim. Native rendering, packaging, cross-platform behavior and actual model-backed same-ID takeover remain unverified until their actual runtime controls execute.
