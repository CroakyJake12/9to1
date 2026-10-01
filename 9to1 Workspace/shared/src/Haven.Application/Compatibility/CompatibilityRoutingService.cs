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
        var app = await applications.ResolveLaunchAsync(applicationId, expectedRevision, cancellationToken);
        RequireApplication(app, actor, applicationId, expectedRevision);
        var observation = await owner.ObserveAsync(actor, app!, cancellationToken);
        if (observation is null)
            throw new IOException("The compatibility owner cannot observe this installed application.");
        if (observation.ApplicationId != applicationId || observation.ApplicationRevision != expectedRevision ||
            string.IsNullOrWhiteSpace(observation.Revision))
            throw new IOException("Compatibility evidence does not match the installed application.");
        // Copy the owner collection before awaiting again. Caller mutations cannot rewrite a retained proposal.
        var backends = observation.Backends?.ToArray()
            ?? throw new IOException("Compatibility backend evidence is missing.");
        if (backends.Length > 128 || backends.Any(b => b is null || string.IsNullOrWhiteSpace(b.BackendId) ||
            string.IsNullOrWhiteSpace(b.EnvironmentId) || string.IsNullOrWhiteSpace(b.EnvironmentRevision) ||
            !Enum.IsDefined(b.Kind) || (!b.Eligible && string.IsNullOrWhiteSpace(b.ExclusionReason))) ||
            backends.Select(b => b.BackendId).Distinct(StringComparer.Ordinal).Count() != backends.Length)
            throw new IOException("Compatibility backend evidence is ambiguous or incomplete.");
        var currentApp = await applications.ResolveLaunchAsync(applicationId, expectedRevision, cancellationToken);
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
