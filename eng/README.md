# Repository build commands

Use `9to1.ps1` for the supported component restore, build, test and package recipes. Select the component that owns the changed source and check the command result. On the development PC, run native SDK work under the existing serial lock and memory guard. The desktop preview is published self-contained into a fresh runtime slot. [Guarded PC workflow](../docs/development/workflow.md)

```powershell
./eng/9to1.ps1 restore -Component core
./eng/9to1.ps1 build -Component core -Configuration Release
./eng/9to1.ps1 test -Component core -Configuration Release
./eng/9to1.ps1 verify -Component core
```

`core` currently means the CUI, Home and Spaces test projects. Use `shared` to
run the large legacy/shared solution separately. This separation keeps the
migration gate honest while platform-specific prerequisites remain unresolved.

On a Linux build host, the registered package recipes are invoked explicitly:

```powershell
./eng/9to1.ps1 package-linux -Component dulche
./eng/9to1.ps1 package-linux -Component canvas
./eng/9to1.ps1 package-linux -Component boards
```

The strict final markup gate is intentionally expected to fail during migration:

```powershell
./eng/9to1.ps1 verify -Component core -RequireCuiOnly
```

It must not pass until active applications are CUI-native and any retained donor
or historical markup has been excluded by an reviewed source-classification
policy. A normal verification reports legacy counts as an unfinished condition.

Development/release evidence, ownership and interruption recovery use the
[durable workflow](../docs/development/workflow.md) and the stdlib helper at
`eng/development/workflow.py`. It extends the existing checkpoint system rather
than replacing the PC coordinator. Run its focused infrastructure invariants with:

```sh
python3 -B -m unittest discover -s eng/development -p test_workflow.py -v
```

## Windows compatibility preparation
`./eng/9to1.ps1 verify -Component compatibility` runs the dependency-free Python
checks for the EXE chooser, ARM64 managed runtimes and stable Ubuntu image
selection. `./eng/9to1.ps1 package-linux -Component compatibility` builds
`artifacts/packages/9to1-windows-compat_0.1.0_all.deb`. GTK, Wine/WinBoat runtime
and real Book4 Edge hardware acceptance are separate checks; see
[`../9to1 OS/tooling/book4edge/README.md`](../9to1%20OS/tooling/book4edge/README.md).

## Packaging and model staging guides

Use the current guides below when preparing a candidate. The corresponding original documents remain at the paths consumed by the ARM source checks. [ARM package pipeline](../.github/scripts/native16-arm64/PIPELINE.current.md) · [Chromium staging](../.github/scripts/native16-arm64/chromium-native/README.current.md) · [Strata host-cache preparation](../9to1%20Models/Dulche%20Alpha/InferenceEngines/NativeStrata/prep/host-cache-v0.2.3.1/README.current.md) · [Dulche bundle](../9to1%20Workspace/Home/Source/Dulche/bundle/README.current.md) · [Offline staging](../9to1%20Workspace/Home/Source/Dulche/bundle/offline-staging-recipe.current.md) · [Preview asset profile](../9to1%20Workspace/Home/Source/Dulche/bundle/preview-asset-profile.current.md)
