# Lane 16 handoff — CUI installed package toolchain + real consumer dependency gate

- Worker: Mercury lane 16 (one of the explicitly authorised 16 Mercury workers in isolated
  worktrees; the 16-worker owner authorization supersedes the earlier six-worker and single-worker
  text. The repo `AGENTS.md` still contains the older six-worker paragraph; the latest direct owner
  instruction for this lane authorizes 16 total and governs this task.)
- Workspace: `/workspace/9to1-mercury-extra16-packages`
- Isolated base commit: `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb`
- Recorded source identity and file hashes: see [`SOURCE-IDENTITY.md`](SOURCE-IDENTITY.md)
- Execution checklist for the coordinator: see [`TESTS.md`](TESTS.md)

## Authority read

- Canonical specification `docs/specification/9to1-development-specification-v2.md`, SHA-256
  `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`.
- Relevant sections: "Shared application lifecycle, launch and modular packaging / Home runtime
  dependency" (spec lines 2642–2656), "Platform and Home runtime integration" (compiler/toolchain
  belongs to the build environment; runtime capability is app/package metadata), "Language/runtime
  versioning", "CUI compiler pipeline", and the native-versus-authenticated-web dependency
  distinction. Browser-hosted web surfaces cannot require a local Home install and use authenticated
  web services; a compiler package must not invent private Home/runtime services or claim a desktop
  package is Web/WASM compatible without the actual dependency graph.
- `AGENTS.md` fully re-read; owner single-app implementation reference
  (`owner-references/2026-10-20-single-app-implementation/owner-correction.json`) read for the
  handoff.
- Typed-transport reference: `/workspace/shared/9to1-test-evidence/parallel6-transport-pass5-frozen/identity.json`
  (frozen Git object `dc4811ac…`, checkpoint `dc4811acd5cbc2a0130af7fa335c5e0312741f87`) was read
  to author against exact API/resource names (distinct `CUI.Compilation.v1` / `CUI.Compilation.v2`,
  genuine V1 lowercase keys, V2 PascalCase keys, ABI2 stateful promotion). The sparse base may not
  contain the typed transport source; no active transport source was overwritten.

## Scope executed (exclusive direct-edit)

1. `framework/CUI/Compiler/CakeOS.Cui.Compiler.csproj` — packaging target/metadata only.
2. `framework/CUI/Build.Tasks/CakeOS.Cui.Build.Compiler.csproj` — packaging metadata only.
3. `framework/CUI/Build.Tasks/buildTransitive/CakeOS.Cui.Build.Compiler.props` — layout clarification
   only (no functional change).
4. `framework/CUI/Build.Tasks.Tests/verify_toolchain_package.py` — updated (see below).
5. `framework/CUI/Build.Tasks.Tests/verify_nuget_consumer_dependencies.py` — NEW.
6. `work/extra16-handoff/` — this handoff.

Not touched (explicitly out of scope): `Compiler/CuiCompiler.cs`, `Compiler/buildTransitive/CakeOS.Cui.targets`
and `.props`, `Build.Tasks/CompileCuiSourcesTask.cs`, Core semantic/transport files, Runtime files/projects,
Loader/Container, vendor, and other shared tests.

## Packaging correction (smallest owned one) and causal evidence

The published `CakeOS.Cui` compiler package previously would emit `lib/net10.0/CakeOS.Cui.Compiler.dll`
(the compiler assembly, a consumer compile/runtime reference) AND declare nuspec dependencies on the
Core/Language/Runtime projects (which transitively drag the desktop Avalonia runtime into every
consumer). That forces desktop/native assets into consumers including web/WASM-targeted ones and lets
build tools masquerade as application runtime controls. Corrections applied to the Compiler project:

- `<IncludeBuildOutput>false</IncludeBuildOutput>` — never publish the compiler assembly under `lib/`;
  it only ships as a build tool under `tools/compiler`.
- `<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>` — the toolchain is
  self-contained under `tools/compiler` (+ `tools/legacy`); no Core/Language/Runtime package
  dependencies are generated, giving a clean PackageReference closure and no forced desktop runtime.
- `<DevelopmentDependency>true</DevelopmentDependency>` — build-tool classification; not propagated
  to downstream projects.

Same metadata applied to the standalone `CakeOS.Cui.Build.Compiler` build-task package (which already
keeps its output under `tools/net10.0` via `BuildOutputTargetFolder=tools`). The Build.Tasks
`buildTransitive/CakeOS.Cui.Build.Compiler.props` is preserved as-is (its `tools/net10.0` layout,
distinct from the Compiler package's `tools/compiler` layout) — a clarifying comment was added only.

Full causal evidence: [`PATCH-packaging.md`](PATCH-packaging.md).

## Tests delivered

### Updated `verify_toolchain_package.py`
- Adds `--dotnet` (removed hard-coded `/home/jacob/.local/bin/dotnet`) and `--probe-dotnet`.
- Preserves the source-versus-installed bootstrap checks (package extracted from nupkg vs source
  checkout), and the "tools are build-time, not app runtime" check.
- Consumes the actual returned transport contract: distinct genuine V1 / V2 artifact identities and
  the legacy single-file V1 layout, whichever the real build emits (adaptive discovery).
- Manifest resources are enumerated authoritatively through a tiny compiled C# probe
  (`Assembly.GetManifestResourceNames()`), not raw byte scans.
- Adds a stateless -> stateful input transition proving stateful source is never raw-shipped under a
  V1 identity (it must move to a distinct V2 artifact; stateful V2 documents are ABI2). If the typed
  V2 transport is not present, the check reports BLOCKED with a precise reason rather than an
  invented PASS.

### NEW `verify_nuget_consumer_dependencies.py`
- Real installed-package dependency gate: an ordinary fresh consumer with a **PackageReference** to
  the exact `CakeOS.Cui` package version and normal SDK imports, restored/built from a **local feed
  only** (offline; isolated `NUGET_PACKAGES` cache; sources + audit cleared).
- No source project path, no direct `Import` of source props/targets, no source-checkout fallback
  (proves the compiler task resolves from `…/tools/compiler/CakeOS.Cui.Build.Compiler.dll` in the
  installed package).
- Inspects the generated nuspec dependency groups (clean closure) and packed assets (tools layout;
  **no `lib/`** runtime assets; no web/WASM/browser dependency or asset claimed).
- Proves compiler identity matches the packaged `tools/compiler/CakeOS.Cui.Compiler.dll`, and that
  tools do not become fake app runtime controls (none of the task assemblies leak into the consumer's
  app output).
- Covers a native/minimal net10 consumer and a browser/WASM-targeted dependency-inspection/consumer
  case only to the actual supported boundary: a precise unsupported/missing-dependency diagnostic is
  reported (BLOCKED) rather than pretending Desktop-only dependencies are web-compatible. The package
  honesty checks (no web dep/lib/WASM assets) are deterministic.

## Prerequisites before root executes (serialized .NET slot)

- The full typed transport (distinct V1/V2 artifacts, `CuiV2Transport`, ABI2 stateful promotion) must
  be integrated so the stateful-transition checks can PASS; otherwise those checks report BLOCKED
  (never fabricated PASS). The toolchain/NuGet gate and the packaging closure checks do not require
  it.
- `dotnet pack` / `restore` / `build` / probe runs must go through the coordinator-supplied guarded SDK
  (the old `/home/jacob/.local/bin/dotnet` path is absent here) with `-m:1` under the allocated slot
  (NOT run here). Pass it explicitly with `--dotnet "$DOTNET"`.
- Offline restore is expected; only the local feed is used (no registries, no package publishing).

## NOT run / NOT done

- No .NET/MSBuild/pack/restore/build/tests/app/display commands were run. Only Python syntax/AST and
  `--help` checks were performed.
- No commit/push, no provider/credential/capability/global config changes, no model/subagent/
  session spawning, no external writes, no image handling.
- No claim is made that a whole native/web app is accepted from a package-metadata check.

## Unresolved / for-integration

- `Runtime`/`Core`/`Language` package metadata were not edited (outside this lane's scope). Because
  the compiler package now carries no runtime dependency, an app that needs the CUI runtime must
  reference that runtime explicitly — this is the correct build-tool/runtime separation. If the
  coordinator wants the runtime package's own dependency surface corrected, that is a separate
  root-owned patch backed by causal evidence.
- The exact package version that `dotnet pack` produces (default `1.0.0` if unset) is parsed from the
  nupkg's nuspec by the gate, so the consumer uses the real version.

## Pass 2 — fix round (concrete harness defects)

All five reported defects were addressed in the owned fixtures; no broad redesign.

1. **run_probe directory bug** — both `verify_toolchain_package.py` and
   `verify_nuget_consumer_dependencies.py` now create the owned `e/probe` directory
   (`mkdir(parents=True, exist_ok=True)`) before writing `Probe.csproj`/`Program.cs`. Authoritative
   manifest enumeration is kept; no byte-scan substitute.
2. **Nested assets reading** — the gate now descends `project → frameworks → net10.0 → dependencies`
   with literal keys (never a dotted split, because the TFM key `net10.0` contains a dot) and resolves
   the version from the `libraries` key (`id/version`), not from `dependency.target` (which is
   `Package`). A `--selfcheck` Python-only fixture (authentic nested shape) proves the correct shape
   passes and a wrong version is detected; all 5 selfcheck cases pass.
3. **Inverted command outcome logic** — `run_dotnet` now returns the raw process; success is the raw
   exit code (`succeeded()`). The native stateful transition and the WASM branch use raw success.
   WASM success/failure paths are correct, and a missing-workload/unsupported outcome is classified
   BLOCKED vs product/harness FAIL. The actual RID output path is read from the recorded `$(TargetPath)`
   (after build) or found under `bin/**`, never a guessed non-RID path.
4. **Stage expectations** — a stateless baseline is genuine V1 output; V2 absence at baseline is
   expected (not BLOCKED). V2 is required only after the stateful transition, and the shipped state is
   read from the assembly's embedded manifest/payload via the probe (`--resource`), proving the stateful
   composer is ABI2 in distinct V2 and absent from the actually shipped raw/V1 payload. Stale `obj`
   files are not treated as shipped; nothing is deleted.
5. **Packaging reviewability** — `PATCH-packaging.md` now states the build-tool classification, that
   removing `lib/` does NOT preserve an old compiler binary forwarder (none recovered), and that the
   gate proves bundling under `tools` (Core/Language/Runtime + Avalonia closure) independently of
   `SuppressDependenciesWhenPacking`. Runtime/Core/Language metadata remains root-only.

The guarded SDK path is now a variable in `TESTS.md` (the old `/home/jacob/.local/bin/dotnet` path is
absent here); every command passes `--dotnet "$DOTNET"`.
