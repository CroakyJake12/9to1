# Partial service-source validation

The Sites source test project compiles the Sites domain, store, hosting services and tests with their real shared sources and Haven.Core. It isolates those components when the normal solution has an unrelated failure. Provider, DNS and authorisation dependencies in this lane use controlled fixtures. Run the ordinary owning application lane as well when reporting a full product result.

```sh
dotnet test eng/service-validation/Sites.Source.Tests.csproj -c Release
node --test cloud/billing/test/*.test.mjs
```

These commands provide local partial evidence. They do not replace the normal
Sites project, deployed provider integration, UI/API parity, installed applications,
or the full specification acceptance suite. The normal project and baseline
failures remain required release blockers. Count discovered, executed and skipped
tests independently; a successful process with no discovered tests is not a pass.

CAKE ID has a separate local Workerd/D1 test suite in the checked-in service. Its results cover that service’s local composition. Preserve the failure output and revision when comparing it with the ordinary project or a deployed route. [CAKE ID service checks](../../cloud/cake-id-auth/README.md)
