# UCML v1 local context backend

UCML means **User Context Markdown Language**. This implementation follows the owner-approved SPUI-194/195 correction in the [Universal Spaces specification](https://docs.google.com/document/d/12AbDnWynZjVbKZ5XhkkiGMmJkINOSqMfnPuHuI6XDBE/edit). The specification intentionally leaves the detailed schema open; the schema here is an initial version, not a pre-existing standard.

## Format

A `.ucml` file is UTF-8 text: `---ucml`, one strict JSON metadata object, `---`, and Markdown. Delimiters occupy their own lines. `UcmlCodec.Parse`, `Serialize`, `ParseMetadata` and `SerializeMetadata` are dependency-free .NET 10 APIs in `NineToOne.Dulche.Context`.

Metadata carries schema version, stable document ID, revision, title, portable owner identity, scope, category, classification, timestamps, expiry, retention, declared consent restrictions, source references, confidence and optional superseded identity. Markdown is inert data. Unknown fields, duplicate properties, unsupported versions, missing required fields, unbounded content, wildcard restrictions and malformed metadata fail closed. The document limit is 512 KiB UTF-8, metadata 64 KiB, Markdown 256 Ki UTF-16 code units. Markdown whitespace and Unicode roundtrip without normalization.

Example metadata (the CLI `pack` command writes delimiters):

```json
{
  "schemaVersion": 1,
  "id": "fb878a2f-7d5a-4cce-b3c6-72cde67bef49",
  "revision": 1,
  "title": "Prefer concrete examples",
  "ownerId": "portable-user-label",
  "scope": { "kind": "personal", "id": "portable-user-label" },
  "category": "communication",
  "classification": "private",
  "createdAt": "2026-10-16T00:00:00Z",
  "updatedAt": "2026-10-16T00:00:00Z",
  "expiresAt": null,
  "retention": "until-revoked",
  "consent": {
    "status": "explicit",
    "apps": ["9to1.assistants"],
    "purposes": ["personalization"],
    "recipients": ["local-model"]
  },
  "sources": [{
    "id": "source-001",
    "title": "User instruction",
    "kind": "conversation",
    "uri": "urn:local:source-001",
    "observedAt": "2026-10-16T00:00:00Z"
  }],
  "confidence": 1,
  "supersedesId": null
}
```

An owner label or declared consent in a file **does not authenticate the caller or grant access**. Provenance remains imported evidence; import approval does not prove every statement true.


## Canonical memory integration

`AssistantMemoryManagementController` provides:

- `PrepareUcmlImportAsync(view, text, operationId)`: validate and freeze a
  reviewable candidate without persistence.
- `CommitAsync(preview)`: obtain the actual Home write decision, then commit
  through the existing canonical Memory/Knowledge owner. Decline writes nothing.
- `ExportUcmlAsync(view, selectedRecord)`: reread current authorized canonical
  content, record the owner-directed export in Home and serialize the preserved
  metadata and Markdown.
- `RetrieveUcmlAsync(binding, request)`: retrieve bounded relevant records for
  exact categories and the supported local Assistant purpose/recipient.

The methods use the original Home, Den, Memory and Knowledge owners. There is
no second context database. In memory, a hydrated record carries metadata with
the reserved `ucml.v1:` marker and its Markdown body. Before SQL persistence,
`CanonicalUcmlRecordProtection` seals the complete record; durable rows use
`ucml.v1:sealed:`, placeholder title/topic and an empty body. Original confidence,
sources and metadata are recovered only within the authorized scoped reader.

Imported records have `KnowledgeOrigin.Imported`; their canonical IDs equal the
UCML IDs. A collision cannot overwrite a record. Reusing the original preview
and operation uses the existing write idempotency owner; a new attempt with an
existing identity cannot turn the create-only transaction into an upsert.

Initial import admits bodies of at most 4,000 UTF-16 code units, a trimmed
title, private classification, revision one with no imported supersedes ID, and
either personal scope whose ID equals its portable owner label or the exact
selected Assistant scope. It requires explicit restrictions to
`9to1.assistants` / `personalization` / `local-model`, `until-revoked` retention
and no document expiry. Unsupported policies are rejected rather than discarded.
The authenticated destination owner comes from Home, never the portable label.

Large profiles must be split into individually sourced records. Use canonical
correction/rejection for existing records: a correction creates a new encrypted
record, advances revision and supersedes identity, preserves original history
and requires Home approval. Rejection stops recall and preserves history; it is
not secure erasure. Historical status or qualitative confidence not represented
by a native field belongs in explicit source annotations/sidecars, not a
fabricated new grant or self-referential supersedes edge.

Legacy prompt-memory readers, the generic prompt formatter and generic
correction/upsert/pin/reject/forget paths exclude UCML. Removing a marker from an
incoming generic update cannot replace an existing UCML record. Its generic
retrieval index contains identity needed by the canonical owner but no UCML
title, body, source text or searchable chunks.

## Current permission and retrieval

The broker authenticates through the issued canonical Assistant binding, current
Home actor, verified store ownership, current memory configuration and live Den
membership. Caller strings can narrow authority, not create it. The supported
request is `9to1.assistants` / `personalization` / `local-model`. Temporary or
archived conversations, unavailable ownership and unsupported remote, plugin,
team, organisation, project or Space routes are refused.

`AssistantMemoryPreferences.UcmlCategories` is the Allow list;
`UcmlAskCategories` is the Ask list. The lists contain exact categories, are
disjoint, and jointly contain at most 64 entries. Empty lists deny disclosure.
`UcmlAccessExpiresAt` expires the policy. Native Assistant controls and the
Linux console expose these settings; importing a record does not add a grant.

For an Ask selection, `HomeUcmlContextApprovalOwner` submits an actual Home
review bound to the original actor, Assistant, conversation, category policy and
record IDs. Acceptance applies only to that source-issued selection. Its
deadline is at most 60 seconds and no later than policy expiry. Decline or
expiry provides no selected UCML context; ordinary chat may continue without
it. Later validation cannot reuse that approval for another request or source.
Home caller revocation and the current category policy remain authoritative.

Retrieval uses deterministic lexical ranking, not embeddings. It scans at most
eight pages of 64 scoped records and returns at most eight records. The management
API defaults to five records and 8,000 serialized characters and admits a requested
budget from 256 to 16,000 characters; `ScanLimitReached` reports an incomplete
scan. Ordinary Assistant Chat uses at most eight records and 12,000 characters,
including serialized provenance and delimiters. Chat ranking uses whole words
with common query words removed and weights title matches. It prioritizes up to
three currently permitted identity records, then fills from query-ranked
candidates, which can include additional permitted identity records. Three is
a priority count, not a disclosure limit; the total eight-record/12,000-character
budget and the same Home review and dispatch checks apply to every record.
Selected versions/status, membership and permissions are rechecked after
selection and asynchronous audit.

UCML text is untrusted evidence. It cannot change a recipient, call a tool or
authorize a write. Source dates, uncertainty and corrections travel with the
selected context. This is an in-process trusted-host API, not a network endpoint
or a promise that a model will never follow malicious text.

## Actual local-model dispatch

The ordinary Assistant Chat integration passes the selection from
`AssistantOriginalMemorySource` through `ChatSessionService`, the provider router
and the actual provider. It requires persistent-memory read permission and a
local-only routing constraint. The separate canonical Task capture/replay path
does not receive this ordinary-chat context.

An opaque live dispatch guard revalidates the same source-issued selection;
the serialized required-marker cannot substitute for a guard lost in
serialization. Routing and fallback require a maintained
`IOriginalPrivateContextModelProvider`. A plain `IsLocal` flag, configurable
localhost URL or unverified Ollama route is insufficient.

The maintained llama.cpp provider uses a pinned Unix socket and verifies its
peer/process, executable, GGUF identity and model capabilities. It performs a
final permission/source check after asynchronous endpoint verification and
before submitting the actual HTTP request over that socket. Revocation, changed
records, missing guards and remote/unverified fallback fail closed. This cannot
revoke bytes already delivered or prevent a model from quoting approved context
in ordinary conversation output.

## Audit and encryption

`UcmlAccessAudit` is the controller's recent 256-entry view. Durable access
observations also use the existing Home permission audit:
`BrokerAllowed`, `DispatchValidated`, `Exported` and `Denied`. Observations
contain request identity, timestamp, outcome and bounded record IDs; queries,
titles, bodies, provenance and keys are excluded. These are durable guarded
Home records, not a claim of cryptographically signed external audit logs.
Write approval and protected receipts remain with their existing owners.

Canonical UCML titles, bodies, confidence, source details and metadata are
encrypted before SQLite persistence. UCML revisions/receipts receive the same
protection, and indexes contain no UCML text. Opaque routing/lifecycle columns
remain outside the envelope and are checked against it. The actual Home profile
determines encryption ownership independently of a portable file's owner label.

`UcmlProtection` also provides sealed interchange envelopes, while
`ProviderSecretUcmlKeyCustody` keeps retained roots behind the platform
credential owner. Plain `.ucml`, Markdown/JSON exports and migration ZIPs remain
plaintext. This is UCML field encryption, not SQLCipher, whole-Den encryption or
erasure of historical plaintext/WAL/backups. See [ENCRYPTION.md](ENCRYPTION.md)
for key retention, recovery and threat-model limits.

## Scope and validation

The implementation covers local personal/Assistant context. Authenticated remote
transport, all-app/team/Space context owners, cross-device reconciliation and key
recovery, and the online Files service are not provided by this route. Full
SPUI-199.I acceptance across those surfaces is not claimed.

Canonical tests exercise actual Home approval, Den identity, protected SQLite,
restart, exact export, preserved provenance/confidence, denial, scoped retrieval,
revision history, encrypted storage, policy races and provider dispatch. Native
UI/console tests exercise the public import/export and permission flows. Codec
and crypto tests cover strict parsing, owner binding, tampering, retained keys
and missing-key denial. A mock-provider test is not real-model inference proof.

From the repository root on the ARM64 Linux development host:

```sh
dotnet test '9to1 Workspace/Assistants/Memory/Tests/HavenOS.Assistants.Memory.Tests.csproj' \
  -p:Platform=AnyCPU -p:PlatformTarget=AnyCPU -m:1 -nr:false
dotnet test '9to1 Models/Dulche Alpha/Context.Tests/NineToOne.Dulche.Context.Tests.csproj' \
  -p:Platform=AnyCPU -p:PlatformTarget=AnyCPU -m:1 -nr:false
```

The platform override exercises the local .NET/SQLite route; it does not verify
Windows speech/native deployment. Execute shared-graph builds serially to avoid
overwriting dependency outputs during another test run.
