namespace Haven.Application;

/// <summary>Source-issued observations of one actual current installed app choice.
/// Public fields/IDs cannot issue installation, controlled process or action consent.</summary>
public interface ICanonicalInstalledApplicationLaunchIntent
{
    AuthenticatedResourceActor Actor { get; }
    Guid OperationId { get; }
    string AppId { get; }
    string PackageId { get; }
    Guid InstalledApplicationId { get; }
    long InstalledApplicationRevision { get; }
    string ActivationSha256 { get; }
    string DescriptorSha256 { get; }
    string OriginalHomeLeaseIdentity { get; }
}
public interface ICanonicalInstalledApplicationLaunchAcknowledgment
{
    ICanonicalInstalledApplicationLaunchIntent OriginalIntent { get; }
    bool Applied { get; }
    int ProcessId { get; }
    string ProcessStartIdentity { get; }
    string ExecutableIdentity { get; }
    string Reason { get; }
}
public interface ICanonicalInstalledApplicationLaunchProducer
{
    bool IsIssuedOriginalLaunchIntent(ICanonicalInstalledApplicationLaunchIntent sameIntent);
    string GetOriginalLaunchIntentDigest(ICanonicalInstalledApplicationLaunchIntent sameIntent);
    Task ValidateOriginalLaunchIntentWithinSourceAsync(ICanonicalInstalledApplicationLaunchIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalPinnedLaunchIntent(ICanonicalInstalledApplicationLaunchIntent sameIntent);
    Task<ICanonicalInstalledApplicationLaunchAcknowledgment> InvokeOriginalLaunchWithinSourceAsync(
        ICanonicalInstalledApplicationLaunchIntent sameIntent, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsOriginalLaunchTask(ICanonicalInstalledApplicationLaunchIntent sameIntent, Task<ICanonicalInstalledApplicationLaunchAcknowledgment> sameAtomic);
    bool IsOwnedOriginalLaunchAcknowledgment(ICanonicalInstalledApplicationLaunchIntent sameIntent,
        ICanonicalInstalledApplicationLaunchAcknowledgment sameAcknowledgment, Task<ICanonicalInstalledApplicationLaunchAcknowledgment> sameAtomic);
}
public interface ICanonicalInstalledApplicationLaunchHomeClaim : IAsyncDisposable
{
    ICanonicalInstalledApplicationLaunchIntent OriginalIntent { get; }
    string OriginalApprovalRequestId { get; }
    Task<ICanonicalInstalledApplicationLaunchHomeClaim> OriginalAcquisition { get; }
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
public interface ICanonicalInstalledApplicationLaunchHomeSource
{
    Task<ICanonicalInstalledApplicationLaunchHomeClaim> AcquireOriginalLaunchWithinSourceAsync(
        ICanonicalInstalledApplicationLaunchIntent intent, Action<Action> scope, Action<Task> retain,
        Action<ICanonicalInstalledApplicationLaunchHomeClaim> capture, CancellationToken token);
    bool IsIssuedOriginalLaunchClaim(ICanonicalInstalledApplicationLaunchHomeClaim claim, ICanonicalInstalledApplicationLaunchIntent intent);
    Task AcquireOriginalLaunchEntryWithinSourceAsync(ICanonicalInstalledApplicationLaunchHomeClaim claim,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalLaunch(ICanonicalInstalledApplicationLaunchHomeClaim claim, ICanonicalInstalledApplicationLaunchIntent intent);
    void RetainOriginalLaunch(ICanonicalInstalledApplicationLaunchHomeClaim claim, Task<ICanonicalInstalledApplicationLaunchAcknowledgment> sameAtomic);
    Task CompleteOriginalLaunchWithinSourceAsync(ICanonicalInstalledApplicationLaunchHomeClaim claim,
        Task<ICanonicalInstalledApplicationLaunchAcknowledgment> sameAtomic, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsAcknowledgedOriginalLaunchRefusal(Task sameActualSource) => false;
    bool IsIssuedOriginalSettlementReleasePhase(ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalInstalledApplicationLaunchIntent,
        ICanonicalInstalledApplicationLaunchAcknowledgment> samePhase) => false;
}
