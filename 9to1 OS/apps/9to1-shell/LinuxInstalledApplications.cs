using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell;

public sealed record LinuxDesktopApplication(string DesktopId, string Path, string Label, string Digest, bool Enabled);

/// <summary>Reads the actual XDG desktop-entry inventory. Home owns persistent identities, not this adapter.</summary>
public sealed class LinuxInstalledApplications(ITrustedHostPrincipalSource principals) : IInstalledApplicationObservationProvider
{
    public string ProviderId => "linux.xdg-desktop";
    public async ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
    {
        var principal = await principals.GetPrincipalAsync(ct);
        if (!OperatingSystem.IsLinux() || principal is null) return [];
        try
        {
            var inventory = await Task.Run(() => ReadInventory(ct), ct);
            if (principal != await principals.GetPrincipalAsync(ct)) throw new UnauthorizedAccessException("The OS principal changed during app discovery.");
            return [new(principal, "This Linux profile", false, true, inventory.Select(a => new InstalledApplicationObservation(
                "desktop:" + a.DesktopId, "desktop:" + a.DesktopId, a.Label, a.Digest, a.Enabled)).ToArray())];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return [new(principal, "This Linux profile", false, false, [])]; }
    }
    public static IReadOnlyList<LinuxDesktopApplication> ReadInventory(CancellationToken ct)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathFullyQualified(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var dirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        var roots = new[] { dataHome }.Concat((string.IsNullOrWhiteSpace(dirs) ? "/usr/local/share:/usr/share" : dirs).Split(':')).Where(Path.IsPathFullyQualified).Distinct(StringComparer.Ordinal);
        var applications = new Dictionary<string, LinuxDesktopApplication>(StringComparer.Ordinal);
        var masked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var directory = Path.Combine(root, "applications");
            if (!Directory.Exists(directory)) continue;
            var entries = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 8 };
            foreach (var file in Directory.EnumerateFiles(directory, "*.desktop", entries).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (masked.Count >= 10000) throw new IOException("Desktop inventory exceeds supported bounds.");
                var id = Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '-');
                if (!masked.Add(id)) continue; // XDG user overrides, including Hidden, mask lower-priority entries.
                var info = new FileInfo(file);
                if (info.Length is < 1 or > 1024 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var bytes = File.ReadAllBytes(file);
                if (bytes.Length > 1024 * 1024) throw new IOException("Desktop entry changed beyond its read bound.");
                var parsed = Parse(id, file, Encoding.UTF8.GetString(bytes), Convert.ToHexString(SHA256.HashData(bytes)));
                if (parsed is not null) applications.Add(id, parsed);
            }
        }
        return applications.Values.OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.DesktopId, StringComparer.Ordinal).ToArray();
    }
    public static LinuxDesktopApplication? Parse(string id, string path, string text, string digest)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal); var active = false;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim(); if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('[')) { active = trimmed == "[Desktop Entry]"; continue; }
            if (!active) continue;
            var equals = trimmed.IndexOf('='); if (equals < 1) continue;
            var key = trimmed[..equals]; if (!values.TryAdd(key, trimmed[(equals + 1)..])) return null;
        }
        if (values.GetValueOrDefault("Type") != "Application" || values.GetValueOrDefault("Hidden") == "true" || values.GetValueOrDefault("NoDisplay") == "true") return null;
        var name = values.GetValueOrDefault("Name");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 4096 || name.Any(char.IsControl)) return null;
        var desktops = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (values.TryGetValue("OnlyShowIn", out var only) && !only.Split(';').Intersect(desktops, StringComparer.Ordinal).Any()) return null;
        if (values.TryGetValue("NotShowIn", out var not) && not.Split(';').Intersect(desktops, StringComparer.Ordinal).Any()) return null;
        var enabled = !string.IsNullOrWhiteSpace(values.GetValueOrDefault("Exec")) || values.GetValueOrDefault("DBusActivatable") == "true";
        if (values.TryGetValue("TryExec", out var executable)) enabled &= ExecutableAvailable(executable);
        return new(id, path, name.Replace("\\s", " ").Replace("\\\\", "\\"), digest, enabled);
    }
    private static bool ExecutableAvailable(string executable)
    {
        if (!OperatingSystem.IsLinux()) return false;
        if (string.IsNullOrWhiteSpace(executable) || executable.Any(char.IsControl)) return false;
        if (Path.IsPathFullyQualified(executable)) return File.Exists(executable) && (File.GetUnixFileMode(executable) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        if (executable.Contains('/')) return false;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Where(Path.IsPathFullyQualified).Any(dir => ExecutableAvailable(Path.Combine(dir, executable)));
    }
}

public sealed class InstalledApplicationResourceResolver(IInstalledApplicationRegistry registry) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "os.installed-application";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
    {
        var denied = new ResourceAccessDecision(false, "ApplicationUnavailable", actor.ActorId, scope.Revision, actor.OrganisationId);
        var read = (actionId == "os.application.read" || actionId == HomeNativeWidgetRegistry.RenderActionId)
            && scope.Access == ResourceAccess.Read;
        var launch = actionId == "os.application.launch" && scope.Access == ResourceAccess.Execute;
        if ((!read && !launch) || !Guid.TryParse(scope.Id, out var id) || id == Guid.Empty ||
            !long.TryParse(scope.Revision, out var revision) || revision < 1) return denied;
        InstalledApplicationReference? app;
        if (read)
        {
            // Existing canonical read only: never refresh providers or initialize missing records.
            if (registry is not IInstalledApplicationOriginalReadRegistry originalRead) return denied;
            var snapshot = await originalRead.ReadExistingForActorAsync(actor, ct);
            app = snapshot?.Applications.SingleOrDefault(a => a.ApplicationId == id && a.Revision == revision &&
                a.Enabled && a.ProfileAccessible);
        }
        else
        {
            if (registry is not IInstalledApplicationOriginalActorRegistry originalRegistry) return denied;
            app = await originalRegistry.ResolveLaunchForActorAsync(id, revision, actor, ct);
        }
        return denied with { Allowed = app is not null && app.HomeProfileId == actor.ProfileId && app.ProviderId == "linux.xdg-desktop" && actor.OrganisationId is null, Code = "CurrentInstalledApplication" };
    }
}

public sealed class LinuxApplicationLauncher(IInstalledApplicationRegistry registry, ResourceAuthorizationService resources, IAuthenticatedResourceActorSource? actors = null)
{
    public async Task<InstalledApplicationReference> ResolveForReadAsync(Guid id, long revision, CancellationToken ct)
    {
        var scope = new ResourceScope("os.installed-application", id.ToString("D"), revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Read);
        var original = await resources.AuthorizeAsync("os.application.read", [scope], ct);
        if (original is null) throw new UnauthorizedAccessException("This installed application is not visible to the current profile.");
        return await ResolveForReadForActorAsync(id, revision, original, ct);
    }
    internal ValueTask RequireOriginalReadActorAsync(AuthenticatedResourceActor expectedActor, CancellationToken ct)
        => RequireOriginalAsync(expectedActor, null, ct);
    public async Task<InstalledApplicationReference> ResolveForReadForActorAsync(Guid id, long revision,
        AuthenticatedResourceActor expectedActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (registry is not IInstalledApplicationOriginalReadRegistry originalRead)
            throw new UnauthorizedAccessException("The installed owner cannot retain the original read session.");
        await RequireOriginalAsync(expectedActor, null, ct);
        var scope = new ResourceScope("os.installed-application", id.ToString("D"), revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Read);
        if (await resources.AuthorizeForActorAsync(expectedActor, "os.application.read", [scope], ct) != expectedActor)
            throw new UnauthorizedAccessException("The original installed application read is unavailable.");
        await RequireOriginalAsync(expectedActor, null, ct);
        var snapshot = await originalRead.ReadExistingForActorAsync(expectedActor, ct);
        var app = snapshot?.Applications.SingleOrDefault(item => item.ApplicationId == id && item.Revision == revision &&
            item.Enabled && item.ProfileAccessible);
        await RequireOriginalAsync(expectedActor, null, ct);
        if (app is null || app.HomeProfileId != expectedActor.ProfileId || app.ProviderId != "linux.xdg-desktop" ||
            await resources.AuthorizeForActorAsync(expectedActor, "os.application.read", [scope], ct) != expectedActor)
            throw new UnauthorizedAccessException("The original installed application changed during discovery.");
        await RequireOriginalAsync(expectedActor, null, ct);
        return app;
    }
    public async Task LaunchCurrentAsync(Guid id, CancellationToken ct)
    {
        var app = (await registry.RefreshAsync(ct)).SingleOrDefault(a => a.ApplicationId == id && a.Enabled && a.ProfileAccessible && a.ProviderId == "linux.xdg-desktop")
            ?? throw new IOException("This pinned application is unavailable for the current Home profile.");
        await LaunchAsync(app.ApplicationId, app.Revision, ct);
    }
    public async Task LaunchCurrentForActorAsync(Guid id, AuthenticatedResourceActor expectedActor,
        Func<CancellationToken, ValueTask<bool>> originalSelectionIsCurrent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(originalSelectionIsCurrent);
        if (registry is not IInstalledApplicationOriginalActorRegistry originalRegistry)
            throw new UnauthorizedAccessException("The installed owner cannot retain the original shell session.");
        await RequireOriginalAsync(expectedActor, originalSelectionIsCurrent, ct);
        var observed = await originalRegistry.RefreshForActorAsync(expectedActor, ct);
        await RequireOriginalAsync(expectedActor, originalSelectionIsCurrent, ct);
        var app = observed.SingleOrDefault(a => a.ApplicationId == id && a.Enabled && a.ProfileAccessible &&
            a.ProviderId == "linux.xdg-desktop" && a.HomeProfileId == expectedActor.ProfileId)
            ?? throw new IOException("This pinned application is unavailable for the original Home profile.");
        await LaunchCoreAsync(app.ApplicationId, app.Revision, expectedActor, ct, originalSelectionIsCurrent);
    }
    private async ValueTask RequireOriginalAsync(AuthenticatedResourceActor expectedActor,
        Func<CancellationToken, ValueTask<bool>>? originalSelectionIsCurrent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (actors is null || await actors.GetCurrentAsync(ct) != expectedActor)
            throw new UnauthorizedAccessException("The original shell actor is no longer current.");
        if (originalSelectionIsCurrent is not null && !await originalSelectionIsCurrent(ct))
            throw new UnauthorizedAccessException("The original displayed shortcut changed.");
        if (await actors.GetCurrentAsync(ct) != expectedActor)
            throw new UnauthorizedAccessException("The original shell actor changed during shortcut admission.");
    }
    public Task LaunchAsync(Guid id, long revision, CancellationToken ct) => LaunchCoreAsync(id, revision, null, ct);
    public Task LaunchForActorAsync(Guid id, long revision, AuthenticatedResourceActor expectedActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        return LaunchCoreAsync(id, revision, expectedActor, ct);
    }
    private async Task LaunchCoreAsync(Guid id, long revision, AuthenticatedResourceActor? expectedActor, CancellationToken ct,
        Func<CancellationToken, ValueTask<bool>>? originalSelectionIsCurrent = null)
    {
        IInstalledApplicationOriginalActorRegistry? originalRegistry = null;
        if (expectedActor is not null)
        {
            originalRegistry = registry as IInstalledApplicationOriginalActorRegistry
                ?? throw new UnauthorizedAccessException("The installed owner cannot retain the original launch session.");
            await RequireOriginalAsync(expectedActor, originalSelectionIsCurrent, ct);
        }
        var scope = new ResourceScope("os.installed-application", id.ToString("D"), revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Execute);
        var actor = expectedActor is null ? await resources.AuthorizeAsync("os.application.launch", [scope], ct)
            : await resources.AuthorizeForActorAsync(expectedActor, "os.application.launch", [scope], ct);
        if (actor is null || expectedActor is not null && actor != expectedActor)
            throw new UnauthorizedAccessException("The installed application is not authorized for the original Go actor.");
        var app = expectedActor is null ? await registry.ResolveLaunchAsync(id, revision, ct)
            : await originalRegistry!.ResolveLaunchForActorAsync(id, revision, expectedActor, ct);
        if (expectedActor is not null) await RequireOriginalAsync(expectedActor, originalSelectionIsCurrent, ct);
        if (app is null || app.HomeProfileId != actor.ProfileId || app.ProviderId != "linux.xdg-desktop") throw new IOException("Application changed; refresh Go before launching.");
        var inventory = await Task.Run(() => LinuxInstalledApplications.ReadInventory(ct), ct);
        if (expectedActor is not null) await RequireOriginalAsync(expectedActor, originalSelectionIsCurrent, ct);
        var desktop = inventory.SingleOrDefault(a => "desktop:" + a.DesktopId == app.Entrypoint && a.Digest == app.Version && a.Enabled);
        if (desktop is null) throw new IOException("Installed entrypoint changed; refresh Go before launching.");
        var finalActor = expectedActor is null ? await resources.AuthorizeAsync("os.application.launch", [scope], ct)
            : await resources.AuthorizeForActorAsync(expectedActor, "os.application.launch", [scope], ct);
        if (finalActor != actor) throw new UnauthorizedAccessException("Profile or installed application authority changed before launch.");
        if (expectedActor is not null) await RequireOriginalAsync(expectedActor, originalSelectionIsCurrent, ct);
        ct.ThrowIfCancellationRequested();
        // gio retains upstream desktop-entry argument expansion/DBus activation. Never interpret Exec through a shell.
        var start = new ProcessStartInfo("/usr/bin/gio") { UseShellExecute = false };
        start.ArgumentList.Add("launch"); start.ArgumentList.Add(desktop.Path);
        using var process = Process.Start(start) ?? throw new IOException("The desktop activation service could not start.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new IOException("The desktop activation service could not launch this application.");
    }
}
