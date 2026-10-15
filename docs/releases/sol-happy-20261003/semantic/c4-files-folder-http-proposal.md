# Bounded Files folder HTTP continuation proposal

This review answers Team B coordination `049876dd3257cf8cce6456383c6805f619dc3c9f` using actual committed source `eecadfc7` and retained a63 Files authority. It proposes a reviewable implementation sequence, not a deployed/authenticated-product acceptance claim. Folder HTTP transport is independent of the six unresolved Project organisation seams. No Project kind, wire, organisation store or fake Space is added.

## Existing source and immediate boundary

`9to1 Workspace/Web/SitesWebDomain.cs` contains the maintained `FilesWebDomain`, with List/Get only. `IFilesProvider` already provides List/Get/Mutate; actual `DurableDriveProvider` supports CreateFolder/Rename/Move/Delete/Restore. It commits item, operation and event together and returns canonical `FilesResult<FilesOperation>` with Committed and ResultRevisionId. It persists real state across processes. It is a local durable provider, not an R2/resumable quota-checked production service.

The current `Web/Program.cs` legacy branch resolves `accountID` from `identity.Authenticate(Token(context)).AccountID`, selects the configured provider for that account, and routes `/api/apps/{domain}/{action}`. There is no browser actor header or actor JSON authority. The existing `CakeAuthenticatedResourceActorSource` uses that actual server identity service, requires registered-client provenance, and produces a resource actor. Its principal string is `cake-account:<D-guid>` whereas the configured provider owner uses `<N-guid>`; an adapter must use the authenticated canonical AccountID mapping, never assume the principal string is the provider owner.

**Remote mode returns before Files domains are registered.** Its maintained `RemoteWebOidcHost` is a client, not a CAKE issuer. It cannot be silently connected to the legacy issuer/actor source or converted into a Home grant. .NET signing/genuine auth_revision and remote server actor resolution remain C2/A1 owner seams. Local folder/provider durability tests can run through the existing explicitly controlled legacy trusted-profile fixture and genuine PKCE/session exchange, labelled local fixture only. No remote fallback or invented auth_revision is proposed.

## Proposed batch 1: owner-acknowledged folder operations

Keep existing List/Get routes and response bodies unchanged. Add exactly CreateFolder, Rename, Move, Delete and Restore to the existing Files action catalogue. Proposed request members below are **for A4/B/C2 review**, not an adopted public wire. Reuse the existing canonical Guid-backed IDs; actor, account, role, state, result revision, timestamp and sequence are server-owned and cannot be supplied by the request. Use explicit typed request validation; no arbitrary operation string dispatch or generic payload acceptance.

| Action | Proposed request fields | Actual provider operation / result |
|---|---|---|
| List | existing optional parentID, cursor | Existing ListAsync page; provider bounded page size remains authoritative |
| Get | existing itemID | Existing GetAsync canonical metadata/result |
| CreateFolder | itemID, operationID, parentID nullable, name | CreateFolder, create precondition null; exact caller-selected item/op IDs retained |
| Rename | itemID, operationID, expectedRevisionID, name | Rename, nonempty expected revision, return canonical operation |
| Move | itemID, operationID, expectedRevisionID, destinationParentID nullable | Move; canonical physical parent mutation only |
| Delete | itemID, operationID, expectedRevisionID | Delete is recoverable Trash, no purge capability |
| Restore | itemID, operationID, expectedRevisionID | Restore uses revision returned by Delete; must not replace expected revision by null or silently fetch latest |

A/B must acknowledge field naming and current serializer shape: the domain currently calls default `SerializeToElement`, so wrappers/enums may not match a new browser expectation automatically. Preserve current List/Get representation. Return the **actual** FilesResult, not a fabricated `ok` envelope or inferred optimistic revision. Browser accepts mutation only if result is successful, returned operation is Committed, and returned ResultRevisionId and target/operation IDs match. On disconnect/unknown outcome, reconcile/replay the same request ID; never generate a second operation ID merely to retry. RevisionConflict and changed-key InvalidState are domain outcomes, not HTTP200 success.

For atomic fresh authority and original-store custody, request A4 acknowledgement of a narrow **additive guarded Mutate overload** in the actual DurableDriveProvider, taking existing FilesCommitAuthorityGuard plus expected original StoreId and invoking existing Store commit-authority checks at the actual commit boundary. Existing structural API lacks this guard even though artifact/content APIs already have it. It must preserve the existing unguarded local call sites, validate original StoreId/owner/location inside the same State mutation, and refuse session/store changes without mutating the state. It must not call network/provider/Home while holding a Files metadata lease or reenter the same store.

The HTTP adapter captures the existing server-verified account/session/client binding, selects only its configured provider, and derives actorID from that verified AccountID using the existing provider configuration convention. A current local session validator can reauthenticate the actual captured bearer through CakeIdentityService before dispatch/commit and verify the same AccountID/SessionID/client binding; C2 must acknowledge the exact callback and revocation semantics. A remote implementation must consume the maintained verified Worker boundary once supplied; it cannot use these local fixture identities as remote proof. Current per-object/shared/organisation ACL scope is not invented: batch 1 is the existing personal configured owner provider only. Unsupported organisation/shared locations return explicit unavailable/denied state until the owning ACL adapter exists.

Idempotency remains provider-owned. The current replay equality compares actor/item/operation/destination/base/NewName. Validate only the exact allowed folder fields; derive server-only fields consistently so retries reach the same provider request. A4 should confirm current-request/store authority must be checked before returning prior replay: current provider code examines prior operation before its owner check. HTTP selection/authentication must never allow a different account to exploit a previously committed key. No Project typed digest algorithm is introduced here.

## Owned files, ACKs and next concrete work

C4 proposes isolated changes to the existing FilesWebDomain body in `Web/SitesWebDomain.cs`, an additive dedicated `Web/Tests/FilesFolderHttpSpecs.cs`, and (after owner ACK) the actual Files provider guarded overload plus focused canonical tests. Shared Files sources remain A4-owned; schema/serializer/action/catalogue adoption needs A4 and B ACK, current session callback needs C2/A1 ACK. C3 confirms no planned WebProgram edit; Admin lifecycle is separately owned. `eecadfc` WebProgram/Admin body must not be replaced by the older 98a or whole-file copy. Any narrow route/composition addition must be root-serialized after C3/Team A review; remote mode remains fail closed.

The first executable fixture will extend the current actual Web test startup with two separately persisted fictional accounts/providers, explicit legacy-mode local configuration, existing registered-client PKCE exchange, bound loopback port/process custody and fresh isolated state. Reopen state with a second genuine provider instance/process and assert exact bytes/IDs/revisions/events. Run the real Web assembly and typed HTTP adapter; a direct fake domain does not satisfy HTTP evidence. This can proceed as soon as the bounded wire and guard ACKs arrive, independently of staging credentials. C2's maintained workerd/D1 fixture then gets a separate agreed actor/transport integration batch; no .NET issuer/signature/auth_revision requirement is waived.

## Acceptance and safe negative controls

1. Real HTTP List/Get existing representation remains compatible; unauthenticated request is denied and creates no metadata state.
2. Create→rename→move→delete→restore uses exact item/operation IDs, correct expected/result revisions and Committed; reopen server/provider and verify same identity, parent/name/Trash state and one event per mutation.
3. Two accounts with independent state: foreign item/key and forged actor/account JSON/header cannot read/mutate another state; byte hash of both state files unchanged on denial.
4. Same operation key/exact request replay produces same IDs/revision and no added event; changed name/target/base/destination with same key fails and leaves persisted bytes unchanged.
5. Concurrent rename/move from one base yields one commit and one RevisionConflict; both authoritative outcomes preserved.
6. Cancel before commit produces no operation/event/item mutation; lost HTTP response after commit then replay/restart recovers the one original result.
7. Revoke actual local session between resolution and metadata commit; guard must refuse and preserve item/operation/event bytes. Controlled omission of guard must fail this test.
8. Replace selected store UUID/owner/location in isolated fixture between selection and commit; original-store check refuses. Controlled omission must be detected.
9. Invalid name, nonexistent destination and cyclic folder move return actual structured provider error; originals and state unchanged.
10. Delete/Restore preserves canonical FileID and requires the Delete result revision; stale restore conflicts, no purge route is exposed.
11. Unknown/schema-incompatible existing state is rejected unchanged; failed persistence keeps prior state/recoverable outcome and no false Committed result.
12. Browser authoritative-outcome tests deny Pending/null revision/wrong-ID responses and surface cancellation/conflict without replacing local unsynced draft; Team B owns this adapter acceptance.

Negative controls alter only isolated fixture/source copies, retain failures/logs, and run the same assertions. Required local source/HTTP evidence, genuine maintained Worker auth integration, actual browser UI, real R2/provider quota/chunk bytes and final release acceptance remain distinct.

## Byte transport: separate next batch, no invented storage

Existing Files content APIs commit trusted immutable references with hash/size and optional original StoreId/FilesCommitAuthorityGuard, but `IFilesProvider` has no byte-stream upload/download contract. Client-supplied filesystem `ProviderContentReference` must never become server authority. Do not expose local paths, accept external paths, fabricate a resumable completion receipt, or bypass C3 committed-plus-reserved accounting.

Before adoption A4/C4/B must agree a registered content capability and request/result mapping to the existing `FilesUploadedContent`/revision APIs; C3 must supply authoritative quota reservation/admission integration, and C2 current read/write permission. A bounded first proposal can stream isolated bytes into the existing Files-managed immutable reference location, verify exact SHA256/length, then guarded metadata commit; failure reconciles the one operation/reservation/reference. Download resolves a current authorised canonical revision/reference, checks path custody and actual hash/length, and streams bytes without disclosing internal paths. Resumable transfer identity/chunk journal/cancel/cleanup and accounting require the owning contract rather than another private store. Folder batch is independently reviewable and must not claim this byte capability exists.
