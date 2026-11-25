using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using WorkFocus = HavenOS.Apps.Assistants.NativeUI.AssistantsCuiBindings.AssistantWorkFocus;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Responsive disclosure and keyboard ownership of this exact native Work tree.</summary>
internal sealed class AssistantWorkspaceKeyboard : IDisposable
{
    private readonly Control _work;
    private readonly AssistantsCuiBindings _bindings;
    private const double SplitMinWidth = 820;
    private const double PaneWidth = 360;
    private const double BackdropTargetWidth = 48;
    private CuiContainerControl? _layout, _pane;
    private Control? _main, _backdrop;
    private bool _compactOpen, _arrangePosted, _disposed;
    private const string BackdropOwner = "Assistant workspace compact backdrop";
    private IDisposable? _coveredConversation;
    private int _focusGeneration;

    internal AssistantWorkspaceKeyboard(Control work, AssistantsCuiBindings bindings)
    {
        _work = work; _bindings = bindings;
        _work.KeyDown += OnWorkKeyDown;
        _work.AttachedToVisualTree += OnAttached;
        WireLayout();
        _bindings.WorkDisclosureChanged += OnDisclosureChanged;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        WireLayout();
        ArrangeWorkspace();
    }

    private void WireLayout()
    {
        if (_layout is not null ||
            Find<CuiContainerControl>("assistant-work-layout") is not { Layout: Grid } layout ||
            layout.AuthoredChildren.OfType<CuiContainerControl>().FirstOrDefault(c => c.Name == "assistant-work-pane") is not { } pane ||
            layout.AuthoredChildren.FirstOrDefault(c => c.Name == "assistant-work-main") is not { } main ||
            layout.AuthoredChildren.FirstOrDefault(c => c.Name == "assistant-workspace-backdrop") is not { } backdrop) return;

        _layout = layout; _pane = pane; _main = main; _backdrop = backdrop;
        backdrop.Tapped += OnBackdropTapped;
        layout.SizeChanged += OnLayoutSizeChanged;
        pane.PropertyChanged += OnPaneChanged;
        ArrangeWorkspace();
    }

    private void OnLayoutSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_arrangePosted || _disposed) return;
        _arrangePosted = true;
        // Avalonia raises SizeChanged inside a layout pass. Apply retained cells and input
        // ownership together after that pass instead of mutating the measured tree in it.
        Dispatcher.UIThread.Post(() =>
        {
            _arrangePosted = false;
            ArrangeWorkspace();
        }, DispatcherPriority.Input);
    }

    private void OnPaneChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty) ArrangeWorkspace();
    }

    private void ArrangeWorkspace()
    {
        if (_disposed || _layout?.Layout is not Grid || _main is not { } main || _pane is not { } pane ||
            _backdrop is not { } backdrop) return;

        var width = _layout.Bounds.Width;
        var narrow = width > 0 && width < SplitMinWidth;
        _bindings.SetAssistantWorkCompact(narrow);
        var navigation = _bindings.IsAssistantNavigationOpen;
        var open = pane.IsVisible && (navigation || _bindings.VisibleWorkspaceIdentity is not null);
        var split = open && !narrow;
        var compact = open && narrow;
        var focusWasInMain = compact && !_compactOpen && main.IsKeyboardFocusWithin;
        _compactOpen = compact;

        // Changes presentation only: keep the same mounted composer, draft, pane and authored actions.
        // The retained conversation is visible through the scrim but is not pointer- or keyboard-active.
        if (compact)
            KeyboardNavigation.SetTabNavigation(pane, KeyboardNavigationMode.Cycle);
        else
            pane.ClearValue(KeyboardNavigation.TabNavigationProperty);
        pane.HorizontalAlignment = compact ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        pane.Width = compact ? Math.Max(0, Math.Min(PaneWidth, _layout.Bounds.Width - BackdropTargetWidth)) : PaneWidth;
        Grid.SetColumn(pane, split ? 1 : 0);
        if (compact)
            _coveredConversation ??= CuiLifecycleProperties.RestrictInteraction(main);
        else
        {
            _coveredConversation?.Dispose();
            _coveredConversation = null;
        }
        CuiLifecycleProperties.SetLiteral(backdrop, "Active", compact ? "True" : "False", CuiPropertyLevel.Overlay, BackdropOwner);
        if (focusWasInMain && _bindings.WorkRouteIdentity is { } identity)
            ScheduleDisclosureFocus(identity, navigation ? WorkFocus.NavigationClose : WorkFocus.WorkspaceClose);
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled || !_compactOpen || !ReferenceEquals(sender, _backdrop) ||
            _bindings.WorkRouteIdentity is not { } identity) return;

        e.Handled = CloseActivePane(identity);
    }

    private void OnWorkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None ||
            _bindings.WorkRouteIdentity is not { } identity || e.Source is not Visual source) return;

        var navigation = _bindings.IsAssistantNavigationOpen;
        var pane = Find<Control>(navigation ? "assistant-navigation" : "assistant-workspace");
        var toggle = Find<Button>(navigation ? "assistant-navigation-toggle" : "assistant-workspace-toggle");
        if (pane is null || !pane.IsEffectivelyVisible || !ReferenceEquals(source, toggle) &&
            !ReferenceEquals(source, pane) && !source.GetVisualAncestors().Contains(pane)) return;

        e.Handled = CloseActivePane(identity);
    }

    private bool CloseActivePane(AssistantIdentity identity)
    {
        var command = _bindings.IsAssistantNavigationOpen ? "assistants.navigation.close" : "assistants.workspace.close";
        if (_bindings.IsActionAvailable(command) != true) return false;
        // Exactly the same synchronous presentation-only owners as the authored close buttons.
        _bindings.DispatchAsync(command, identity).GetAwaiter().GetResult();
        return true;
    }

    private void OnDisclosureChanged(AssistantIdentity identity, WorkFocus focus)
    {
        WireLayout();
        ArrangeWorkspace();
        ScheduleDisclosureFocus(identity, focus);
    }

    private void ScheduleDisclosureFocus(AssistantIdentity identity, WorkFocus focus)
    {
        var generation = ++_focusGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            // A route or identity can change between the click and layout. Never steal focus
            // from another Assistant, screen, or an earlier iteration of the disclosure.
            if (_disposed || generation != _focusGeneration || _bindings.WorkRouteIdentity != identity) return;

            var active = _bindings.IsAssistantNavigationOpen ? WorkFocus.NavigationClose :
                _bindings.VisibleWorkspaceIdentity == identity ? WorkFocus.WorkspaceClose : (WorkFocus?)null;
            if (active is { } current ? current != focus : focus is WorkFocus.WorkspaceClose or WorkFocus.NavigationClose)
                return;

            var id = focus switch
            {
                WorkFocus.WorkspaceClose => "assistant-workspace-close",
                WorkFocus.NavigationClose => "assistant-navigation-close",
                WorkFocus.NavigationToggle => "assistant-navigation-toggle",
                WorkFocus.Home => "work-home",
                _ => "assistant-workspace-toggle"
            };
            var control = Find<Button>(id);
            if (control is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }) control.Focus();
        }, DispatcherPriority.Input);
    }

    private T? Find<T>(string id) where T : Control =>
        _work.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == id);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _work.KeyDown -= OnWorkKeyDown;
        _work.AttachedToVisualTree -= OnAttached;
        if (_layout is not null) _layout.SizeChanged -= OnLayoutSizeChanged;
        if (_pane is not null) _pane.PropertyChanged -= OnPaneChanged;
        _coveredConversation?.Dispose();
        _coveredConversation = null;
        if (_backdrop is not null)
        {
            _backdrop.Tapped -= OnBackdropTapped;
            // The original CUI host may already have retired this projection.
            if (CuiPropertyPresentation.TryGetExisting(_backdrop, out var properties))
                properties?.Remove("Active", CuiPropertyLevel.Overlay, BackdropOwner);
        }
        _bindings.WorkDisclosureChanged -= OnDisclosureChanged;
    }
}
