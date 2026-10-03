using System.Globalization;
using System.Runtime.CompilerServices;
using Haven.Application;

namespace Haven.Android;

internal sealed record AndroidPlatformShortcut(string Id, string Label);
internal sealed record AndroidApplicationShortcut(Guid ApplicationId, long RegistryRevision, string ShortcutId, string Label);

internal sealed class AndroidOriginalShortcutSelection(AndroidInstalledApplicationShortcuts issuer,
    AuthenticatedResourceActor originalActor, AndroidApplicationShortcut command)
{
    internal AndroidInstalledApplicationShortcuts Issuer { get; } = issuer;
    internal AuthenticatedResourceActor OriginalActor { get; } = originalActor;
    internal AndroidApplicationShortcut Command { get; } = command;
    public string Label => Command.Label;
}

/// <summary>Typed app-owned shortcut actions; package/profile resolution always comes from Home.</summary>
internal sealed class AndroidInstalledApplicationShortcuts(IInstalledApplicationRegistry registry,
    ResourceAuthorizationService resources, AndroidLauncherPlatformCatalog platform)
{
    private readonly ConditionalWeakTable<AndroidOriginalShortcutSelection, object> originalSelections = new();
    public async Task<IReadOnlyList<AndroidOriginalShortcutSelection>> QueryForActorAsync(Guid applicationId, long revision,
        AuthenticatedResourceActor originalActor, CancellationToken ct)
    {
        var app = await ResolveForActorAsync(applicationId, revision, ResourceAccess.Read, originalActor, ct);
        ct.ThrowIfCancellationRequested();
        var shortcuts = platform.ListShortcuts(app.PlatformProfileId, app.Entrypoint);
        if (originalActor != await resources.AuthorizeForActorAsync(originalActor, "os.application.read", [Scope(applicationId, revision, ResourceAccess.Read)], ct))
            throw new UnauthorizedAccessException("The original Home shortcut session changed.");
        var selected = new List<AndroidOriginalShortcutSelection>();
        foreach (var shortcut in shortcuts.Where(s => ValidId(s.Id)).Take(64))
        {
            var selection = new AndroidOriginalShortcutSelection(this, originalActor, new(applicationId, revision, shortcut.Id, shortcut.Label));
            originalSelections.Add(selection, new object()); selected.Add(selection);
        }
        return selected;
    }
    public async Task InvokeOriginalAsync(AndroidOriginalShortcutSelection originalSelection, Func<bool> originalHostCurrent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalSelection); ArgumentNullException.ThrowIfNull(originalHostCurrent);
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original shortcut host is unavailable.");
        if (!ReferenceEquals(originalSelection.Issuer, this) || !originalSelections.TryGetValue(originalSelection, out _))
            throw new UnauthorizedAccessException("The actual original shortcut selection is unavailable.");
        var command = originalSelection.Command; var actor = originalSelection.OriginalActor;
        if (!ValidId(command.ShortcutId)) throw new UnauthorizedAccessException("The original shortcut is invalid.");
        var app = await ResolveForActorAsync(command.ApplicationId, command.RegistryRevision, ResourceAccess.Execute, actor, ct);
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original shortcut host changed.");
        if (actor != await resources.AuthorizeForActorAsync(actor, "os.application.launch", [Scope(command.ApplicationId, command.RegistryRevision, ResourceAccess.Execute)], ct))
            throw new UnauthorizedAccessException("The original Home shortcut session changed.");
        ct.ThrowIfCancellationRequested();
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original shortcut host changed before activation.");
        // Native API freshly requires the exact original package/profile and enabled shortcut; no final native atomic lease is claimed.
        platform.LaunchShortcut(app.PlatformProfileId, app.Entrypoint, command.ShortcutId);
    }
    private async Task<InstalledApplicationReference> ResolveForActorAsync(Guid id, long revision, ResourceAccess access,
        AuthenticatedResourceActor originalActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        if (registry is not IInstalledApplicationOriginalActorRegistry originalRegistry)
            throw new UnauthorizedAccessException("Original-owner shortcut registry is unavailable.");
        var action = access == ResourceAccess.Read ? "os.application.read" : "os.application.launch";
        if (originalActor != await resources.AuthorizeForActorAsync(originalActor, action, [Scope(id, revision, access)], ct))
            throw new UnauthorizedAccessException("The original shortcut owner is unavailable.");
        var app = await originalRegistry.ResolveLaunchForActorAsync(id, revision, originalActor, ct);
        if (originalActor.OrganisationId is not null || app is null || !app.Enabled || !app.ProfileAccessible ||
            app.HomeProfileId != originalActor.ProfileId || app.ProviderId != AndroidLauncherPlatformCatalog.ProviderId ||
            originalActor != await resources.AuthorizeForActorAsync(originalActor, action, [Scope(id, revision, access)], ct))
            throw new UnauthorizedAccessException("The original app or its owning profile changed.");
        return app;
    }
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
