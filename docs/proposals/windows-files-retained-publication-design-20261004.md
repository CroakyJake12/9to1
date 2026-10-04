# Windows Files publication: retained evidence and store transactions

This is an isolated design checkpoint. It adds no runtime implementation, registration, trust policy, installer receipt format, controlled-launch grant, or production selection. The native verifier at `40e836ceddb4f06a804a265b51c4e63adaa16ffb` remains unchanged. The Files RPC source at `465f9c637f8b1e14d1d61bc0989140837d4e35bc` is a separate source proposal. Their future union requires current API and target comparison. No native invocation or tests were performed for this document.

## Concrete blocker

The current RPC acquires the installed guard before the Files owner guard. A Home/device lock retained by the first acquisition can conflict with a Files metadata writer that already holds its metadata semaphore and process lock while validating Home authority. An ordinary call to the native verifier does not solve this: it disposes its process/file evidence before returning, reads the package store and profile through ordinary locking APIs, and has only transient receipt and controlled-launch checks.

The current interfaces cannot establish a retained physical publication transaction. They must continue to return unavailable when an authentic transaction implementation is absent. A previously successful verifier result or `IsCurrentAsync` boolean is not a retained lease.

## Exact source basis

All paths below are repository-relative. The full files were read at the stated immutable refs.

| Ref | Path | Bytes | Git blob | Relevant boundary |
| --- | --- | ---: | --- | --- |
| `465f9c637f8b1e14d1d61bc0989140837d4e35bc` | `9to1 Workspace/Home/Source/Home/Core/HomeNativeFilesPublicationContracts.cs` | 1865 | `2024d737838cf8311b83348415512df897a08331` | Guard checks cannot reacquire Home, Files or Context; installed acquisition has no owner transaction context. |
| same | `9to1 Workspace/Home/Source/Home/Core/HomeNativeCoreApiSessions.Files.cs` | 10368 | `f04c6879b882d1a3548d45edfaffeb0eee9f4a6f` | Lines 91/94 acquire installed then owner; line 106 awaits the original physical frame; lines 123-130 independently check and release. |
| same | `9to1 Workspace/Home/Source/Home/Core/HomeWindowsCoreConnection.cs` | 12835 | `4557e7b035970841cdad41a37af03a82104f3bc7` | Lines 86/93 retain and await the publication task; lines 123-134 independently join original work; borrowed resources remain with their owner. |
| same | `9to1 Workspace/Files/NativeHost/FilesNativeOriginalPublicationSource.cs` | 1759 | `ba1db751ea0ff59cea53714a5988ec6086a935bf` | Trusted source must retain actual Home/configuration/receipt and provider metadata transactions. |
| same | `9to1 Workspace/Files/NativeHost/FilesNativeHomeDomainOwner.cs` | 32872 | `4a5735b091071e2de91e0ae3693894fd33ece40d` | Lines 399-436 issue the original read from the private reply map and retain the source acquisition task. |
| same | `9to1 Workspace/Files/CUI/Services/VersionedJsonStateStore.cs` | 9496 | `2025a1822fe2c6b46612f0c4a6a37a6ac9105ce2` | Line 68 acquires an existing metadata read lease; lines 115/196 invoke authority validation under the metadata writer. |
| `40e836ceddb4f06a804a265b51c4e63adaa16ffb` | `9to1 Workspace/Home/Source/Home/Core/HomeNativeWindowsProtectedInstalledPeerVerifier.cs` | 38132 | `d6ce7724fc4716f09484fde212f06a199ce042a9` | Lines 81-82 close original evidence before returning; lines 109/159 use ordinary package reads; final checks are explicitly sequential. |
| same | `9to1 Workspace/Home/Source/Home/Core/HomeNativeWindowsProtectedPeerEvidence.cs` | 24644 | `42abb0089efb97cc58e8a60ba9064a8483eaca9f` | Owns original process, birth, SID, image, protected file and ancestor handles; independent cleanup is preserved. |
| same | `9to1 Workspace/Home/Source/Home/Core/HomeNativeWindowsProtectedInstallationReceiptAuthority.cs` | 4204 | `7210d20f988d7500c44a0f685f117a2aaa47a75a` | Authenticating receipt port currently returns only a currentness boolean. |
| same | `9to1 Workspace/shared/src/Haven.Application/Authorization/HomeNativeControlledLaunch.cs` | 1553 | `d454f3e373b02ccbe35e17b7460c32409a910dab` | Genuine launch authority currently returns only a currentness boolean. |
| same | `9to1 Workspace/Home/Source/Home/Core/HomeLocalOperationLease.cs` | 6103 | `1b8d0be2a231001c97a2419de8902f6bcc054d9c` | Actual retained Home lock, raw profile check and raw state fingerprint; no ordinary Home read inside the lease. |
| same | `9to1 Workspace/Home/Source/Home/Core/FileHomeCoreStateStore.cs` | 17413 | `7ce410f3ffc5dfbe2241d4b2d7039cf34fb69437` | Canonical per-path gate and persistent process lock are shared by actual reads and writes. |
| same | `9to1 Workspace/Home/Source/Home/Core/HomeLocalProfileIdentity.cs` | 7615 | `f855d77bd9034658b9a29c5fe4d812a9ee3e3b7f` | Raw `CheckAsync(HomeCoreStoredState,...)` avoids the ordinary profile/store gate. |
| same | `9to1 Workspace/Home/Source/Home/Apps/HomePackageDatabase.cs` | 20178 | `f5ac83342975eaf15b4213008516c622c5e2f409` | Same registry `home.packages.registry`, schema 1; parsing currently follows ordinary store read. |

## Compatible ownership split

1. The private authenticated Session completes ordinary issuer/current, owner, broker/resource and provider checks before retaining publication locks. It retains the same Context, observed peer, installed tuple, actor, original connection and reply.
2. The Files owner acquires its actual metadata transaction first. Its registered coordinator then acquires actual profile/configuration/permission Home state and, when distinct, canonical device package state. A shared actual store is acquired once.
3. The Session issues a sealed publication context containing the SAME acquired owner guard and private original references. It has no public constructor and is not reconstructed from actor, request, reply, PID, or wire fields.
4. Native installed acquisition receives that exact context. It requires an actual factory-issued raw state capability bound to its configured profile, package database, device store, original Home lease and actor. A generic guard with `IsHeld == true` cannot supply this capability.
5. Native acquisition retains original process and protected file evidence, authentic receipt-owner and controlled-launch transactions. These remain owned until the same original frame task settles and independent final checks finish.

The Files RPC owns context issuance and acquisition order. The Home store owner owns the raw read transaction factory. The package owner owns reuse of the existing canonical parser. The native verifier owns physical evidence and the receipt/launch lease adaptation. No second registry, listener, epoch cache or publication-task tracker is needed.

## Raw state capability required from the actual store owner

Proposed name: `HomeNativeFilesRetainedStateRead`, a sealed Home-Core capability with no public construction and no exposed mutable raw state. The genuine store factory binds actual store and lease identities. An owner guard may expose this capability only after successful original transaction acquisition.

The native-facing operations are:

- `IsBoundTo(HomeLocalProfileIdentity originalProfiles, HomePackageDatabase originalPackages, IHomeCoreStateStore originalDeviceStore, HomeNativeSessionLease originalLease, AuthenticatedResourceActor originalActor)`.
- `ReadOriginalPackagesAsync(CancellationToken)`, returning the existing `HomePackageDatabaseReadResult` through the SAME schema, canonical record and validation parser applied to the held `HomeCoreStoredState`.
- `DemandOriginalActorAndStateAsync(CancellationToken)`, checking raw state fingerprints/revisions, actual `HomeLocalProfileIdentity.CheckAsync(rawState, actor, HomeStateCommitPhase.Publication, ct)), original lease identity/held state, and cancellation before and after awaits.

These are proposed operations, not implemented APIs. They must not call `HomeLocalProfileIdentity.GetCurrentAsync`, `HomePackageDatabase.ReadAsync`, store `ReadAsync`, broker/resource/provider resolvers or Context.Gate from inside retained locks. The actual principal source must also avoid owner reentry.

The existing profile-bound Home lease is useful implementation material, but does not cover a different device store. A genuine device read transaction and canonical parser factor are required. Separate profile and device stores are legitimate; equality must not be imposed. Different wrapper objects sharing a semaphore, sidecar, physical file or alias require genuine owner lock identity/deduplication or explicit refusal before nested acquisition.

## Retained receipt and launch ports

Additive optional extensions belong in Home.Core, so Application does not depend on Home:

```text
IHomeNativeWindowsProtectedInstallationPublicationAuthority
  : IHomeNativeWindowsProtectedInstallationReceiptAuthority
  AcquireOriginalPublicationAsync(
    HomeNativeWindowsProtectedInstallationReceiptObservation original,
    HomeNativeFilesOriginalPublicationContext originalContext,
    CancellationToken ct) -> ValueTask<IHomeNativeFilesPublicationGuard?>

IHomeNativeControlledLaunchPublicationAuthority
  : IHomeNativeControlledLaunchAuthority
  AcquireOriginalPublicationAsync(
    HomeNativeControlledLaunchObservation original,
    HomeNativeFilesOriginalPublicationContext originalContext,
    CancellationToken ct) -> ValueTask<IHomeNativeFilesPublicationGuard?>
```

These are design names only. The supplied installation owner must retain and authenticate the actual accepted receipt format, signed bytes, current protected installation generation, canonical registry/entry revisions, installed tuple and original peer. The launch owner must retain the actual controlled launch matched to PID, birth, SID, image, profile and SAME Home lease. Their checks use retained transactions and cannot reacquire Home/Files/Context or invoke callbacks that do so.

Existing transient `IsCurrentAsync` implementations are not fallbacks. Missing authentic adapters, controlled-launch ownership, configured publisher/signature trust or actual protected receipt remain typed unavailable. The proposed receipt codec is not itself a maintained canonical Windows installer writer.

## Lock order and lifecycle constraints

The proposed supported order is Files metadata, then profile/configuration/permission Home state, then distinct device package state, then authentic receipt and launch transactions. Native evidence is acquired within that coordinated ownership.

A complete participating writer lock-order and alias census has not been established. This document does not certify that every writer follows this order. A reverse-order or unsupported composition must remain unavailable until its actual owning paths are proven compatible. The coordinator cannot assume an installation owner's receipt/launch lock is safe merely because its interface exists.

Ordinary observations complete outside retained locks. Inside them, guard checks use raw locked canonical snapshots, actual native handles and retained authority transactions. Holding a process handle does not freeze process lifetime; protected leaf sharing does not replace authentic installer coordination. Sequential native checks do not establish atomic control over arbitrary privileged or external mutation.

After both guards pass, await the SAME original physical frame task with Context.Gate and all actual transactions retained. Cancellation does not release authority before that task settles. Independently run every final guard check, retaining all distinct original causes. With owner-first acquisition, dispose the installed/native guard before the owner guard. Each independently settles its original launch/receipt/physical or device/Home/Files resources in reverse acquisition order. Only then release Context.Gate and linked lifetime resources.

The existing Windows connection already retains and independently joins its original publication task. Its borrowed physical pipe, Home lease and provider stay owned by the outer host until those drains finish. A sent frame remains a known physical outcome if later checks or cleanup fail. Partial writes remain ambiguous; faults retire the original channel rather than issue another frame or infer no effect.

## Meaningful validation required before an adapter is selected

- Real Files metadata and Home state writer waiters remain blocked while the original frame task is held, then settle after its release without recursive Home read or deadlock. Cover shared and genuinely distinct stores.
- Unsupported, aliased or unproven reverse-order composition refuses before the frame starts.
- Copied/public observations cannot replace the Session-issued context or original owner transaction.
- Authentic retained receipt or launch retirement refuses publication; no cached transient success is used.
- Held frame, cancellation callback error, independent postcheck faults and independent disposal faults preserve original task custody and every distinct cause. No provider, pipe or dependent lock closes before the frame settles.
- Missing genuine installation/launch/publisher configuration remains unavailable. Scripted authorities in managed fixtures do not establish Windows installed trust or native acceptance.

No tests or native operations were run for this design checkpoint.
