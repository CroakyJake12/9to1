# C5 online services — proposed bounded patch, incomplete release

Run `sol-happy-20261003`; baseline `98a08827c9fbc486fe987da73f4aee8398120d31`;
branch `team-c/sol-happy-c5`; canonical remote `https://github.com/CroakyJake12/9to1.git`.
Specification source: live document `1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg`,
revision `AHj4eMRBcI3x5FFYbDM2m-hiWUCk_SbZphbsfCtNVrsp0bqkXxEmCGQMUSBbZxWMB76lDNvgrMwyepTjeCqVIpuSFZx5BKabswqtngHgOTo`,
read by coordinator 2026-10-03T16:55:39.608Z. This is provisional branch evidence,
not integrated-candidate, hosted-service, real-provider or release acceptance.

## Proposed ownership and repair

Team A acknowledgement is required before integration of the shared provider
implementation. Root authorised preparation on this isolated branch while that
acknowledgement is pending. No public signatures, canonical models or accounting
contracts change. C3 retains authoritative usage/reservation/settlement ownership.

`9to1 Workspace/shared/src/Haven.Infrastructure/Providers/CloudModelProviders.cs`
is the existing production OpenAI-compatible provider implementation. It previously
silently finished on truncated EOF or HTTP-200 SSE error frames, and buffered
StreamReader lines could consume completion after caller cancellation. The patch
rejects provider errors and EOF without `[DONE]`, preserves already emitted output,
and checks cancellation for every line and EOF. Error packets are not copied into
exceptions. IOException intentionally does not match the existing routing layer's
recoverable fallback classifier, so an uncertain response does not automatically
dispatch another provider. No retry, balance, charge or external action is added.

## Requirement-to-test map

These are C5 hosted-service slices of canonical requirements, not a replacement
for the full-spec release denominator. No hosted journey below has passed.

| ID / source | Required test/procedure and expected result | State / evidence |
|---|---|---|
| C5-STREAM / SH-10 streaming | `Completion_marker_preserves_content_and_ignores_trailing_transport_data`, `Null_error_and_empty_usage_frame_do_not_break_completed_stream`: exact content, one request, completion acknowledged | Local implementation verified only; deployed provider BLOCKED |
| C5-INTERRUPT / SH-10 failures/interrupted sessions | `Eof_without_completion_is_failure_without_repeating_request` (3 cases), `Provider_error_frame_is_failure_and_does_not_expose_error_payload` (2): explicit incomplete exception, partial output intact, disposal, no repeated dispatch, private detail absent | Local implementation verified only; persisted cloud recovery MISSING |
| C5-CANCEL / SH-10 cancellation | `Cancellation_during_stream_is_propagated_and_disposes_response`: partial text then cancelled enumeration, token exception and disposed response | Local implementation verified only; provider-side cancellation/settlement BLOCKED |
| C5-USAGE / SH-10 reserve/run/settle | `Confirmed_usage_is_preserved_for_completed_and_interrupted_responses` (2): actual reported tokens retained, absent counts remain null, consume does not invent usage | Local capture verified; C3 transactional reservation/settlement integration BLOCKED, not established by this test |
| C5-ENDPOINT / Privacy Ceiling transport | Existing `ProviderEndpointSecurityTests` (6): remote HTTP/invalid rejected; HTTPS/local HTTP accepted | Local unchanged regression verified; deployed credentials/privacy gates BLOCKED |
| C5-AUTH / SH-10 preflight | Isolated authenticated personal/org calls, revoke permission before dispatch; denied model/route/limits cause zero provider requests | MISSING hosted gateway; depends C2 identity, C3 entitlements, Team A permission/model contracts |
| C5-BUDGET / SH-10 parallel requests | Concurrent reserve/run/settle against C3 ledger; insufficient funds deny; uncertain execution reconciles; local tools/models incur no cloud charge | BLOCKED C3 authoritative accounting integration; no duplicate ledger created |
| C5-CHANNEL / SH-10 shared allocation | Desktop/web/Personal API requests resolve same canonical account allocation and eligibility, with separate organisation funding | BLOCKED shared account/entitlement APIs and real runtime/provider access |
| C5-JOB / SH-10 durable jobs | Start actual cloud job, close browser, restart executor, reconnect with same JobID; results and revision survive and completed actions do not replay | MISSING hosted executor/deployment; existing local runtimes are not a cloud service |
| C5-LOCALITY / SH-10 execution locality | Device-bound job with enrolled device offline returns DeviceOffline/unavailable; cloud work continues without UI | BLOCKED enrolled-device/cloud runtime integration; no device fabricated |
| C5-IDENTITY / SH-10 durable authority | Recover job with persisted caller/organisation/scope/funding; revoked grants stop future actions; wrong account cannot inspect or resume | MISSING hosted job authority boundary; depends C2/C3/shared permission contracts |
| C5-AUTOMATION / Automations durable Wait/retry | Canonical graph pinned Active revision; durable Wait/Approval survives restart; duplicate TriggerEventID and uncertain consequential outcomes never repeat effects | Existing local orchestration IMPLEMENTED-UNVERIFIED; hosted execution MISSING; owning app integration remains Team A/B |
| C5-AGENT / Agents lifecycle/Den | Same AgentID/definition revision across surfaces; app closes, persistent Agent continues; narrowed permissions, queue, checkpoint, pause/resume/retry/delegation obey identity/budgets | Local Den/runtime IMPLEMENTED-UNVERIFIED; real hosted adapters MISSING; no parallel Agent model |
| C5-NOTIFY / Home notifications, SH-10/11 | Failure/approval notification preserves NotificationID/actions/read-dismiss state after restart; delivery failures visible, denied cross-account read | Native implementation IMPLEMENTED-UNVERIFIED; hosted notification delivery MISSING |
| C5-COLLAB / SH-10 online dependencies | Two authorised clients edit same canonical artifact; scoped live updates, conflict/revision checks, revoke membership/share then suppress content | MISSING hosted collaboration integration; app-semantic ownership preserved |
| C5-CONNECT / SH-10 Forms/published integrations | Real authorised connector/Forms submission persists exact canonical response; replay deduplication, revoked credentials and provider failure truthful | BLOCKED provider connections and hosted owning-service implementation |
| C5-OPERATE / SH-11/12 | Real health/alerts, bounded admission, failed-job recovery, backup restore, representative workload, service operates without developer PC | MISSING deployment/runtime validation, credentials and approved operational configuration |
| C5-PRIVACY / Privacy Ceiling, SH-11 | Cross-account denial, local/cloud authorisation, payload/log minimisation, processor locality, retention/delete/export checks against deployed provider paths | BLOCKED deployed service and approved privacy/retention configuration; local error-redaction test is partial only |

General Rules, full Agentic Operating Contract and Terra-form were read. TF-X01/X02
require canonical IDs, owning APIs, scopes and durable operations; this patch changes
none of those contracts. Hosted artifact services remain the C4/owning-team dependency.
Automations' full app acceptance and Agents' full product acceptance remain part of
the shared release ledger; these hosted slices do not exempt other requirements.

## Reproduction and raw evidence

Linux x64; SDK `/workspace/.tools/dotnet/dotnet` (10.0.401); isolated transport fixtures
only, no credentials, provider spending, provisioning, live charge or deployment.
All commands run from the isolated branch root. Environment prefix for each dotnet
command (without changing HOME):

```bash
export DOTNET_CLI_HOME=/workspace/team-c/evidence/c5/dotnet-home
export NUGET_PACKAGES=/workspace/team-c/evidence/c5/nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
```

Production-project commands:

```bash
/workspace/.tools/dotnet/dotnet test '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj' -c Debug --filter FullyQualifiedName~CloudModelProviderStreamTests --logger 'trx;LogFileName=c5-provider-debug.trx' --results-directory /workspace/team-c/evidence/c5/debug
/workspace/.tools/dotnet/dotnet test '9to1 Workspace/shared/tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj' -c Release --logger 'trx;LogFileName=c5-full-release.trx' --results-directory /workspace/team-c/evidence/c5/full-release
```

Both exit 1 before discovery (zero executed), unchanged
`Haven.Application/Canvas/CanvasArtifactSession.cs:511` CS1593: two-argument lambda
passed to `Func<CanvasChangeEvent,bool>`. Logs `debug-test.log`, `full-release.log`
under `/workspace/team-c/evidence/c5`. Do not count these commands as passing tests.
Initial launch also failed due read-only default CLI home; isolated CLI_HOME resolved it.

Source-linked partial harness (full changed production provider file, real Core,
verbatim source excerpts for request records/usage buffer, original full provider
contracts, same tests; no replacement implementation):

```bash
python3 docs/releases/sol-happy-20261003/c5-provider-harness.py /workspace/team-c/evidence/c5/harness
/workspace/.tools/dotnet/dotnet test /workspace/team-c/evidence/c5/harness/C5.Provider.Tests.csproj -c Debug --logger 'trx;LogFileName=c5-harness-debug-final.trx' --results-directory /workspace/team-c/evidence/c5/harness-debug
/workspace/.tools/dotnet/dotnet test /workspace/team-c/evidence/c5/harness/C5.Provider.Tests.csproj -c Release --logger 'trx;LogFileName=c5-harness-release-final.trx' --results-directory /workspace/team-c/evidence/c5/harness-release
```

Final Debug and Release: exit 0, 16 discovered/executed/passed, zero failed/skipped
(10 new stream cases + 6 unchanged endpoint cases), no build warnings/errors.
Logs `harness-debug-final.log`, `harness-release-final.log`; TRX files in the named
result directories; original-source SHA256 manifest `harness-source-hashes.txt`.

Controlled negative test, isolated original provider source only:

```bash
git show '98a08827c9fbc486fe987da73f4aee8398120d31:9to1 Workspace/shared/src/Haven.Infrastructure/Providers/CloudModelProviders.cs' > /workspace/team-c/evidence/c5/CloudModelProviders.baseline.cs
python3 docs/releases/sol-happy-20261003/c5-provider-harness.py /workspace/team-c/evidence/c5/baseline-harness --provider-source /workspace/team-c/evidence/c5/CloudModelProviders.baseline.cs
/workspace/.tools/dotnet/dotnet test /workspace/team-c/evidence/c5/baseline-harness/C5.Provider.Tests.csproj -c Debug --logger 'trx;LogFileName=c5-baseline-negative-final.trx' --results-directory /workspace/team-c/evidence/c5/baseline-negative
```

Negative control: exit 1, 16 executed, 7 fail / 9 pass / 0 skip: premature EOF,
provider-declared errors, buffered cancellation and interrupted usage tests detect
the original violations. Earlier 8-case control failed 6/passed 2. Initial patched
8-case run failed cancellation (1 fail/7 pass); explicit buffered-line cancellation
repair then passed 8/8. All intermediate logs/TRX are retained, not overwritten.

Historical GenUI source-index metadata is stale: 9450 records match its count but
actual SHA256 `f91a560e6004cdcdc946e7481d5d93c931709c90448ed5befe5d1c4c8b74e0ec`
differs from declared `FB8770C7B2675B7349ADF48AF7857099017E109984473D5892CC126649D51784`.
That ledger is untouched and is not accepted as current release evidence.

Required follow-through: Team A ownership acknowledgement, independent C6/root
review, repair the owning-team compilation defect, run full Debug/Release suites
and current integrated-candidate runtime/provider/cloud jobs acceptance. No hosted
service is declared online. Production configuration, credentials, budgets,
deployment and billing approval remain distinct external gates.

C6 independently reviewed the diff and routing classifier and reproduced Debug
16/16 before commit; no bounded-patch blocking finding. C6 identified a material
compatibility gate: requiring `[DONE]` must be verified against each supported real
OpenAI-compatible provider. Existing line-based SSE parsing is retained; these tests
do not establish support for arbitrary multi-line SSE/protocol variants.

## Existing unmerged Astra comparison

Root identified draft/held Astra source
`a63d77fe5a9dfea56c938eaa85678a9e170368ec`, not merged to accepted main. `git show`
comparison confirms its CloudModelProviders.cs bytes exactly match baseline
(SHA256 `80ecb0678d3890445991ae1ae287255dce48a915d3b2a842943a70993218da35`),
so this stream repair is absent there, not duplicate accepted or unmerged work.
UsageModels.cs and ModelUsageRepository.cs also have no difference. Existing
unmerged changes to Abstractions.cs add verified transport-locality defaults;
ModelProviderAbstractions.cs adds ModelCataloguePolicy/scoped model enumeration.
These additions are preserved as Team A's distinct unmerged work, not copied or
reimplemented here. The private implementation patch changes no affected interface.
Comparison evidence: `/workspace/team-c/evidence/c5/astra-contract-comparison.diff`.
