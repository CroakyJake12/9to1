using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Go;

namespace NineToOne.Os.Shell;

public sealed class InstalledApplicationsGoProvider(IInstalledApplicationRegistry registry, LinuxApplicationLauncher launcher, IInstalledApplicationCompatibilityNavigation? compatibility = null) : IGoCanonicalResolver, IGoOriginalActorInvocation, IGoOriginalActorQuery, IGoOriginalActorCanonicalResolver
{
    public string ProviderId => "os.installed-applications";
    public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        if (query.Category is not (null or "Apps")) yield break;
        foreach (var app in (await registry.RefreshAsync(ct)).OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.ApplicationId))
        {
            ct.ThrowIfCancellationRequested();
            if (!app.Enabled || !app.ProfileAccessible || app.ProviderId != "linux.xdg-desktop" || !app.Label.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
            InstalledApplicationReference visible;
            try { visible = await launcher.ResolveForReadAsync(app.ApplicationId, app.Revision, ct); }
            catch (UnauthorizedAccessException) { continue; }
            yield return new(ProviderId, new("Home", "os.installed-application", app.ApplicationId.ToString("D"), app.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), visible.Label, "Apps", [new("Open", "Open")]);
        }
    }
    public async Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct)
    {
        if (locator is not { Owner: "Home", Kind: "os.installed-application" } || !Guid.TryParse(locator.Id, out var id) || id == Guid.Empty) return null;
        var app = (await registry.RefreshAsync(ct)).SingleOrDefault(a => a.ApplicationId == id && a.Enabled && a.ProfileAccessible && a.ProviderId == "linux.xdg-desktop");
        if (app is null) return null;
        InstalledApplicationReference visible;
        try { visible = await launcher.ResolveForReadAsync(app.ApplicationId, app.Revision, ct); }
        catch (UnauthorizedAccessException) { return null; }
        return new(ProviderId, new("Home", "os.installed-application", visible.ApplicationId.ToString("D"), visible.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            visible.Label, "Apps", [new("Open", "Open")]);
    }
    public async IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor expectedActor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (registry is not IInstalledApplicationOriginalActorRegistry originalRegistry)
            throw new UnauthorizedAccessException("The installed owner cannot retain the original query session.");
        if (query.Category is not (null or "Apps")) yield break;
        await launcher.RequireOriginalReadActorAsync(expectedActor, ct);
        var observed = await originalRegistry.RefreshForActorAsync(expectedActor, ct);
        await launcher.RequireOriginalReadActorAsync(expectedActor, ct);
        foreach (var app in observed.OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.ApplicationId))
        {
            ct.ThrowIfCancellationRequested();
            if (!app.Enabled || !app.ProfileAccessible || app.ProviderId != "linux.xdg-desktop" ||
                app.HomeProfileId != expectedActor.ProfileId || !app.Label.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
            var visible = await launcher.ResolveForReadForActorAsync(app.ApplicationId, app.Revision, expectedActor, ct);
            await launcher.RequireOriginalReadActorAsync(expectedActor, ct);
            yield return Result(visible);
        }
        await launcher.RequireOriginalReadActorAsync(expectedActor, ct);
    }
    public async Task<GoResult?> ResolveForActorAsync(GoCanonicalLocator locator, AuthenticatedResourceActor expectedActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (registry is not IInstalledApplicationOriginalActorRegistry originalRegistry)
            throw new UnauthorizedAccessException("The installed owner cannot retain the original resolution session.");
        if (locator is not { Owner: "Home", Kind: "os.installed-application" } || !Guid.TryParseExact(locator.Id, "D", out var id) || id == Guid.Empty) return null;
        await launcher.RequireOriginalReadActorAsync(expectedActor, ct);
        var observed = await originalRegistry.RefreshForActorAsync(expectedActor, ct);
        await launcher.RequireOriginalReadActorAsync(expectedActor, ct);
        var app = observed.SingleOrDefault(a => a.ApplicationId == id && a.Enabled && a.ProfileAccessible &&
            a.ProviderId == "linux.xdg-desktop" && a.HomeProfileId == expectedActor.ProfileId);
        if (app is null) return null;
        var visible = await launcher.ResolveForReadForActorAsync(app.ApplicationId, app.Revision, expectedActor, ct);
        await launcher.RequireOriginalReadActorAsync(expectedActor, ct);
        return Result(visible);
    }
    private GoResult Result(InstalledApplicationReference app) => new(ProviderId,
        new("Home", "os.installed-application", app.ApplicationId.ToString("D"), app.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        app.Label, "Apps", compatibility is null ? [new("Open", "Open")] : [new("Open", "Open"), new("Frameworks", "Application frameworks")]);
    public Task InvokeForActorAsync(GoCanonicalReference reference, string actionId, AuthenticatedResourceActor expectedActor, CancellationToken ct)
    {
        if (actionId == "Frameworks")
        {
            if (compatibility is null || reference.Owner != "Home" || reference.Kind != "os.installed-application" ||
                !Guid.TryParseExact(reference.Id, "D", out var selectedId) || selectedId == Guid.Empty ||
                !long.TryParse(reference.Revision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var selectedRevision) || selectedRevision <= 0)
                throw new UnauthorizedAccessException("The original compatibility navigation is unavailable.");
            return compatibility.OpenForActorAsync(selectedId, selectedRevision, expectedActor, ct);
        }
        if (reference.Owner != "Home" || reference.Kind != "os.installed-application" || actionId != "Open" || !Guid.TryParse(reference.Id, out var id) || !long.TryParse(reference.Revision, out var revision))
            throw new UnauthorizedAccessException("Unknown canonical application action.");
        return launcher.LaunchForActorAsync(id, revision, expectedActor, ct);
    }
    public Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct)
    {
        if (reference.Owner != "Home" || reference.Kind != "os.installed-application" || actionId != "Open" || !Guid.TryParse(reference.Id, out var id) || !long.TryParse(reference.Revision, out var revision))
            throw new UnauthorizedAccessException("Unknown canonical application action.");
        return launcher.LaunchAsync(id, revision, ct);
    }
}
