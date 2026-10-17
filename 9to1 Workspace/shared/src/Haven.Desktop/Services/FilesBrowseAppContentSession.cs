using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Files;
using HavenOS.Apps.Browse;
using HavenOS.Files.NativeUI;

namespace Haven.Desktop.Services;

/// <summary>A distinct Files view owner per presentation, borrowing the original shared services.</summary>
internal sealed class FilesBrowseAppContentSession : IBrowseNativeAppContentSession
{
    private readonly BrowseAppContentRequest _request;
    private readonly FilesBrowseAppContentAddress _location;
    private readonly NativeFilesDesktopRoute _route;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly object _gate = new();
    private readonly Func<Control, Task<string?>> _chooseFolder;
    private Task? _initialization, _close;
    private Task<bool>? _preparation;
    private Task<Task>? _initializationDispatch, _closeDispatch;
    private Task? _initializationWork, _closeWork, _actualAdmission, _actualOpen, _actualLocation, _actualClose;
    private readonly List<Task> _preparations = [];
    private readonly List<Task> _setupOperations = [];
    private Task? _setupAction, _actualSetupClose;
    private FilesWorkspaceSetupPage? _setupPage;
    private volatile FilesNativeBrowserSurface? _view;
    private volatile AppContentAddress _address;
    private volatile string _title = "Files";
    private volatile bool _published, _closing;

    internal FilesBrowseAppContentSession(BrowseAppContentRequest request, FilesBrowseAppContentAddress location,
        NativeFilesDesktopRoute route, CancellationTokenSource lifetime, Func<Control, Task<string?>>? chooseFolder = null)
    {
        _request = request; _location = location; _route = route; _lifetime = lifetime; _token = lifetime.Token;
        _address = request.Address; _chooseFolder = chooseFolder ?? FilesWorkspaceSetupPage.ChooseFolderAsync;
    }

    public AppContentAddress Address => _address;
    public string Title => _title;
    public string IconKey => "folder";
    public event EventHandler? StateChanged;
    public bool IsPublicationCurrent => _published && !_closing && !_token.IsCancellationRequested
        && _route.IsOriginalRouteAlive
        && OriginalInitialization is { IsCompletedSuccessfully: true }
        && (_view is { OriginalPublicationIsAlive: true } || _view is null && _setupPage is { IsAlive: true });
    public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsPublicationCurrent)
                throw new InvalidOperationException("The original Files view has not completed admission.");
            _route.CheckOriginalAppContentProfile(_request.ProfileId);
            if (_view is { } view) _route.CheckPublicationCurrent(view);
            // Once mounted, the presentation root never changes. Only the setup
            // page's child changes to this route's real configured Files view.
            return _setupPage ?? (Control?)_view
                ?? throw new InvalidOperationException("The original Files presentation is absent.");
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            DemandAlive();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = InitializeDriverAsync(gate.Task, cancellationToken);
            gate.SetResult(); return _initialization;
        }
    }

    private async Task InitializeDriverAsync(Task gate, CancellationToken caller)
    {
        await gate.ConfigureAwait(false);
        // Retain both the exact dispatcher work and the nested owning operation, even on refusal.
        _initializationDispatch = Dispatcher.UIThread.InvokeAsync<Task>(() => InitializeOnUiAsync(caller)).GetTask();
        _initializationWork = await _initializationDispatch.ConfigureAwait(false);
        await _initializationWork.ConfigureAwait(false);
    }

    private async Task InitializeOnUiAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        DemandAlive(); linked.Token.ThrowIfCancellationRequested();
        _actualAdmission = _route.AdmitOriginalShellInitializationAsync(linked.Token);
        await _actualAdmission;
        _route.CheckOriginalAppContentProfile(_request.ProfileId); DemandAlive();
        if (_location.StoreId is null && _route.SupportsOriginalWorkspaceSetup
            && await _route.ReadOriginalSetupConfigurationAsync(linked.Token) is null)
        {
            _route.CheckOriginalAppContentProfile(_request.ProfileId); DemandAlive(); linked.Token.ThrowIfCancellationRequested();
            _setupPage = new FilesWorkspaceSetupPage(ChooseWorkspaceAsync);
            _setupPage.Initialize();
            _published = true;
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            return;
        }
        await OpenConfiguredViewAsync(linked.Token);
    }

    private async Task OpenConfiguredViewAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandAlive(); token.ThrowIfCancellationRequested();
        var opening = _route.OpenAsync(token); _actualOpen = opening;
        var view = await opening;
        // Retain the actual view even if the requested store/folder is refused afterward.
        _view = view;
        if (_location.StoreId is { } store)
        {
            _actualLocation = view.OpenOriginalLocationAsync(store, _location.FolderId, token);
            await _actualLocation;
        }
        await _route.RevalidateBeforePublicationAsync(view, token);
        _route.CheckOriginalAppContentProfile(_request.ProfileId);
        _route.CheckPublicationCurrent(view); DemandAlive(); token.ThrowIfCancellationRequested();
        UpdateLocation(view);
        view.PropertyChanged += OnSurfaceChanged;
        _setupPage?.ShowConfigured(view);
        _published = true;
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
        _route.CheckPublicationCurrent(view); DemandAlive();
    }

    private Task ChooseWorkspaceAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandAlive();
        if (_setupAction is { IsCompleted: false }) return _setupAction;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = ChooseWorkspaceCoreAsync(start.Task, caller);
        _setupAction = actual;
        lock (_gate)
        {
            _setupOperations.RemoveAll(row => row.IsCompletedSuccessfully);
            _setupOperations.Add(actual); // Before the native picker callback.
        }
        start.SetResult();
        return actual;
    }

    private async Task ChooseWorkspaceCoreAsync(Task start, CancellationToken caller)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var token = linked.Token;
        var page = _setupPage ?? throw new InvalidOperationException("The original setup page is unavailable.");
        DemandAlive(); token.ThrowIfCancellationRequested(); _route.CheckOriginalAppContentProfile(_request.ProfileId);
        page.SetBusy(true); page.SetStatus("Choose an empty folder in the native folder picker.");
        try
        {
            // Another Files tab may have completed setup while this one was open.
            if (await _route.ReadOriginalSetupConfigurationAsync(token) is null)
            {
                var selected = await _chooseFolder(page);
                DemandAlive(); token.ThrowIfCancellationRequested();
                if (selected is null) { page.SetStatus("No folder selected. Your Files configuration is unchanged."); return; }
                // User input refusal before any write. The canonical owner repeats
                // these checks and enforces symlink, profile and ownership boundaries.
                if (!Path.IsPathFullyQualified(selected) || !Directory.Exists(selected)
                    || Directory.EnumerateFileSystemEntries(selected).Any())
                {
                    page.SetStatus("Choose an existing empty local folder. Existing files were preserved.");
                    return;
                }
                if (await _route.ReadOriginalSetupConfigurationAsync(token) is null)
                    await _route.ConfigureOriginalWorkspaceAsync(selected, token);
            }
            DemandAlive(); token.ThrowIfCancellationRequested();
            await OpenConfiguredViewAsync(token);
        }
        catch (Exception error)
        {
            // A refused configured-view acquisition/publication never remains
            // advertised as a current Files presentation merely because its
            // first-use metadata page had previously been admitted.
            if (_actualOpen is not null) _published = false;
            if (!_closing) page.SetStatus("Files setup could not finish: " + error.Message + " Existing data was preserved.");
            throw; // The exact failed original remains joined at external close.
        }
        finally { page.SetBusy(false); }
    }

    private void OnSurfaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_published || _closing || sender is not FilesNativeBrowserSurface view || !ReferenceEquals(view, _view)) return;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        if (view.OriginalPublicationIsAlive) UpdateLocation(view);
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
    }

    private void UpdateLocation(FilesNativeBrowserSurface view)
    {
        if (!view.TryGetOriginalLocation(out var store, out var folder, out var title)) return;
        _address = FilesBrowseAppContentAddress.For(store, folder);
        _title = title == "Files" ? "Files" : $"{title} — Files";
    }

    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return _preparation ??= Task.FromResult(false);
            if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
            _preparations.RemoveAll(actual => actual.IsCompletedSuccessfully);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _preparation = PrepareDriverAsync(gate.Task, cancellationToken);
            _preparations.Add(_preparation); gate.SetResult(); return _preparation;
        }
    }

    private async Task<bool> PrepareDriverAsync(Task gate, CancellationToken caller)
    {
        await gate.ConfigureAwait(false);
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task<bool>>(async () =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
            linked.Token.ThrowIfCancellationRequested();
            if (_closing || OriginalInitialization is not { IsCompletedSuccessfully: true }
                || _setupAction is { IsCompleted: false }) return false;
            if (_view is not { } view)
            {
                if (_setupPage is not { IsAlive: true }) return false;
                await _route.AdmitOriginalShellInitializationAsync(linked.Token);
                _route.CheckOriginalAppContentProfile(_request.ProfileId);
                return !_closing && _setupAction is not { IsCompleted: false };
            }
            if (!view.CanPrepareOriginalClose()) return false;
            await _route.RevalidateBeforePublicationAsync(view, linked.Token);
            _route.CheckOriginalAppContentProfile(_request.ProfileId);
            return !_closing && view.CanPrepareOriginalClose();
        }).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        lock (_gate) { _preparations.Add(dispatch); _preparations.Add(actual); }
        return await actual.ConfigureAwait(false);
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true; _published = false;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseDriverAsync(gate.Task); gate.SetResult(); return _close;
        }
    }

    private async Task CloseDriverAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        _closeDispatch = Dispatcher.UIThread.InvokeAsync<Task>(CloseOnUiAsync).GetTask();
        _closeWork = await _closeDispatch.ConfigureAwait(false);
        await _closeWork.ConfigureAwait(false);
    }

    private async Task CloseOnUiAsync()
    {
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        List<Exception> failures = [];
        void Add(Exception error) { if (!failures.Any(row => ReferenceEquals(row, error))) failures.Add(error); }
        async Task Join(Task? original) { if (original is null) return; try { await original; } catch (Exception error) { Add(error); } }
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _lifetime.Cancel(); return true; }); }
        catch (Exception error) { Add(error); }
        try { if (_setupPage is not null) _actualSetupClose = _setupPage.CloseAndDrainAsync(); }
        catch (Exception error) { Add(error); }
        // Begin cancellation/retirement before joining initialization or active owner work.
        try { _actualClose = CloudflareOriginalExecutionGuard.InvokeOriginal(this, _route.CloseAndDrainAsync); }
        catch (Exception error) { Add(error); }
        await Join(OriginalInitialization);
        Task[] setup; lock (_gate) setup = _setupOperations.ToArray();
        foreach (var actual in setup) await Join(actual);
        await Join(_actualSetupClose);
        Task[] preparations; lock (_gate) preparations = _preparations.ToArray();
        foreach (var actual in preparations) await Join(actual);
        await Join(_actualClose);
        if (_view is { } view) view.PropertyChanged -= OnSurfaceChanged;
        try { _lifetime.Dispose(); } catch (Exception error) { Add(error); }
        if (failures.Count > 0) throw new AggregateException("The original Files presentation did not retire cleanly.", failures);
        _setupPage?.ReleaseRetiredContent();
    }

    private void DemandAlive()
    { ObjectDisposedException.ThrowIf(_closing, this); _token.ThrowIfCancellationRequested(); }
    private void DemandExternalJoin()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _route.DemandExternalOriginalRetirementJoin(); }
}
