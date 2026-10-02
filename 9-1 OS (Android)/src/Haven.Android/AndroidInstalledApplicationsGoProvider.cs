using System.Globalization;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Go;

namespace Haven.Android;

/// <summary>Same canonical Go contracts as OS search; Android owns the observed profile/component activation.</summary>
internal sealed class AndroidInstalledApplicationsGoProvider(IInstalledApplicationRegistry registry,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
    AndroidLauncherPlatformCatalog platform) : IGoCanonicalResolver, IGoOriginalActorQuery, IGoOriginalActorCanonicalResolver, IGoOriginalActorInvocation, IAndroidGoOriginalHostInvocation
{
    public const string Id = "android.installed-applications";
    public string ProviderId => Id;
    public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        if (query.Category is not (null or "Apps")) yield break;
        var actor = await actors.GetCurrentAsync(ct) ?? throw new UnauthorizedAccessException("The current Home profile is unavailable.");
        await foreach (var result in QueryForActorAsync(query, actor, ct)) yield return result;
    }
    public async IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor originalActor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await SameActor(originalActor, ct);
        if (query.Category is not (null or "Apps")) yield break;
        var originalRegistry = OriginalRegistry();
        var apps = await originalRegistry.RefreshForActorAsync(originalActor, ct);
        await SameActor(originalActor, ct);
        foreach (var app in apps.OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.ApplicationId))
        {
            ct.ThrowIfCancellationRequested(); await SameActor(originalActor, ct);
            if (!app.Enabled || !app.ProfileAccessible || app.HomeProfileId != originalActor.ProfileId ||
                app.ProviderId != AndroidLauncherPlatformCatalog.ProviderId ||
                !(app.Label.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase) || app.OsApplicationId.Contains(query.Text, StringComparison.OrdinalIgnoreCase))) continue;
            var scope = new ResourceScope("os.installed-application", app.ApplicationId.ToString("D"), app.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read);
            var allowed = false;
            try { allowed = originalActor == await resources.AuthorizeForActorAsync(originalActor, "os.application.read", [scope], ct); }
            catch (UnauthorizedAccessException) { }
            await SameActor(originalActor, ct);
            if (!allowed) continue;
            yield return Result(app);
        }
        await SameActor(originalActor, ct);
    }
    public async Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct);
        return actor is null ? null : await ResolveForActorAsync(locator, actor, ct);
    }
    public async Task<GoResult?> ResolveForActorAsync(GoCanonicalLocator locator, AuthenticatedResourceActor originalActor, CancellationToken ct)
    {
        await SameActor(originalActor, ct);
        if (locator is not { Owner: "Home", Kind: "os.installed-application" } || !Guid.TryParse(locator.Id, out var id) || id == Guid.Empty) return null;
        var apps = await OriginalRegistry().RefreshForActorAsync(originalActor, ct);
        await SameActor(originalActor, ct);
        var app = apps.SingleOrDefault(a => a.ApplicationId == id && a.Enabled && a.ProfileAccessible &&
            a.HomeProfileId == originalActor.ProfileId && a.ProviderId == AndroidLauncherPlatformCatalog.ProviderId);
        if (app is null) return null;
        var scope = new ResourceScope("os.installed-application", app.ApplicationId.ToString("D"), app.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Read);
        var allowed = false;
        try { allowed = originalActor == await resources.AuthorizeForActorAsync(originalActor, "os.application.read", [scope], ct); }
        catch (UnauthorizedAccessException) { }
        await SameActor(originalActor, ct);
        return allowed ? Result(app) : null;
    }
    public async Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct) ?? throw new UnauthorizedAccessException("The current Home profile is unavailable.");
        await InvokeForActorAsync(reference, actionId, actor, ct);
    }
    public Task InvokeForActorAsync(GoCanonicalReference reference, string actionId, AuthenticatedResourceActor originalActor, CancellationToken ct)
        => InvokeForOriginalHostAsync(reference, actionId, originalActor, () => true, ct);
    public async Task InvokeForOriginalHostAsync(GoCanonicalReference reference, string actionId, AuthenticatedResourceActor originalActor,
        Func<bool> originalHostCurrent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalHostCurrent);
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original Android application host is unavailable.");
        await SameActor(originalActor, ct);
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original Android application host changed.");
        if (reference is not { Owner: "Home", Kind: "os.installed-application" } || actionId != "Open" ||
            !Guid.TryParse(reference.Id, out var id) || !long.TryParse(reference.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
            throw new UnauthorizedAccessException("The canonical Android application action is invalid.");
        var originalRegistry = OriginalRegistry();
        var scope = new ResourceScope(reference.Kind, reference.Id, reference.Revision, ResourceAccess.Execute);
        if (originalActor != await resources.AuthorizeForActorAsync(originalActor, "os.application.launch", [scope], ct))
            throw new UnauthorizedAccessException("This application is unavailable for the original Home profile.");
        await SameActor(originalActor, ct);
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original Android application host changed during admission.");
        var app = await originalRegistry.ResolveLaunchForActorAsync(id, revision, originalActor, ct);
        await SameActor(originalActor, ct);
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original Android application host changed during registry resolution.");
        if (app is null || app.HomeProfileId != originalActor.ProfileId || app.ProviderId != AndroidLauncherPlatformCatalog.ProviderId)
            throw new InvalidOperationException("The application or its owning Android profile changed. Refresh the launcher.");
        if (originalActor != await resources.AuthorizeForActorAsync(originalActor, "os.application.launch", [scope], ct))
            throw new UnauthorizedAccessException("Application ownership changed before activation.");
        await SameActor(originalActor, ct); ct.ThrowIfCancellationRequested();
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original Android application host changed before activation.");
        platform.Launch(app.PlatformProfileId, app.Entrypoint);
    }
    private IInstalledApplicationOriginalActorRegistry OriginalRegistry() => registry as IInstalledApplicationOriginalActorRegistry
        ?? throw new UnauthorizedAccessException("Original-owner Android application registry is unavailable.");
    private async Task SameActor(AuthenticatedResourceActor originalActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        if (await actors.GetCurrentAsync(ct) != originalActor) throw new UnauthorizedAccessException("The original Home Android session changed.");
    }
    private static GoResult Result(InstalledApplicationReference app) => new(Id,
        new("Home", "os.installed-application", app.ApplicationId.ToString("D"), app.Revision.ToString(CultureInfo.InvariantCulture)),
        app.Label, "Apps", [new("Open", "Open")]);

}

internal sealed class AndroidInstalledApplicationResourceResolver(IInstalledApplicationRegistry registry) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "os.installed-application";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
    {
        var denied = new ResourceAccessDecision(false, "ApplicationUnavailable", actor.ActorId, scope.Revision, actor.OrganisationId);
        if (!((actionId == "os.application.launch" && scope.Access == ResourceAccess.Execute) ||
            (actionId == "os.application.read" && scope.Access == ResourceAccess.Read)) || !Guid.TryParse(scope.Id, out var id) ||
            !long.TryParse(scope.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)) return denied;
        if (registry is not IInstalledApplicationOriginalActorRegistry originalRegistry) return denied;
        var app = await originalRegistry.ResolveLaunchForActorAsync(id, revision, actor, ct);
        return denied with { Allowed = app is { Enabled: true, ProfileAccessible: true } && app.HomeProfileId == actor.ProfileId &&
            app.ProviderId == AndroidLauncherPlatformCatalog.ProviderId && actor.OrganisationId is null, Code = "CurrentInstalledAndroidApplication" };
    }
}
