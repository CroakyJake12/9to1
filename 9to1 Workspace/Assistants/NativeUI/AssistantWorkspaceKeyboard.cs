using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;

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
        _bindings.WorkspaceDisclosureChanged += OnDisclosureChanged;
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
            layout.AuthoredChildren.OfType<CuiContainerControl>().FirstOrDefault(c => c.Name == "assistant-workspace") is not { } pane ||
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

        var open = pane.IsVisible && _bindings.VisibleWorkspaceIdentity is not null;
        var split = open && _layout.Bounds.Width >= SplitMinWidth;
        var compact = open && !split;
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
        if (focusWasInMain && _bindings.VisibleWorkspaceIdentity is { } identity) ScheduleDisclosureFocus(identity, true);
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled || !_compactOpen || !ReferenceEquals(sender, _backdrop) ||
            _bindings.VisibleWorkspaceIdentity is not { } identity) return;

        _bindings.DispatchAsync("assistants.workspace.close", identity).GetAwaiter().GetResult();
        e.Handled = true;
    }

    private void OnWorkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None ||
            _bindings.VisibleWorkspaceIdentity is not { } identity ||
            e.Source is not Visual source) return;

        var pane = Find<Control>("assistant-workspace");
        var toggle = Find<Button>("assistant-workspace-toggle");
        if (pane is null || !pane.IsEffectivelyVisible || !ReferenceEquals(source, toggle) &&
            !ReferenceEquals(source, pane) && !source.GetVisualAncestors().Contains(pane)) return;

        // Exactly the same synchronous presentation-only owner used by the authored close button.
        _bindings.DispatchAsync("assistants.workspace.close", identity).GetAwaiter().GetResult();
        e.Handled = true;
    }

    private void OnDisclosureChanged(AssistantIdentity identity, bool opened)
    {
        WireLayout();
        ArrangeWorkspace();
        ScheduleDisclosureFocus(identity, opened);
    }

    private void ScheduleDisclosureFocus(AssistantIdentity identity, bool opened)
    {
        var generation = ++_focusGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            // A route or identity can change between the click and layout. Never steal focus
            // from another Assistant, screen, or an earlier iteration of the disclosure.
            if (_disposed || generation != _focusGeneration || _bindings.WorkRouteIdentity != identity ||
                (_bindings.VisibleWorkspaceIdentity == identity) != opened) return;
            var control = Find<Button>(opened ? "assistant-workspace-close" : "assistant-workspace-toggle");
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
        _bindings.WorkspaceDisclosureChanged -= OnDisclosureChanged;
    }
}
