using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeNativeCoreApiSessions
{
    /// <summary>Read-only currentness borrowed by the original package operation
    /// ALREADY running inside its native Context gate. The registered package owner
    /// retains/drains this exact Task before retiring the borrowed session issuer.
    /// Never call under a device-store writer or reacquire the non-reentrant gate.
    /// Matching metadata is not approval, publisher enrollment or a new session.</summary>
    public async Task DemandOriginalPackageArtifactSessionCurrentAsync(AuthenticatedResourceActor originalActor,
        HomeNativeInstalledPeer originalCaller, string originalSessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalActor); ArgumentNullException.ThrowIfNull(originalCaller);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Text(originalSessionId) || _closing)
            throw new UnauthorizedAccessException("An original retained native package session is required.");
        var context = _sessions.Values.SingleOrDefault(value => value.SessionId == originalSessionId);
        var caller = Copy(originalCaller);
        if (context is null || caller is null || context.Gate.CurrentCount != 0 ||
            context.Actor != originalActor || !SameOwner(caller, context.Peer) ||
            !_sessions.TryGetValue(context.Id, out var issued) || !ReferenceEquals(context, issued))
            throw new UnauthorizedAccessException("The original actor/caller/session tuple is not an in-progress native package context.");
        if (!await CurrentAsync(context, cancellationToken).ConfigureAwait(false) ||
            context.Gate.CurrentCount != 0 || context.Actor != originalActor || !SameOwner(caller, context.Peer) ||
            !_sessions.TryGetValue(context.Id, out issued) || !ReferenceEquals(context, issued))
            throw new UnauthorizedAccessException("The original native package actor, process, installation or session retired.");
    }
}
