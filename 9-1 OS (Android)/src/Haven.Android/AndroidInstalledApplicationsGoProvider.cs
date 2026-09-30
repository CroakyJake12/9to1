using System.Globalization;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Go;

namespace Haven.Android;

/// <summary>Same canonical Go contracts as OS search; Android owns the observed profile/component activation.</summary>
internal sealed class AndroidInstalledApplicationsGoProvider(IInstalledApplicationRegistry registry,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
    AndroidLauncherPlatformCatalog platform) : IGoProvider
{
    public const string Id = "android.installed-applications";
    public string ProviderId => Id;
    public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        if (query.Category is not (null or "Apps")) yield break;
        var actor = await actors.GetCurrentAsync(ct);
        if (actor is null) throw new UnauthorizedAccessException("The current Home profile is unavailable.");
        foreach (var app in (await registry.RefreshAsync(ct)).OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.ApplicationId))
        {
            ct.ThrowIfCancellationRequested();
            if (!app.Enabled || !app.ProfileAccessible || app.HomeProfileId != actor.ProfileId ||
                app.ProviderId != AndroidLauncherPlatformCatalog.ProviderId ||
                !(app.Label.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase) || app.OsApplicationId.Contains(query.Text, StringComparison.OrdinalIgnoreCase))) continue;
            if (actor != await actors.GetCurrentAsync(ct)) throw new UnauthorizedAccessException("The Home profile changed during discovery.");
            yield return new(Id, new("Home", "os.installed-application", app.ApplicationId.ToString("D"), app.Revision.ToString(CultureInfo.InvariantCulture)),
                app.Label, "Apps", [new("Open", "Open")]);
        }
    }
    public async Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct)
    {
        if (reference is not { Owner: "Home", Kind: "os.installed-application" } || actionId != "Open" ||
            !Guid.TryParse(reference.Id, out var id) || !long.TryParse(reference.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
            throw new UnauthorizedAccessException("The canonical Android application action is invalid.");
        var scope = new ResourceScope(reference.Kind, reference.Id, reference.Revision, ResourceAccess.Execute);
        var actor = await resources.AuthorizeAsync("os.application.launch", [scope], ct);
        if (actor is null) throw new UnauthorizedAccessException("This application is unavailable for the current Home profile.");
        var app = await registry.ResolveLaunchAsync(id, revision, ct);
        if (app is null || app.HomeProfileId != actor.ProfileId || app.ProviderId != AndroidLauncherPlatformCatalog.ProviderId)
            throw new InvalidOperationException("The application or its owning Android profile changed. Refresh the launcher.");
        if (actor != await resources.AuthorizeAsync("os.application.launch", [scope], ct))
            throw new UnauthorizedAccessException("Application ownership changed before activation.");
        ct.ThrowIfCancellationRequested();
        platform.Launch(app.PlatformProfileId, app.Entrypoint);
    }
}

internal sealed class AndroidInstalledApplicationResourceResolver(IInstalledApplicationRegistry registry) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "os.installed-application";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
    {
        var denied = new ResourceAccessDecision(false, "ApplicationUnavailable", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (actionId != "os.application.launch" || scope.Access != ResourceAccess.Execute || !Guid.TryParse(scope.Id, out var id) ||
            !long.TryParse(scope.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)) return denied;
        var app = await registry.ResolveLaunchAsync(id, revision, ct);
        return denied with { Allowed = app is { Enabled: true, ProfileAccessible: true } && app.HomeProfileId == actor.ProfileId &&
            app.ProviderId == AndroidLauncherPlatformCatalog.ProviderId && actor.OrganisationId is null, Code = "CurrentInstalledAndroidApplication" };
    }
}
