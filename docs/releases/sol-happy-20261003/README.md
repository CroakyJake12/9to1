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
Team B's browser bootstrap/account adapters are held on three independently
reproduced review defects: registered Home routes bypass authenticated handlers,
profile input remains mutable across token acquisition, and updates accept an
unchanged response revision. Exact requests, probes and reviews are retained in
[final/](final/) and published through the shared transport.

Proposed Home session identity binding `297f361` and shared provider streaming
repair `4f604027` remain on separate remotely published branches pending Team A
acknowledgement. Their bounded evidence is retained; they are not integrated here.

The strict .NET consumer and hosted issuer still have five concrete compatibility
gaps described in [CONTRACT_COMPATIBILITY.md](../../../cloud/cake-id-auth/CONTRACT_COMPATIBILITY.md).
No alternate identity service, fabricated authentication revision, weakened
signature check or guessed organisation/permission/billing contract was introduced.

## Specification and remaining gates

The canonical document is `1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg`, revision
`AHj4eMQyVp2mJyiR-7I0WS7iJcwSYMmwoEkTb7sm6ijqRk99EaQHJbJWrLy37ObunOyOJESw8FHvgyJNClwj6QdcNpG8weoklA97x6a9tG0`,
last rechecked unchanged after local validation. The change from the earlier
snapshot adds Study App text at source line 10002; it remains unresolved.

The lossless inventory retains 8,344 nonblank source clauses. That is not a
requirement count. The hosted scoped maps contain 228 semantic records and zero
VERIFIED results; they overlap inherited criteria and do not form a global
denominator. Full mandatory/default-SHOULD/donor/platform decomposition and A/B/C
reconciliation remain incomplete. Every deployed SH-12 journey remains NOT-RUN.

Windows, 9to1-OS, browser and other canonically declared product platforms still
need their exact owning installed/runtime/provider evidence. Linux local checks
in this handoff do not certify those platforms. Canonical exception bases are
recorded in [c6/platform-and-exception-bases.jsonl](c6/platform-and-exception-bases.jsonl);
no broad exception or mobile fallback is inferred.

Release remains blocked by current-source review/integration, the browser defects,
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

Open [Cloudflare](https://chatgpt.com/plugins/plugins_6ac139c43c988191aef5c01e99c9f7d4)
in the host to finish connection and refresh the session's tools. No credentials
belong in chat. Assembly provenance and status are in [cloudflare-setup.json](cloudflare-setup.json).

## Recovery and delivery

Main remains `98a08827c9fbc486fe987da73f4aee8398120d31`. There has been no production
deployment, commercial activation, DNS cutover, WordPress retirement or main
merge. The source history, proposal branches and evidence permit review and
recovery. Revert an adopted bounded change through normal reviewed history;
do not force-push shared branches. Future deployment requires retained immutable
artifacts/configuration, current provider verification and an authorized tested
rollback. A source PR/cloud-workspace file does not prove delivery to Jacob's PC.
