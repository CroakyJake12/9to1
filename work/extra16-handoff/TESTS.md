# Tests and ordered execution (Lane 16, pass 2)

Prerequisites: the coordinator must allocate the serialized .NET slot and supply the guarded SDK
path. The old `/home/jacob/.local/bin/dotnet` path is **absent** in this environment, so every
command below takes the guarded SDK from a variable (`DOTNET`). Pass it explicitly with `--dotnet`;
the scripts accept it and do not hard-code a working path.

Full typed transport (distinct V1/V2 artifacts) should be integrated before the stateful-transition
checks can PASS; otherwise they report BLOCKED (never an invented PASS).

## Step 0 — Python-only preflight (no SDK)

```bash
cd /workspace/9to1-mercury-extra16-packages/framework/CUI/Build.Tasks.Tests
python3 verify_nuget_consumer_dependencies.py --selfcheck
# Expect: {"mode":"selfcheck","checks":5,"passed":5} and exit 0.
# Proves the nested project.assets.json reader passes the authentic shape and detects a wrong version.
```

## Step 1 — build and pack the compiler package

```bash
cd /workspace/9to1-mercury-extra16-packages
DOTNET=/absolute/path/to/guarded/dotnet     # supplied by coordinator
mkdir -p work/extra16-handoff/out
"$DOTNET" pack framework/CUI/Compiler/CakeOS.Cui.Compiler.csproj \
  -c Release -m:1 -o "$(pwd)/work/extra16-handoff/out" -p:Configuration=Release
# Package version is whatever dotnet pack produced (default 1.0.0 if unset).
# Example: work/extra16-handoff/out/CakeOS.Cui.1.0.0.nupkg
```

## Step 2 — toolchain package verifier (source vs installed bootstrap)

```bash
cd /workspace/9to1-mercury-extra16-packages
PACKAGE="$PWD/work/extra16-handoff/out/CakeOS.Cui.1.0.0.nupkg"
python3 framework/CUI/Build.Tasks.Tests/verify_toolchain_package.py \
  --package "$PACKAGE" \
  --evidence "$PWD/work/extra16-handoff/evidence-toolchain" \
  --dotnet "$DOTNET"
```

## Step 3 — real PackageReference consumer dependency gate

```bash
cd /workspace/9to1-mercury-extra16-packages
PACKAGE="$PWD/work/extra16-handoff/out/CakeOS.Cui.1.0.0.nupkg"
python3 framework/CUI/Build.Tasks.Tests/verify_nuget_consumer_dependencies.py \
  --package "$PACKAGE" \
  --evidence "$PWD/work/extra16-handoff/evidence-nuget" \
  --dotnet "$DOTNET"
```

- Restores/builds an ordinary fresh consumer with a `PackageReference` to the exact version from the
  nupkg, from a local feed only (offline; isolated `NUGET_PACKAGES` cache; sources + audit cleared).
- Exit code: `0` = all checks PASS; `2` = some checks BLOCKED (or FAILED); `1` = usage error.

## What each gateway asserts (summary)

`verify_toolchain_package.py`:
- packaged tools selected (installed path) vs source tool project selected (checkout);
- no source-project dependency leaked in the packaged bootstrap;
- a **stateless baseline is genuine V1 output** (V2 absence at baseline is expected, not a defect);
- compiler identity matches the packaged/source compiler DLL;
- authoritative manifest resource enumeration via the compiled C# probe (no byte scans);
- stateful transition: distinct V2 required only **after** the transition; the stateful composer is
  present with ABI2 in the actually embedded V2 payload and absent from the actually embedded raw/V1
  payload (read from the shipped assembly, never stale `obj` files);
- build tools are not app runtime controls.

`verify_nuget_consumer_dependencies.py`:
- exact package id/version resolved from the **nested** `project.frameworks.net10.0` plus
  `libraries` (version from the library key, not `dependency.target`);
- clean nuspec dependency groups; **every tool dependency bundled under tools** (Core/Language/Runtime
  plus the Avalonia closure) — not merely suppressed dependencies;
- no `lib/` runtime assets; no web/WASM/browser dependency or asset claimed;
- restore/build succeeds offline from the local feed; `project.assets.json` shows a clean closure;
- compiler task resolves from the installed package (`tools/compiler/…`), no source-checkout fallback;
- compiler identity matches the packaged DLL; tools do not leak into consumer app output;
- native/minimal net10 consumer + stateful transition (embedded-payload proof);
- browser/WASM-targeted dependency-inspection case at the actual supported boundary: real RID output
  path located (recorded `TargetPath` or actual `bin` match); precise unsupported/missing-workload
  BLOCKED distinguished from product/harness FAIL — never an invented PASS.

## Not run here

None of the SDK commands above were executed by this lane; only Python syntax/AST, `--selfcheck`,
and `--help` checks ran. They are the coordinator's ordered steps.
