using System.Runtime.CompilerServices;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Typed original Files metadata publication into the existing Home Library index.
/// Only actual top-level folder references are supported; file-item launch is not guessed.</summary>
internal sealed class NativeHomeFilesLibrarySource : IHomeOriginalLibrarySource
{
    private sealed record Entry(HomeArtifactReference View, FilesNativeBrowserPage Page, HostedItemMetadata Row);
    private sealed record Operation(Task Task, CancellationToken Token);
    private readonly FilesNativeBrowserService _browser;
    private readonly AuthenticatedResourceActor _actor;
    private readonly IHomeCoreStateStore _home;
    private readonly Func<CancellationToken, Task<bool>> _ownerCurrent;
    private readonly Action<Action> _parent;
    private readonly Func<bool> _alive;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Operation> _operations = [];
    private readonly ConditionalWeakTable<Exception, object> _refusals = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private HomeLibraryQuery? _query;
    private CloudflareOriginalTaskLedger? _currentOriginal;
    private bool _retiring;
    private Task? _close;
    internal NativeHomeFilesLibrarySource(FilesNativeBrowserService browser, AuthenticatedResourceActor actor,
        IHomeCoreStateStore home, Func<CancellationToken, Task<bool>> ownerCurrent,
        Action<Action> parent, Func<bool> alive)
    { _browser = browser; _actor = actor; _home = home; _ownerCurrent = ownerCurrent; _parent = parent; _alive = alive; }
    public string? AvailabilityNotice => "Files currently shows your top-level folder references.";
    public HomeLibraryService CreateService(IHomeDeepLinkRouter router) => new(this, this, router, new HomeCoreLibraryPreferences(_home));
    public Task RevalidateAsync(CancellationToken token) => RunAsync(async (source, caller) =>
    { await RequireOwnerAsync(source, caller).ConfigureAwait(false); return true; }, token);
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);

    public Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken token) =>
        RunAsync<IReadOnlyList<HomeArtifactReference>>(async (source, caller) =>
        {
            if (query.Cursor is not null || query.Text?.Length > 256)
                throw Refuse("InvalidQuery", "Use a search of at most 256 characters and the current Library page.");
            var identity = query with { Offset = 0, Cursor = null };
            if (query.Offset != 0)
            {
                if (_query != identity) throw Refuse("LibraryPageChanged", "Refresh Files references before opening another Library page.");
                foreach (var page in _entries.Values.Select(entry => entry.Page).Distinct<FilesNativeBrowserPage>(ReferenceEqualityComparer.Instance))
                    await source.AwaitAsync(source.Invoke(() => ValidatePageAsync(page, caller))).ConfigureAwait(false);
                await RequireOwnerAsync(source, caller).ConfigureAwait(false);
                return _entries.Values.Select(entry => entry.View).ToArray();
            }
            _entries.Clear(); _query = null;
            var available = await source.AwaitAsync(source.Invoke(() =>
                _browser.ObserveOriginalHomeLibraryAvailabilityWithinSourceAsync(_actor, _alive,
                    Own, Retain, caller))).ConfigureAwait(false);
            if (!_browser.IsIssuedOriginalHomeLibraryAvailability(available, _actor))
                throw new InvalidDataException("The Files availability observation has a different original issuer.");
            if (!available.IsAvailable || available.StoreId is not { } store || store == Guid.Empty)
                throw Refuse(available.Code, available.Message);
            FilesNativeBrowserCursor? cursor = null;
            var offsets = new HashSet<string>(StringComparer.Ordinal);
            string? revision = null;
            var rowsObserved = 0;
            do
            {
                var page = await source.AwaitAsync(source.Invoke(() =>
                    _browser.ListOriginalHomeLibraryPageWithinSourceAsync(_actor, store, query.Text ?? "", cursor,
                        _alive, Own, Retain, caller))).ConfigureAwait(false);
                if (page.StoreID != store || page.ParentID is not null || page.IsTrash ||
                    revision is not null && revision != page.StoreRevision)
                    throw new InvalidDataException("The original Files Library page changed its store, parent or revision.");
                revision ??= page.StoreRevision;
                rowsObserved = checked(rowsObserved + page.Items.Count);
                if (rowsObserved > 4096) throw Refuse("LibraryQueryTooLarge", "Narrow the search to browse these Files folder references.");
                foreach (var row in page.Items.Where(item => item.Kind == HostedItemKind.Folder))
                {
                    if (row.Id.Value == Guid.Empty || string.IsNullOrWhiteSpace(row.Name) || row.ParentId is not null)
                        throw new InvalidDataException("The original Files owner returned invalid top-level folder metadata.");
                    var id = store.ToString("D") + "/" + row.Id.Value.ToString("D");
                    var view = new HomeArtifactReference("files", id, row.Name, "Files folder", page.FolderTitle,
                        row.OwnerPrincipalId, null, row.ModifiedAt, false, false, row.IsShared, false, [],
                        new(HomeRoute.Library, "files", id, "open", "files.folder"));
                    if (!_entries.TryAdd(id, new(view, page, row)))
                        throw new InvalidDataException("The original Files cursor returned duplicate folder identities.");
                }
                cursor = page.Next;
                if (cursor is not null && (cursor.StoreID != store || cursor.StoreRevision != revision ||
                    cursor.ParentID is not null || cursor.OriginalActor != _actor || cursor.IsTrash ||
                    cursor.Search != (query.Text ?? "") || !offsets.Add(cursor.Offset)))
                    throw new InvalidDataException("The original Files Library cursor changed or repeated.");
            } while (cursor is not null);
            // All cursor values came from the original issuer; no offset is incremented or guessed.
            foreach (var page in _entries.Values.Select(entry => entry.Page).Distinct<FilesNativeBrowserPage>(ReferenceEqualityComparer.Instance))
                await source.AwaitAsync(source.Invoke(() => ValidatePageAsync(page, caller))).ConfigureAwait(false);
            await RequireOwnerAsync(source, caller).ConfigureAwait(false);
            _query = identity;
            return _entries.Values.Select(entry => entry.View).ToArray();
        }, token);
    public Task<HomeArtifactReference?> GetByIdAsync(string app, string id, CancellationToken token) =>
        RunAsync<HomeArtifactReference?>(async (source, caller) => app == "files" && _entries.TryGetValue(id, out var entry) &&
            await IsCurrentAsync(entry, source, caller).ConfigureAwait(false) ? entry.View : null, token);
    public Task<bool> CanReadAsync(string app, string id, CancellationToken token) =>
        RunAsync(async (source, caller) => app == "files" && _entries.TryGetValue(id, out var entry) &&
            await IsCurrentAsync(entry, source, caller).ConfigureAwait(false), token);
    public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken token) =>
        RunAsync(async (source, caller) => link.OwnerApp == "files" && link.TargetRouteId == "files.folder" &&
            _entries.TryGetValue(link.ObjectId, out var entry) && ReferenceEquals(entry.View.OpenLink, link) &&
            await IsCurrentAsync(entry, source, caller).ConfigureAwait(false), token);
    private async Task<bool> IsCurrentAsync(Entry entry, CloudflareOriginalTaskLedger source, CancellationToken token)
    {
        if (entry.Row.Kind != HostedItemKind.Folder || !entry.Page.Items.Any(row => ReferenceEquals(row, entry.Row))) return false;
        await source.AwaitAsync(source.Invoke(() => ValidatePageAsync(entry.Page, token))).ConfigureAwait(false);
        await RequireOwnerAsync(source, token).ConfigureAwait(false);
        return true;
    }
    private Task ValidatePageAsync(FilesNativeBrowserPage page, CancellationToken token) =>
        _browser.RevalidateOriginalHomeLibraryPageWithinSourceAsync(page, _actor, _alive, Own, Retain, token);
    private void DemandOwner() { if (!_alive()) throw new ObjectDisposedException(nameof(NativeHomeFilesLibrarySource)); }
    private async Task RequireOwnerAsync(CloudflareOriginalTaskLedger source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); DemandOwner();
        if (!await source.AwaitAsync(source.Invoke(() => _ownerCurrent(token))).ConfigureAwait(false))
            throw Refuse("LibraryProfileChanged", "Reopen Library for the actual Home profile and services.");
        token.ThrowIfCancellationRequested(); DemandOwner();
    }
    private CloudflareOriginalTaskLedger Current()
    { lock (_sync) return _currentOriginal ?? throw new InvalidOperationException("Retain the actual serialized Files Library source."); }
    private void Own(Action body) => Current().Invoke(() => { DemandOwner(); body(); return true; });
    private void Retain(Task actual) { _ = Current().Track(actual); }
    private HomeFeatureProviderException Refuse(string code, string message)
    { var error = new HomeFeatureProviderException(code, message); _refusals.Add(error, new()); return error; }
    public bool IsExpectedRefusal(Exception error) => error is AggregateException group
        ? group.InnerExceptions.Count != 0 && group.InnerExceptions.All(IsExpectedRefusal) : _refusals.TryGetValue(error, out _);
    private Task<T> RunAsync<T>(Func<CloudflareOriginalTaskLedger, CancellationToken, Task<T>> body, CancellationToken token)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retiring, this); DemandOwner();
            _operations.RemoveAll(item => item.Task.IsCompletedSuccessfully || item.Task.IsFaulted && IsExpectedRefusal(item.Task.Exception!));
            if (_operations.Count >= 128) throw new InvalidOperationException("Settle the original Files Library failures first.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = Run(start.Task); _operations.Add(new(original, token)); start.SetResult(); return Expose(original);
        }
        async Task<T> Run(Task start)
        {
            await start.ConfigureAwait(false);
            var gate = _gate.WaitAsync(token); await gate.ConfigureAwait(false);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this); source.BindOriginalCallerCallback(_parent);
            lock (_sync) _currentOriginal = source;
            try { return await source.RunToOriginalSettlementAsync(async () =>
                { await RequireOwnerAsync(source, token).ConfigureAwait(false); return await body(source, token).ConfigureAwait(false); }).ConfigureAwait(false); }
            finally { lock (_sync) _currentOriginal = null; _gate.Release(); }
        }
    }
    private async Task<T> Expose<T>(Task<T> original)
    {
        try { return await original.ConfigureAwait(false); }
        catch
        {
            var error = original.Exception;
            if (error is not null && IsExpectedRefusal(error))
            { Exception first = error; while (first is AggregateException group) first = group.InnerExceptions[0]; System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw(); }
            throw; // Raw/mixed/faulted cancellation remains the actual retained source failure.
        }
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalJoin();
        lock (_sync)
        {
            if (_close is not null) return new(_close);
            _retiring = true; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseAsync(start.Task, _operations.ToArray()); start.SetResult(); return new(_close);
        }
    }
    private async Task CloseAsync(Task start, Operation[] operations)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        foreach (var item in operations)
            try { await item.Task.ConfigureAwait(false); }
            catch (OperationCanceledException cause) when (item.Task.IsCanceled && item.Token.IsCancellationRequested && cause.CancellationToken == item.Token) { }
            catch (Exception cause) { var error = item.Task.Exception ?? cause; if (!IsExpectedRefusal(error)) failures.Add(error); }
        _entries.Clear(); _gate.Dispose();
        if (failures.Count != 0) throw new AggregateException("Original Files Library sources failed.", failures);
    }
}
