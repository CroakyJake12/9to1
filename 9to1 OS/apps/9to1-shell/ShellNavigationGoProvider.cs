using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Haven.Application.Go;
using Haven.Application;

namespace NineToOne.Os.Shell;

/// <summary>Live references into the current Home-owned shell record, never a second desktop index.</summary>
public sealed class ShellNavigationGoProvider(ShellConfigurationService configuration) : IGoOriginalActorInvocation, IGoOriginalActorQuery
{
    public const string Id = "os.shell-navigation";
    public string ProviderId => Id;
    private static string Stamp(ShellStoredConfiguration stored) => stored.AuthorityId + "@" + stored.Revision.ToString(CultureInfo.InvariantCulture) + ":" +
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored.SessionActor)));
    public IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, CancellationToken ct) => QueryCoreAsync(query, null, ct);
    public IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor expectedActor, CancellationToken ct)
    { ArgumentNullException.ThrowIfNull(expectedActor); return QueryCoreAsync(query, expectedActor, ct); }
    private async IAsyncEnumerable<GoResult> QueryCoreAsync(GoQuery query, AuthenticatedResourceActor? expectedActor, [EnumeratorCancellation] CancellationToken ct)
    {
        if (query.Category is not (null or "Desktop Spaces" or "Desktop Pages" or "Taskbar Layers")) yield break;
        var snapshot = expectedActor is null ? await configuration.GetAsync(ct) : await configuration.GetForActorAsync(expectedActor, ct);
        var config = snapshot.Stored.Current;
        IEnumerable<(string Kind, Guid Id, string Label, string Category)> Entries()
        {
            foreach (var space in config.Spaces)
            {
                yield return ("os.desktop-space", space.Id, space.Name, "Desktop Spaces");
                foreach (var layer in space.Taskbar.Layers)
                    yield return ("os.taskbar-layer", layer.Id, $"{space.Name} · {layer.Name}", "Taskbar Layers");
                if (space.DesktopSurface is { } local)
                    foreach (var page in local.Pages)
                        yield return ("os.desktop-page", page.Id, $"{space.Name} · {page.Name}", "Desktop Pages");
            }
            if (config.Spaces.Any(s => s.DesktopSurface is null))
                foreach (var page in config.GlobalDesktopSurface!.Pages)
                    yield return ("os.desktop-page", page.Id, $"Global desktop · {page.Name}", "Desktop Pages");
        }
        foreach (var entry in Entries())
        {
            ct.ThrowIfCancellationRequested();
            if (query.Category is { } category && category != entry.Category || !entry.Label.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
            // Recheck profile/revision before each visible result, including between asynchronous consumers.
            var current = expectedActor is null ? await configuration.GetAsync(ct) : await configuration.GetForOriginalAsync(snapshot.Stored, ct);
            if (Stamp(current.Stored) != Stamp(snapshot.Stored) || current.Stored.SessionActor != snapshot.Stored.SessionActor) throw new UnauthorizedAccessException("The current Home shell changed during discovery.");
            yield return new(Id, new("OS", entry.Kind, entry.Id.ToString("D"), Stamp(snapshot.Stored)), entry.Label, entry.Category,
                [new("Navigate", "Preview switch")]);
        }
    }
    public async Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct)
    {
        if (reference.Owner != "OS" || actionId != "Navigate" || !Guid.TryParse(reference.Id, out var id))
            throw new UnauthorizedAccessException("This shell navigation reference is invalid.");
        var snapshot = await configuration.GetAsync(ct);
        if (reference.Revision != Stamp(snapshot.Stored)) throw new ShellConfigurationConflictException();
        if (snapshot.Preview is not null) throw new InvalidOperationException("Keep or revert the current shell preview before switching through Go.");
        var candidate = Navigate(snapshot.Stored.Current, reference.Kind, id);
        await configuration.PreviewAsync(snapshot.Stored, candidate, TimeSpan.FromSeconds(30), ct);
    }
    public async Task InvokeForActorAsync(GoCanonicalReference reference, string actionId, AuthenticatedResourceActor expectedActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (reference.Owner != "OS" || actionId != "Navigate" || !Guid.TryParse(reference.Id, out var id))
            throw new UnauthorizedAccessException("This shell navigation reference is invalid.");
        var snapshot = await configuration.GetForActorAsync(expectedActor, ct);
        if (snapshot.Stored.SessionActor != expectedActor || !await configuration.IsCurrentSessionAsync(snapshot.Stored, ct))
            throw new UnauthorizedAccessException("The original Go actor changed before navigation.");
        if (reference.Revision != Stamp(snapshot.Stored)) throw new ShellConfigurationConflictException();
        if (snapshot.Preview is not null) throw new InvalidOperationException("Keep or revert the current shell preview before switching through Go.");
        var candidate = Navigate(snapshot.Stored.Current, reference.Kind, id);
        await configuration.PreviewAsync(snapshot.Stored, candidate, TimeSpan.FromSeconds(30), ct);
    }
    private static ShellConfiguration Navigate(ShellConfiguration config, string kind, Guid id)
    {
        if (kind == "os.desktop-space" && config.Spaces.Any(s => s.Id == id)) return config with { ActiveSpaceId = id };
        if (kind == "os.taskbar-layer")
        {
            var owner = config.Spaces.SingleOrDefault(s => s.Taskbar.Layers.Any(l => l.Id == id));
            if (owner is not null) return config with { ActiveSpaceId = owner.Id, Spaces = config.Spaces.Select(s => s.Id == owner.Id ? s with { Taskbar = s.Taskbar with { ActiveLayerId = id } } : s).ToArray() };
        }
        if (kind == "os.desktop-page")
        {
            var owner = config.Spaces.SingleOrDefault(s => s.DesktopSurface?.Pages.Any(p => p.Id == id) == true);
            if (owner is null && config.GlobalDesktopSurface!.Pages.Any(p => p.Id == id))
                owner = config.ActiveSpace.DesktopSurface is null ? config.ActiveSpace : config.Spaces.FirstOrDefault(s => s.DesktopSurface is null);
            if (owner is not null) return DesktopPageEdits.SelectPage(config with { ActiveSpaceId = owner.Id }, id);
        }
        throw new InvalidOperationException("This canonical desktop target is no longer available.");
    }
}

public sealed record ShellGoAction(GoResult Result, GoAction Action);
