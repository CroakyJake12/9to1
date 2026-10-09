using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace HavenOS.Apps.Browse;

/// <summary>A real installed engine adapter supplies its SAME native view.
/// Neither this interface nor the view grants Home/profile permissions.</summary>
public interface IBrowseNativeEngineTab : IBrowseEngineTab
{
    Control OriginalNativeView { get; }
    bool CanRetireOriginalView { get; }
}

public static class BrowseNativeSurface
{
    public static CuiNativeScene CreateScene(BrowseNativeWorkspace workspace, ICuiSceneReadiness originalReadiness, CuiAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(originalReadiness);
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("browse.engine", _ => new BrowseEngineViewport(workspace));
        return new("browse", "Browse", "Browse", BrowseNativeWorkspace.LoadDocument(), workspace, workspace, originalReadiness)
            { ControlRegistry = registry, Appearance = appearance, IsPublicationCurrent = () => workspace.IsOriginalPublicationCurrent };
    }
    public static BrowseNativeWindow CreateWindow(BrowseNativeWorkspace workspace, ICuiSceneReadiness originalReadiness, CuiAppearance? appearance = null)
        => new(workspace, CreateScene(workspace, originalReadiness, appearance));
}

/// <summary>Retains the actual initialization/action/retirement sources. Chrome
/// owns the native engines; this window owns their CUI presentation only.</summary>
public sealed class BrowseNativeWindow : Window
{
    private readonly BrowseNativeWorkspace _workspace;
    private readonly CuiNativeScene _originalScene;
    private readonly CuiSceneHost _scene;
    private Task<CuiSceneAvailability>? _initialization;
    private Task? _originalClose, _originalSceneClose, _originalWorkspaceClose;
    private bool _allowClose;
    public CuiSceneHost SceneHost => _scene;
    public Task? OriginalInitialization => _initialization;
    public Task? OriginalClose => _originalClose;
    internal BrowseNativeWindow(BrowseNativeWorkspace workspace, CuiNativeScene originalScene)
    {
        _workspace = workspace; _originalScene = originalScene; _scene = new(originalScene.ControlRegistry);
        Content = _scene; Title = "Browse"; Width = 1180; Height = 800; MinWidth = 360; MinHeight = 520;
        Closing += OnClosing; KeyDown += OnKeyDown;
    }
    public Task<CuiSceneAvailability> InitializeAsync(CancellationToken token = default)
    {
        if (_initialization is not null) return _initialization;
        if (_originalClose is not null || _allowClose) throw new InvalidOperationException("Browse cannot initialize after retirement.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _initialization = InitializeCoreAsync(start.Task, token); start.SetResult(); return _initialization;
    }
    private async Task<CuiSceneAvailability> InitializeCoreAsync(Task start, CancellationToken token)
    {
        await start;
        var actualAvailability = await _scene.ShowAsync(_originalScene, token);
        _workspace.ObserveReadiness(actualAvailability);
        if (_scene.TryFindResource("CuiBackgroundBrush", out var value) && value is IBrush brush) Background = brush;
        return actualAvailability;
    }
    public bool CanAdmitOriginalClose()
    { _workspace.DemandOriginalFilesExternalJoin(); return _originalClose is not null || _workspace.CanAdmitClose(); }
    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_allowClose) return; args.Cancel = true;
        _workspace.DemandOriginalFilesExternalJoin();
        if (_originalClose is null && !_workspace.CanAdmitClose()) return;
        if (_originalClose is null)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = CloseAsync(start.Task); start.SetResult();
        }
        ObserveOriginalClose(_originalClose);
    }
    private async void ObserveOriginalClose(Task original)
    {
        try { await original; }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse retained its same original close failure: {0}", failure); }
    }
    private async Task CloseAsync(Task start)
    {
        await start; var failures = new List<Exception>();
        async Task Join(Task? source) { if (source is null) return; try { await source; } catch (Exception failure) { failures.Add(failure); } }
        _workspace.WithdrawForClose();
        // Ready native engines retire while their views still belong to this
        // scene. A pending readiness attempt has no mounted engine surface;
        // withdrawing that original scene first releases its actual attempt.
        var originalReady = _initialization is { IsCompletedSuccessfully: true } &&
            _initialization.Result.State == CuiSceneAvailabilityState.Ready;
        if (!originalReady)
            try { _originalSceneClose = _scene.CloseOriginalAsync(); } catch (Exception failure) { failures.Add(failure); }
        await Join(_initialization);
        foreach (var source in _workspace.OriginalCommands) await Join(source);
        try { _originalWorkspaceClose = _workspace.DisposeAsync().AsTask(); } catch (Exception failure) { failures.Add(failure); }
        await Join(_originalWorkspaceClose);
        if (originalReady)
            try { _originalSceneClose = _scene.CloseOriginalAsync(); } catch (Exception failure) { failures.Add(failure); }
        await Join(_originalSceneClose);
        if (failures.Count != 0) throw new AggregateException("Browse retained its actual original source failures.", failures);
        _allowClose = true; Close();
    }
    private async void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_originalClose is not null || _initialization is not { IsCompletedSuccessfully: true } ||
            _initialization.Result.State != CuiSceneAvailabilityState.Ready) return;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key == Key.L)
        {
            var address = this.GetLogicalDescendants().OfType<TextBox>().SingleOrDefault(control => control.Name == "BrowseAddressBox");
            address?.Focus(); address?.SelectAll(); args.Handled = true; return;
        }
        var command = args.Key switch
        {
            Key.T when args.KeyModifiers.HasFlag(KeyModifiers.Control) => args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "9to1.Browse.NewPrivateTab" : "9to1.Browse.NewTab",
            Key.W when args.KeyModifiers.HasFlag(KeyModifiers.Control) => "9to1.Browse.CloseTab",
            Key.R when args.KeyModifiers.HasFlag(KeyModifiers.Control) => "9to1.Browse.Reload",
            Key.Left when args.KeyModifiers.HasFlag(KeyModifiers.Alt) => "9to1.Browse.Back",
            Key.Right when args.KeyModifiers.HasFlag(KeyModifiers.Alt) => "9to1.Browse.Forward",
            Key.Enter when this.GetLogicalDescendants().OfType<TextBox>().SingleOrDefault(control => control.Name == "BrowseAddressBox")?.IsFocused == true => "9to1.Browse.Navigate",
            _ => null
        };
        if (command is null || _workspace.IsActionAvailable(command) != true) return;
        args.Handled = true;
        try { await _workspace.DispatchAsync(command, null); }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse retained its original keyboard task: {0}", failure); }
    }
}

internal sealed class BrowseEngineViewport : ContentControl
{
    private readonly BrowseNativeWorkspace _workspace;
    private readonly Grid _surfaces = new();
    private readonly Dictionary<IBrowseEngineTab, Control> _originalViews = new(ReferenceEqualityComparer.Instance);
    private readonly TextBlock _unavailable = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    public BrowseEngineViewport(BrowseNativeWorkspace workspace)
    {
        _workspace = workspace; Content = _surfaces; _surfaces.Children.Add(_unavailable);
        AttachedToVisualTree += OnAttached; DetachedFromVisualTree += OnDetached;
    }
    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    { _workspace.PropertyChanged += OnChanged; RefreshOriginalViews(); }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    { _workspace.PropertyChanged -= OnChanged; }
    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => RefreshOriginalViews();
    private void RefreshOriginalViews()
    {
        if (!_workspace.IsReady) return;
        var state = _workspace.State; var current = new HashSet<IBrowseEngineTab>(ReferenceEqualityComparer.Instance);
        var selectedHasView = false;
        foreach (var tab in state.Tabs)
        {
            var original = _workspace.OriginalChrome.ObserveOriginalEngine(tab.Id);
            if (original is not IBrowseNativeEngineTab native) continue;
            current.Add(original);
            var originalView = native.OriginalNativeView ?? throw new InvalidOperationException("The original engine did not supply its native view.");
            if (!_originalViews.TryGetValue(original, out var mounted))
            {
                if (originalView.Parent is not null) throw new InvalidOperationException("The original engine view already belongs to another native owner.");
                _originalViews.Add(original, originalView); _surfaces.Children.Add(originalView);
            }
            else if (!ReferenceEquals(mounted, originalView)) throw new InvalidOperationException("The same engine replaced its original native view.");
            originalView.IsVisible = tab.Id == state.SelectedTabId;
            if (originalView.IsVisible) selectedHasView = true;
        }
        foreach (var original in _originalViews.Keys.Where(original => !current.Contains(original)).ToArray())
        { _surfaces.Children.Remove(_originalViews[original]); _originalViews.Remove(original); }
        // Inactive views remain mounted: selecting a tab does not destroy its
        // native adapter, authentication state or navigation history.
        _unavailable.IsVisible = !selectedHasView;
        var selected = state.SelectedTab;
        _unavailable.Text = selected.EngineState == BrowseEngineState.Crashed ? selected.Status :
            "The native " + (selected.Engine == BrowseEngineKind.Gecko ? "Firefox" : "Chromium") + " renderer is not connected. Tab identity, bookmarks and browser settings are retained.";
    }
}
