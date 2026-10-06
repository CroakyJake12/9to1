using Haven.Core;

namespace Haven.Application;

/// <summary>Display-only facts on a private Files/saved-workspace issuance. Neither these
/// values nor an arbitrary implementation authorize reading or executing a project.</summary>
public interface IDeveloperWorkspaceOriginalExecutionBinding
{
    Guid WorkspaceId { get; }
    Guid ProjectId { get; }
    Guid RootId { get; }
    long WorkspaceRevision { get; }
    string CanonicalRoot { get; }
    AuthenticatedResourceActor OriginalActor { get; }
}

/// <summary>The configured actual Files/saved-workspace/kernel source owns the binding.
/// Its fresh validation occurs outside any held Home/native/policy gate.</summary>
public interface IDeveloperWorkspaceOriginalExecutionBindingSource
{
    bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding sameBinding);
    Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        AuthenticatedResourceActor sameActor, CancellationToken cancellationToken);
    IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding sameBinding);
    void DemandExternalOriginalExecutionBindingJoin();
}

/// <summary>Opaque explicit Home consent for one genuine preparation and saved-root binding.
/// Source recognition, Task/model/tool/action permission and the held entry remain mandatory.</summary>
public interface IWorkspaceOriginalProcessStartConsent : IAsyncDisposable { }

public interface IWorkspaceOriginalProcessStartConsentSource
{
    Task<IWorkspaceOriginalProcessStartConsent> AcquireOriginalAsync(
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IWorkspaceToolActionPreparation samePreparation, TaskExecutionSnapshot current,
        Action<Action> originalCallerCallback, CancellationToken cancellationToken);
    bool IsIssuedOriginalConsent(IWorkspaceOriginalProcessStartConsent sameConsent,
        ITaskRunToolActionPreparation samePreparation);
    Task ValidateOriginalConsentAsync(IWorkspaceOriginalProcessStartConsent sameConsent,
        ITaskRunToolActionPreparation samePreparation, CancellationToken cancellationToken);
    Task<IWorkspaceOriginalProcessStartEntry> EnterOriginalProcessStartAsync(
        IWorkspaceOriginalProcessStartConsent sameConsent, ITaskRunToolActionPreparation samePreparation,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalEntry(IWorkspaceOriginalProcessStartConsent sameConsent,
        ITaskRunToolActionPreparation samePreparation, IWorkspaceOriginalProcessStartEntry sameEntry);
    void DemandExternalOriginalProcessStartConsentJoin();
}

/// <summary>Only the actual configured Workspace preparation issuer may bind a genuine
/// consent. A Composite delegates using its private issuing-preparation registry.</summary>
public interface IWorkspaceOriginalProcessStartConsentBindingOwner
{
    void BindOriginalProcessStartConsent(ITaskRunToolActionPreparation samePreparation,
        IWorkspaceOriginalProcessStartConsent sameConsent);
}

/// <summary>Separate completion then Home held entry, used only for the finite native Start.
/// Pure Demand/Run perform no profile/store/Files/resource acquisition or policy I/O.
/// Actual fresh Home checks happened under its retained lease before publication.</summary>
public interface IWorkspaceOriginalProcessStartEntry : IAsyncDisposable
{
    void DemandExternalOriginalProcessStartEntryJoin();
    void DemandOriginalProcessStart(string canonicalRoot, string canonicalTarget, string originalRequestSha256);
    T RunOriginalProcessStart<T>(string canonicalRoot, string canonicalTarget,
        string originalRequestSha256, Func<T> originalNativeStart);
}

/// <summary>Actual physical source confirms its existing Start finally consumes the exact
/// entry-release fence. Missing support refuses consent binding before canonical action CAS;
/// an unmodified physical executor must never hold Home through its process wait.</summary>
public interface IWorkspaceOriginalProcessStartEntryReleaseSource
{
    void DemandOriginalProcessStartEntryReleaseSupport(IWorkspaceToolFinalFence sameFence);
}
