using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        => CreateScene(workspace, originalReadiness, appearance, themePreference: null);

    public static CuiNativeScene CreateScene(BrowseNativeWorkspace workspace, ICuiSceneReadiness originalReadiness,
        CuiAppearance? appearance, CuiThemePreferenceContext? themePreference)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(originalReadiness);
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("browse.engine", _ => new BrowseEngineViewport(workspace));
        return new("browse", "Browse", "Browse", BrowseNativeWorkspace.LoadDocument(), workspace, workspace, originalReadiness)
            { ControlRegistry = registry, Appearance = appearance, ThemePreference = themePreference, IsPublicationCurrent = () => workspace.IsOriginalPublicationCurrent };
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
    private Task<bool>? _originalClosePreparation;
    private readonly List<Task<bool>> _closePreparations = [];
    private bool _allowClose;
    public CuiSceneHost SceneHost => _scene;
    public Task? OriginalInitialization => _initialization;
    public Task? OriginalClose => _originalClose;
    public Task<bool>? OriginalClosePreparation => _originalClosePreparation;
    public IReadOnlyList<Task<bool>> OriginalClosePreparations => _closePreparations.ToArray();
    internal BrowseNativeWindow(BrowseNativeWorkspace workspace, CuiNativeScene originalScene)
    {
        _workspace = workspace; _originalScene = originalScene;
        _scene = new(originalScene.ControlRegistry, failure =>
            _workspace.OriginalChrome.IsVerifiedSettledActionFailure(failure));
        Content = _scene; Title = "9to1 Haven"; Width = 1180; Height = 800; MinWidth = 360; MinHeight = 520;
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
        if (_originalClose is null && _workspace.OriginalChrome.HasAppContent)
        {
            if (_originalClosePreparation is { IsCompleted: false } or { IsFaulted: true }) return;
            var original = _workspace.OriginalChrome.TryPrepareToCloseAsync();
            _originalClosePreparation = original; _closePreparations.Add(original);
            ObserveClosePreparation(original); return;
        }
        BeginOriginalClose();
    }
    private async void ObserveClosePreparation(Task<bool> original)
    {
        try
        {
            if (!await original || _originalClose is not null || !_workspace.CanAdmitClose()) return;
            BeginOriginalClose();
        }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse retained native close preparation failure: {0}", failure); }
    }
    private void BeginOriginalClose()
    {
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
        // Ready native engines retire while their views still belong to this
        // scene. A pending readiness attempt has no mounted engine surface;
        // withdrawing that original scene first releases its actual attempt.
        var originalReady = _initialization is { IsCompletedSuccessfully: true } &&
            _initialization.Result.State == CuiSceneAvailabilityState.Ready;
        if (!originalReady)
            try { _originalSceneClose = _scene.CloseOriginalAsync(); } catch (Exception failure) { failures.Add(failure); }
        await Join(_initialization);
        try { _originalWorkspaceClose = _workspace.DisposeAsync().AsTask(); } catch (Exception failure) { failures.Add(failure); }
        await Join(_originalWorkspaceClose);
        foreach (var source in _workspace.OriginalCommands) await Join(source);
        // A failed actual app close keeps its native view and scene mounted.
        if (originalReady && _originalWorkspaceClose is { IsCompletedSuccessfully: true })
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
        if (args.Key == Key.Escape && _workspace.TryGetValue("ProfileMenuOpen", out var open) && open is true)
        {
            args.Handled = true;
            try { await _workspace.DispatchAsync("9to1.Browse.CloseProfileMenu", null); }
            catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse retained its profile-menu close task: {0}", failure); }
            return;
        }
        var command = args.Key switch
        {
            Key.T when args.KeyModifiers.HasFlag(KeyModifiers.Control) && !args.KeyModifiers.HasFlag(KeyModifiers.Alt) => args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "9to1.Browse.ReopenClosedTab" : "9to1.Browse.NewTab",
            Key.P when args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) => "9to1.Browse.NewPrivateTab",
            Key.W when args.KeyModifiers.HasFlag(KeyModifiers.Control) => "9to1.Browse.CloseTab",
            Key.R when args.KeyModifiers.HasFlag(KeyModifiers.Control) => "9to1.Browse.Reload",
            Key.Right when args.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift) &&
                _workspace.State.SplitOrientation == Haven.Core.SplitOrientation.Vertical => "9to1.Browse.WidenPrimaryPane",
            Key.Left when args.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift) &&
                _workspace.State.SplitOrientation == Haven.Core.SplitOrientation.Vertical => "9to1.Browse.NarrowPrimaryPane",
            Key.Down when args.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift) &&
                _workspace.State.SplitOrientation == Haven.Core.SplitOrientation.Horizontal => "9to1.Browse.WidenPrimaryPane",
            Key.Up when args.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift) &&
                _workspace.State.SplitOrientation == Haven.Core.SplitOrientation.Horizontal => "9to1.Browse.NarrowPrimaryPane",
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
    private CuiSceneHost? _findKeyScope;
    private Window? _menuWindow;
    private readonly Grid _root = new();
    private readonly Grid _surfaces = new();
    private readonly Canvas _dragAnchorLayer = new() { ClipToBounds = false, IsHitTestVisible = false };
    private readonly Border _tabDragAnchor = new() { Name = "BrowseTabDragAnchor", Width = 1, Height = 1, Background = Brushes.Transparent,
        IsHitTestVisible = false };
    private readonly Border _splitDivider = new() { Background = Brushes.Gray, Width = 1, IsVisible = false };
    private readonly Dictionary<IBrowseEngineTab, Control> _originalViews = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IBrowseAppContentSession, Border> _originalAppViews = new(ReferenceEqualityComparer.Instance);
    private BrowseNativeWorkspace.TabRow? _tabDragSource;
    private IPointer? _tabDragPointer;
    private IInputElement? _tabDragCapture;
    private Point _tabDragStart;
    private bool _tabDragActive;
    private BrowseNativeWorkspace.TabRow? _tabDropTarget;
    private BrowseNativeWorkspace.TabDropZone _tabDropZone;
    private Popup? _tabDragPreview;
    private readonly TextBlock _unavailable = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    public BrowseEngineViewport(BrowseNativeWorkspace workspace)
    {
        _workspace = workspace; _root.Children.Add(_surfaces); _root.Children.Add(_dragAnchorLayer);
        _dragAnchorLayer.Children.Add(_tabDragAnchor); Content = _root;
        _surfaces.ColumnDefinitions = new ColumnDefinitions("*,1,*");
        _surfaces.RowDefinitions = new RowDefinitions("*,1,*");
        _surfaces.Children.Add(_unavailable); _surfaces.Children.Add(_splitDivider);
        AttachedToVisualTree += OnAttached; DetachedFromVisualTree += OnDetached;
    }
    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _workspace.PropertyChanged += OnChanged; RefreshOriginalViews();
        // The same CUI scene owns standalone Browse and embedded Haven. Scope the
        // keyboard adapter here, rather than only to the standalone window.
        _findKeyScope = this.GetVisualAncestors().OfType<CuiSceneHost>().FirstOrDefault();
        _findKeyScope?.AddHandler(KeyDownEvent, OnFindKeyDown, RoutingStrategies.Tunnel);
        _findKeyScope?.AddHandler(InputElement.PointerPressedEvent, OnTransientMenuPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _menuWindow = TopLevel.GetTopLevel(this) as Window;
        if (_menuWindow is not null) _menuWindow.PropertyChanged += OnMenuWindowChanged;
        _findKeyScope?.AddHandler(InputElement.PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _findKeyScope?.AddHandler(InputElement.PointerMovedEvent, OnTabPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _findKeyScope?.AddHandler(InputElement.PointerReleasedEvent, OnTabPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _workspace.PropertyChanged -= OnChanged;
        _workspace.DismissTransientMenus();
        if (_menuWindow is not null) _menuWindow.PropertyChanged -= OnMenuWindowChanged;
        _menuWindow = null;
        _findKeyScope?.RemoveHandler(InputElement.PointerPressedEvent, OnTransientMenuPointerPressed);
        _findKeyScope?.RemoveHandler(KeyDownEvent, OnFindKeyDown);
        _findKeyScope?.RemoveHandler(InputElement.PointerPressedEvent, OnTabPointerPressed);
        _findKeyScope?.RemoveHandler(InputElement.PointerMovedEvent, OnTabPointerMoved);
        _findKeyScope?.RemoveHandler(InputElement.PointerReleasedEvent, OnTabPointerReleased);
        ClearTabDrag();
        _findKeyScope = null;
    }
    private void OnTransientMenuPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!_workspace.HasOpenTransientMenu) return;
        var source = args.Source as Control;
        // Native submenu content may return through the logical owner rather than
        // the visual tree. Check both; never treat an inside submenu press as outside.
        var controls = source is null ? Enumerable.Empty<Control>() :
            source.GetVisualAncestors().OfType<Control>().Concat(source.GetLogicalAncestors().OfType<Control>()).Prepend(source);
        if (controls.Any(control => control.Name is "BrowseProfileOverlay" or "BrowseManageUsersOverlay" or "HavenProfileButton")) return;
        _workspace.DismissTransientMenus();
        // Preserve the original pointer target/default action and its focus/caret.
    }

    private void OnMenuWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.WindowStateProperty && _menuWindow?.WindowState == WindowState.Minimized)
            _workspace.DismissTransientMenus();
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!_workspace.IsReady || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            !TryFindTabRow(args.Source, out _, out var row)) return;
        ClearTabDrag();
        _tabDragSource = row; _tabDragPointer = args.Pointer; _tabDragCapture = args.Pointer.Captured; _tabDragStart = args.GetPosition(this); _tabDragActive = false;
        // Capture loss is direct and does not reach the scene when capture moves
        // between sibling controls. Observe the exact captured input owner.
        if (_tabDragCapture is InputElement captured)
            captured.AddHandler(InputElement.PointerCaptureLostEvent, OnTabPointerCaptureLost, RoutingStrategies.Direct);
    }
    private void OnTabPointerMoved(object? sender, PointerEventArgs args)
    {
        if (_tabDragSource is null || !ReferenceEquals(_tabDragPointer, args.Pointer) ||
            !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (!_tabDragActive)
        {
            var position = args.GetPosition(this);
            var deltaX = position.X - _tabDragStart.X; var deltaY = position.Y - _tabDragStart.Y;
            if (deltaX * deltaX + deltaY * deltaY < 64) return;
            _tabDragActive = true;
            ShowTabDragPreview(args.GetPosition(this));
        }
        else MoveTabDragAnchor(args.GetPosition(this));
        UpdateTabDropTarget(args.GetPosition(_findKeyScope));
        args.Handled = true;
    }
    private async void OnTabPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        var source = _tabDragSource;
        if (source is null || !ReferenceEquals(_tabDragPointer, args.Pointer)) return;
        var wasDrag = _tabDragActive;
        if (wasDrag && _findKeyScope is { } currentScope) UpdateTabDropTarget(args.GetPosition(currentScope));
        var target = _tabDropTarget;
        var zone = _tabDropZone;
        ClearTabDrag();
        if (!wasDrag) return;
        if (!_workspace.IsCurrentTabRow(source)) return;
        args.Handled = true;
        if (target is null || zone is BrowseNativeWorkspace.TabDropZone.None or BrowseNativeWorkspace.TabDropZone.Invalid ||
            !_workspace.IsCurrentTabRow(target)) return;
        try
        {
            await _workspace.ApplyTabDropAsync(source, target, zone);
        }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse retained its original tab-drop task: {0}", failure); }
    }
    private bool TryFindTabRow(object? source, out Button button, out BrowseNativeWorkspace.TabRow row)
    {
        for (var control = source as Control; control is not null; control = control.GetVisualParent() as Control)
            if (control is Button candidate && candidate.Name == "BrowseTabButton")
            {
                if (TryGetTabRow(candidate, out var current) && current is not null)
                { button = candidate; row = current; return true; }
                break;
            }
        button = null!; row = null!; return false;
    }
    private bool TryGetTabRow(Button button, out BrowseNativeWorkspace.TabRow? row)
    {
        row = null;
        var stableId = CuiRuntimeIdentity.GetStableId(button);
        if (stableId is null) return false;
        var marker = stableId.LastIndexOf("|key:", StringComparison.Ordinal);
        if (marker < 0) return false;
        var valueStart = marker + 5;
        var lengthEnd = stableId.IndexOf(':', valueStart);
        if (lengthEnd < 0 || !int.TryParse(stableId.AsSpan(valueStart, lengthEnd - valueStart), out var length) || length < 1)
            return false;
        var keyStart = lengthEnd + 1;
        if (keyStart + length > stableId.Length) return false;
        return _workspace.TryGetCurrentTabRow(stableId.Substring(keyStart, length), out row);
    }
    private void OnTabPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (ReferenceEquals(_tabDragPointer, args.Pointer)) ClearTabDrag();
    }
    private void ShowTabDragPreview(Point position)
    {
        if (_tabDragSource is not { } source || _findKeyScope is not { } scope) return;
        MoveTabDragAnchor(position);
        if (_tabDragPreview is null)
        {
            var foreground = new SolidColorBrush(source.ForegroundColor);
            var background = scope.TryFindResource("CuiPanelBrush", out var panel) && panel is IBrush panelBrush
                ? panelBrush : Brushes.Transparent;
            var preview = new Border
            {
                Width = 190, Height = 34, Padding = new Thickness(12, 4), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2), BorderBrush = foreground, Background = background,
                Opacity = .92, IsHitTestVisible = false, Child = new TextBlock
                {
                    Text = source.Label, Foreground = foreground, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };
            _tabDragPreview = new Popup { PlacementTarget = _tabDragAnchor, Placement = PlacementMode.Bottom, IsLightDismissEnabled = false,
                IsHitTestVisible = false, TakesFocusFromNativeControl = false, Child = preview };
            CuiPopupPresentation.RequestTransparency(_tabDragPreview);
            _root.Children.Add(_tabDragPreview);
        }
        _tabDragPreview.IsOpen = true;
    }
    private void MoveTabDragAnchor(Point position)
    {
        Canvas.SetLeft(_tabDragAnchor, position.X);
        Canvas.SetTop(_tabDragAnchor, position.Y);
    }
    private void UpdateTabDropTarget(Point point)
    {
        if (_findKeyScope is not { } scope || !_tabDragActive)
        { _workspace.SetTabDropIndicator(null, BrowseNativeWorkspace.TabDropZone.None); _tabDropTarget = null; _tabDropZone = BrowseNativeWorkspace.TabDropZone.None; return; }
        // Hit-test the actual clipped viewport, so overflow arrows and rows
        // outside its visible bounds cannot receive a drop. Avoid scanning the
        // entire app scene for every held pointer movement.
        var hit = scope.InputHitTest(point) as Visual;
        var rowControl = hit?.GetSelfAndVisualAncestors().OfType<CuiContainerControl>()
            .FirstOrDefault(control => control.Name == "BrowseTabRow");
        var button = rowControl?.AuthoredChildren.OfType<Button>().FirstOrDefault(control => control.Name == "BrowseTabButton");
        if (rowControl is not null && button is not null && rowControl.TranslatePoint(default, scope) is { } origin &&
            TryGetTabRow(button, out var row) && row is not null)
        {
            var relativeX = Math.Clamp((point.X - origin.X) / Math.Max(1, rowControl.Bounds.Width), 0, 1);
            var zone = relativeX < .15 ? BrowseNativeWorkspace.TabDropZone.SwapBefore :
                relativeX < .35 ? BrowseNativeWorkspace.TabDropZone.SplitBefore :
                relativeX < .65 ? BrowseNativeWorkspace.TabDropZone.Group :
                relativeX < .85 ? BrowseNativeWorkspace.TabDropZone.SplitAfter : BrowseNativeWorkspace.TabDropZone.SwapAfter;
            if (_tabDragSource is not null && !_workspace.CanDropTabRows(_tabDragSource, row, zone))
                zone = BrowseNativeWorkspace.TabDropZone.Invalid;
            _tabDropTarget = row; _tabDropZone = zone; _workspace.SetTabDropIndicator(row, zone); return;
        }
        _tabDropTarget = null; _tabDropZone = BrowseNativeWorkspace.TabDropZone.None;
        _workspace.SetTabDropIndicator(null, BrowseNativeWorkspace.TabDropZone.None);
    }
    private void ClearTabDrag()
    {
        var preview = _tabDragPreview;
        var target = _tabDropTarget;
        var pointer = _tabDragPointer; var capture = _tabDragCapture; var wasDrag = _tabDragActive;
        _tabDragPreview = null; _tabDropTarget = null; _tabDropZone = BrowseNativeWorkspace.TabDropZone.None;
        _tabDragSource = null; _tabDragPointer = null; _tabDragCapture = null; _tabDragActive = false;
        if (capture is InputElement captured)
            captured.RemoveHandler(InputElement.PointerCaptureLostEvent, OnTabPointerCaptureLost);
        if (preview is not null)
        {
            preview.IsOpen = false; preview.Child = null; _root.Children.Remove(preview);
        }
        // Capture loss resets the donor Button's pressed state even when the
        // drag consumed release or was cancelled before release. A normal click
        // keeps its capture so the Button can still perform its own release.
        if (wasDrag && pointer is not null && capture is not null && ReferenceEquals(pointer.Captured, capture))
            pointer.Capture(null);
        target?.SetDropZone(BrowseNativeWorkspace.TabDropZone.None);
        _workspace.SetTabDropIndicator(null, BrowseNativeWorkspace.TabDropZone.None);
    }
    private async void OnFindKeyDown(object? sender, KeyEventArgs args)
    {
        if (_tabDragActive && args.Key == Key.Escape)
        { ClearTabDrag(); args.Handled = true; return; }
        if (args.Handled || !_workspace.IsReady || _findKeyScope is not { } scope) return;
        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None && _workspace.HasOpenTransientMenu)
        {
            _workspace.DismissTransientMenus(returnFromSubmenu: true);
            args.Handled = true;
            return;
        }
        var findBox = scope.GetLogicalDescendants().OfType<TextBox>()
            .SingleOrDefault(control => control.Name == "BrowseFindBox");
        var inFindBox = findBox?.IsFocused == true;
        var closeFind = args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None && inFindBox;
        // Native apps keep their own Ctrl+F/F3/editor shortcuts. Only a real selected
        // web renderer with the existing find capability admits browser page search.
        if (!closeFind && (!_workspace.State.SelectedTab.Address.IsWeb ||
            _workspace.IsActionAvailable("9to1.Browse.FindNext") != true)) return;
        var command = args.Key switch
        {
            Key.F when args.KeyModifiers == KeyModifiers.Control => "9to1.Browse.OpenFind",
            Key.F3 when args.KeyModifiers == KeyModifiers.None => "9to1.Browse.FindNext",
            Key.F3 when args.KeyModifiers == KeyModifiers.Shift => "9to1.Browse.FindPrevious",
            Key.Enter when inFindBox && args.KeyModifiers == KeyModifiers.None => "9to1.Browse.FindNext",
            Key.Enter when inFindBox && args.KeyModifiers == KeyModifiers.Shift => "9to1.Browse.FindPrevious",
            Key.Escape when closeFind => "9to1.Browse.CloseProfileMenu",
            _ => null
        };
        if (command is null || _workspace.IsActionAvailable(command) != true) return;
        args.Handled = true;
        var tabId = _workspace.State.SelectedTabId;
        try
        {
            await _workspace.DispatchAsync(command, null);
            if (!_workspace.IsReady || _workspace.State.SelectedTabId != tabId) return;
            if (command == "9to1.Browse.OpenFind")
                await _workspace.FocusFindControlAsync(() =>
                {
                    if (!ReferenceEquals(_findKeyScope, scope) || _workspace.State.SelectedTabId != tabId ||
                        !_workspace.TryGetValue("ProfileMenuOpen", out var open) || open is not true) return;
                    var current = scope.GetLogicalDescendants().OfType<TextBox>()
                        .SingleOrDefault(control => control.Name == "BrowseFindBox");
                    if (current?.IsEffectivelyVisible == true) { current.Focus(); current.SelectAll(); }
                });
            else if (closeFind && _workspace.OriginalChrome.ObserveOriginalEngine(tabId) is IBrowseNativeEngineTab native)
                native.OriginalNativeView.Focus();
        }
        catch (Exception failure)
        { System.Diagnostics.Trace.TraceError("Browse retained its original page-find task: {0}", failure); }
    }
    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_tabDragSource is not null && !IsTabDragContextCurrent()) ClearTabDrag();
        RefreshOriginalViews();
    }
    private bool IsTabDragContextCurrent()
    {
        if (!_workspace.IsReady || _tabDragSource is not { } source || !_workspace.IsCurrentTabRow(source)) return false;
        if (_tabDropTarget is not { } target) return true;
        if (!_workspace.IsCurrentTabRow(target)) return false;
        return _tabDropZone is BrowseNativeWorkspace.TabDropZone.None or BrowseNativeWorkspace.TabDropZone.Invalid ||
            _workspace.CanDropTabRows(source, target, _tabDropZone);
    }
    private void RefreshOriginalViews()
    {
        if (!_workspace.IsReady) return;
        var state = _workspace.State; var current = new HashSet<IBrowseEngineTab>(ReferenceEqualityComparer.Instance);
        var currentApps = new HashSet<IBrowseAppContentSession>(ReferenceEqualityComparer.Instance);
        var split = state.PaneTabIds.Count == 2;
        _splitDivider.IsVisible = split;
        if (split && state.SplitOrientation == Haven.Core.SplitOrientation.Horizontal)
        {
            _surfaces.ColumnDefinitions = new ColumnDefinitions("*");
            _surfaces.RowDefinitions = new RowDefinitions(SplitDefinitions(state.PrimaryPaneRatio));
            _splitDivider.Width = double.NaN; _splitDivider.Height = 1;
            Grid.SetColumn(_splitDivider, 0); Grid.SetRow(_splitDivider, 1);
            Grid.SetColumnSpan(_splitDivider, 1); Grid.SetRowSpan(_splitDivider, 1);
        }
        else if (split)
        {
            _surfaces.ColumnDefinitions = new ColumnDefinitions(SplitDefinitions(state.PrimaryPaneRatio));
            _surfaces.RowDefinitions = new RowDefinitions("*");
            _splitDivider.Width = 1; _splitDivider.Height = double.NaN;
            Grid.SetColumn(_splitDivider, 1); Grid.SetRow(_splitDivider, 0);
            Grid.SetColumnSpan(_splitDivider, 1); Grid.SetRowSpan(_splitDivider, 1);
        }
        else
        {
            _surfaces.ColumnDefinitions = new ColumnDefinitions("*,1,*");
            _surfaces.RowDefinitions = new RowDefinitions("*,1,*");
        }
        var selectedHasView = false;
        foreach (var tab in state.Tabs)
        {
            if (_workspace.OriginalChrome.ObserveOriginalAppContent(tab.Id) is IBrowseNativeAppContentSession app)
            {
                currentApps.Add(app);
                if (app.OriginalInitialization is { IsCompletedSuccessfully: true } && app.IsPublicationCurrent)
                {
                    var view = app.OriginalNativeView;
                    if (!_originalAppViews.TryGetValue(app, out var mountedApp))
                    {
                        if (view.Parent is not null) throw new InvalidOperationException("The original app view belongs to another native owner.");
                        // The app owns its authored visibility and bindings. The
                        // host owns only this stable tab presentation boundary.
                        mountedApp = new Border { Child = view };
                        _originalAppViews.Add(app, mountedApp); _surfaces.Children.Add(mountedApp);
                    }
                    else if (!ReferenceEquals(mountedApp.Child, view)) throw new InvalidOperationException("The app replaced its original native view.");
                    var visible = split ? state.PaneTabIds.Contains(tab.Id) : tab.Id == state.SelectedTabId;
                    mountedApp.IsVisible = visible;
                    PlaceInPane(mountedApp, state.PaneTabIds, tab.Id, state.SplitOrientation);
                    if (tab.Id == state.SelectedTabId && mountedApp.IsVisible) selectedHasView = true;
                }
                else if (_originalAppViews.TryGetValue(app, out var retained)) retained.IsVisible = false;
            }
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
            originalView.IsVisible = (split ? state.PaneTabIds.Contains(tab.Id) : tab.Id == state.SelectedTabId) &&
                tab.Address.IsWeb && tab.EngineState == BrowseEngineState.Ready;
            PlaceInPane(originalView, state.PaneTabIds, tab.Id, state.SplitOrientation);
            if (tab.Id == state.SelectedTabId && originalView.IsVisible) selectedHasView = true;
        }
        foreach (var original in _originalViews.Keys.Where(original => !current.Contains(original)).ToArray())
        {
            if (original is IBrowseNativeEngineTab native && !native.CanRetireOriginalView)
                throw new InvalidOperationException("The original renderer still owns its native view.");
            _surfaces.Children.Remove(_originalViews[original]); _originalViews.Remove(original);
        }
        foreach (var original in _originalAppViews.Keys.Where(original => !currentApps.Contains(original)).ToArray())
        {
            if (original.OriginalClose is not { IsCompletedSuccessfully: true } || !original.CanRetireOriginalView)
                throw new InvalidOperationException("The original app still owns its native view.");
            _surfaces.Children.Remove(_originalAppViews[original]); _originalAppViews.Remove(original);
        }
        // Inactive views remain mounted: selecting a tab does not destroy its
        // native adapter, authentication state or navigation history.
        var selected = state.SelectedTab;
        _unavailable.IsVisible = !selectedHasView && selected.NativePage == Haven.Browser.BrowserNativePage.None;
        _unavailable.Text = !selected.Address.IsWeb || selected.EngineState == BrowseEngineState.Crashed ? selected.Status :
            "The native " + (selected.Engine == BrowseEngineKind.Gecko ? "Firefox" : "Chromium") + " renderer is not connected. Tab identity, bookmarks and browser settings are retained.";
        PlaceInPane(_unavailable, state.PaneTabIds, selected.Id, state.SplitOrientation);
    }

    private static void PlaceInPane(Control view, IReadOnlyList<Guid> paneTabIds, Guid tabId,
        Haven.Core.SplitOrientation orientation)
    {
        if (paneTabIds.Count != 2)
        {
            Grid.SetColumn(view, 0); Grid.SetRow(view, 0);
            Grid.SetColumnSpan(view, 3); Grid.SetRowSpan(view, 3); return;
        }
        var pane = Array.IndexOf(paneTabIds.ToArray(), tabId);
        if (orientation == Haven.Core.SplitOrientation.Horizontal)
        {
            Grid.SetColumn(view, 0); Grid.SetRow(view, pane == 1 ? 2 : 0);
            Grid.SetColumnSpan(view, 1); Grid.SetRowSpan(view, 1);
        }
        else
        {
            Grid.SetColumn(view, pane == 1 ? 2 : 0); Grid.SetRow(view, 0);
            Grid.SetColumnSpan(view, 1); Grid.SetRowSpan(view, 1);
        }
    }

    private static string SplitDefinitions(double ratio) => ratio.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
        "*,1," + (1 - ratio).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "*";
}
