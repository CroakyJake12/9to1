using Haven.Core;

namespace Haven.Application;

// Literal ACKed by Data/Root. Missing actual context/custody producers keep renewal unavailable.
// These declarations and an identity lease alone never replace a canonical owner binding.

/// <summary>Renews ownership of the same suspended Task/Run; it never resumes work or grants route/tool/domain access.</summary>
public interface ITaskRunOwnerReauthenticationAuthority
{
    ValueTask<ITaskRunOwnerReauthenticationAdmission> PrepareOriginalAsync(
        TaskExecutionSnapshot currentSuspended,
        ITaskRunReauthenticationQuiescence sameOriginalQuiescence,
        CancellationToken cancellationToken);

    ValueTask ActivateAcknowledgedOriginalAsync(
        ITaskRunOwnerReauthenticationAdmission sameOriginalAdmission,
        ITaskRunReauthenticationAcknowledgment sameOriginalAcknowledgment,
        CancellationToken cancellationToken);
}

/// <summary>Issuer-private scope. Bindings are detached observations; copied objects and receipt text convey no authority.</summary>
public interface ITaskRunOwnerReauthenticationAdmission : IAsyncDisposable
{
    TaskExecutionOwnerBinding PreviousOwner { get; }
    TaskExecutionOwnerBinding NextOwner { get; }
    long ExpectedPersistenceRevision { get; }

    ValueTask RevalidateOriginalAsync(
        TaskExecutionSnapshot currentSuspended, CancellationToken cancellationToken);

    // Pure retirement/activation lifetime exclusion. All actor/context/policy/repository reads
    // must finish beforehand. Only the owner's expected-revision binding CAS runs under it.
    ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken cancellationToken);
}

/// <summary>Configured actual signed-session/account/profile owner, never a caller-provided actor DTO.</summary>
public interface ITaskRunVerifiedReauthenticationSource
{
    ValueTask<ITaskRunVerifiedReauthenticationLease> AcquireOriginalAsync(
        TaskExecutionOwnerBinding previousOwner, CancellationToken cancellationToken);

    // The source validates its private registry/reference and SAME live verified generation.
    // Having an implementation of the public lease interface is insufficient.
    ValueTask ValidateOriginalAsync(
        ITaskRunVerifiedReauthenticationLease sameOriginal,
        TaskExecutionOwnerBinding previousOwner, CancellationToken cancellationToken);
}

public interface ITaskRunVerifiedReauthenticationLease : IAsyncDisposable
{
    AuthenticatedResourceActor CurrentActor { get; }
}

/// <summary>Actual context owner authorizes current Task access for the freshly verified identity. It issues no execution grant.</summary>
public interface ITaskRunContextReauthenticationSource
{
    ValueTask<ITaskRunContextReauthenticationLease> AcquireOriginalAsync(
        TaskExecutionSnapshot currentSuspended,
        ITaskRunVerifiedReauthenticationLease sameVerifiedIdentity,
        CancellationToken cancellationToken);

    // Must recheck actual context ownership/current applicable policy, not stored Task scope text.
    ValueTask ValidateOriginalAsync(
        ITaskRunContextReauthenticationLease sameOriginal,
        TaskExecutionSnapshot currentSuspended,
        ITaskRunVerifiedReauthenticationLease sameVerifiedIdentity,
        CancellationToken cancellationToken);
}

public interface ITaskRunContextReauthenticationLease : IAsyncDisposable { }

/// <summary>Independent Data-owned original ledger shared by coordinator and permission issuer; no constructor cycle through coordinator.</summary>
public interface ITaskRunReauthenticationCustodySource
{
    ValueTask<ITaskRunReauthenticationQuiescence> AcquireOriginalAsync(
        TaskExecutionSnapshot currentSuspended, CancellationToken cancellationToken);

    ValueTask ValidateOriginalAsync(
        ITaskRunReauthenticationQuiescence sameOriginal,
        TaskExecutionSnapshot currentSuspended, CancellationToken cancellationToken);

    // Registry validation binds SAME admission, genuine expected-revision CAS, exact old/new
    // binding, acknowledged revision and preserved run state. Snapshot equality is insufficient.
    ValueTask ValidateAcknowledgmentAsync(
        ITaskRunOwnerReauthenticationAdmission sameOriginalAdmission,
        ITaskRunReauthenticationAcknowledgment sameOriginalAcknowledgment,
        CancellationToken cancellationToken);
}

/// <summary>Source-private whole Chat + attempt/frame/lease no-overlap custody. Not proof of success, no-effect or safe replay.</summary>
public interface ITaskRunReauthenticationQuiescence : IAsyncDisposable
{
    // This also excludes fresh producer/admission entry until binding activation or retained
    // failure reconciliation. It never waits for Chat or invokes policy under a Task CAS lock.
    ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken cancellationToken);
}

/// <summary>Opaque actual Data owner acknowledgment; the configured custody source alone validates issuing identity.</summary>
public interface ITaskRunReauthenticationAcknowledgment { }
