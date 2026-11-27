using System.ComponentModel;
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
    private const double NavigationCompactWidth = 750;
    private const double NavigationWidth = 218;
    private const double CompactNavigationWidth = 230;
    private const double WorkspaceSplitMinWidth = 1038; // 218px navigation + 360px Workspace + 460px conversation.
    private const double PaneWidth = 360;
    private const double BackdropTargetWidth = 48;
    private CuiContainerControl? _layout, _pane, _navigation;
    private Control? _main, _backdrop, _activeOverlay;
    private bool _arranging, _arrangePosted, _disposed;
    private const string BackdropOwner = "Assistant workspace compact backdrop";
    private const string LayoutOwner = "Assistant responsive work layout";
    private IDisposable? _coveredConversation;
    private int _focusGeneration;

    internal AssistantWorkspaceKeyboard(Control work, AssistantsCuiBindings bindings)
    {
        _work = work; _bindings = bindings;
        _work.KeyDown += OnWorkKeyDown;
        _work.AttachedToVisualTree += OnAttached;
        WireLayout();
        _bindings.WorkDisclosureChanged += OnDisclosureChanged;
        _bindings.PropertyChanged += OnBindingsChanged;
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
            layout.AuthoredChildren.OfType<CuiContainerControl>().FirstOrDefault(c => c.Name == "assistant-navigation") is not { } navigation ||
            layout.AuthoredChildren.FirstOrDefault(c => c.Name == "assistant-work-main") is not { } main ||
            layout.AuthoredChildren.FirstOrDefault(c => c.Name == "assistant-workspace-backdrop") is not { } backdrop) return;

        _layout = layout; _pane = pane; _navigation = navigation; _main = main; _backdrop = backdrop;
        backdrop.Tapped += OnBackdropTapped;
        layout.SizeChanged += OnLayoutSizeChanged;
        pane.PropertyChanged += OnPaneChanged;
        navigation.PropertyChanged += OnPaneChanged;
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

    private void OnBindingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Work is retained while Home/configuration is visible. A route/identity publication
        // must establish its lanes before the first interaction, even without a size change.
        if (e.PropertyName is "ShowWork" or "WorkspaceAssistantIdentity")
        {
            WireLayout();
            ArrangeWorkspace();
        }
    }

    private void OnPaneChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty) ArrangeWorkspace();
    }

    private void ArrangeWorkspace()
    {
        if (_disposed || _arranging || _layout?.Layout is not Grid || _main is not { } main || _pane is not { } pane ||
            _navigation is not { } navigation || _backdrop is not { } backdrop) return;

        _arranging = true;
        try
        {
            var focused = TopLevel.GetTopLevel(_work)?.FocusManager?.GetFocusedElement() as Control;
            var focusedInNavigation = focused is not null &&
                (ReferenceEquals(focused, navigation) || focused.GetVisualAncestors().Contains(navigation));
            var focusedNavigationToggle = ReferenceEquals(focused, Find<Button>("assistant-navigation-toggle"));
            var focusedInMain = main.IsKeyboardFocusWithin;
            var previousOverlay = _activeOverlay;
            var width = _layout.Bounds.Width;
            var compactNavigation = width > 0 && width <= NavigationCompactWidth;
            _bindings.SetAssistantWorkCompact(compactNavigation);

            var navigationOpen = _bindings.IsAssistantNavigationOpen;
            var navigationDocked = !compactNavigation && _bindings.HasAssistantWorkRoute;
            var workspaceOpen = _bindings.VisibleWorkspaceIdentity is not null && !navigationOpen;
            var workspaceSplit = workspaceOpen && width >= WorkspaceSplitMinWidth;
            var workspaceOverlay = workspaceOpen && !workspaceSplit;
            var centreWidth = Math.Max(0, width - (navigationDocked ? NavigationWidth : 0));
            _activeOverlay = navigationOpen ? navigation : workspaceOverlay ? pane : null;
            var navigationFocusWillHide = focused is not null &&
                (compactNavigation && !navigationOpen && focusedInNavigation || !compactNavigation &&
                    (focusedNavigationToggle || focused.Name is "assistant-navigation-close" or "assistant-navigation-home" or "assistant-navigation-to-workspace"));

            // Both panels and the composer remain mounted. Only the compact navigation drawer
            // replaces Workspace; the wide navigation stays available beside a Workspace overlay.
            // Responsive placement is a CUI overlay, so authored Column values cannot restore a stale Auto lane.
            // Fixed side lanes keep the centre stable while retained panels are measured, hidden or republished.
            CuiLayoutProperties.SetLiteral(_layout, "ColumnSizes",
                FormattableString.Invariant($"{(navigationDocked ? NavigationWidth : 0)} 1fr {(workspaceSplit ? PaneWidth : 0)}"),
                CuiPropertyLevel.Overlay, LayoutOwner);
            CuiLayoutProperties.SetLiteral(navigation, "Column", compactNavigation ? "1" : "0", CuiPropertyLevel.Overlay, LayoutOwner);
            CuiLayoutProperties.SetLiteral(pane, "Column", workspaceSplit ? "2" : "1", CuiPropertyLevel.Overlay, LayoutOwner);
            navigation.HorizontalAlignment = compactNavigation ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            navigation.Width = compactNavigation ? Math.Max(0, Math.Min(CompactNavigationWidth, width - BackdropTargetWidth)) : NavigationWidth;
            pane.HorizontalAlignment = workspaceOverlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            pane.Width = workspaceOverlay ? Math.Max(0, Math.Min(PaneWidth, centreWidth - BackdropTargetWidth)) : PaneWidth;
            if (navigationOpen) KeyboardNavigation.SetTabNavigation(navigation, KeyboardNavigationMode.Cycle);
            else navigation.ClearValue(KeyboardNavigation.TabNavigationProperty);
            if (workspaceOverlay) KeyboardNavigation.SetTabNavigation(pane, KeyboardNavigationMode.Cycle);
            else pane.ClearValue(KeyboardNavigation.TabNavigationProperty);

            if (_activeOverlay is not null)
                _coveredConversation ??= CuiLifecycleProperties.RestrictInteraction(main);
            else
            {
                _coveredConversation?.Dispose();
                _coveredConversation = null;
            }
            CuiLifecycleProperties.SetLiteral(backdrop, "Active", _activeOverlay is not null ? "True" : "False", CuiPropertyLevel.Overlay, BackdropOwner);

            if (_bindings.WorkRouteIdentity is not { } identity) return;
            if (previousOverlay is null && _activeOverlay is not null && focusedInMain)
                ScheduleDisclosureFocus(identity, navigationOpen ? WorkFocus.NavigationClose : WorkFocus.WorkspaceClose);
            // Visibility can settle after its native focus is cleared. Use the captured focus and intended
            // mode, rather than observing IsEffectivelyVisible again after publishing the new disclosure.
            else if (navigationFocusWillHide)
                ScheduleDisclosureFocus(identity, navigationOpen ? WorkFocus.NavigationClose : workspaceOverlay ? WorkFocus.WorkspaceClose :
                    compactNavigation ? WorkFocus.NavigationToggle : WorkFocus.Home);
            else if (ReferenceEquals(previousOverlay, navigation) && !navigation.IsVisible && focused is null)
                ScheduleDisclosureFocus(identity, workspaceOverlay ? WorkFocus.WorkspaceClose : WorkFocus.NavigationToggle);
        }
        finally { _arranging = false; }
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled || _activeOverlay is null || !ReferenceEquals(sender, _backdrop) ||
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
        RetainedContent(_work).OfType<T>().FirstOrDefault(control => control.Name == id);

    private static IEnumerable<Control> RetainedContent(Control root)
    {
        yield return root;
        // Match the CUI loader's authored/content ownership. A scene host already owns its
        // root before its ContentControl template exposes native visual descendants.
        IEnumerable<Control> children = root switch
        {
            CuiContainerControl container => container.AuthoredChildren,
            ContentControl { Content: Control child } => [child],
            Decorator { Child: Control child } => [child],
            Panel panel => panel.Children,
            ItemsControl items => items.Items.OfType<Control>(),
            _ => []
        };
        foreach (var child in children)
            foreach (var descendant in RetainedContent(child)) yield return descendant;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _work.KeyDown -= OnWorkKeyDown;
        _work.AttachedToVisualTree -= OnAttached;
        if (_layout is not null) _layout.SizeChanged -= OnLayoutSizeChanged;
        if (_pane is not null)
        {
            _pane.PropertyChanged -= OnPaneChanged;
            _pane.ClearValue(KeyboardNavigation.TabNavigationProperty);
        }
        if (_navigation is not null)
        {
            _navigation.PropertyChanged -= OnPaneChanged;
            _navigation.ClearValue(KeyboardNavigation.TabNavigationProperty);
        }
        if (_layout is not null && CuiPropertyPresentation.TryGetExisting(_layout, out var layoutProperties))
            layoutProperties?.Remove("ColumnSizes", CuiPropertyLevel.Overlay, LayoutOwner);
        foreach (var panel in new[] { _navigation, _pane })
            if (panel is not null && CuiPropertyPresentation.TryGetExisting(panel, out var panelProperties))
                panelProperties?.Remove("Column", CuiPropertyLevel.Overlay, LayoutOwner);
        _activeOverlay = null;
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
        _bindings.PropertyChanged -= OnBindingsChanged;
    }
}
