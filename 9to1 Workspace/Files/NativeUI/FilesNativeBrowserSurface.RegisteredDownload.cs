using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

/// <summary>Readiness over the SAME Home source, scoped to one Files reveal.
/// The readiness owner remains responsible for its original raw work.</summary>
public interface IFilesOriginalRegisteredRevealReadiness : ICuiSceneReadiness
{
    ValueTask<CuiSceneAvailability> CheckRegisteredRevealWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}

public sealed partial class FilesNativeBrowserSurface
{
    // The canonical host presents an already-issued Files page. No path opener,
    // replacement metadata query, copied row or additional Files identity exists.
    public Task RevealOriginalRegisteredPageWithinSourceAsync(FilesNativeBrowserService sameBrowser,
        FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow,
        AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain,
        CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(originalPage);
        ArgumentNullException.ThrowIfNull(originalRow);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(retain);
        if (!ReferenceEquals(sameBrowser, _browser) || originalActor != _originalActor ||
            originalRow.Kind != HostedItemKind.File || originalRow.CurrentRevisionId is null ||
            originalRow.ParentId?.Value != originalPage.ParentID ||
            !originalPage.Items.Any(row => ReferenceEquals(row, originalRow)))
            throw new UnauthorizedAccessException("Select the SAME original registered Files page and file.");
        if (_pendingMutation is not null)
            throw new InvalidOperationException("Finish the pending Files change before revealing another item.");
        return TrackOriginalAsync(() => DriveOriginalRegisteredPageRevealAsync(originalPage,
            originalRow, scope, retain, token));
    }

    private sealed class RegisteredRevealOriginal(FilesNativeBrowserSurface owner, RegisteredRevealOriginal? parent)
    {
        internal readonly FilesNativeBrowserSurface Owner = owner;
        internal readonly RegisteredRevealOriginal? Parent = parent;
        internal volatile bool Live = true;
    }
    private static readonly AsyncLocal<RegisteredRevealOriginal?> RegisteredRevealExecuting = new();
    [ThreadStatic] private static List<FilesNativeBrowserSurface>? RegisteredRevealCallbacks;

    private async Task DriveOriginalRegisteredPageRevealAsync(FilesNativeBrowserPage page,
        HostedItemMetadata row, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var previous = RegisteredRevealExecuting.Value;
        var original = new RegisteredRevealOriginal(this, previous);
        RegisteredRevealExecuting.Value = original;
        try
        {
            void OriginalScope(Action body) => InvokeRegisteredRevealCallback(scope, body);
            void OriginalRetain(Task actual) => InvokeRegisteredRevealCallback(scope, () => retain(actual));
            await RevealOriginalRegisteredPageCoreAsync(page, row, OriginalScope, OriginalRetain, token);
        }
        finally { original.Live = false; RegisteredRevealExecuting.Value = previous; }
    }

    private void InvokeRegisteredRevealCallback(Action<Action> originalScope, Action body)
    {
        var markers = RegisteredRevealCallbacks ??= [];
        markers.Add(this);
        var currentThread = Environment.CurrentManagedThreadId;
        var entered = false;
        var ended = false;
        List<Exception> failures = [];
        HashSet<Exception> captured = new(ReferenceEqualityComparer.Instance);
        void Capture(Exception error)
        {
            lock (failures)
                if (captured.Add(error)) AddRegisteredRevealCallbackFailure(failures, error);
        }
        InvalidOperationException ProtocolFailure(string message)
        {
            var error = new InvalidOperationException(message);
            lock (failures) Add(failures, error);
            return error;
        }
        try
        {
            try { originalScope(() =>
            {
                if (ended || entered || Environment.CurrentManagedThreadId != currentThread)
                    throw ProtocolFailure("The original Files reveal callback must execute once synchronously on its calling thread.");
                entered = true;
                markers.Add(this);
                try { body(); }
                catch (Exception error)
                {
                    Capture(error);
                    throw;
                }
                finally { markers.RemoveAt(markers.Count - 1); }
            }); }
            catch (Exception error) { Capture(error); }
            if (!entered) _ = ProtocolFailure("The original Files reveal callback did not execute.");
        }
        finally { ended = true; markers.RemoveAt(markers.Count - 1); }
        // A supplied scope cannot turn a swallowed/replaced actual callback failure
        // into success. Preserve its original occurrence alongside the scope failure.
        lock (failures) ThrowOriginalFailures(failures, "The actual Files reveal callback and its scope failed.");
    }

    private void DemandExternalRegisteredRevealJoin()
    {
        if (RegisteredRevealCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("The actual Files reveal callback must return before its surface close.");
        for (var original = RegisteredRevealExecuting.Value; original is not null; original = original.Parent)
            if (original.Live && ReferenceEquals(original.Owner, this))
                throw new InvalidOperationException("The actual Files reveal original cannot join its encompassing surface close.");
    }

    private async Task RevealOriginalRegisteredPageCoreAsync(FilesNativeBrowserPage page,
        HostedItemMetadata row, Action<Action> scope, Action<Task> retain, CancellationToken caller)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        var token = linked.Token;
        await _operations.WaitAsync(token);
        List<Exception> failures = [];
        try
        {
            RequireOriginalAlive(token);
            if (_pendingMutation is not null || _boundStoreId is { } store && store != page.StoreID)
                throw new UnauthorizedAccessException("The SAME Files store and settled view are required.");
            _busy = true;
            scope(Changed);
            await RequireRegisteredRevealReadyAsync(scope, retain, token);
            var current = await ObserveRegisteredRevealSourceAsync(() =>
                _browser.CaptureOriginalBrowserDownloadPageReadCheckWithinSourceAsync(page,
                    _originalActor, OriginalAlive, scope, retain, token), scope, retain);
            if (!await ObserveRegisteredRevealSourceAsync(() => current(token).AsTask(), scope, retain))
                throw new UnauthorizedAccessException("The original Files page read retired.");
            if (!_initialized)
            {
                _initialized = true;
                // This actual initialization is retained before registering native retirement.
                scope(() => _originalHostRetirement = _originalHostLifetime.Register(
                    static state => _ = ((FilesNativeBrowserSurface)state!).CloseAndDrainAsync(), this));
                scope(() => _scene = CreateOriginalCanonicalSceneHost());
                var readiness = new RegisteredRevealReadiness(this, _readiness, scope, retain);
                CuiSceneAvailability available;
                try
                {
                    available = await ObserveRegisteredRevealSourceAsync(() => _scene!.ShowAsync(
                        new("files", "Files", "Browser", LoadDocument(), this, this, readiness), token),
                        scope, retain);
                }
                finally { readiness.ReleaseOriginalRevealCallbacks(); }
                if (available.State != CuiSceneAvailabilityState.Ready)
                    throw new UnauthorizedAccessException(available.Message);
            }
            await RequireRegisteredRevealReadyAsync(scope, retain, token);
            if (!await ObserveRegisteredRevealSourceAsync(() => current(token).AsTask(), scope, retain))
                throw new UnauthorizedAccessException("The original Files page read retired before selection.");
            RequireOriginalAlive(token);
            scope(() =>
            {
                RequireOriginalAlive(token);
                _boundStoreId = page.StoreID;
                _page = page;
                // The maintained page check is independent of the completed Browser
                // command. Later refresh/publication is owned by this Files surface.
                _originalPageCurrent = current;
                _query = "";
                _search.Text = "";
                RequireOriginalAlive(token);
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                if (_history.Count >= 128) { _history.RemoveAt(0); _historyIndex--; }
                _history.Add((page.ParentID, "Files folder"));
                _historyIndex = _history.Count - 1;
                ApplyDisplayedNameSort();
                RequireOriginalAlive(token);
                _items.SelectedItem = row;
                RequireOriginalAlive(token);
                _items.ScrollIntoView(row);
                RequireOriginalAlive(token);
                Content = _scene;
                RequireOriginalAlive(token);
                _status = $"Selected {row.Name}";
                Changed();
                RequireOriginalAlive(token);
            });
        }
        catch (Exception error) { Add(failures, error); }
        finally
        {
            // Release the actual gate even when a native notification refuses.
            _busy = false;
            try { _operations.Release(); } catch (Exception error) { Add(failures, error); }
            try { scope(Changed); } catch (Exception error) { Add(failures, error); }
        }
        ThrowOriginalFailures(failures, "Original Files selection and native publication failed.");
    }

    private Task<CuiSceneAvailability> ObserveRegisteredRevealReadinessAsync(ICuiSceneReadiness source,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => ObserveRegisteredRevealSourceAsync(() => source is IFilesOriginalRegisteredRevealReadiness aware
            ? aware.CheckRegisteredRevealWithinSourceAsync(scope, retain, token).AsTask()
            : source.CheckAsync(token).AsTask(), scope, retain);

    private async Task RequireRegisteredRevealReadyAsync(Action<Action> scope, Action<Task> retain,
        CancellationToken token)
    {
        var available = await ObserveRegisteredRevealReadinessAsync(_readiness, scope, retain, token);
        if (available.State != CuiSceneAvailabilityState.Ready)
            throw new UnauthorizedAccessException(available.Message);
    }

    private sealed class RegisteredRevealReadiness(FilesNativeBrowserSurface owner,
        ICuiSceneReadiness source, Action<Action> scope, Action<Task> retain) : ICuiSceneReadiness
    {
        private sealed record RevealCallbacks(Action<Action> Scope, Action<Task> Retain);
        private RevealCallbacks? _callbacks = new(scope, retain);
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            var callbacks = Volatile.Read(ref _callbacks);
            return callbacks is null ? source.CheckAsync(token) : new(
                owner.ObserveRegisteredRevealReadinessAsync(source, callbacks.Scope, callbacks.Retain, token));
        }
        internal void ReleaseOriginalRevealCallbacks() => Interlocked.Exchange(ref _callbacks, null);
    }

    private async Task<T> ObserveRegisteredRevealSourceAsync<T>(Func<Task<T>> acquire,
        Action<Action> scope, Action<Task> retain)
    {
        Task<T>? actual = null;
        List<Exception> failures = [];
        try { scope(() => actual = acquire() ?? throw new InvalidOperationException("The actual Files source returned no Task.")); }
        catch (Exception error) { AddRegisteredRevealCallbackFailure(failures, error); }
        if (actual is null)
        {
            if (failures.Count == 0) Add(failures, new InvalidOperationException("The Files callback did not execute."));
            ThrowOriginalFailures(failures, "Original Files source acquisition failed.");
            throw new InvalidOperationException("Unreachable Files source acquisition.");
        }
        var source = actual;
        lock (_originalTasksSync) _originalTasks.Add(source);
        try { retain(source); } catch (Exception error) { AddRegisteredRevealCallbackFailure(failures, error); }
        // A post-callback protocol/retainer refusal cannot abandon the raw child
        // already acquired. Join it independently and preserve both occurrences.
        try { await source; }
        catch (Exception caught)
        {
            if (source.Exception is { } group)
                foreach (var error in group.InnerExceptions) Add(failures, error);
            else Add(failures, caught);
        }
        ThrowOriginalFailures(failures, "Original Files source and callback custody failed.");
        return source.GetAwaiter().GetResult(); // The exact raw source has now actually joined.
    }

    private static void AddRegisteredRevealCallbackFailure(List<Exception> failures, Exception error)
        => Add(failures, error is OperationCanceledException
            ? new AggregateException("The synchronous Files callback supplied no canceled original Task.", error)
            : error);

    private CuiSceneHost CreateOriginalCanonicalSceneHost()
    {
        var registry = new CuiControlRegistry();
        registry.RegisterControlType("FilesCanonicalList", _ => _items);
        registry.RegisterControlType("FilesSearchInput", _ => _search);
        registry.RegisterControlType("FilesDisplayedSortInput", _ => _sort);
        registry.RegisterControlType("FilesNameInput", _ => _editName);
        return new CuiSceneHost(registry);
    }
}
