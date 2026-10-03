# C3 semantic billing and commercial map

`c3-billing-commercial.jsonl` decomposes SH-09 into observable state/authority
invariants and SH-13 into affected commercial/service launch decisions. A record
is a semantic obligation, not a source line, test count or global coverage unit.
This scoped map does not establish full-spec coverage. SH-13 customer-site
isolation is owned/mapped by C4; browser capability and service-wide targets need
C1/Team B/coordinator reconciliation. This map includes their dependency where
billing launch is affected and does not waive any platform or full-release gate.

Source: fresh canonical snapshot `spec-fresh.txt`, Google Doc
`1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg`, revision
`AHj4eMQyVp2mJyiR-7I0WS7iJcwSYMmwoEkTb7sm6ijqRk99EaQHJbJWrLy37ObunOyOJESw8FHvgyJNClwj6QdcNpG8weoklA97x6a9tG0`,
read 2026-10-03T17:30:25.713Z. Each record contains exact source ranges/quotes,
snapshot hash, invariant, owning authority, dependencies and required acceptance
procedure/expected result. Accounts 1256–1368 and SH-12 are supporting contracts.
Their broader subscription/API/Admin criteria still require owning-team maps;
this SH-09/SH-13 cohort is not a replacement for those criteria.

## Evidence boundaries

- Accepted `origin/main`: `98a08827c9fbc486fe987da73f4aee8398120d31`.
  Inspected baseline has no Accounts domain or Stripe adapter. `state` describes
  this accepted baseline: MISSING functionality or BLOCKED unresolved decisions.
- Unaccepted Astra draft: `a63d77fe5a9dfea56c938eaa85678a9e170368ec`.
  Existing Accounts domain owns subscription state, personal ledger, quotes,
  purchases, organisation billing, Dust pools, rollover and policy ports. Source
  evidence is IMPLEMENTED-UNVERIFIED, not acceptance or permission to import.
  Records reference partial supporting files; this does not assert that each
  whole obligation is implemented. No draft tests were executed by this map pass.
- Provisional C3 adapter: `cb4e1149f58f899c7f16c7f819a095cdb71af802`.
  Local SQLite/Web Crypto receipt/claim tests passed 12/12 at that commit; relevant
  IDs are linked as partial evidence only. Receipt durability, signature and
  lease ownership are not proof of billing business effects or deployed D1.
  No provider/deployed/full acceptance has passed. Every prescribed real
  acceptance procedure in this map remains NOT-RUN.

The receipt adapter adds no subscription model or balances. Accounts remains
canonical. Its draft .NET JSON/mutex persistence is single-host; Cloudflare needs
an owner-approved hosting/transaction bridge, not a replacement ledger. Current
`ProcessProviderEventAsync` APIs require raw signed provider envelopes; later
provider GET responses cannot recreate them. The minimal metadata-only inbox
needs an owner-approved trusted-current-object reconciliation API, correct
merchant/customer/subscription/price/invoice bindings, service authentication,
revision checks and atomic business-effect idempotency before it can activate or
fund anything.

SH-13 unresolved allocations, prices, periods, rollover, grace/retention and
currency/entitlement policy remain launch blockers. Draft policy ports and
fixtures are not approved commercial policies. No Stripe credentials, calls,
charges, deployments, settings changes or domain-code changes were made by this
analysis pass. Team C coordinator alone integrates this evidence.

## Inspect the map

```sh
python3 -c 'import json; from pathlib import Path; rows=[json.loads(x) for x in Path("docs/releases/sol-happy-20261003/semantic/c3-billing-commercial.jsonl").read_text().splitlines()]; assert len({r["id"] for r in rows})==len(rows); assert all(r["requiredAcceptance"]["outcome"]=="NOT-RUN" for r in rows); print(len(rows), "scoped semantic records; no global completion percentage")'
```

This integrity check verifies record syntax/uniqueness, not behavior. Integration
must retain accepted-head versus draft/provisional evidence separately and rerun
provider/native/web acceptance against its exact final candidate. A single
record may need several platform/provider cases; its test procedure is an
execution contract, not an already discovered automated test.
