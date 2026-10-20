# Tests and ordered execution (Lane 16)

Prerequisites: the coordinator must allocate the serialized .NET slot. The guarded dotnet
`/home/jacob/.local/bin/dotnet` must be used and `. -m:1`. The full typed transport (distinct V1/V2
artifacts) should be integrated before the stateful-transition checks can PASS; otherwise they report
BLOCKED (never an invented PASS).

## Step 0 — build and pack the compiler package

```bash
cd /workspace/9to1-mercury-extra16-packages
mkdir -p work/extra16-handoff/out
/home/jacob/.local/bin/dotnet pack framework/CUI/Compiler/CakeOS.Cui.Compiler.csproj \
  -c Release -m:1 -o "$(pwd)/work/extra16-handoff/out" -p:Configuration=Release
# The package version is whatever dotnet pack produced (default 1.0.0 if unset).
# Example filename: work/extra16-handoff/out/CakeOS.Cui.1.0.0.nupkg
```

## Step 1 — toolchain package verifier (source vs installed bootstrap)

```bash
cd /workspace/9to1-mercury-extra16-packages
PACKAGE="$PWD/work/extra16-handoff/out/CakeOS.Cui.1.0.0.nupkg"
python3 framework/CUI/Build.Tasks.Tests/verify_toolchain_package.py \
  --package "$PACKAGE" \
  --evidence "$PWD/work/extra16-handoff/evidence-toolchain" \
  --dotnet /home/jacob/.local/bin/dotnet
```

- Default `--dotnet` is the guarded path; override with `--dotnet` if needed.
- Requires a built nupkg (Step 0) — it extracts the package for the installed-mode bootstrap and
  uses the source checkout for the source-mode bootstrap.

## Step 2 — real PackageReference consumer dependency gate

```bash
cd /workspace/9to1-mercury-extra16-packages
PACKAGE="$PWD/work/extra16-handoff/out/CakeOS.Cui.1.0.0.nupkg"
python3 framework/CUI/Build.Tasks.Tests/verify_nuget_consumer_dependencies.py \
  --package "$PACKAGE" \
  --evidence "$PWD/work/extra16-handoff/evidence-nuget" \
  --dotnet /home/jacob/.local/bin/dotnet
```

- Restores/builds an ordinary fresh consumer with a `PackageReference` to the exact version from the
  nupkg, from a local feed only (offline; isolated `NUGET_PACKAGES` cache).
- Exit code: `0` = all checks PASS; `2` = some checks BLOCKED (or FAILED); `1` = usage error.

## What each gateway asserts (summary)

`verify_toolchain_package.py`:
- packaged tools selected (installed path) vs source tool project selected (checkout);
- no source-project dependency leaked in the packaged bootstrap;
- genuine V1 (and, when emitted, distinct V2) artifact identities consumed from the actual transport;
- compiler identity matches the packaged/source compiler DLL;
- authoritative manifest resource enumeration via the compiled C# probe (no byte scans);
- stateless -> stateful transition proves stateful raw is not raw-shipped under a V1 identity;
- build tools are not app runtime controls.

`verify_nuget_consumer_dependencies.py`:
- exact package id/version resolved; clean nuspec dependency groups (no leaked Core/Language/Runtime);
- packed assets layout present; **no `lib/`** runtime assets; no web/WASM/browser dependency or asset
  claimed;
- restore/build succeeds offline from the local feed; `project.assets.json` shows the clean closure;
- compiler task resolves from the installed package (`tools/compiler/…`), no source-checkout fallback;
- compiler identity matches the packaged DLL; tools do not leak into consumer app output;
- native/minimal net10 consumer + browser/WASM-targeted dependency-inspection case at the actual
  supported boundary (precise blocked/unsupported diagnostic, never an invented PASS).

## Not run here

None of these SDK commands were executed by this lane; they are the coordinator's ordered steps.
