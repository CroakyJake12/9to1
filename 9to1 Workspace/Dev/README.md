# HavenOS Dev app surface

This directory owns the independent HavenOS Dev domain surface. It currently provides versioned multi-root workspace persistence, provider-driven project-template discovery with preflight toolchain readiness, a stage-oriented task executor, and a code-intelligence adapter over Haven's maintained language service.

## Current capabilities

- Workspace and project records use stable IDs; workspace files are versioned, atomically saved, and protected from stale writers.
- Project templates can be registered by providers and filtered by language, framework, platform, product surface, search text, and toolchain readiness.
- Task execution validates dependencies, requires workspace trust where declared, requests Home authorization before executing, enforces stage timeouts, and returns stage-level states and diagnostics.
- Code intelligence delegates semantic operations to the existing advanced service. Code-action edits require a cached exact proposal and an explicit Home grant; edits are rejected when workspace identity/revision/path changes. Rename remains unavailable until the shared service can provide a reviewable preview before mutation.
- Structured results carry stable error codes, target IDs, retry metadata, and request IDs.

## Ownership and integration limits

Dev does not replace Files, Stack, Terminal, Sites, AI Studio, Home, or extension hosting. Providers must connect the domain seams here to those owning services. This checkout does not yet contain a registered native Dev shell, source editor, full project wizard, toolchain installer, terminal adapter, DAP debugger, Test Explorer, package providers, Stack review UI, AI ChangeSet persistence/revert, CUI builder, remote environment adapters, or exhaustive first-party API/event catalogue. These are open implementation requirements, not completed features.

The source editor and shared UI/runtime are owned by their existing workspace and platform layers. Dev's domain APIs must be wired into those layers through their owners; this project adds no alternate editor runtime or platform-specific dependency. Visual Studio extension provenance remains outside this package.

## Focused validation

Run from the repository root:

```powershell
dotnet build "9to1 Workspace/Dev/HavenOS.Dev.csproj"
dotnet test "9to1 Workspace/Dev/Tests/HavenOS.Dev.Tests.csproj"
```

Focused Dev validation passes 21 tests with normal project-reference rebuilding: Core, Application, Dev, and Dev.Tests all build successfully with zero warnings/errors. UI, packaging, cross-platform, and end-to-end acceptance remain unverified.
