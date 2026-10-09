using System.Collections.Frozen;
using Haven.Application.Go;
using Haven.Core.Shelf;

namespace Haven.Application.Shelf;

public sealed record ShelfActivationResult(bool Requested, string Code);
public sealed record ShelfActivationResolution(bool Available, string Code, GoResult? Result, long LibraryRevision);

/// <summary>Invokes the actual host's canonical Go owner action. Completion means platform activation
/// was requested, never proof of a running process or authenticated installed Home IPC.</summary>
public sealed class ShelfInstalledApplicationActivation
{
    private readonly ShelfLibraryService _library;
    private readonly GoService _go;
    private readonly string _providerID;
    public GoScope Scope { get; }

    public ShelfInstalledApplicationActivation(ShelfLibraryService library, GoService go, string installedProviderID)
    {
        if (installedProviderID is not ("os.installed-applications" or "android.installed-applications"))
            throw new ArgumentException("Select the actual installed host provider.", nameof(installedProviderID));
        _library = library; _go = go; _providerID = installedProviderID;
        Scope = new(new[] { installedProviderID }.ToFrozenSet(StringComparer.Ordinal),
            new[] { "Home" }.ToFrozenSet(StringComparer.Ordinal), new[] { "os.installed-application" }.ToFrozenSet(StringComparer.Ordinal),
            new[] { "Open" }.ToFrozenSet(StringComparer.Ordinal));
    }

    public IAsyncEnumerable<GoUpdate> DiscoverAsync(string query, CancellationToken token = default) =>
        _go.QueryAsync(new(query, "Apps", 100, Scope), token);

    /// <summary>Resolves the retained identity through its actual owner after restart or rename.
    /// Missing targets remain in Shelf; text matching and cross-platform substitution are never used.</summary>
    public async Task<ShelfActivationResolution> ResolveAsync(Guid itemID, long expectedLibraryRevision, CancellationToken token = default)
    {
        var snapshot = await _library.ReadAsync(token).ConfigureAwait(false);
        if (snapshot.Library.Revision != expectedLibraryRevision) return new(false, "RevisionConflict", null, snapshot.Library.Revision);
        var item = snapshot.Library.Items.SingleOrDefault(item => item.Id == itemID);
        if (item is null || snapshot.ArchivedItemIds.Contains(itemID)) return new(false, "ItemUnavailable", null, snapshot.Library.Revision);
        var target = item.Target;
        if (target.Kind != ShelfTargetKind.InstalledApplication || target.ProviderId != _providerID || target.OwnerApp != "Home"
            || !Guid.TryParse(target.CanonicalId, out var id) || id == Guid.Empty)
            return new(false, "TargetMismatch", null, snapshot.Library.Revision);
        if (item.Behaviour is not (ShelfLaunchBehaviour.Open or ShelfLaunchBehaviour.Launch)
            || target.Arguments is { Count: > 0 } || !string.IsNullOrEmpty(target.WorkingDirectory))
            return new(false, "ActivationCapabilityUnavailable", null, snapshot.Library.Revision);
        var current = await _go.ResolveAsync(_providerID, new("Home", "os.installed-application", id.ToString("D")), Scope, token).ConfigureAwait(false);
        var refreshed = await _library.ReadAsync(token).ConfigureAwait(false);
        if (refreshed.Library.Revision != snapshot.Library.Revision) return new(false, "RevisionConflict", null, refreshed.Library.Revision);
        if (current is null || !current.Actions.Any(action => action.Id == "Open"))
            return new(false, "TargetUnavailable", null, refreshed.Library.Revision);
        return new(true, "Resolved", Capture(current), refreshed.Library.Revision);
    }

    public async Task<ShelfActivationResult> ActivateCurrentAsync(Guid itemID, long expectedLibraryRevision, CancellationToken token = default)
    {
        var resolved = await ResolveAsync(itemID, expectedLibraryRevision, token).ConfigureAwait(false);
        return resolved.Available
            ? await ActivateAsync(itemID, resolved.LibraryRevision, resolved.Result!, token).ConfigureAwait(false)
            : new(false, resolved.Code);
    }

    public ShelfLaunchItem CreateItem(GoResult originalResult)
    {
        var result = Capture(originalResult);
        return new(Guid.NewGuid(), result.Label, new(ShelfTargetKind.InstalledApplication, result.Reference.Id,
            OwnerApp: result.Reference.Owner, ProviderId: result.ProviderId));
    }

    public async Task<ShelfActivationResult> ActivateAsync(Guid itemID, long expectedLibraryRevision,
        GoResult originalResult, CancellationToken token = default)
    {
        var result = Capture(originalResult);
        var snapshot = await _library.ReadAsync(token).ConfigureAwait(false);
        if (snapshot.Library.Revision != expectedLibraryRevision) return new(false, "RevisionConflict");
        var item = snapshot.Library.Items.SingleOrDefault(item => item.Id == itemID);
        if (item is null || snapshot.ArchivedItemIds.Contains(itemID)) return new(false, "ItemUnavailable");
        var target = item.Target;
        if (target.Kind != ShelfTargetKind.InstalledApplication || target.ProviderId != _providerID || target.OwnerApp != "Home"
            || !Guid.TryParse(target.CanonicalId, out var id) || id != Guid.Parse(result.Reference.Id))
            return new(false, "TargetMismatch");
        if (item.Behaviour is not (ShelfLaunchBehaviour.Open or ShelfLaunchBehaviour.Launch)
            || target.Arguments is { Count: > 0 } || !string.IsNullOrEmpty(target.WorkingDirectory))
            return new(false, "ActivationCapabilityUnavailable");
        // Preserve the discovered reference/revision. The owner re-resolves it and authorizes the
        // current actor; a stored UUID, Shelf revision or caller-supplied result is never a grant.
        await _go.InvokeAsync(result, "Open", Scope, token).ConfigureAwait(false);
        return new(true, "ActivationRequested");
    }

    private GoResult Capture(GoResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ProviderId != _providerID || result.Reference is not { Owner: "Home", Kind: "os.installed-application" } reference
            || !Guid.TryParse(reference.Id, out var id) || id == Guid.Empty || string.IsNullOrWhiteSpace(reference.Revision)
            || reference.Revision.Length > 4096 || string.IsNullOrWhiteSpace(result.Label) || result.Label.Length > 4096
            || result.Actions is null || result.Actions.Count > 64 || !result.Actions.Any(action => action?.Id == "Open"))
            throw new ArgumentException("An original canonical installed-application result with Open is required.", nameof(result));
        return result with { Actions = Array.AsReadOnly(result.Actions.Where(action => action is not null).ToArray()) };
    }
}
