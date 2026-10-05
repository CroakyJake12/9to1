This is the unadopted successor02 source proposal; original01 and its setup defect remain preserved. Install the workflow as `.github/workflows/team-b-ordinary-native.yml` and the driver as `apps/Web/Tests/ci/run-ordinary-native.py` only after root/B6 review. B1 has not pushed, dispatched, built or executed the workflow.

The workflow accepts only Team B branches (`team-b/**`); it checks out `github.sha` and the driver rejects a different actual HEAD. Six independent Ubuntu 24.04 matrix jobs use SDK 10.0.401, read-only repository permissions, a 30-minute job timeout and a 26-minute driver deadline. Each job restores and builds its genuine maintained project through ordinary SDK/NuGet resolution with its own fresh artifact, CLI, package, temp and fixture directories. The two required Avalonia gitlinks are initialized at their checked-out commits; unrelated app/OS submodules are not fetched.

| Job | Maintained project | Required unchanged checks | Native argv after apphost |
|---|---|---:|---|
| navigation5 | apps/Web/Tests/BrowserNavigationCancellation.Tests.csproj | 5 | isolated output.json |
| navigation37 | apps/Web/Tests/BrowserNavigation.Tests.csproj | 37 | none |
| bindings15 | apps/Web/Accounts/Tests/BrowserAccounts.Tests.csproj | 15 | none |
| factory6 | apps/Web/Accounts/Tests/AccountSettings.Factory.Tests.csproj | 6 | none |
| files17 | apps/Web/Files/Tests/FilesBrowser.Tests.csproj | 17 | isolated Files directory |
| sites18 | apps/Web/Sites/Tests/SitesBrowser.Tests.csproj | 18 | suite, isolated Sites directory |

Root must first adopt the reviewed B-owned cancellation repair and repository navigation test/project, and the reviewed Files/Sites receipt fixes. Current Sites source has 13 cases; the intended B4 successor has 18. The CI driver deliberately fails 13 rather than relabel it 18. The Files successor keeps 17 cases. It never patches source or loads the prior compiled fixture. No future commit is baked into this proposal.

The commands use Release, linux-x64, SelfContained=false, disabled shared compilation/build servers and node reuse, normal owner warning policies, and fresh per-job `--artifacts-path`. The genuine source-built Avalonia task location follows the already observed maintained project output path. No local disk workaround or copy suppression is imported. CLI/project restore resolves the normal official packages using the root maintained NuGet.Config; no bodies, assets or tool DLLs are carried over from local evidence.

Restore failure stops build/native for that project. Build or actual target/apphost validation failure stops its native execution. Other matrix jobs remain independent and `fail-fast` is false. Every invoked command records stdout EOF, exit status, held child births/pidfds, final ECHILD and forced signals. Timeout, incomplete closure, any forced cleanup, log overflow or nonzero exit is a failure. Native summaries and per-case PASS counts must match the exact required counts; navigation5 also checks the genuine five-result JSON. There is no changed behavioral assertion or provider acceptance claim.

Uploads are restricted to `diagnostics`: command logs/receipts, actual HEAD, tracked git blob/gitlink source IDs, submodule IDs, dotnet --info/version, evaluated target properties, PE/PDB counts and result metadata. Each log is capped at 8 MiB. Fixture data, repositories/profiles, caches, packages, assemblies, apphosts, native libraries and raw native5 fixture JSON are outside the upload directory. No app secrets, authenticated provider, production service, deployment or publishing step exists.

The original03 512 MiB RESOURCE_HOLD, actual17 120 MiB additional RESOURCE_HOLD, forced-family result and every historical NOT_RUN remain separate evidence. A future CI success would establish only these six native/source-bound regression suites on that exact GitHub-hosted commit. WASM/browser, accessibility/layout, authenticated services, deployment and full application parity remain separate gates.

`source-preflight.json` records syntax and pure parser/count negative controls only. It is not execution of the driver, subprocess family, SDK, apphost or workflow.

Successor02 permits the three temporary-directory environment aliases to share their one fresh owned directory. It also requires the actual Sites IndependentProcesses child-created seed/verify log+exit receipts, exact original 17/6 assertion counts and a nonempty native canonical-oracle hash/size witness. The native C# identity/schema/index/renderer/artifact checks remain unchanged. Oracle and fixture bodies/IDs are never uploaded. These checks complement the same18 parent cases; they do not add those child assertions to the parent-case denominator.
