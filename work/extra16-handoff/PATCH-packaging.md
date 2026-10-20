# Bounded packaging correction — causal evidence (pass 2)

## Problem

The `CakeOS.Cui` compiler package (`framework/CUI/Compiler/CakeOS.Cui.Compiler.csproj`) packs the CUI
toolchain (`IncludeCuiCompilerToolchain` copies build outputs to `tools/compiler` and `tools/legacy`)
and ships `buildTransitive/CakeOS.Cui.{props,targets}`. Its `ProjectReference`s to `Core`, `Language`
and `Runtime` had two harmful packaging side effects:

1. Default `dotnet pack` publishes the project's own build output under `lib/net10.0/`, i.e.
   `CakeOS.Cui.Compiler.dll` becomes a consumer compile/runtime reference. Because the compiler
   references `Runtime` (which references the desktop Avalonia/Skia/Headless/Themes stack), a consumer
   referencing `CakeOS.Cui` would pull the desktop runtime as an application dependency — including
   into web/WASM-targeted consumers where it is not supported.
2. The ProjectReferences generate nuspec dependencies on `CakeOS.Cui.Core` / `CakeOS.Cui.Language` /
   `CakeOS.Cui.Runtime` packages, transitively dragging the desktop runtime into every consumer and
   making the build tools appear to be application runtime controls.

This violates the canonical home-runtime / native-vs-web distinction and the CUI build-vs-runtime
separation: the compiler/toolchain belongs to the build environment; it must not force desktop/native
assets into a web app or claim Web/WASM compatibility without an actual dependency graph.

## Fix (smallest owned packaging correction, Compiler package)

In `framework/CUI/Compiler/CakeOS.Cui.Compiler.csproj` (packaging metadata only):

- `<IncludeBuildOutput>false</IncludeBuildOutput>` — never publish the compiler assembly under
  `lib/`; it is a build tool and ships only under `tools/compiler`.
- `<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>` — suppress the
  ProjectReference package dependencies; the toolchain is self-contained under `tools/compiler`.
- `<DevelopmentDependency>true</DevelopmentDependency>` — classify as a build-time development
  dependency; it is not propagated to downstream projects.

Same metadata applied to the standalone `CakeOS.Cui.Build.Compiler` build-task package. The Build.Tasks
`buildTransitive/CakeOS.Cui.Build.Compiler.props` is preserved as-is (its `tools/net10.0` layout,
distinct from the Compiler package's `tools/compiler` layout) — a clarifying comment was added only.

## Public package-contract note (honesty)

These changes intentionally reclassify `CakeOS.Cui` as a build-tool/development-only package. They
**remove** the previously shipped `lib/net10.0` compiler assembly and its package dependencies from the
packed artifact; the public compiler/runtime ABI of that old `lib` payload is not preserved by any
forwarder. **No old Compiler binary forwarder is proven or recovered** — none exists in this sparse
base. Runtime/Core/Language package metadata remains a root-owned integration patch only and was not
edited here.

## Bundling proof (not just dependency suppression)

The gate must prove every tool dependency is physically bundled under `tools`, not merely that package
dependencies are suppressed. The `IncludeCuiCompilerToolchain` target globs the Build.Tasks output
(`../Build.Tasks/bin/$(Configuration)/net10.0/**/*`) into `tools/compiler`; that output is populated
with the full copy-local closure (`CopyLocalLockFileAssemblies=true`) of the compiler task, i.e. the
compiler assembly plus Core/Language/Runtime and the Avalonia/Skia dependencies. The
`verify_nuget_consumer_dependencies.py` gate therefore asserts under `tools/compiler`:
- `CakeOS.Cui.Compiler.dll`, `CakeOS.Cui.Build.Compiler.dll`;
- `CakeOS.Cui.Core.dll`, `CakeOS.Cui.Language.dll`, `CakeOS.Cui.Runtime.dll`;
- at least one `Avalonia.*` assembly (the desktop runtime closure) bundled with the toolchain.

If the pack mechanism did not actually copy the closure, these asset-presence checks fail — so they
are an independent bundling proof rather than a proxy for `SuppressDependenciesWhenPacking`.

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
