using System.Globalization;
using Haven.Application;

namespace Haven.Android;

internal sealed record AndroidPlatformShortcut(string Id, string Label);
internal sealed record AndroidApplicationShortcut(Guid ApplicationId, long RegistryRevision, string ShortcutId, string Label);

/// <summary>Typed app-owned shortcut actions; package/profile resolution always comes from Home.</summary>
internal sealed class AndroidInstalledApplicationShortcuts(IInstalledApplicationRegistry registry,
    ResourceAuthorizationService resources, AndroidLauncherPlatformCatalog platform)
{
    public async Task<IReadOnlyList<AndroidApplicationShortcut>> QueryAsync(Guid applicationId, long revision, CancellationToken ct)
    {
        var (actor, app) = await ResolveAsync(applicationId, revision, ResourceAccess.Read, ct);
        ct.ThrowIfCancellationRequested();
        var shortcuts = platform.ListShortcuts(app.PlatformProfileId, app.Entrypoint);
        if (actor != await resources.AuthorizeAsync("os.application.read", [Scope(applicationId, revision, ResourceAccess.Read)], ct))
            throw new UnauthorizedAccessException("Your Home profile changed. Open the app's shortcuts again.");
        return shortcuts.Where(s => ValidId(s.Id)).Take(64)
            .Select(s => new AndroidApplicationShortcut(applicationId, revision, s.Id, s.Label)).ToArray();
    }

    public async Task InvokeAsync(AndroidApplicationShortcut command, CancellationToken ct)
    {
        if (!ValidId(command.ShortcutId)) throw new UnauthorizedAccessException("This app shortcut is no longer available.");
        var (actor, app) = await ResolveAsync(command.ApplicationId, command.RegistryRevision, ResourceAccess.Execute, ct);
        if (actor != await resources.AuthorizeAsync("os.application.launch", [Scope(command.ApplicationId, command.RegistryRevision, ResourceAccess.Execute)], ct))
            throw new UnauthorizedAccessException("The app or Home profile changed. Open its shortcuts again.");
        ct.ThrowIfCancellationRequested();
        // The platform re-queries this exact package/profile and requires the shortcut still be enabled.
        platform.LaunchShortcut(app.PlatformProfileId, app.Entrypoint, command.ShortcutId);
    }

    private async Task<(AuthenticatedResourceActor Actor, InstalledApplicationReference App)> ResolveAsync(Guid id, long revision, ResourceAccess access, CancellationToken ct)
    {
        var action = access == ResourceAccess.Read ? "os.application.read" : "os.application.launch";
        var actor = await resources.AuthorizeAsync(action, [Scope(id, revision, access)], ct);
        var app = await registry.ResolveLaunchAsync(id, revision, ct);
        if (actor is null || actor.OrganisationId is not null || app is null || !app.Enabled || !app.ProfileAccessible ||
            app.HomeProfileId != actor.ProfileId || app.ProviderId != AndroidLauncherPlatformCatalog.ProviderId)
            throw new UnauthorizedAccessException("This app or its owning profile is unavailable.");
        if (actor != await resources.AuthorizeAsync(action, [Scope(id, revision, access)], ct))
            throw new UnauthorizedAccessException("The app or Home profile changed. Open its shortcuts again.");
        return (actor, app);
    }
    private static ResourceScope Scope(Guid id, long revision, ResourceAccess access)
        => new("os.installed-application", id.ToString("D"), revision.ToString(CultureInfo.InvariantCulture), access);
    private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 1024 && !id.Any(char.IsControl);
}
