using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

/// <summary>The native host supplies its same Home service graph and captures the original actor before awaiting.
/// This surface owns no provider, storage root, Home graph or installation authority.</summary>
public sealed class FilesNativeBrowserSurface : UserControl, IDisposable,
    ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly FilesNativeBrowserService _browser;
    private readonly FilesCompatibilityPackageOpenCoordinator _packages;
    private readonly AuthenticatedResourceActor _originalActor;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly List<(Guid? ID, string Title)> _history = [(null, "Files")];
    private readonly ListBox _items = new();
    private readonly TextBox _search = new() { PlaceholderText = "Search this folder", Width = 280, MaxLength = 256 };
    private CuiSceneHost? _scene;
    private FilesNativeBrowserPage? _page;
    private Guid? _boundStoreId;
    private int _historyIndex;
    private bool _busy;
    private bool _disposed;
    private bool _initialized;
    private string _status = "Opening Files";
    private string _query = "";
    public new event PropertyChangedEventHandler? PropertyChanged;

    public FilesNativeBrowserSurface(FilesNativeBrowserService browser, FilesCompatibilityPackageOpenCoordinator packages,
        AuthenticatedResourceActor originalActor, ICuiSceneReadiness readiness, CancellationToken hostLifetime)
    {
        _browser = browser; _packages = packages; _originalActor = originalActor; _readiness = readiness;
        ArgumentNullException.ThrowIfNull(originalActor);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
        _items.ItemTemplate = new FuncDataTemplate<HostedItemMetadata>((item, _) => new TextBlock
        { Text = item is null ? "" : $"{(item.Kind == HostedItemKind.Folder ? "Folder" : item.Kind.ToString())}  {item.Name}", Margin = new(8) });
        _items.SelectionChanged += (_, _) => Changed();
        _items.DoubleTapped += async (_, _) => await InvokeAsync("9to1.Files.Open", CancellationToken.None);
        _items.KeyDown += async (_, args) =>
        { if (args.Key == Key.Enter) { args.Handled = true; await InvokeAsync("9to1.Files.Open", CancellationToken.None); } };
        _search.KeyDown += async (_, args) =>
        { if (args.Key == Key.Enter) { args.Handled = true; await InvokeAsync("9to1.Files.Search", CancellationToken.None); } };
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("The original Files view is already mounted.");
        _initialized = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            await RequireReadyAsync(linked.Token);
            var page = await _browser.ListAsync(_originalActor, token: linked.Token);
            await _browser.RevalidateAsync(page, _originalActor, linked.Token);
            var registry = new CuiControlRegistry();
            registry.RegisterControlType("FilesCanonicalList", _ => _items);
            registry.RegisterControlType("FilesSearchInput", _ => _search);
            _scene = new CuiSceneHost(registry);
            var available = await _scene.ShowAsync(new("files", "Files", "Browser", LoadDocument(), this, this, _readiness), linked.Token);
            if (available.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(available.Message);
            await _browser.RevalidateAsync(page, _originalActor, linked.Token);
            await RequireReadyAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            _boundStoreId = page.StoreID; _page = page; _items.ItemsSource = page.Items; Content = _scene;
            _status = $"{page.Items.Count} items"; Changed();
        }
        catch { Dispose(); throw; }
    }

    public async Task RefreshAsync(CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operations.WaitAsync(linked.Token);
        try
        {
            _busy = true; Changed();
            await RequireReadyAsync(linked.Token);
            var retained = _page ?? throw new InvalidOperationException("The Files browser is not open.");
            await _browser.RevalidateAsync(retained, _originalActor, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_page, retained)) throw new InvalidOperationException("The displayed Files page changed.");
        }
        catch { ClearSources(); throw; }
        finally { _busy = false; Changed(); _operations.Release(); }
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "FolderTitle" => _history[_historyIndex].Title,
            "Status" => _status,
            "CanNavigate" => Available,
            "CanBack" => Available && _historyIndex > 0,
            "CanForward" => Available && _historyIndex + 1 < _history.Count,
            "CanMore" => Available && _page?.Next is not null,
            "CanOpen" => Available && CanOpenSelection,
            _ => null
        };
        return path is "FolderTitle" or "Status" or "CanNavigate" or "CanBack" or "CanForward" or "CanMore" or "CanOpen";
    }
    private bool Available => !_disposed && !_busy && !_lifetime.IsCancellationRequested && _scene is not null;
    private bool CanOpenSelection => _page is not null && _items.SelectedItem is HostedItemMetadata selected &&
        (selected.Kind == HostedItemKind.Folder || selected.Kind == HostedItemKind.File &&
         Path.GetExtension(selected.Name).ToLowerInvariant() is ".exe" or ".msi" or ".apk");
    public bool? IsActionAvailable(string command) => command switch
    {
        "9to1.Files.Home" or "9to1.Files.Refresh" or "9to1.Files.Search" => Available,
        "9to1.Files.Back" => Available && _historyIndex > 0,
        "9to1.Files.Forward" => Available && _historyIndex + 1 < _history.Count,
        "9to1.Files.More" => Available && _page?.Next is not null,
        "9to1.Files.Open" => Available && CanOpenSelection,
        _ => false
    };
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (parameter is not null) throw new ArgumentException("Files commands use the owning canonical selection, not caller paths or identity.");
        return new(InvokeAsync(command, cancellationToken));
    }

    private async Task InvokeAsync(string command, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) return;
        var originalPage = _page;
        var originalSelection = _items.SelectedItem as HostedItemMetadata;
        var originalHistoryIndex = _historyIndex;
        var originalSearch = _search.Text ?? "";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { await _operations.WaitAsync(linked.Token); }
        catch (OperationCanceledException) { ClearSources(); return; }
        try
        {
            if (IsActionAvailable(command) != true) return;
            RequireRetainedPage();
            _busy = true; Changed();
            await RequireReadyAsync(linked.Token);
            RequireRetainedPage();
            if (command != "9to1.Files.Refresh" && originalPage is not null)
                await _browser.RevalidateAsync(originalPage, _originalActor, linked.Token);
            RequireRetainedPage();
            var index = _historyIndex; var destination = _history[index];
            FilesNativeBrowserCursor? cursor = null;
            var appendHistory = false;
            if (command == "9to1.Files.Open")
            {
                var selected = originalSelection ?? throw new InvalidOperationException("Select an item.");
                if (selected.Kind == HostedItemKind.Folder)
                { destination = (selected.Id.Value, selected.Name); appendHistory = true; _query = ""; _search.Text = ""; }
                else
                {
                    var selection = await _browser.ReadPackageSelectionAsync(originalPage!, selected, _originalActor, 256L * 1024 * 1024, linked.Token);
                    RequireRetainedPage();
                    await _packages.OpenAsync(selection, linked.Token);
                    RequireRetainedPage();
                    await _browser.RevalidateAsync(originalPage!, _originalActor, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    _status = "Opened package inspection"; return;
                }
            }
            else if (command == "9to1.Files.Home") { destination = (null, "Files"); appendHistory = true; _query = ""; _search.Text = ""; }
            else if (command == "9to1.Files.Back") { index--; destination = _history[index]; _query = ""; _search.Text = ""; }
            else if (command == "9to1.Files.Forward") { index++; destination = _history[index]; _query = ""; _search.Text = ""; }
            else if (command == "9to1.Files.Search") _query = originalSearch;
            else if (command == "9to1.Files.More") cursor = _page!.Next;
            var page = await _browser.ListAsync(_originalActor, destination.ID, _query, cursor, linked.Token, expectedStoreId: _boundStoreId);
            await _browser.RevalidateAsync(page, _originalActor, linked.Token);
            await RequireReadyAsync(linked.Token); linked.Token.ThrowIfCancellationRequested();
            RequireRetainedPage();
            if (appendHistory)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                if (_history.Count >= 128) { _history.RemoveAt(0); _historyIndex--; }
                _history.Add(destination); index = _history.Count - 1;
            }
            _historyIndex = index; _page = page; _items.SelectedItem = null; _items.ItemsSource = page.Items;
            _status = $"{page.Items.Count} items";
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException or IOException or NotSupportedException or ArgumentException or OperationCanceledException)
        { ClearSources(); _status = error is OperationCanceledException ? "Files view closed" : error.Message; }
        finally { _busy = false; Changed(); _operations.Release(); }
        void RequireRetainedPage()
        {
            if (_disposed || !ReferenceEquals(_page, originalPage) || _historyIndex != originalHistoryIndex ||
                command == "9to1.Files.Open" && _items.SelectedItem as HostedItemMetadata != originalSelection)
                throw new InvalidOperationException("The original Files selection changed before navigation.");
        }
    }

    private async Task RequireReadyAsync(CancellationToken token)
    { if ((await _readiness.CheckAsync(token)).State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException("Home cannot authorise this Files view."); }
    private void ClearSources() { _page = null; _items.SelectedItem = null; _items.ItemsSource = null; Changed(); }
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); ClearSources(); Content = null; _scene?.Dispose(); _lifetime.Dispose();
    }
    private static CuiDocument LoadDocument()
    {
        const string name = "HavenOS.Files.NativeUI.UI.FilesBrowser.cui";
        using var stream = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException("Owning Files browser CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), name);
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
}
