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
        if (!((actionId == "os.application.launch" && scope.Access == ResourceAccess.Execute) || (actionId == "os.application.read" && scope.Access == ResourceAccess.Read)) || !Guid.TryParse(scope.Id, out var id) || !long.TryParse(scope.Revision, out var revision)) return denied;
        var app = await registry.ResolveLaunchAsync(id, revision, ct);
        return denied with { Allowed = app is not null && app.HomeProfileId == actor.ProfileId && app.ProviderId == "linux.xdg-desktop" && actor.OrganisationId is null, Code = "CurrentInstalledApplication" };
    }
}

public sealed class LinuxApplicationLauncher(IInstalledApplicationRegistry registry, ResourceAuthorizationService resources)
{
    public async Task<InstalledApplicationReference> ResolveForReadAsync(Guid id, long revision, CancellationToken ct)
    {
        var scope = new ResourceScope("os.installed-application", id.ToString("D"), revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Read);
        var actor = await resources.AuthorizeAsync("os.application.read", [scope], ct);
        if (actor is null) throw new UnauthorizedAccessException("This installed application is not visible to the current profile.");
        var app = await registry.ResolveLaunchAsync(id, revision, ct);
        if (app is null || app.HomeProfileId != actor.ProfileId || app.ProviderId != "linux.xdg-desktop" ||
            actor != await resources.AuthorizeAsync("os.application.read", [scope], ct))
            throw new UnauthorizedAccessException("The installed application or profile changed during discovery.");
        return app;
    }
    public async Task LaunchCurrentAsync(Guid id, CancellationToken ct)
    {
        var app = (await registry.RefreshAsync(ct)).SingleOrDefault(a => a.ApplicationId == id && a.Enabled && a.ProfileAccessible && a.ProviderId == "linux.xdg-desktop")
            ?? throw new IOException("This pinned application is unavailable for the current Home profile.");
        await LaunchAsync(app.ApplicationId, app.Revision, ct);
    }
    public async Task LaunchAsync(Guid id, long revision, CancellationToken ct)
    {
        var scope = new ResourceScope("os.installed-application", id.ToString("D"), revision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Execute);
        var actor = await resources.AuthorizeAsync("os.application.launch", [scope], ct);
        if (actor is null) throw new UnauthorizedAccessException("The installed application is no longer authorized for this profile.");
        var app = await registry.ResolveLaunchAsync(id, revision, ct);
        if (app is null || app.HomeProfileId != actor.ProfileId || app.ProviderId != "linux.xdg-desktop") throw new IOException("Application changed; refresh Go before launching.");
        var desktop = (await Task.Run(() => LinuxInstalledApplications.ReadInventory(ct), ct)).SingleOrDefault(a => "desktop:" + a.DesktopId == app.Entrypoint && a.Digest == app.Version && a.Enabled);
        if (desktop is null) throw new IOException("Installed entrypoint changed; refresh Go before launching.");
        if (await resources.AuthorizeAsync("os.application.launch", [scope], ct) != actor) throw new UnauthorizedAccessException("Profile or installed application authority changed before launch.");
        ct.ThrowIfCancellationRequested();
        // gio retains upstream desktop-entry argument expansion/DBus activation. Never interpret Exec through a shell.
        var start = new ProcessStartInfo("/usr/bin/gio") { UseShellExecute = false };
        start.ArgumentList.Add("launch"); start.ArgumentList.Add(desktop.Path);
        using var process = Process.Start(start) ?? throw new IOException("The desktop activation service could not start.");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new IOException("The desktop activation service could not launch this application.");
    }
}
