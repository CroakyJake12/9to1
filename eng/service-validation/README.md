# Partial service-source validation

`Sites.Source.Tests.csproj` compiles the actual Sites domain, store, application,
hosting services and existing tests, with actual shared abstraction sources and
Haven.Core. It enables reproducible bounded checks while the normal Sites project
is blocked by unrelated shared application compilation. No source is copied or
replaced. Provider, DNS and authorization fixtures remain controlled test doubles.

```sh
dotnet test eng/service-validation/Sites.Source.Tests.csproj -c Release
node --test cloud/billing/test/*.test.mjs
```

These commands provide local partial evidence. They do not replace the normal
Sites project, deployed provider integration, UI/API parity, installed applications,
or the full specification acceptance suite. The normal project and baseline
failures remain required release blockers. Count discovered, executed and skipped
tests independently; a successful process with no discovered tests is not a pass.

The recovered CAKE ID package has its own documented local Workerd/D1 tests under
`cloud/cake-id-auth`. It must be reviewed and integrated before those paths exist
on this candidate. No command here provisions remote infrastructure.
