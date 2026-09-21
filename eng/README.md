# Repository commands

`9to1.ps1` is the root entry point for the buildable consolidation sets and the
package recipes that actually exist. It deliberately fails when a requested
package path is not implemented instead of reporting a placeholder as success.

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
