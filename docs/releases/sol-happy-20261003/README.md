# Sol Happy preparatory source handoff — BLOCKED

This branch contains reviewed hosted-service repairs and retained evidence. It
is not an accepted release, deployed service, installed product or verified
delivery to Jacob's PC. The combined accepted-head pointer remains null.

## Changes available for review

- Sites deployment and rollback retain artifact/source/configuration provenance,
  validation and secret checks; disabled environments and cancellation cannot
  produce a misleading successful deployment.
- Domain verification rejects stale positive/negative DNS results after challenge
  rotation or concurrent ownership verification.
- The Stripe adapter verifies raw-body signatures and durably records minimal
  receipts before acknowledgement, with deduplication and restart-safe processing
  ownership. It contains no replacement account ledger, balance, entitlement
  consumer, checkout or approved commercial policy.
- The existing CAKE ID Worker backup `7cc52df` is retained in history. Bounded
  repairs cover authoritative administrator checks, OIDC resource scopes/issuer,
  durable login enforcement, required profile fields and reserved-handle owner
  binding. Local tests exercise recovery, profile revisions, refresh and session
  revocation. Real Jacob provisioning and external email remain unverified.
- Team B's evidence-integrity validator is included. It never marks inventory
  completeness or product parity true; independent probes demonstrate that
  semantic applicability still requires human review.

## Exact local evidence

Service runtime/test/package/schema source was tested at
`a44ca3a10a538afd0715f3ad3dcd8d83931c2f70`, tree
`c1e9df46618f14985aff5f222706b448a34c65da`. Later changes add documentation,
evidence and the separately tested evidence validator; recorded service source
bytes remain unchanged. Old results are not relabelled as final release acceptance.
Commands, identities, raw output, TRX results and SHA-256 hashes are retained in
[final/summary.json](final/summary.json) and [final/manifest.json](final/manifest.json).

| Check | Exact scope | Result |
|---|---|---|
| Sites | Actual-source linked services/tests; controlled provider/DNS/permission fixtures | 39 discovered/executed/passed, zero failed/skipped |
| Billing | Actual Worker fetch, Web Crypto and local real SQLite | 12 discovered/executed/passed, zero failed/skipped |
| Identity | Native Linux Workerd, local D1, maintained OAuth library and local email capture | 1 program, 222 executed assertions, exit 0 |
| Identity supporting checks | Typecheck, schema validation, UI bundle build | All exit 0 |
| Browser evidence validator | Record-integrity unit checks at `136631c2dbfc6d56a39f5f20530cb4202cc23977` | 32 discovered/executed/passed, zero failed/skipped |

The first integrated identity invocation exited before readiness because Wrangler
used a protected default logging directory. Its original failure log is retained.
A same-source retry used supported task-specific config/log paths and passed;
no source or assertion was changed.

Independent implementations, negative controls and C6 reproductions are retained
under [c6/](c6/). The isolated, uncommitted Astra reconciliation tree
`674bd79202e3ce531ad0601d94b479353c6f813a` passed normal core 386/386 and normal
Sites 66/66, with the tree unchanged before/after. It is a compatibility rehearsal,
not a delivered commit or source-acceptance decision. Incoming Astra `a63d77f`
independently passed normal core 386/386. Earlier `98a0882` compilation/trust
failures are historical baseline evidence and do not establish failures on Astra.

## Run the bounded checks

Use .NET 10 and Node 24 with `node:sqlite`. Run from the repository root:

```sh
dotnet test eng/service-validation/Sites.Source.Tests.csproj -c Release
node --test cloud/billing/test/stripe-webhook.test.mjs
python -m unittest discover -s eng/browser/tests -p 'test_*.py' -v
```

For the issuer, follow [cloud/cake-id-auth/README.md](../../../cloud/cake-id-auth/README.md):
`npm ci`, `npm run typecheck`, `npm run schema:validate`, `npm run build:ui`,
then `npm test` in that directory. Reserve loopback port 8798 for the local test
program. In a restricted managed environment set writable `XDG_CONFIG_HOME`,
`XDG_CACHE_HOME` and `WRANGLER_LOG_PATH`, and set `WRANGLER_SEND_METRICS=false`.
The program creates isolated synthetic data and removes its ephemeral secret file.
These commands provision no remote infrastructure. There is no verified deployed
test URL. A loopback test URL is not a public service.

## Ownership and reconciliation

The remotely verified transport is `coord/sol-happy-20261003` in
`https://github.com/CroakyJake12/9to1.git`. Team A published an explicit existing
integrator handover to Team C and acknowledged C1/C4 ownership. Team C alone
writes this release branch, the combined index and accepted-head pointer.
Team A retains native/shared source branches; Team B retains browser surfaces.

Incoming Astra history contains 1,568 changed files relative to main and has
successful provisional CI. Its source acceptance and final integration remain
pending; successful CI does not supply full native/provider/package acceptance.
Team B fixed the three independently reproduced browser review defects in incoming
source `830f747b70df07797c586f6124890fa5028452b2`: registered Home owners now execute
before unavailable fallback, profile fields are captured before token acquisition,
and update success requires exactly the next revision. Independent exact-source
checks passed 22/22 route tests and 28/28 account tests; the original account probes
also confirm both repairs. Closure evidence is in [browser-review-update/](browser-review-update/).
Original failure evidence remains in [final/](final/). This browser source depends
on provisional Astra `a63d77f`, including shared CUI theme/font helpers absent from
this branch. Team A1 subsequently acknowledged the maintained CUI font/theme API
and exact Runtime dependency. Incorporating provisional shared source, published
WASM artifacts and actual mounted-browser/provider acceptance remain pending.

Team B subsequently published runtime fixes `9697281` and `c74c73c`, retaining
the earlier Skia startup and exited-runtime failures. Its coordination report
`986fbb2c` records local startup/fragment/history passes, with Library pointer
interaction, Dashboard layout and semantic-control accessibility still failing or
unaccepted. These are owning-team reports; the full browser evidence package and
independent runtime verification have not been delivered here. They do not change
the bounded three-finding closure at `830f747` or establish full browser acceptance.

C1's [new runtime source review](browser-review-update/runtime-source-review.md)
corrects an earlier inference: the false native-input flags also disabled the
transitive native packages. That inference in the preserved original review is
withdrawn. The newer source restores actual SDK-selected inputs and uses its
public `runMain` API. Source review found no actionable bounded regression;
native ABI/runtime and the reported startup results remain independently unverified.

Team A acknowledged narrowly scoped Home identity and provider-stream edits and
ownership of configured OIDC resource/response-envelope repairs. Home's isolated
rebase `b73a45b4` onto `a63d77f` preserves the existing service body outside the three
approved production edits. Normal Home tests passed 331/331; unchanged Astra with
only the new theory failed both regression controls. Exact A1 owner acknowledgement
at `eecadfc7463c944bcc6f9788075c293ab14120f9` clears the normal candidate's
ownership/current-body qualification. The delivered future AppliedGrantId body
remains reference only and must preserve these narrow edits in later composition.
Root adoption and integrated runtime acceptance remain pending. Evidence is in
[shared-source-proposals/](shared-source-proposals/). These proposals are not
integrated here, and local checks do not verify the full issuer/native flow.

Provider proposal `54766d2a` applies exactly the seven acknowledged stream edits
to Astra. The actual Infrastructure-project closure passed 35/35 in Debug and
Release, including an exact committed-source Release run; original-source controls
failed 10 of 24 cases as expected. C1 independently reviewed the patch, actual
routing/usage tests, generator and raw results. The full normal test project still
fails before discovery because its pinned XamlX dependency is absent from that
checkout. Those failures are retained. The delivered per-file map now selects the
unchanged actual A63 provider and
clears that provider-file source-equivalence condition. Wider current/RAM tree
equality, integrated runtime and real provider acceptance remain unverified.
The [source-selection review](C5-provider-source-equivalence-review.md) retains
those limits. Reproduction details are in
[C5-provider-reconciled.md](C5-provider-reconciled.md); its generator is used in
the separately checked-out proposal where the referenced current test files exist.

OIDC proposal `542aca10` supplies the configured resource at authorization and
code exchange and unwraps the actual Worker profile/session envelopes. Two
maintained-source controlled harnesses passed, with five new named protocol
checks; these executable suites report no total discovery count. C1 independently
reviewed every changed line and reproduced both passes. Earlier protocol failures
are retained. Authentication guards stay intact; genuine HTTPS issuer execution,
approved signing/key compatibility and the authentication-revision producer
contract remain unverified. A3 now gives exact source-owner acknowledgement for
`542aca10`, with its complete original peer record retained and all eight source
hashes plus four unchanged guard APIs checked by root. That clears source-owner
review only. The proposal remains on its own remote branch, with no adoption or
new runtime acceptance.

The strict .NET consumer and hosted issuer still have five concrete compatibility
gaps described in [CONTRACT_COMPATIBILITY.md](../../../cloud/cake-id-auth/CONTRACT_COMPATIBILITY.md).
No alternate identity service, fabricated authentication revision, weakened
signature check or guessed organisation/permission/billing contract was introduced.

C4 gives a bounded [Files Project design acknowledgement](semantic/c4-project-contract-ack.md)
for existing identities, the same durable State authority, logical membership and
preservation of originals. Six wire/revision/digest/migration/ACL/reference seams
remain unresolved. No Project wire or synthetic Space was introduced. Admin
source04/SDK03's 17 source products, six original validation targets and metadata
are now delivered at `eecadfc7463c944bcc6f9788075c293ab14120f9`; all 33 received
manifest/blob identities match. Independent receiving-path review identified a
missing tracked complete source cut. An isolated validation successor supplies
that cut while preserving the original driver/source/metadata bytes. C1 verified
the complete receiving maps and root issued independent external pins. The first
original driver run refused before SDK work because `execv` could not resolve
bare `dotnet`. A separately reviewed launcher correction allowed all 27 commands
in the second run to exit zero, including the complete original Debug builds.
Only 26/27 original sessions drained: an orphaned MSBuild zombie kept the final
strict disappearance check false. No original suite, Release build or full
evaluated-source/PDB phase ran. Both failures and their original receipts remain
in [Admin SDK03 evidence](evidence/c3-admin-sdk03/README.md).

A separate maintained Accounts regression reproduced a persisted-key defect:
request validation accepted control characters that the next shared-state read
rejected. The proposed one-line correction rejects those keys before writes.
The full maintained Accounts assembly passes with the original checks and 16
new operation/control cases, while the exact original-source regression fails.
Actual before/fixed outputs and source/PDB proofs are retained. This is bounded
local Accounts evidence, with no Web HTTP, provider or whole-product acceptance.
The key and launcher fixes remain isolated proposals awaiting source-owner
acknowledgement and integration. Installed SDK 10.0.401 differs from the workflow's
10.0.301. A genuine owned-child reaping correction is under separate review;
failed process custody has not been waived.

Requested original native CI bytes are retained under [c6/original-custody/](c6/original-custody/).
Both requested outer ZIP hashes and all 159 included indexed pieces match. The
original Home log proves a display-acknowledgement failure; Canvas shows 91/96
passed and five failed, with core 13/13. Pending drains and source/maps in other
bundles remain unverified. This is evidence custody, with no runtime rerun or
native acceptance. Large ZIPs remain outside git; bounded extracted originals,
complete indexes, hashes and a credential-free replay are versioned.

Additional [Linux18 original custody](c6/linux18-custody/) verifies both requested
outer/inner archives, 435 pieces and 454 descriptors. The original receipt/TRX
records Home 340/340 and OS 199/199; these results belong to `e6ee0cb3`, with no
fresh execution here. Native16 qualifications, pending drains, source/PDB receipt
limits and the absence of a proven full native cause remain explicit. Original
byte custody does not establish installed, signed or physical-device acceptance.

The newer browser pointer source `83c24b2f0033fb2828169921fc905a327120143b`
adds `position: relative` to the owned browser root. C1 independently reviewed
that complete change and the actual pinned native canvas/input contract. C6
verified the sealed `1356a134` package: all 405 listed artifact hashes, two root
documents and current/original source hashes match. The inspected owning results
record B1 14/14 and unchanged B6 pointer 5/5; the earlier c74 timeout remains
retained. The initially missing exact archive was delivered at `bc04bd3608f2fa29d5cfcb9c5ea86ac054d4bec3`.
C6 then independently executed the unedited pointer runner against those exact
180 files: 5/5 passed at 1440×1000 in Chromium 151.0.7922.173, with no runtime
errors and every published file hash unchanged afterward. Root read the runner,
command/raw report and Dashboard screenshot. [Runtime evidence](c6/browser83-runtime/review.json)
retains logs, screenshots and trace; [initial review](browser-pointer-review/manifest.json)
preserves the historical delivery blocker. This is anonymous local startup/pointer
coverage. Shared Dashboard overlap and missing painted-control accessibility
semantics remain failed, alongside services, cross-client and full browser gates.
This browser source is not incorporated into the release branch.

The requested [original custody followup](c6/custody-followup/README.md)
verifies Graph02, Forms12, Home14 part05 and Canvas11 part00. Root repeated all
four outer/member CRC/SHA checks with zero errors. Graph02's original Desktop
build fails before discovery on `Assert.Throws(Func<Task>)` (CS0619/xUnit2014).
Forms12 records eight managed TRX results but lacks the native runtime failure
receipt, so that native cause remains unresolved. Home14 retains a missing settled
callback marker despite drained records. Canvas parts00/01 reconstruct all 295
indexed files and preserve all five failures. These are historical byte custody
and causal observations; no payload was executed or native acceptance inferred.

## Specification and remaining gates

The canonical document is `1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg`.
Existing maps and historical validation retain revision
`AHj4eMQyVp2mJyiR-7I0WS7iJcwSYMmwoEkTb7sm6ijqRk99EaQHJbJWrLy37ObunOyOJESw8FHvgyJNClwj6QdcNpG8weoklA97x6a9tG0`
and its 8,344 nonblank source clauses. A newly captured atomic native revision
`AHj4eMQdy-bFUoMrCqGybqoEO_G5Z1JnTxVEl03pJVillty8itZgguU-bcLZz2GHEt6DzYfY-fMNSVeS8-eyKzYBo_BoN9gK2NvcK9zBIlI`
contains 8,358 paragraphs. Its exact nonblank comparison changes two final-pass
regions: Spaces/Dev quality-of-life notes and pass headings replace the former
Android/Study future-pass notes. C4 verifies all 63 C4 quote bodies unchanged;
pass timing, duplicate Pass4, platform/API/transport and acceptance interpretation
remain unresolved. The current text specifies a sidebar default with a Settings
choice. The interim export captured a partially edited sentence and remains
historical. Full native text, hashes and the independent review are retained in
[specification-update/manifest.json](specification-update/manifest.json).

These clause counts are not requirement counts. The prior hosted scoped maps
contain 228 semantic records and zero VERIFIED results; they overlap inherited
criteria and do not form a global denominator. Their original source line refs
are preserved. Fresh whole-map reconciliation and full mandatory/default-SHOULD/
donor/platform decomposition remain incomplete. Every deployed SH-12 journey
remains NOT-RUN. Historical runtime results are not relabelled to the new revision.

Windows, 9to1-OS, browser and other canonically declared product platforms still
need their exact owning installed/runtime/provider evidence. Linux local checks
in this handoff do not certify those platforms. Canonical exception bases are
recorded in [c6/platform-and-exception-bases.jsonl](c6/platform-and-exception-bases.jsonl);
no broad exception or mobile fallback is inferred.

Release remains blocked by current-source review/integration and browser runtime acceptance,
cross-process identity contracts, missing authenticated provider/staging services,
SH-13 commercial/architecture/isolation/capacity/recovery/budget decisions, full
product/donor/accessibility/security/recovery/platform gates and verified local-PC
delivery. The connected Sandbox capability/Operating Contract was unavailable
after discovery; no such execution capability is claimed.

## Cloudflare setup

The official requested guide and official Cloudflare skill sources were fetched.
Global CLI registration/install encountered protected managed-environment paths.
A private account plugin preserves the official 16 skills and registers the guide's
five MCP endpoints. Its saved files were read back from the backend; the host
reports it installed. This session exposes no Cloudflare MCP tools, so OAuth,
account access and deployed-provider acceptance remain unverified.

The user's subsequently selected `cloudflare@openai-curated-remote` reference is
distinct from the saved private plugin ID; equivalence was not established. After
that selection, an earlier discovery exposed neither Cloudflare tools nor skills.
The latest discovery now exposes all 16 official Cloudflare skills, while callable
account tools remain absent. Current managed-environment revision 11 is observed
and enforced, with no provider identity, credentials or runtime variables. Skill
loading does not prove OAuth or account access. Account tools and connection must
become available before account verification can run. The official guide requires
an agent restart to load newly configured MCP servers. The saved private package
is [Cloudflare](https://chatgpt.com/plugins/plugins_6ac139c43c988191aef5c01e99c9f7d4);
assembly provenance and status are in [cloudflare-setup.json](cloudflare-setup.json).

## Recovery and delivery

Main remains `98a08827c9fbc486fe987da73f4aee8398120d31`. There has been no production
deployment, commercial activation, DNS cutover, WordPress retirement or main
merge. The source history, proposal branches and evidence permit review and
recovery. Revert an adopted bounded change through normal reviewed history;
do not force-push shared branches. Future deployment requires retained immutable
artifacts/configuration, current provider verification and an authorized tested
rollback. A source PR/cloud-workspace file does not prove delivery to Jacob's PC.
