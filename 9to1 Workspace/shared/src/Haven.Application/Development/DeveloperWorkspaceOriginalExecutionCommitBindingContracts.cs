namespace Haven.Application;

/// <summary>Optional commit support on the SAME configured saved-workspace/Files binding
/// issuer. A public binding, path, revision, setup ACK or successful read cannot issue a pin.
/// Acquire must retain the actual saved document, registered binding and working-root native
/// identities, with every original acquisition/close Task published before source callbacks.
/// Actual actor/configuration checks occur before Home is held; no logical Files/store lease
/// may be retained across Home entry acquisition.</summary>
public interface IDeveloperWorkspaceOriginalExecutionCommitBindingSource
{
    Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinAsync(
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, CancellationToken cancellationToken);
    bool IsIssuedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin);
}

/// <summary>Private source-issued native descriptor custody for one exact saved-root binding.
/// Demand runs at actual Process.Start and rechecks all consumed inode/version/root facts.
/// It performs no Home/profile/Files/store reacquisition or business callback. Disposal joins
/// the actual coalesced close and every independently retained raw descriptor/resource cause;
/// disposing a flag or returning a completed notification never establishes that drain.</summary>
public interface IDeveloperWorkspaceOriginalExecutionCommitPin : IAsyncDisposable
{
    void DemandOriginalExecutionBinding();
}
