using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService : IWorkspaceOriginalProcessStartEntryReleaseSource
{
    [ThreadStatic] private static HashSet<Invocation>? _physicalProcessStartEntrySources;

    public void DemandOriginalProcessStartEntryReleaseSupport(IWorkspaceToolFinalFence sameFence)
    {
        ArgumentNullException.ThrowIfNull(sameFence);
        if (_originalAuthority is null || !_originalAuthority.IsIssuedOriginal(sameFence) ||
            sameFence is not IWorkspaceOriginalProcessStartEntryReleaseFence)
            throw new UnauthorizedAccessException("The SAME configured final fence lacks original process-start entry release support.");
    }

    private static Task InvokeOriginalProcessStartEntryRelease(Invocation original,
        IWorkspaceOriginalProcessStartEntryReleaseFence source, string target, string digest)
    {
        // Physical synchronous ancestry survives a callback restoring an older ExecutionContext.
        // The actual source's own pure guard covers its post-await original callback ancestry.
        var scopes = _physicalProcessStartEntrySources ??= [];
        if (!scopes.Add(original))
            throw new InvalidOperationException("The SAME original process-start entry source is already executing.");
        try
        {
            source.DemandExternalOriginalProcessStartEntryJoin();
            return source.ReleaseOriginalProcessStartEntryAsync(original.Root, target, digest)
                ?? throw new InvalidOperationException("The original process-start release source returned no actual Task.");
        }
        finally { scopes.Remove(original); }
    }

    private sealed partial class Invocation
    {
        // One declared process per invocation. A failed/canceled release remains inspectable
        // and is never replaced by a completion notification or automatically retried.
        private readonly List<Task> _originalProcessStartEntryReleases = [];

        internal void RetainOriginalProcessStartEntryRelease(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            lock (_gate)
            {
                if (_originalProcessStartEntryReleases.Any(value => ReferenceEquals(value, actual))) return;
                if (_originalProcessStartEntryReleases.Count != 0)
                    throw new InvalidOperationException("The original process-start entry release was already retained.");
                _originalProcessStartEntryReleases.Add(actual);
            }
        }

        private void DemandExternalOriginalProcessStartReleaseJoin()
        {
            if (_physicalProcessStartEntrySources?.Contains(this) == true)
                throw new InvalidOperationException("An actual process-start release callback cannot join its encompassing invocation.");
            if (Fence is IWorkspaceOriginalProcessStartEntryReleaseFence source)
                source.DemandExternalOriginalProcessStartEntryJoin();
        }
    }
}
