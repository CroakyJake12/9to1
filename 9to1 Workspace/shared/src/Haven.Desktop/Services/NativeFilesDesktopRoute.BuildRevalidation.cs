using Haven.Application;
using Avalonia.Threading;

namespace Haven.Desktop.Services;

/// <summary>Genuine retained async revalidation for the Build (Dev) native content session over the
/// SAME Home/Files route. Unlike the cached identity-only <see cref="CheckOriginalAppContentProfile"/>,
/// it re-reads the ACTUAL current actor/runtime and verifies the request's profile label matches, so
/// revocation or actor replacement is observed (a matching cached label is never authorization). It
/// returns a retained task the route's close drains, and it is used around awaited acquisition/publication.</summary>
internal sealed partial class NativeFilesDesktopRoute
{
    internal Task RevalidateOriginalAppContentForBuildAsync(string expectedProfile, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sync)
        {
            ThrowIfRetired(); cancellationToken.ThrowIfCancellationRequested();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RevalidateOriginalAppContentForBuildCoreAsync(gate.Task, expectedProfile, cancellationToken);
            _originalPublications.RemoveWhere(row => row.IsCompleted);
            _originalPublications.Add(original);
            gate.SetResult();
            return original;
        }
    }

    private async Task RevalidateOriginalAppContentForBuildCoreAsync(Task start, string expectedProfile, CancellationToken token)
    {
        await start;
        using var phase = _sameProcessHome is null ? null : CloudflareOriginalExecutionGuard.EnterOriginal(this);
        // Re-read the CURRENT actor from the genuine Home runtime; this is not cached profile metadata.
        var actor = await ReadOriginalRouteActorAsync(token)
            ?? throw new UnauthorizedAccessException("The original Build/Files actor is unavailable.");
        lock (_sync)
        {
            ThrowIfRetired();
            if (_originalActor is not null && _originalActor != actor)
                throw new UnauthorizedAccessException("The original Build/Files actor changed.");
        }
        if (!string.Equals(actor.ProfileId, expectedProfile, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("This Build tab belongs to a different authenticated Home profile.");
        await RequireReadyAsync(token);
        lock (_sync)
        {
            ThrowIfRetired();
            if (_originalActor is not null && _originalActor != actor)
                throw new UnauthorizedAccessException("The original Build/Files actor changed during revalidation.");
        }
    }
}
