# C5 acknowledged provider patch — isolated a63 proposal

Base source `a63d77fe5a9dfea56c938eaa85678a9e170368ec`; branch
`team-c/sol-happy-c5-provider-reconciled`. This is a proposed isolated source
successor, not root release/reconciliation, accepted main or deployed service.
Remote main identity remains `98a08827c9fbc486fe987da73f4aee8398120d31`, with
full-specification acceptance unverified.

## Qualified owner acknowledgement and source distinction

Root supplied remotely verified Team A record `3e5a293f` on coordination head
`10edea30`, `coordination/sol-happy-20261003/team-a.json`,
`sharedPatchAcknowledgements.C5`: A3 acknowledges the exact seven EOF/error/
buffered-cancellation lines of C5 `4f6040271094748f9065b3b53f1239b0d7f2190d`
**applied to current normal30**, with no overlapping provider draft or public
model/signature change. Input manifest SHA256
`17bcee5655bcddf855ad8b4dac223501849d375cbe84c344d99a015c91c92e02`.
The acknowledgement expressly retains real provider/protocol/timeout/privacy/
billing/current integrated source/PDB acceptance gates.

Current normal30/RAM source bodies were not delivered to this workspace. Root is
requesting exact source/hashes; **a63 is not assumed identical to normal30**.
This proposal applies precisely those seven lines to the verified Git a63 body.
No normal30 adoption, integration or stronger acknowledgement is claimed.
Owning current contracts, selected RAM work and unrelated development remain intact.

The production diff contains only seven added lines in
`9to1 Workspace/shared/src/Haven.Infrastructure/Providers/CloudModelProviders.cs`.
It is byte-identical to the original acknowledged provider diff; no current public
signature, request/body payload contract, routing classifier or usage authority
is replaced. Before-patch provider SHA256
`80ecb0678d3890445991ae1ae287255dce48a915d3b2a842943a70993218da35`;
after-patch SHA256 `fd162747b5b95cb02bb5ca2e47cf7c596353d92a30551080622f265e24cbebd0`.
Current a63 routing SHA256
`89c673378bae8f3b43000db93414a3a869a3bfcd25de50ffe6e585a252ed0caf`;
current usage SHA256
`3bf306530e1cb848336dc08856cd39ffc44ea0efd7d6c3cdf1247def02dcbc1a`.

## Behaviour and test boundaries

SSE error packets and EOF without completion fail as IOException without copying
private provider details. Cancellation is checked for buffered lines and EOF.
Current `ResilientProviderRoutingModelClient` excludes IOException from automatic
fallback. New tests run actual OpenAI and compatible provider classes through
actual registry/routing implementations with an available discovered/configured
fallback; incomplete empty/error/partial responses issue exactly one first-provider
POST and **zero fallback POSTs**. The partial case retains exact emitted content
and provider-confirmed usage; absent usage stays null, no credit/charge is invented.
No accounting/budget/entitlement model or provider credential is introduced.

Transport, configuration and secret fixtures remain local doubles. Even tests of
normal production assemblies do not establish a real remote provider, hosted
gateway, durable cloud job or reserve/run/settle integration. C3 remains the
authoritative accounting owner. `[DONE]` completion framing must be checked against
every supported real compatible provider; existing line-based SSE parser is retained.

## Reproduction

Linux x64; .NET SDK 10.0.401. Worktree
`/workspace/team-c/c5-provider-reconciled`. Environment prefix for dotnet commands:

```bash
export DOTNET_CLI_HOME=/workspace/team-c/evidence/c5/reconciled/dotnet-home
export NUGET_PACKAGES=/workspace/team-c/evidence/c5/nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
```

```bash
# Original 16 scoped cases, current source/contract excerpts:
python3 docs/releases/sol-happy-20261003/c5-provider-harness.py /workspace/team-c/evidence/c5/reconciled/harness
/workspace/.tools/dotnet/dotnet test /workspace/team-c/evidence/c5/reconciled/harness/C5.Provider.Tests.csproj -c Debug --logger 'trx;LogFileName=c5-reconciled-sixteen.trx' --results-directory /workspace/team-c/evidence/c5/reconciled/sixteen

# Full current routing source + 16 prior cases + 3 new routing cases + 5 existing routing regressions:
python3 docs/releases/sol-happy-20261003/c5-provider-harness.py /workspace/team-c/evidence/c5/reconciled/routing-harness --routing
/workspace/.tools/dotnet/dotnet test /workspace/team-c/evidence/c5/reconciled/routing-harness/C5.Provider.Tests.csproj -c Release --logger 'trx;LogFileName=c5-reconciled-routing-release.trx' --results-directory /workspace/team-c/evidence/c5/reconciled/routing-release

# Stronger normal production-project closure, all five current owning test files:
python3 docs/releases/sol-happy-20261003/c5-provider-harness.py /workspace/team-c/evidence/c5/reconciled/production-project-harness --production-project
/workspace/.tools/dotnet/dotnet test /workspace/team-c/evidence/c5/reconciled/production-project-harness/C5.Provider.Tests.csproj -c Debug --logger 'trx;LogFileName=c5-production-scope-debug.trx' --results-directory /workspace/team-c/evidence/c5/reconciled/production-debug
/workspace/.tools/dotnet/dotnet test /workspace/team-c/evidence/c5/reconciled/production-project-harness/C5.Provider.Tests.csproj -c Release --logger 'trx;LogFileName=c5-production-scope-release.trx' --results-directory /workspace/team-c/evidence/c5/reconciled/production-release
```

The production-project harness references the **real Infrastructure.csproj**,
building its normal Application/Home/Dulche/Core dependencies. It links full current
test files, no copied/extracted production declarations or substituted implementations.
It avoids the unrelated Browse/Avalonia build-task dependency in the overall test
project, whose failure is retained and remains a gate. It includes all 14 current
endpoint cases (including pinned transport redirect test), 10 stream cases, 3 new
routing cases, 5 unchanged routing cases and 3 actual usage/SQLite persistence cases.
Source-linked mode explicitly includes only the six endpoint cases relevant to its
closure; the expanded original tests remain unchanged and are all included in the
stronger production-project harness, never removed/skipped/weakened.

| Check | Result |
|---|---|
| Original scoped source-linked cases | Debug 16 discovered/executed/passed; exit 0; 0 fail/skip |
| Current source-linked routing closure | Debug/Release 24 discovered/executed/passed each; exit 0; 0 fail/skip |
| Actual normal Infrastructure project closure | Debug/Release 35 discovered/executed/passed each; exit 0; 0 fail/skip |
| a63 original provider negative control | 24 executed: 10 failed, 14 passed, 0 skipped; exit 1 expected |
| Overall Infrastructure test project | Debug/Release exit 1 before discovery; 0 executed, missing XamlX in Avalonia.Build.Tasks |

Negative control uses only isolated original a63 provider bytes; actual working
source is not reverted and no production/provider fault is injected:

```bash
git show 'a63d77fe5a9dfea56c938eaa85678a9e170368ec:9to1 Workspace/shared/src/Haven.Infrastructure/Providers/CloudModelProviders.cs' > /workspace/team-c/evidence/c5/reconciled/CloudModelProviders.a63negative.cs
python3 docs/releases/sol-happy-20261003/c5-provider-harness.py /workspace/team-c/evidence/c5/reconciled/negative-routing-harness --routing --provider-source /workspace/team-c/evidence/c5/reconciled/CloudModelProviders.a63negative.cs
/workspace/.tools/dotnet/dotnet test /workspace/team-c/evidence/c5/reconciled/negative-routing-harness/C5.Provider.Tests.csproj -c Debug --logger 'trx;LogFileName=c5-reconciled-negative.trx' --results-directory /workspace/team-c/evidence/c5/reconciled/negative
```

Normal overall attempts:

```bash
/workspace/.tools/dotnet/dotnet test '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj' -c Debug --filter 'FullyQualifiedName~CloudModelProviderStreamTests|FullyQualifiedName~ProviderEndpointSecurityTests|FullyQualifiedName~ResilientProviderRoutingModelClientTests|FullyQualifiedName~ModelUsageRepositoryTests' --logger 'trx;LogFileName=c5-reconciled-project-debug.trx' --results-directory /workspace/team-c/evidence/c5/reconciled/project-debug
/workspace/.tools/dotnet/dotnet test '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj' -c Release --filter 'FullyQualifiedName~CloudModelProviderStreamTests|FullyQualifiedName~CloudModelProviderRoutingFailureTests|FullyQualifiedName~ProviderEndpointSecurityTests|FullyQualifiedName~ResilientProviderRoutingModelClientTests|FullyQualifiedName~ModelUsageRepositoryTests' --logger 'trx;LogFileName=c5-reconciled-project-release.trx' --results-directory /workspace/team-c/evidence/c5/reconciled/project-release
```

All raw logs, source manifests and TRX are preserved under
`/workspace/team-c/evidence/c5/reconciled`; hashes/counts are in the adjacent
`C5-provider-reconciled-results.json`. Initial source-linked harness compile failure
on current expanded endpoint dependencies is retained as `harness-debug.log`; it is
not a passing run. Old main Canvas failures are historical and are not relabelled
as current a63 failures. Exact committed-head results are recorded separately
before proposed-branch publication. No release/coord/main branch is changed.

Remaining gates: delivered normal30 source/body comparison, qualified current owner
acceptance, independent C6/root review, overall project dependency repair by owner,
integrated exact-source/PDB tests, real provider/protocol/timeout/privacy/funding and
deployed-runtime acceptance. No provider spending, provisioning or deployment occurred.
