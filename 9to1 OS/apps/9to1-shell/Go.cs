using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Go;

namespace NineToOne.Os.Shell;

public sealed class InstalledApplicationsGoProvider(IInstalledApplicationRegistry registry, LinuxApplicationLauncher launcher) : IGoProvider
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
    public Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct)
    {
        if (reference.Owner != "Home" || reference.Kind != "os.installed-application" || actionId != "Open" || !Guid.TryParse(reference.Id, out var id) || !long.TryParse(reference.Revision, out var revision))
            throw new UnauthorizedAccessException("Unknown canonical application action.");
        return launcher.LaunchAsync(id, revision, ct);
    }
}
