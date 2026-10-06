namespace Haven.Application;

/// <summary>Home-owned recovery of an already known original remote create. This never sends
/// another create, and never infers ownership from a matching title/list/namespace argument.</summary>
public interface ICloudflareKnownCreateReconciliationSource
{
    Task<ICloudflareOriginalKnownCreateReview> PrepareKnownCreateReconciliationAsync(
        CloudflareCompiledInvocation sameIssuedOriginal, CancellationToken cancellationToken);
}
/// <summary>Request-only seal and SAME original external close; neither is permission or effect settlement.</summary>
public interface ICloudflareKnownCreateRecoveryRetirementSource
{
    void RequestOriginalRecoveryRetirement();
    void DemandExternalOriginalRecoveryJoin();
    Task CloseAndDrainOriginalRecoveriesAsync();
}
public interface ICloudflareOriginalKnownCreateReview : IAsyncDisposable
{
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
    string RequestId { get; }
    Task<CloudflareNamespaceRecoveryObservation> SubmitOriginalAsync(CancellationToken cancellationToken);
    Task<CloudflareNamespaceRecoveryObservation> CommitOriginalAsync(CancellationToken cancellationToken);
}
/// <summary>Actual Home ownership/audit observation only. This accepts no canonical action,
/// grants no future provider/tool permission and does not clear its retained recovery causes.</summary>
public sealed record CloudflareNamespaceRecoveryObservation(string OriginalOperationKey,
    string NamespaceId, long NamespaceRevision, string ReviewRequestId, string Code);
