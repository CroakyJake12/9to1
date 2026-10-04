using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;
namespace HavenOS.Files.NativeUI;

/// <summary>The app borrows its SAME real Home connection. DTOs supply display values, never actor/store authority.</summary>
public sealed class FilesNativeRemoteBrowserSurface : UserControl, IDisposable, IAsyncDisposable,
    ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly HomeNativeWindowsAppConnection _connection;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CancellationToken _originalLifetime;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _sync = new();
    private readonly List<Task> _originalTasks = [];
    private readonly List<Exception> _originalErrors = [];
    private readonly ListBox _items = new();
    private readonly TextBox _search = new() { PlaceholderText = "Search this folder", MaxLength = 256, Width = 280 };
    private readonly ComboBox _sort = new() { ItemsSource = new[] { "Displayed names: A–Z", "Displayed names: Z–A" }, SelectedIndex = 0, Width = 200 };
    private CuiSceneHost? _scene;
    private HomeNativeFilesPage? _page;
    private Guid? _store;
    private Task? _pending;
    private Task? _close;
    private CancellationTokenRegistration _retirement;
    private bool _registrationMade;
    private bool _closing;
    private bool _busy;
    private string _status = "Opening Files";
    public new event PropertyChangedEventHandler? PropertyChanged;

    public FilesNativeRemoteBrowserSurface(HomeNativeWindowsAppConnection originalConnection,
        ICuiSceneReadiness originalReadiness, CancellationToken originalAppLifetime)
    {
        Dispatcher.UIThread.VerifyAccess();
        _connection = originalConnection ?? throw new ArgumentNullException(nameof(originalConnection));
        _readiness = originalReadiness ?? throw new ArgumentNullException(nameof(originalReadiness));
        if (!originalAppLifetime.CanBeCanceled || originalAppLifetime.IsCancellationRequested)
            throw new UnauthorizedAccessException("Retain the original app-owned lifetime.");
        _originalLifetime = originalAppLifetime;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalAppLifetime);
        _items.ItemTemplate = new FuncDataTemplate<HomeNativeFilesItem>((item, _) => new TextBlock
            { Text = item is null ? "" : item.Kind + "  " + item.Name, Margin = new(8) });
        _items.SelectionChanged += (_, _) => NotifyOriginalSelection();
        _sort.SelectionChanged += (_, _) => NotifyOriginalSelection(sort: true);
        _items.DoubleTapped += async (_, _) => await InvokeEventAsync("9to1.Files.Open");
        _items.KeyDown += async (_, args) =>
        { if (args.Key == Key.Enter) { args.Handled = true; await InvokeEventAsync("9to1.Files.Open"); } };
        _search.KeyDown += async (_, args) =>
        { if (args.Key == Key.Enter) { args.Handled = true; await InvokeEventAsync("9to1.Files.Search"); } };
    }

    public Task InitializeAsync(CancellationToken token = default) => Track(() => NavigateAsync("9to1.Files.Home", token));
    public Task RefreshAsync(CancellationToken token = default) => Track(() => NavigateAsync("9to1.Files.Refresh", token));
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null) throw new ArgumentException("Use the actual current Files selection.");
        return new(Track(() => NavigateAsync(command, cancellationToken)));
    }
    private async Task InvokeEventAsync(string command)
    {
        try { if (IsActionAvailable(command) == true) await Track(() => NavigateAsync(command, _lifetime.Token)); }
        catch { /* SAME original task/cause is retained for the app owner's awaited close. */ }
    }
    private async Task NavigateAsync(string command, CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandAlive(caller);
        var originalPage = _page;
        var originalSelection = _items.SelectedItem as HomeNativeFilesItem;
        var query = _search.Text ?? "";
        if (!_registrationMade)
        {
            _registrationMade = true;
            _retirement = _originalLifetime.Register(static state =>
                _ = ((FilesNativeRemoteBrowserSurface)state!).CloseAndDrainAsync(), this);
        }
        using var active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
        Exception? primary = null;
        List<Exception> publicationErrors = [];
        try
        {
            _busy = true; Changed(); DemandRetained();
            if ((await _readiness.CheckAsync(active.Token)).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("Home is not ready for this original Files view.");
            DemandRetained();
            HomeNativeFilesRequest request = command switch
            {
                "9to1.Files.Home" => new("BrowseRoot"),
                "9to1.Files.Refresh" when originalPage is null => new("BrowseRoot"),
                "9to1.Files.Refresh" when originalPage is not null => new("Refresh", originalPage.OriginalPage, Search: query),
                "9to1.Files.Search" when originalPage is not null => new("Refresh", originalPage.OriginalPage, Search: query),
                "9to1.Files.Up" when originalPage?.ParentId is not null => new("Up", originalPage.OriginalPage),
                "9to1.Files.More" when originalPage?.NextOffset is { } offset => new("NextPage", originalPage.OriginalPage, Offset: offset),
                "9to1.Files.Open" when originalPage is not null && originalSelection?.Kind == "Folder" &&
                    originalPage.Items.Any(row => ReferenceEquals(row, originalSelection)) =>
                    new("OpenFolder", originalPage.OriginalPage, originalSelection.ItemId),
                _ => throw new InvalidOperationException("Select an available read-only Files navigation action.")
            };
            var originalReply = await _connection.InvokeOriginalFilesAsync(request, active.Token);
            DemandRetained();
            if (originalReply.State != "Succeeded" || originalReply.Page is not { } admittedPage)
            {
                ClearDisplayed();
                DemandAlive(active.Token);
                _status = originalReply.Message;
                if (_scene is null) Content = new TextBlock { Text = _status };
                DemandAlive(active.Token); Changed(); DemandAlive(active.Token);
                return; // Actual pending/denied/unavailable outcome can be retried after Home review.
            }
            if (_store is { } originalStore && admittedPage.StoreId != originalStore)
                throw new UnauthorizedAccessException("The original Files store changed.");
            if (_scene is null)
            {
                var registry = new CuiControlRegistry();
                registry.RegisterControlType("FilesCanonicalList", _ => _items);
                registry.RegisterControlType("FilesSearchInput", _ => _search);
                registry.RegisterControlType("FilesDisplayedSortInput", _ => _sort);
                _scene = new CuiSceneHost(registry);
                var availability = await _scene.ShowAsync(new("files", "Files", "Browser", LoadDocument(),
                    this, this, _readiness), active.Token);
                if (availability.State != CuiSceneAvailabilityState.Ready)
                    throw new UnauthorizedAccessException(availability.Message);
                DemandRetained();
            }
            // Scene startup may await Home. Final domain revalidation follows that last readiness await.
            var originalFinal = await _connection.InvokeOriginalFilesAsync(new("Revalidate", admittedPage.OriginalPage), active.Token);
            DemandRetained();
            if (originalFinal.State != "Succeeded" || originalFinal.Page is not { } finalPage ||
                finalPage.OriginalPage != admittedPage.OriginalPage || finalPage.StoreId != admittedPage.StoreId ||
                finalPage.StoreRevision != admittedPage.StoreRevision)
                throw new UnauthorizedAccessException("The original Files page retired during native initialization.");
            _store ??= finalPage.StoreId;
            _page = finalPage;
            _items.SelectedItem = null; DemandAlive(active.Token);
            SortDisplayed(); DemandAlive(active.Token);
            Content = _scene; DemandAlive(active.Token);
            _status = finalPage.Items.Count + " items"; Changed(); DemandAlive(active.Token);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            _busy = false;
            try { Changed(); } catch (Exception error) { Add(publicationErrors, error); }
            if (primary is not null && !publicationErrors.Any(error => ReferenceEquals(error, primary)))
                publicationErrors.Insert(0, primary);
            if (publicationErrors.Count == 1) ExceptionDispatchInfo.Capture(publicationErrors[0]).Throw();
            if (publicationErrors.Count > 1) throw new AggregateException("Original Files navigation and native publication failed.", publicationErrors);
        }
        void DemandRetained()
        {
            DemandAlive(active.Token);
            if (!ReferenceEquals(_page, originalPage) ||
                command == "9to1.Files.Open" && !ReferenceEquals(_items.SelectedItem, originalSelection))
                throw new InvalidOperationException("The original native Files selection changed.");
        }
    }

    public bool? IsActionAvailable(string command) => command switch
    {
        "9to1.Files.Home" or "9to1.Files.Refresh" => Available,
        "9to1.Files.Search" => Available && _page is not null,
        "9to1.Files.Up" => Available && _page?.ParentId is not null,
        "9to1.Files.More" => Available && _page?.HasMore == true,
        "9to1.Files.Open" => Available && _items.SelectedItem is HomeNativeFilesItem { Kind: "Folder" } selected &&
            _page?.Items.Any(row => ReferenceEquals(row, selected)) == true,
        _ => false
    };
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "FolderTitle" => _page?.Title ?? "Files",
            "Status" => _status,
            "SelectedDetails" => Details,
            "CanNavigate" => Available,
            "CanUp" => IsActionAvailable("9to1.Files.Up"),
            "CanMore" => IsActionAvailable("9to1.Files.More"),
            "CanOpen" => IsActionAvailable("9to1.Files.Open"),
            "CanBack" or "CanForward" => false,
            _ => null
        };
        return path is "FolderTitle" or "Status" or "SelectedDetails" or "CanNavigate" or "CanUp" or
            "CanMore" or "CanOpen" or "CanBack" or "CanForward";
    }
    private string Details => Alive && _items.SelectedItem is HomeNativeFilesItem selected &&
        _page?.Items.Any(row => ReferenceEquals(row, selected)) == true ?
        selected.Name + "\nType: " + selected.Kind + "\nAvailability: " + selected.Availability +
        "\nSize: " + (selected.SizeBytes?.ToString() ?? "Not reported") : "Select an item to view its displayed details.";
    private bool Alive => !_closing && !_originalLifetime.IsCancellationRequested && !_lifetime.IsCancellationRequested;
    private bool Available => Alive && !_busy;
    private void DemandAlive(CancellationToken token)
    { token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(!Alive, this); }
    private void Changed()
    {
        var live = Alive;
        PropertyChanged?.Invoke(this, new(null));
        if (live) DemandAlive(CancellationToken.None);
    }
    private void SortDisplayed()
    {
        DemandAlive(CancellationToken.None);
        if (_page is null) return;
        var selected = _items.SelectedItem;
        var ordered = _sort.SelectedIndex == 1
            ? _page.Items.OrderByDescending(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            : _page.Items.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase);
        _items.ItemsSource = ordered.ThenBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.ItemId).ToArray();
        DemandAlive(CancellationToken.None);
        _items.SelectedItem = selected; DemandAlive(CancellationToken.None);
    }
    private void NotifyOriginalSelection(bool sort = false)
    {
        if (!Alive) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (!Alive) return;
            _originalTasks.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_originalTasks.Count == 64) throw new InvalidOperationException("The bounded native notification custody is full.");
            _originalTasks.Add(completion.Task);
        }
        try
        {
            if (sort && !_busy && _page is not null) SortDisplayed();
            Changed(); DemandAlive(CancellationToken.None); completion.SetResult();
        }
        catch (Exception error)
        {
            lock (_sync) { Add(_originalErrors, error); _closing = true; }
            completion.SetException(error);
        }
    }
    private Task Track(Func<Task> operation)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sync)
        {
            DemandAlive(CancellationToken.None);
            if (_pending is { IsCompleted: false }) throw new InvalidOperationException("The original native Files request is pending.");
            _originalTasks.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_originalTasks.Count == 64) throw new InvalidOperationException("The bounded native Files task custody is full.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RunAsync(start.Task, operation);
            _originalTasks.Add(original); _pending = original; start.SetResult();
            return original;
        }
    }
    private async Task RunAsync(Task start, Func<Task> operation)
    {
        await start;
        try { await operation(); }
        catch (Exception error)
        {
            lock (_sync) { Add(_originalErrors, error); _closing = true; }
            throw;
        }
    }
    private void ClearDisplayed()
    { _page = null; _items.SelectedItem = null; _items.ItemsSource = null; Changed(); }
    public Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            List<Exception> withdrawal = [];
            _close = CloseAsync(start.Task, _originalTasks.ToArray(), withdrawal);
            if (Dispatcher.UIThread.CheckAccess())
            {
                try { ClearDisplayed(); } catch (Exception error) { Add(withdrawal, error); }
                try { Content = null; } catch (Exception error) { Add(withdrawal, error); }
            }
            start.SetResult(); return _close;
        }
    }
    private async Task CloseAsync(Task start, Task[] originals, List<Exception> withdrawal)
    {
        await start;
        List<Exception> errors = [..withdrawal];
        try { _lifetime.Cancel(); } catch (Exception error) { Add(errors, error); }
        async Task UiAsync(Action action)
        { if (Dispatcher.UIThread.CheckAccess()) action(); else await Dispatcher.UIThread.InvokeAsync(action); }
        try { await UiAsync(ClearDisplayed); } catch (Exception error) { Add(errors, error); }
        try { await UiAsync(() => Content = null); } catch (Exception error) { Add(errors, error); }
        foreach (var original in originals)
            try { await original; } catch (Exception error) { Add(errors, error); }
        lock (_sync) foreach (var error in _originalErrors) Add(errors, error);
        try { await UiAsync(() => _scene?.Dispose()); } catch (Exception error) { Add(errors, error); }
        try { _retirement.Dispose(); } catch (Exception error) { Add(errors, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { Add(errors, error); }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original remote Files work and view retirement failed.", errors);
        // Borrowed app connection is closed only by the actual app owner after this drain.
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(original => ReferenceEquals(original, error))) errors.Add(error); }
    public void Dispose() => _ = CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static CuiDocument LoadDocument()
    {
        const string resource = "HavenOS.Files.NativeUI.UI.FilesBrowser.cui";
        using var stream = typeof(FilesNativeRemoteBrowserSurface).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("Owning Files CUI source is absent.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), resource);
        if (parser.Diagnostics.Diagnostics.Any(row => row.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Owning Files CUI source has parse errors.");
        return document;
    }
}
