# Bounded packaging correction — causal evidence

## Problem

The `CakeOS.Cui` compiler package (`framework/CUI/Compiler/CakeOS.Cui.Compiler.csproj`) packs the CUI
toolchain (`IncludeCuiCompilerToolchain` copies build outputs to `tools/compiler` and `tools/legacy`)
and ships `buildTransitive/CakeOS.Cui.{props,targets}`. Its `ProjectReference`s to `Core`, `Language`
and `Runtime` have two harmful packaging side effects:

1. Default `dotnet pack` publishes the project's own build output under `lib/net10.0/`, i.e.
   `CakeOS.Cui.Compiler.dll` becomes a consumer compile/runtime reference. Because the compiler
   references `Runtime` (which references the desktop Avalonia/Skia/Headless/Themes stack), a consumer
   referencing `CakeOS.Cui` would pull the desktop runtime as an application dependency — including
   into web/WASM-targeted consumers where it is not supported.
2. The ProjectReferences generate nuspec dependencies on `CakeOS.Cui.Core` / `CakeOS.Cui.Language` /
   `CakeOS.Cui.Runtime` packages, transitively dragging the desktop runtime into every consumer and
   making the build tools appear to be application runtime controls.

This violates the canonical home-runtime / native-vs-web distinction and the CUI build-vs-runtime
separation: the compiler/toolchain belongs to the build environment; it must not invent private
Home/runtime services, force desktop/native assets into a web app, or claim Web/WASM compatibility
without an actual dependency graph.

## Fix (smallest owned packaging correction, Compiler package)

In `framework/CUI/Compiler/CakeOS.Cui.Compiler.csproj` (packaging metadata only):

- `<IncludeBuildOutput>false</IncludeBuildOutput>` — never publish the compiler assembly under
  `lib/`; it is a build tool and ships only under `tools/compiler`.
- `<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>` — suppress the
  ProjectReference package dependencies; the toolchain is self-contained under `tools/compiler`
  (`CopyLocalLockFileAssemblies` on the Build.Tasks project already copies the compiler + Core +
  Language + Runtime assemblies beside the task), so no package dependency is needed and the closure
  is clean.
- `<DevelopmentDependency>true</DevelopmentDependency>` — classify as a build-time development
  dependency; it is not propagated to downstream projects.

## Fix (Build.Tasks package)

In `framework/CUI/Build.Tasks/CakeOS.Cui.Build.Compiler.csproj` (packaging metadata only): the same
`SuppressDependenciesWhenPacking` and `DevelopmentDependency` metadata, because this standalone build-
task package is also self-contained under `tools/net10.0` (`BuildOutputTargetFolder=tools` +
`CopyLocalLockFileAssemblies`) and must not leak a dependency on `CakeOS.Cui`.

## Layouts preserved

- `CakeOS.Cui` package: `tools/compiler/*` (net10.0 task + compiler + deps), `tools/legacy/`
  (`netstandard2.0` resource task), `buildTransitive/CakeOS.Cui.{props,targets}`.
- `CakeOS.Cui.Build.Compiler` package: `tools/net10.0/*` (`BuildOutputTargetFolder=tools`),
  `buildTransitive/CakeOS.Cui.Build.Compiler.props` — selects `tools/net10.0`; distinct from the
  `CakeOS.Cui` package's `tools/compiler` selection. Both layouts are preserved, not flattened.

## Effect on consumers

- A normal `PackageReference` to `CakeOS.Cui` restores a clean, self-contained toolchain with no
  leaked desktop-runtime or Core/Language/Runtime package dependencies. The compiler task resolves
  from `…/tools/compiler/CakeOS.Cui.Build.Compiler.dll` in the installed package (auto-imported by
  NuGet from `buildTransitive/`).
- The package has no `lib/` content, so it can never become an app runtime reference; tools cannot
  masquerade as app runtime controls.
- No web/WASM/browser dependency or asset is claimed, so the package is honest at the supported
  boundary; a Web/WASM-targeted consumer that actually needs the Desktop-only runtime fails with a
  precise unsupported/missing-dependency diagnostic rather than a fabricated compatibility claim.

## Dependency / out-of-scope

- `Runtime` / `Core` / `Language` package metadata were not edited (outside this lane's scope). With
  the compiler package's dependencies suppressed, an app that needs the CUI runtime must reference the
  runtime explicitly — the correct separation. If the coordinator determines the runtime package's
  own metadata needs correction, that is a separate root-owned patch backed by causal evidence.
