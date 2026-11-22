# Compile canonical CUI during an SDK build

The `CakeOS.Cui.Build.Compiler` MSBuild integration consumes first-party CUI inputs and verifies the owning project’s input set and emits its compilation artifact. Import the maintained build-transitive props and targets. These are build dependencies. Existing runtime consumers may still load their original resource format; inspect the host’s actual loading path when validating an artifact.

Keep the document’s identity, resource inputs and diagnostics attached to its owning project. A failed compiler invocation must leave the build failed rather than selecting stale output. Use the owning compiler tests to check invalid input, incremental rebuilds and output identity, then confirm the runtime selects the same artifact.

Use the exact declarations from the repository where adding a consumer. On the development PC, execute the owning target through the serial build allocation.

## Inputs and emitted artifact

`CuiSourceRoot` supplies discovery roots, `Cui` adds explicit source and `CuiRequiredCapability` declares requirements. Source-checkout builds import `buildTransitive/CakeOS.Cui.props` and its targets. The task embeds `CUI.Compilation.v1` with source hashes, compiler identity, typed output, maps and dependencies. SDK resource packaging reads the validated content-addressed input snapshots; existing callers can still consume their original runtime resources.

`CuiCompilerTaskAssembly` with `CuiUsePrebuiltCompiler=true` selects an explicit compatible task for the isolated build tests. A failed compilation leaves the previous artifact available for diagnosis and fails the current build. Inspect both the compiler test and the separate packaging-custody probe before changing these boundaries.

[Build tasks](.) · [Tests](../Build.Tasks.Tests) · [Compiler](../Compiler) · [Build procedure](../../../docs/development/workflow.md) · [Compiler task](CompileCuiSourcesTask.cs) · [MSBuild verifier](../Build.Tasks.Tests/verify_msbuild.py) · [Package custody verifier](../Build.Tasks.Tests/verify_package_custody.py)
