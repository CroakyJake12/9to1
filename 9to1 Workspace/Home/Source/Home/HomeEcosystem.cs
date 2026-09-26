using System.Collections.ObjectModel;

namespace HavenOS.Home;

public enum HomeRoute { Dashboard, Apps, Library, Events, Discover, Mesh, Settings, Permissions, Notifications, Spaces }

/// <summary>Process-local navigation state. Destinations retain their own query and selection while Home lives.</summary>
public sealed class HomeNavigationState
{
    private readonly Dictionary<HomeRoute, HomeDestinationState> _destinations = new();
    public HomeRoute Current { get; private set; } = HomeRoute.Dashboard;
    public HomeDestinationState State(HomeRoute route) => _destinations.TryGetValue(route, out var state)
        ? state : _destinations[route] = new HomeDestinationState();
    public void Navigate(HomeRoute route) => Current = route;
    public bool OpenDeepLink(HomeDeepLink link, IHomeDeepLinkRouter router)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(router);
        if (!router.CanOpen(link)) return false;
        Current = link.Destination;
        State(link.Destination).SelectedObjectId = link.ObjectId;
        return router.Open(link);
    }
}

public sealed class HomeDestinationState
{
    public string Query { get; set; } = string.Empty;
    public string? Filter { get; set; }
    public string? SelectedObjectId { get; set; }
    public int ScrollOffset { get; set; }
}

public sealed record HomeDeepLink(HomeRoute Destination, string OwnerApp, string ObjectId, string? Action = null);
public interface IHomeDeepLinkRouter { bool CanOpen(HomeDeepLink link); bool Open(HomeDeepLink link); }

public enum HomeTileSize { Small, Medium, Large, Wide }
public enum HomeTileVisibility { Visible, Hidden }
public enum HomeTileLifetime { Session, Until, Persistent }
public enum HomeTileChangeKind { Manual, Ai }
public sealed record HomeTileAction(string ActionName, string OwnerApp, string? ObjectId, string RiskLevel, bool Reversible, bool HasExternalSideEffects);
public sealed record HomeTileProviderDescriptor(
    int ContractVersion, string TileType, string SourceApp, IReadOnlySet<HomeTileSize> SupportedSizes,
    IReadOnlyList<string> RequiredPermissions, TimeSpan RefreshInterval, IReadOnlyList<HomeTileAction> Actions,
    bool CanInstantiateManually, bool AllowsGeneratedContent);
public sealed record HomeTileInstance(
    string TileInstanceId, string TileType, string ProviderId, int Order, HomeTileSize Size,
    HomeTileVisibility Visibility, bool Pinned, bool Locked, IReadOnlyDictionary<string, string> Configuration,
    string? Provenance, IReadOnlyList<string> SourceEntityIds, HomeTileLifetime Lifetime, DateTimeOffset? ExpiresAt = null);
public sealed record HomeDashboardLayout(int SchemaVersion, long Revision, IReadOnlyList<HomeTileInstance> Tiles,
    bool AllowAiGeneratedTiles, bool AllowAiReorder, string? ParentRevision = null, HomeTileChangeKind ChangeKind = HomeTileChangeKind.Manual);
public sealed record HomeLayoutResult(bool Succeeded, string Code, string Message, HomeDashboardLayout Layout,
    bool Recoverable = true);

public interface IHomeDashboardLayoutStore
{
    Task<HomeDashboardLayout?> LoadAsync(CancellationToken cancellationToken);
    /// <summary>Must compare expected revision atomically; conflicting revisions are retained by the store.</summary>
    Task<bool> TrySaveAsync(long expectedRevision, HomeDashboardLayout layout, CancellationToken cancellationToken);
}

/// <summary>Local versioned layout store. Conflicting writes are retained for later merge/recovery.</summary>
public sealed class HomeJsonDashboardLayoutStore(string filePath) : IHomeDashboardLayoutStore, IHomeDashboardLayoutHistory
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath = Path.GetFullPath(filePath ?? throw new ArgumentNullException(nameof(filePath)));
    private string ConflictFolder => Path.Combine(Path.GetDirectoryName(_filePath)!, Path.GetFileNameWithoutExtension(_filePath) + ".conflicts");

    public async Task<HomeDashboardLayout?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return null;
        await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<HomeDashboardLayout>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Dashboard layout file is empty or invalid.");
    }

    public async Task<bool> TrySaveAsync(long expectedRevision, HomeDashboardLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using var gate = await AcquireGateAsync(cancellationToken).ConfigureAwait(false);
        var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if ((current?.Revision ?? 0) != expectedRevision)
        {
            Directory.CreateDirectory(ConflictFolder);
            var conflict = Path.Combine(ConflictFolder, $"revision-{layout.Revision}-{Guid.NewGuid():N}.json");
            await WriteAtomicAsync(conflict, layout, cancellationToken).ConfigureAwait(false);
            return false;
        }
        if (current is not null)
        {
            Directory.CreateDirectory(ConflictFolder);
            var previous = Path.Combine(ConflictFolder, $"revision-{current.Revision}-{Guid.NewGuid():N}.json");
            await WriteAtomicAsync(previous, current, cancellationToken).ConfigureAwait(false);
        }
        await WriteAtomicAsync(_filePath, layout, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<HomeDashboardLayout?> GetRevisionAsync(long revision, CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (current?.Revision == revision) return current;
        if (!Directory.Exists(ConflictFolder)) return null;
        foreach (var path in Directory.EnumerateFiles(ConflictFolder, $"revision-{revision}-*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var candidate = await System.Text.Json.JsonSerializer.DeserializeAsync<HomeDashboardLayout>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (candidate?.Revision == revision) return candidate;
        }
        return null;
    }

    private async Task<FileStream> AcquireGateAsync(CancellationToken cancellationToken)
    {
        var gatePath = _filePath + ".lock";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(gatePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) { await Task.Delay(20, cancellationToken).ConfigureAwait(false); }
        }
    }

    private static async Task WriteAtomicAsync(string path, HomeDashboardLayout value, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await System.Text.Json.JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public interface IHomeTileProvider
{
    HomeTileProviderDescriptor Descriptor { get; }
    Task<HomeTileContent> RefreshAsync(HomeTileInstance tile, CancellationToken cancellationToken);
}
public sealed record HomeTileContent(string State, string Summary, IReadOnlyList<string> SourceEntityIds,
    IReadOnlyDictionary<string, string> Values, IReadOnlyList<HomeTileAction> Actions, string? ErrorCode = null);
public interface IHomeTileProviderRegistry { IReadOnlyList<IHomeTileProvider> GetProviders(); IHomeTileProvider? Find(string providerId); }
public interface IHomeTilePermissionGate
{
    Task<bool> CanReadProviderAsync(string providerId, IReadOnlyList<string> permissions, CancellationToken cancellationToken);
}
public interface IHomeDashboardLayoutHistory { Task<HomeDashboardLayout?> GetRevisionAsync(long revision, CancellationToken cancellationToken = default); }
public interface IHomeDashboardAuditSink { Task RecordLayoutChangeAsync(HomeDashboardLayout before, HomeDashboardLayout after, string actor, CancellationToken cancellationToken); }

/// <summary>One domain surface for CUI and automation callers. Mutations use optimistic revision checks.</summary>
public sealed class HomeDashboardLayoutService(IHomeDashboardLayoutStore store, IHomeTileProviderRegistry providers,
    IHomeDashboardAuditSink? audit = null, IHomeDashboardLayoutHistory? history = null, IHomeTilePermissionGate? permissionGate = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IHomeDashboardLayoutHistory? _history = history ?? store as IHomeDashboardLayoutHistory;
    public async Task<HomeDashboardLayout> GetAsync(CancellationToken cancellationToken = default)
    {
        var layout = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return Validate(layout ?? new HomeDashboardLayout(1, 0, [], false, false));
    }
    public IReadOnlyList<HomeTileProviderDescriptor> ListProviders() => providers.GetProviders().Select(x => x.Descriptor).ToArray();
    public async Task<HomeTileContent> RefreshTileAsync(string tileInstanceId, CancellationToken ct = default)
    {
        var layout = await GetAsync(ct).ConfigureAwait(false);
        var tile = layout.Tiles.FirstOrDefault(x => x.TileInstanceId == tileInstanceId);
        if (tile is null) return new("unavailable", "This tile is no longer available.", [], new Dictionary<string, string>(), [], "TileNotFound");
        var provider = providers.Find(tile.ProviderId);
        if (provider is null || provider.Descriptor.ContractVersion != 1 || provider.Descriptor.TileType != tile.TileType)
            return new("unavailable", "The tile provider is unavailable or incompatible.", tile.SourceEntityIds, new Dictionary<string, string>(), [], "ProviderUnavailable");
        if (provider.Descriptor.RequiredPermissions.Count > 0 &&
            (permissionGate is null || !await permissionGate.CanReadProviderAsync(tile.ProviderId, provider.Descriptor.RequiredPermissions, ct).ConfigureAwait(false)))
            return new("stale", "Access to this tile's source is unavailable.", [], new Dictionary<string, string>(), [], "PermissionRevoked");
        try
        {
            var content = await provider.RefreshAsync(tile, ct).ConfigureAwait(false);
            if (content.SourceEntityIds.Count == 0 || content.Actions.Any(action => !provider.Descriptor.Actions.Contains(action)))
                return new("unavailable", "The provider returned content without valid provenance or declared actions.", [], new Dictionary<string, string>(), [], "InvalidProviderResponse");
            return content;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return new("stale", "The tile source could not be refreshed; its previous content may be stale.", tile.SourceEntityIds,
                new Dictionary<string, string>(), [], "ProviderRefreshFailed");
        }
    }
    public Task<HomeLayoutResult> AddAsync(long revision, string providerId, string tileId, CancellationToken ct = default) =>
        MutateAsync(revision, layout =>
        {
            var provider = providers.Find(providerId);
            if (provider is null || !provider.Descriptor.CanInstantiateManually) return (layout, "ProviderUnavailable", "This tile provider is unavailable.");
            if (layout.Tiles.Any(x => x.TileInstanceId == tileId)) return (layout, "DuplicateTileId", "TileInstanceID is already present.");
            var descriptor = provider.Descriptor;
            var size = descriptor.SupportedSizes.OrderBy(x => x).FirstOrDefault();
            return (layout with { Tiles = layout.Tiles.Append(new HomeTileInstance(tileId, descriptor.TileType, providerId,
                layout.Tiles.Count == 0 ? 0 : layout.Tiles.Max(x => x.Order) + 1, size, HomeTileVisibility.Visible,
                false, false, new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()), null, [], HomeTileLifetime.Persistent)).ToArray() }, "", "");
        }, HomeTileChangeKind.Manual, "user", ct);
    public Task<HomeLayoutResult> RemoveAsync(long revision, string tileId, CancellationToken ct = default) =>
        MutateAsync(revision, layout => (layout with { Tiles = layout.Tiles.Where(x => x.TileInstanceId != tileId).ToArray() }, "", ""), HomeTileChangeKind.Manual, "user", ct);
    public Task<HomeLayoutResult> SetVisibilityAsync(long revision, string tileId, HomeTileVisibility visibility, CancellationToken ct = default) =>
        UpdateTileAsync(revision, tileId, tile => tile with { Visibility = visibility }, HomeTileChangeKind.Manual, ct);
    public Task<HomeLayoutResult> SetPinnedAsync(long revision, string tileId, bool pinned, CancellationToken ct = default) =>
        UpdateTileAsync(revision, tileId, tile => tile with { Pinned = pinned }, HomeTileChangeKind.Manual, ct);
    public Task<HomeLayoutResult> SetLockedAsync(long revision, string tileId, bool locked, CancellationToken ct = default) =>
        UpdateTileAsync(revision, tileId, tile => tile with { Locked = locked }, HomeTileChangeKind.Manual, ct);
    public Task<HomeLayoutResult> ResizeAsync(long revision, string tileId, HomeTileSize size, CancellationToken ct = default) =>
        UpdateTileAsync(revision, tileId, tile =>
        {
            var provider = providers.Find(tile.ProviderId);
            if (provider is null || !provider.Descriptor.SupportedSizes.Contains(size)) throw new HomeContractException("UnsupportedTileSize", "The provider does not support this tile size.");
            return tile with { Size = size };
        }, HomeTileChangeKind.Manual, ct);
    public Task<HomeLayoutResult> ConfigureTileAsync(long revision, string tileId, IReadOnlyDictionary<string, string> configuration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var copy = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(configuration, StringComparer.Ordinal));
        return UpdateTileAsync(revision, tileId, tile => tile with { Configuration = copy }, HomeTileChangeKind.Manual, ct);
    }
    public Task<HomeLayoutResult> ReorderAsync(long revision, IReadOnlyList<string> orderedIds, CancellationToken ct = default) =>
        MutateAsync(revision, layout =>
        {
            if (orderedIds.Count != layout.Tiles.Count || orderedIds.Distinct(StringComparer.Ordinal).Count() != layout.Tiles.Count ||
                !orderedIds.ToHashSet(StringComparer.Ordinal).SetEquals(layout.Tiles.Select(x => x.TileInstanceId)))
                return (layout, "InvalidOrder", "The order must include every tile exactly once.");
            var map = orderedIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
            return (layout with { Tiles = layout.Tiles.Select(x => x with { Order = map[x.TileInstanceId] }).OrderBy(x => x.Order).ToArray() }, "", "");
        }, HomeTileChangeKind.Manual, "user", ct);
    public Task<HomeLayoutResult> SetAiControlsAsync(long revision, bool allowGeneratedTiles, bool allowReorder, CancellationToken ct = default) =>
        MutateAsync(revision, layout => (layout with { AllowAiGeneratedTiles = allowGeneratedTiles, AllowAiReorder = allowReorder }, "", ""), HomeTileChangeKind.Manual, "user", ct);
    public Task<HomeLayoutResult> ResetAsync(long revision, CancellationToken ct = default) =>
        MutateAsync(revision, layout => (layout with { Tiles = [], AllowAiGeneratedTiles = false, AllowAiReorder = false }, "", ""), HomeTileChangeKind.Manual, "user", ct);
    public async Task<HomeLayoutResult> UndoLastAiChangeAsync(long revision, CancellationToken ct = default)
    {
        var current = await GetAsync(ct).ConfigureAwait(false);
        if (current.Revision != revision) return new(false, "RevisionConflict", "Dashboard changed; refresh before retrying.", current);
        if (current.ChangeKind != HomeTileChangeKind.Ai || !long.TryParse(current.ParentRevision, out var parent) || _history is null)
            return new(false, "NoAiChangeToUndo", "There is no recoverable AI layout change to undo.", current);
        var previous = await _history.GetRevisionAsync(parent, ct).ConfigureAwait(false);
        if (previous is null) return new(false, "RevisionUnavailable", "The prior layout revision is unavailable.", current);
        return await MutateAsync(revision, layout => (layout with { Tiles = previous.Tiles,
            AllowAiGeneratedTiles = previous.AllowAiGeneratedTiles, AllowAiReorder = previous.AllowAiReorder }, "", ""),
            HomeTileChangeKind.Manual, "user", ct).ConfigureAwait(false);
    }
    public Task<HomeLayoutResult> ApplyAiLayoutAsync(long revision, IReadOnlyList<HomeTileInstance> changes, CancellationToken ct = default) =>
        MutateAsync(revision, layout =>
        {
            if (changes.Select(x => x.TileInstanceId).Distinct(StringComparer.Ordinal).Count() != changes.Count)
                return (layout, "DuplicateTileId", "An AI layout update cannot include duplicate TileInstanceID values.");
            if (!layout.AllowAiReorder && changes.Any(x => layout.Tiles.Any(y => y.TileInstanceId == x.TileInstanceId)))
                return (layout, "AiReorderDisabled", "AI layout changes are disabled.");
            if (!layout.AllowAiGeneratedTiles && changes.Any(x => layout.Tiles.All(y => y.TileInstanceId != x.TileInstanceId)))
                return (layout, "AiTileCreationDisabled", "AI tile creation is disabled.");
            foreach (var item in changes)
            {
                var provider = providers.Find(item.ProviderId);
                if (provider is null || !provider.Descriptor.SupportedSizes.Contains(item.Size) || string.IsNullOrWhiteSpace(item.Provenance) || item.SourceEntityIds.Count == 0)
                    return (layout, "InvalidGeneratedTile", "Generated tiles require a registered provider, supported size, provenance and source entities.");
                var old = layout.Tiles.FirstOrDefault(x => x.TileInstanceId == item.TileInstanceId);
                if (old is not null && (old.Pinned || old.Locked || old.Visibility == HomeTileVisibility.Hidden) && !SameLayout(old, item))
                    return (layout, "TileProtected", "AI cannot move pinned, locked or explicitly hidden tiles.");
            }
            var all = layout.Tiles.ToDictionary(x => x.TileInstanceId, StringComparer.Ordinal);
            foreach (var item in changes) all[item.TileInstanceId] = item;
            return (layout with { Tiles = all.Values.OrderBy(x => x.Order).ToArray() }, "", "");
        }, HomeTileChangeKind.Ai, "dulche", ct);
    private static bool SameLayout(HomeTileInstance left, HomeTileInstance right) => left.TileInstanceId == right.TileInstanceId &&
        left.TileType == right.TileType && left.ProviderId == right.ProviderId && left.Order == right.Order && left.Size == right.Size &&
        left.Visibility == right.Visibility && left.Pinned == right.Pinned && left.Locked == right.Locked &&
        left.Configuration.OrderBy(x => x.Key).SequenceEqual(right.Configuration.OrderBy(x => x.Key));
    private Task<HomeLayoutResult> UpdateTileAsync(long revision, string id, Func<HomeTileInstance, HomeTileInstance> update, HomeTileChangeKind kind, CancellationToken ct) =>
        MutateAsync(revision, layout =>
        {
            var exists = layout.Tiles.Any(x => x.TileInstanceId == id);
            if (!exists) return (layout, "TileNotFound", "The requested tile no longer exists.");
            try { return (layout with { Tiles = layout.Tiles.Select(x => x.TileInstanceId == id ? update(x) : x).ToArray() }, "", ""); }
            catch (HomeContractException e) { return (layout, e.Code, e.Message); }
        }, kind, "user", ct);
    private async Task<HomeLayoutResult> MutateAsync(long revision, Func<HomeDashboardLayout, (HomeDashboardLayout Layout, string Code, string Message)> edit,
        HomeTileChangeKind kind, string actor, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var before = await GetAsync(ct).ConfigureAwait(false);
            if (revision != before.Revision) return new(false, "RevisionConflict", "Dashboard changed; refresh before retrying.", before);
            if (kind == HomeTileChangeKind.Ai && audit is null)
                return new(false, "AuditUnavailable", "AI layout changes require the Home audit service.", before);
            var (next, code, message) = edit(before);
            if (code.Length > 0) return new(false, code, message, before);
            next = Validate(next with { SchemaVersion = 1, Revision = before.Revision + 1, ParentRevision = before.Revision.ToString(), ChangeKind = kind });
            if (!await store.TrySaveAsync(before.Revision, next, ct).ConfigureAwait(false)) return new(false, "RevisionConflict", "Dashboard changed concurrently; both revisions must be retained.", await GetAsync(ct).ConfigureAwait(false));
            if (kind == HomeTileChangeKind.Ai)
            {
                try { await audit!.RecordLayoutChangeAsync(before, next, actor, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    var rollback = before with { Revision = next.Revision + 1, ParentRevision = next.Revision.ToString(), ChangeKind = HomeTileChangeKind.Manual };
                    if (await store.TrySaveAsync(next.Revision, rollback, ct).ConfigureAwait(false))
                        return new(false, "AuditFailedRolledBack", "AI layout change was reverted because its audit record could not be saved.", rollback);
                    return new(false, "AuditFailedRollbackConflict", "Audit recording and automatic rollback both failed; inspect preserved layout revisions.", await GetAsync(ct).ConfigureAwait(false));
                }
            }
            return new(true, "Succeeded", "Dashboard layout saved.", next);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new(false, "LayoutOperationFailed", e.Message, await GetAsync(ct).ConfigureAwait(false));
        }
        finally { _gate.Release(); }
    }
    private static HomeDashboardLayout Validate(HomeDashboardLayout layout)
    {
        if (layout.SchemaVersion != 1) throw new InvalidDataException($"Unsupported dashboard layout schema version {layout.SchemaVersion}.");
        if (layout.Revision < 0 || layout.Tiles.Any(x => string.IsNullOrWhiteSpace(x.TileInstanceId) || string.IsNullOrWhiteSpace(x.TileType) || string.IsNullOrWhiteSpace(x.ProviderId)))
            throw new InvalidDataException("Dashboard layout contains an invalid revision or tile identity.");
        if (layout.Tiles.Select(x => x.TileInstanceId).Distinct(StringComparer.Ordinal).Count() != layout.Tiles.Count)
            throw new InvalidDataException("Dashboard layout contains duplicate TileInstanceID values.");
        return layout with { Tiles = layout.Tiles.OrderBy(x => x.Order).ToArray() };
    }
}
public sealed class HomeContractException(string code, string message) : Exception(message) { public string Code { get; } = code; }

public sealed record HomeArtifactReference(string OwnerApp, string ArtifactId, string DisplayName, string ArtifactType,
    string? Location, string? Owner, string? SharedBy, DateTimeOffset LastModified, bool IsRecent, bool IsPinned,
    bool IsShared, bool IsGenerated, IReadOnlyList<string> Tags, HomeDeepLink OpenLink);
public sealed record HomeLibraryQuery(string? Text = null, string? OwnerApp = null, string? ArtifactType = null,
    bool? Pinned = null, bool? Shared = null, bool? Generated = null, string Sort = "recent", int Offset = 0, int Limit = 50);
public sealed record HomeLibraryPage(IReadOnlyList<HomeArtifactReference> Items, int Offset, int Limit, bool HasMore);
public sealed record HomeDomainResult<T>(bool Succeeded, string Code, string Message, T? Value = default, bool Recoverable = true);
public interface IHomeArtifactSearchIndex
{
    Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken cancellationToken);
    Task<HomeArtifactReference?> GetByIdAsync(string ownerApp, string artifactId, CancellationToken cancellationToken);
}
public interface IHomeArtifactAuthorization
{
    Task<bool> CanReadAsync(string ownerApp, string artifactId, CancellationToken cancellationToken);
    Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken cancellationToken);
}
public interface IHomeLibraryPreferences { Task<bool> IsPinnedAsync(string ownerApp, string artifactId, CancellationToken cancellationToken); Task SetPinnedAsync(string ownerApp, string artifactId, bool pinned, CancellationToken cancellationToken); }
public sealed record HomeLibraryPreferenceDocument(int SchemaVersion, string[] PinnedArtifactKeys);
public sealed class HomeJsonLibraryPreferences(string filePath) : IHomeLibraryPreferences
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath = Path.GetFullPath(filePath ?? throw new ArgumentNullException(nameof(filePath)));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private async Task<HashSet<string>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath)) return new(StringComparer.Ordinal);
        await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await System.Text.Json.JsonSerializer.DeserializeAsync<HomeLibraryPreferenceDocument>(stream, JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Library pin state is empty or invalid.");
        if (document.SchemaVersion != 1) throw new InvalidDataException($"Unsupported Library preference schema version {document.SchemaVersion}.");
        return new HashSet<string>(document.PinnedArtifactKeys, StringComparer.Ordinal);
    }
    public async Task<bool> IsPinnedAsync(string ownerApp, string artifactId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Contains(Key(ownerApp, artifactId)); }
        finally { _gate.Release(); }
    }
    public async Task SetPinnedAsync(string ownerApp, string artifactId, bool pinned, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var values = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var key = Key(ownerApp, artifactId);
            if (pinned) values.Add(key); else values.Remove(key);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await System.Text.Json.JsonSerializer.SerializeAsync(stream,
                        new HomeLibraryPreferenceDocument(1, values.Order(StringComparer.Ordinal).ToArray()), JsonOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporary, _filePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
    }
    private static string Key(string ownerApp, string artifactId)
    {
        if (string.IsNullOrWhiteSpace(ownerApp) || string.IsNullOrWhiteSpace(artifactId)) throw new ArgumentException("Owner app and stable artifact ID are required.");
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ownerApp + "\0" + artifactId));
    }
}
public sealed class HomeLibraryService(IHomeArtifactSearchIndex index, IHomeArtifactAuthorization authorization,
    IHomeDeepLinkRouter router, IHomeLibraryPreferences preferences)
{
    public async Task<HomeLibraryPage> SearchAsync(HomeLibraryQuery query, CancellationToken ct = default)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(query));
        var all = await index.SearchAsync(query, ct).ConfigureAwait(false);
        var unique = all.Where(x => !string.IsNullOrWhiteSpace(x.OwnerApp) && !string.IsNullOrWhiteSpace(x.ArtifactId) &&
                !string.IsNullOrWhiteSpace(x.DisplayName) && !string.IsNullOrWhiteSpace(x.ArtifactType) &&
                x.OpenLink.OwnerApp == x.OwnerApp && x.OpenLink.ObjectId == x.ArtifactId)
            .GroupBy(x => (x.OwnerApp, x.ArtifactId)).Select(g => g.OrderByDescending(x => x.LastModified).First())
            .Where(x => (query.OwnerApp is null || x.OwnerApp == query.OwnerApp) && (query.ArtifactType is null || x.ArtifactType == query.ArtifactType));
        var permitted = new List<HomeArtifactReference>();
        foreach (var item in unique)
            if (await authorization.CanReadAsync(item.OwnerApp, item.ArtifactId, ct).ConfigureAwait(false))
                permitted.Add(item with { IsPinned = await preferences.IsPinnedAsync(item.OwnerApp, item.ArtifactId, ct).ConfigureAwait(false) });
        var filtered = permitted.Where(x => (query.Pinned is null || x.IsPinned == query.Pinned) &&
            (query.Shared is null || x.IsShared == query.Shared) && (query.Generated is null || x.IsGenerated == query.Generated));
        var ordered = query.Sort switch
        {
            "name" => filtered.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "type" => filtered.OrderBy(x => x.ArtifactType, StringComparer.Ordinal).ThenByDescending(x => x.LastModified),
            "oldest" => filtered.OrderBy(x => x.LastModified),
            "recent" => filtered.OrderByDescending(x => x.LastModified),
            _ => throw new ArgumentException("Unsupported Library sort order.", nameof(query)),
        };
        var materialized = ordered.ToArray();
        var page = materialized.Skip(query.Offset).Take(query.Limit).ToArray();
        return new(page, query.Offset, query.Limit, query.Offset + page.Length < materialized.Length);
    }
    public async Task<HomeDomainResult<HomeDeepLink>> OpenAsync(string ownerApp, string artifactId, CancellationToken ct = default)
    {
        var item = await index.GetByIdAsync(ownerApp, artifactId, ct).ConfigureAwait(false);
        if (item is null || item.OwnerApp != ownerApp || item.ArtifactId != artifactId ||
            !await authorization.CanReadAsync(ownerApp, artifactId, ct).ConfigureAwait(false) ||
            !await authorization.CanOpenAsync(item.OpenLink, ct).ConfigureAwait(false))
            return new(false, "ArtifactUnavailable", "This artifact is unavailable or access was revoked.");
        if (!router.CanOpen(item.OpenLink) || !router.Open(item.OpenLink)) return new(false, "OwnerUnavailable", "The owning app could not open this artifact.");
        return new(true, "Succeeded", "Opened in the owning app.", item.OpenLink);
    }
    public async Task<HomeDomainResult<bool>> SetPinnedAsync(string ownerApp, string artifactId, bool pinned, CancellationToken ct = default)
    {
        if (!await authorization.CanReadAsync(ownerApp, artifactId, ct).ConfigureAwait(false)) return new(false, "ArtifactUnavailable", "This artifact is unavailable or access was revoked.");
        await preferences.SetPinnedAsync(ownerApp, artifactId, pinned, ct).ConfigureAwait(false);
        return new(true, "Succeeded", pinned ? "Artifact pinned." : "Artifact unpinned.", pinned);
    }
}

public sealed record HomeActivityEvent(string EventId, DateTimeOffset Timestamp, string SourceApp, string EventType,
    string? ActorIdentity, string? ActorDisplayName, IReadOnlyList<HomeDeepLink> Objects, string Severity,
    string Category, string Summary, string? RequestId = null, string? ActionGraphId = null);
public sealed record HomeEventsQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, string? SourceApp = null,
    string? Category = null, string? Severity = null, int Offset = 0, int Limit = 50);
public sealed record HomeEventsPage(IReadOnlyList<HomeActivityEvent> Items, int Offset, int Limit, bool HasMore);
public interface IHomeActivitySource
{
    string SourceId { get; }
    Task<IReadOnlyList<HomeActivityEvent>> ReadAsync(HomeEventsQuery query, CancellationToken cancellationToken);
    Task<HomeActivityEvent?> GetByIdAsync(string eventId, CancellationToken cancellationToken);
}
public interface IHomeActivityAuthorization { Task<bool> CanReadAsync(HomeActivityEvent activity, CancellationToken cancellationToken); }
public interface IHomeEventDigest { Task<string?> SummarizeAsync(IReadOnlyList<HomeActivityEvent> sourceEvents, CancellationToken cancellationToken); }
public sealed class HomeEventsService(IEnumerable<IHomeActivitySource> sources, IHomeActivityAuthorization authorization)
{
    public async Task<HomeEventsPage> ListAsync(HomeEventsQuery query, CancellationToken ct = default)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(query));
        var read = await Task.WhenAll(sources.Select(async source => (Source: source.SourceId, Events: await source.ReadAsync(query, ct).ConfigureAwait(false))))
            .ConfigureAwait(false);
        var records = read.SelectMany(x => x.Events.Select(e => (x.Source, Event: e)))
            .Where(x => x.Event.SourceApp == x.Source && !string.IsNullOrWhiteSpace(x.Event.EventId) &&
                !string.IsNullOrWhiteSpace(x.Event.EventType) && !string.IsNullOrWhiteSpace(x.Event.Summary) &&
                !string.IsNullOrWhiteSpace(x.Event.Severity) && !string.IsNullOrWhiteSpace(x.Event.Category) &&
                x.Event.Timestamp != default)
            .GroupBy(x => x.Event.EventId, StringComparer.Ordinal).Select(x => x.First().Event);
        var permitted = new List<HomeActivityEvent>();
        foreach (var record in records)
            if (await authorization.CanReadAsync(record, ct).ConfigureAwait(false)) permitted.Add(record);
        var filtered = permitted.Where(e => (query.From is null || e.Timestamp >= query.From) && (query.To is null || e.Timestamp <= query.To) &&
            (query.SourceApp is null || e.SourceApp == query.SourceApp) && (query.Category is null || e.Category == query.Category) &&
            (query.Severity is null || e.Severity == query.Severity)).OrderByDescending(e => e.Timestamp).ToArray();
        var page = filtered.Skip(query.Offset).Take(query.Limit).ToArray();
        return new(page, query.Offset, query.Limit, query.Offset + page.Length < filtered.Length);
    }
    public async Task<string?> DigestAsync(HomeEventsQuery query, IHomeEventDigest digest, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(digest);
        var page = await ListAsync(query with { Offset = 0, Limit = 200 }, ct).ConfigureAwait(false);
        return await digest.SummarizeAsync(page.Items, ct).ConfigureAwait(false);
    }

    public async Task<HomeDomainResult<HomeDeepLink>> OpenObjectAsync(string eventId, int objectIndex, IHomeDeepLinkRouter router, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId) || objectIndex < 0) return new(false, "InvalidEventReference", "A valid event and object reference are required.");
        ArgumentNullException.ThrowIfNull(router);
        HomeActivityEvent? activity = null;
        foreach (var source in sources)
        {
            var candidate = await source.GetByIdAsync(eventId, ct).ConfigureAwait(false);
            if (candidate is not null && candidate.SourceApp == source.SourceId &&
                (await authorization.CanReadAsync(candidate, ct).ConfigureAwait(false))) { activity = candidate; break; }
        }
        if (activity is null || objectIndex >= activity.Objects.Count || !await authorization.CanReadAsync(activity, ct).ConfigureAwait(false))
            return new(false, "EventUnavailable", "This event or linked object is unavailable.");
        var link = activity.Objects[objectIndex];
        if (!router.CanOpen(link) || !router.Open(link)) return new(false, "OwnerUnavailable", "The linked app could not open this object.");
        return new(true, "Succeeded", "Opened the event object in its owning app.", link);
    }
}
