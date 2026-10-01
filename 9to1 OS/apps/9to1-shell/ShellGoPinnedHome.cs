using Haven.Application.Go;

namespace NineToOne.Os.Shell;

/// <summary>Fresh canonical Go references for pins already owned by the current Home shell record.</summary>
public sealed class ShellGoPinnedHome(ShellConfigurationService configuration, GoService go)
{
    public async Task<IReadOnlyList<GoResult>> ReadAsync(ShellStoredConfiguration original, CancellationToken ct)
    {
        async Task<ShellConfigurationSnapshot> CurrentAsync()
        {
            if (original.SessionActor is null || !await configuration.IsCurrentSessionAsync(original, ct))
                throw new UnauthorizedAccessException("Reopen Go in the original Home session.");
            var current = await configuration.GetAsync(ct);
            if (current.Stored.SessionActor != original.SessionActor || current.Stored.AuthorityId != original.AuthorityId || current.Stored.Revision != original.Revision ||
                !await configuration.IsCurrentSessionAsync(original, ct))
                throw new UnauthorizedAccessException("The original Go home configuration changed.");
            return current;
        }
        var snapshot = await CurrentAsync();
        // Retain locators only. Labels, revisions and actions come from each actual owner resolution.
        var stored = snapshot.Stored.Current;
        var active = stored.ActiveSpace;
        var layer = active.Taskbar.Layers.Single(x => x.Id == active.Taskbar.ActiveLayerId);
        var locators = layer.Items.Select(x => x.Target)
            .Concat(DesktopPageEdits.Effective(stored).ActivePage.Items.Select(x => x.Target))
            .Where(x => x is { Owner: "Home", Kind: "os.installed-application" })
            .Select(x => new GoCanonicalLocator(x!.Owner, x.Kind, x.Id)).Distinct().Take(1024).ToArray();
        var results = new List<GoResult>();
        foreach (var locator in locators)
        {
            ct.ThrowIfCancellationRequested();
            await CurrentAsync();
            var result = await go.ResolveAsync("os.installed-applications", locator, ct: ct);
            await CurrentAsync();
            if (result is not null) results.Add(result);
        }
        await CurrentAsync();
        return results.AsReadOnly();
    }
}
