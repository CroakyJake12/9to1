using System.Collections.ObjectModel;

namespace Haven.Application.Compatibility;

/// <summary>One policy-aware routing seam for launch surfaces. This service never starts a foreign process.</summary>
public sealed class CompatibilityRoutingService(IAuthenticatedResourceActorSource actors,
    IInstalledApplicationRegistry applications, ICompatibilityApplicationOwner owner)
{
    public async ValueTask<CompatibilityRoutingPreview> PreviewAsync(Guid applicationId, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty || expectedRevision <= 0)
            throw new ArgumentException("A canonical installed application identity and revision are required.");
        var actor = await actors.GetCurrentAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("An authenticated Home profile is required.");
        return await PreviewCoreAsync(applicationId, expectedRevision, actor, null, false, cancellationToken);
    }

    /// <summary>Original-session, explicit framework choice. Read-only proposal; never a review receipt or execution grant.</summary>
    public async ValueTask<CompatibilityRoutingPreview> PreviewForActorAsync(Guid applicationId, long expectedRevision,
        AuthenticatedResourceActor expectedActor, string? requestedBackendId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (applicationId == Guid.Empty || expectedRevision <= 0 || requestedBackendId is { Length: > 4096 } ||
            requestedBackendId is not null && (string.IsNullOrWhiteSpace(requestedBackendId) || requestedBackendId.Any(char.IsControl)))
            throw new ArgumentException("Retain a canonical app revision and a bounded explicit framework identity.");
        if (await actors.GetCurrentAsync(cancellationToken) != expectedActor)
            throw new UnauthorizedAccessException("The original Home actor is no longer current.");
        if (applications is not IInstalledApplicationOriginalActorRegistry)
            throw new UnauthorizedAccessException("The installed owner cannot preserve the original actor.");
        return await PreviewCoreAsync(applicationId, expectedRevision, expectedActor, requestedBackendId, true, cancellationToken);
    }

    private async ValueTask<CompatibilityRoutingPreview> PreviewCoreAsync(Guid applicationId, long expectedRevision,
        AuthenticatedResourceActor actor, string? requestedBackendId, bool originalOnly, CancellationToken cancellationToken)
    {
        async ValueTask<InstalledApplicationReference?> ResolveAsync() => originalOnly
            ? await ((IInstalledApplicationOriginalActorRegistry)applications).ResolveLaunchForActorAsync(applicationId, expectedRevision, actor, cancellationToken)
            : await applications.ResolveLaunchAsync(applicationId, expectedRevision, cancellationToken);
        var app = await ResolveAsync();
        if (originalOnly && await actors.GetCurrentAsync(cancellationToken) != actor)
            throw new UnauthorizedAccessException("Home changed while resolving the original installed app.");
        RequireApplication(app, actor, applicationId, expectedRevision);
        var observation = await owner.ObserveAsync(actor, app!, cancellationToken);
        if (observation is null)
            throw new IOException("The compatibility owner cannot observe this installed application.");
        if (observation.ApplicationId != applicationId || observation.ApplicationRevision != expectedRevision ||
            string.IsNullOrWhiteSpace(observation.Revision))
            throw new IOException("Compatibility evidence does not match the installed application.");
        // Copy the owner collection before awaiting again. Caller mutations cannot rewrite a retained proposal.
        if (observation.Backends is null) throw new IOException("Compatibility backend evidence is missing.");
        var capturedBackends = new List<CompatibilityBackendObservation>();
        foreach (var backend in observation.Backends)
        {
            if (capturedBackends.Count == 128) throw new IOException("Compatibility backend evidence exceeds the supported bound.");
            capturedBackends.Add(backend);
        }
        var backends = capturedBackends.ToArray();
        if (backends.Length > 128 || backends.Any(b => b is null || string.IsNullOrWhiteSpace(b.BackendId) ||
            string.IsNullOrWhiteSpace(b.EnvironmentId) || string.IsNullOrWhiteSpace(b.EnvironmentRevision) ||
            !Enum.IsDefined(b.Kind) || (!b.Eligible && string.IsNullOrWhiteSpace(b.ExclusionReason))) ||
            backends.Select(b => b.BackendId).Distinct(StringComparer.Ordinal).Count() != backends.Length)
            throw new IOException("Compatibility backend evidence is ambiguous or incomplete.");
        var currentApp = await ResolveAsync();
        var currentActor = await actors.GetCurrentAsync(cancellationToken);
        if (currentActor != actor || currentApp != app)
            throw new UnauthorizedAccessException("Home or the installed application changed during routing.");
        cancellationToken.ThrowIfCancellationRequested();
        var frozen = new ReadOnlyCollection<CompatibilityBackendObservation>(backends);
        CompatibilityRoutingPreview Result(CompatibilityRoutingStatus status, string reason,
            CompatibilityBackendObservation? backend = null) => new(status, reason, applicationId,
                expectedRevision, observation.Revision, backend, frozen);
        if (!observation.PolicyAllowed)
            return Result(CompatibilityRoutingStatus.Denied, observation.PolicyReason ?? "Managed compatibility policy denies this application.");
        if (!observation.PackageTrustVerified)
            return Result(CompatibilityRoutingStatus.Denied, "The package owner has not verified application trust.");
        var eligible = backends.Where(b => b.Eligible).OrderByDescending(b => b.Priority)
            .ThenBy(b => b.BackendId, StringComparer.Ordinal).ToArray();
        if (requestedBackendId is not null)
        {
            var chosen = eligible.SingleOrDefault(b => b.BackendId == requestedBackendId);
            return chosen is null
                ? Result(CompatibilityRoutingStatus.RequestedBackendUnavailable, "The requested framework is unavailable or ineligible. No other framework was substituted.")
                : Result(CompatibilityRoutingStatus.ReviewRequired, "Review this explicitly chosen framework and its host permissions before launch.", chosen);
        }
        if (observation.PreferredBackendId is { } preferred)
        {
            var selected = eligible.SingleOrDefault(b => b.BackendId == preferred);
            return selected is null
                ? Result(CompatibilityRoutingStatus.PreferredBackendUnavailable, "The preferred framework is unavailable or ineligible. Choose another framework explicitly.")
                : Result(CompatibilityRoutingStatus.ReviewRequired, "Review the current framework and its host permissions before launch.", selected);
        }
        return eligible.Length == 0
            ? Result(CompatibilityRoutingStatus.Unavailable, "No eligible compatibility framework is currently available.")
            : Result(CompatibilityRoutingStatus.ReviewRequired, "Review the current framework and its host permissions before launch.", eligible[0]);
    }

    private static void RequireApplication(InstalledApplicationReference? app, AuthenticatedResourceActor actor,
        Guid id, long revision)
    {
        if (app is null || app.ApplicationId != id || app.Revision != revision || app.HomeProfileId != actor.ProfileId ||
            !app.Enabled || !app.ProfileAccessible)
            throw new UnauthorizedAccessException("The installed application is unavailable to the current Home profile.");
    }
}
