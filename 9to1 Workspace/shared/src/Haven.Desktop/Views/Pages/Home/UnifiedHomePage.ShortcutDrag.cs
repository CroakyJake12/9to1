using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private const string ShortcutDragAutomationPrefix = "new-tab-shortcut-drag-";
    private const string ShortcutDragPresentation = "New Tab shortcut drag";
    private ShortcutDrag? _shortcutDrag;

    private sealed record ShortcutDrag(IPointer Pointer, Point Start, Button Handle, CuiContainerControl Source,
        string SourceId, ImmutableArray<NewTabShortcutPlacement> Order, CuiContainerControl Grid, long Selection,
        bool IsDragging = false, CuiContainerControl? Target = null);

    private bool CanOpenNewTabShortcutEditor => CanEditShortcuts && !ShortcutSurfaceOpen &&
        !_widgetPickerOpen && _widgetOptionsId is null && _widgetDrag is null;
    private bool CanReorderNewTabShortcuts => IsNewTabCustomising && CanOpenNewTabShortcutEditor && SavedShortcuts.Length > 1;
    private bool CanOpenNewTabWidgetEditor => CanEditWidgetLayout && !_widgetPickerOpen && !ShortcutSurfaceOpen && _shortcutDrag is null;

    private void PublishNewTabEditorAvailability()
    {
        var canOpenWidgetEditor = CanOpenNewTabWidgetEditor && !_widgetBusy;
        Set(_newTabCustomiseAddWidget, "IsEnabled", canOpenWidgetEditor);
        _widgetBindings.Set("WidgetCanOpenEditor", canOpenWidgetEditor);
        _railBindings.SetNewTabShortcut("CanOpenEditor", CanOpenNewTabShortcutEditor);
        _railBindings.SetNewTabShortcut("CanReorder", CanReorderNewTabShortcuts);
    }

    private void RegisterNewTabShortcutDrag()
    {
        AddHandler(PointerPressedEvent, OnShortcutPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnShortcutPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnShortcutPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        DetachedFromVisualTree += OnShortcutDragDetached;
    }

    private void StopNewTabShortcutDrag()
    {
        EndNewTabShortcutDrag();
        RemoveHandler(PointerPressedEvent, OnShortcutPointerPressed);
        RemoveHandler(PointerMovedEvent, OnShortcutPointerMoved);
        RemoveHandler(PointerReleasedEvent, OnShortcutPointerReleased);
        DetachedFromVisualTree -= OnShortcutDragDetached;
    }

    private void OnShortcutDragDetached(object? sender, VisualTreeAttachmentEventArgs args) => EndNewTabShortcutDrag();

    private void OnShortcutPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (_shortcutDrag is not null || !CanReorderNewTabShortcuts || _newTabShortcuts is not { } grid ||
            args.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            args.Source is not Visual hit ||
            hit.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(control =>
                control.Name == "home-newtab-shortcut-drag" && control.IsEffectivelyEnabled) is not { } handle ||
            FindShortcutDragCard(handle) is not { } source || !grid.IsVisualAncestorOf(source) ||
            GetShortcutDragCardId(source) is not { } id)
            return;
        var drag = new ShortcutDrag(args.Pointer, args.GetPosition(this), handle, source, id, SavedShortcuts, grid, _selection);
        _shortcutDrag = drag;
        handle.PointerCaptureLost += OnShortcutPointerCaptureLost;
        args.Pointer.Capture(handle);
        // Capture and focus may synchronously change the active page or cancel this gesture.
        if (!ReferenceEquals(_shortcutDrag, drag)) return;
        if (!ReferenceEquals(args.Pointer.Captured, handle) || !IsCurrentShortcutDrag(drag))
        {
            EndNewTabShortcutDrag();
            return;
        }
        args.PreventGestureRecognition();
        handle.Focus(NavigationMethod.Pointer);
        if (ReferenceEquals(_shortcutDrag, drag) && !IsCurrentShortcutDrag(drag))
            EndNewTabShortcutDrag();
    }

    private void OnShortcutPointerMoved(object? sender, PointerEventArgs args)
    {
        if (_shortcutDrag is not { } drag || args.Pointer != drag.Pointer) return;
        if (!IsCurrentShortcutDrag(drag))
        {
            EndNewTabShortcutDrag();
            return;
        }
        if (!drag.IsDragging)
        {
            var offset = args.GetPosition(this) - drag.Start;
            if (Math.Abs(offset.X) < 6 && Math.Abs(offset.Y) < 6) return;
            _shortcutDrag = drag = drag with { IsDragging = true };
            drag.Grid.PointerCaptureLost += OnShortcutPointerCaptureLost;
            args.Pointer.Capture(drag.Grid);
            if (!ReferenceEquals(_shortcutDrag, drag)) return;
            if (!ReferenceEquals(args.Pointer.Captured, drag.Grid) || !IsCurrentShortcutDrag(drag))
            {
                EndNewTabShortcutDrag();
                return;
            }
            SetShortcutDragPresentation(drag.Source, .66);
        }
        var target = FindShortcutDropTarget(drag, args);
        if (target != drag.Target)
        {
            SetShortcutDragPresentation(drag.Target, null);
            SetShortcutDragPresentation(target, .78);
            _shortcutDrag = drag with { Target = target };
        }
        args.Handled = true;
    }

    private void OnShortcutPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_shortcutDrag is not { } drag || args.Pointer != drag.Pointer ||
            args.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
            return;
        var selection = drag.Selection;
        var target = drag.IsDragging && IsCurrentShortcutDrag(drag)
            ? FindShortcutDropTarget(drag, args) : null;
        EndNewTabShortcutDrag(cancelPress: false);
        if (!drag.IsDragging) return;
        args.Handled = true;
        if (target is null || GetShortcutDragCardId(target) is not { } targetId) return;
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsAlive && _section == "newtab" && _selection == selection);
            // Capture release can reenter the UI, and the admitted original may
            // start later. Reject a stale drop before mutating saved preferences.
            if (!original.IsAcceptedPublicationCurrent || !CanReorderNewTabShortcuts ||
                _shortcutDrag is not null || SavedShortcuts != drag.Order)
                return;
            if (await SaveNewTabShortcutOrderAsync(drag.SourceId, layout => layout with
                {
                    Shortcuts = ReorderShortcuts(layout.Shortcuts, drag.SourceId, targetId)
                }, original, original.Token))
                FocusNewTabShortcutGrip(drag.SourceId);
        }, task => _shortcutReorderAction = task);
    }

    private void OnShortcutPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (_shortcutDrag is { } drag && args.Pointer == drag.Pointer &&
            ReferenceEquals(sender, drag.IsDragging ? drag.Grid : drag.Handle))
            EndNewTabShortcutDrag();
    }

    private bool IsCurrentShortcutDrag(ShortcutDrag drag) =>
        IsAlive && _section == "newtab" && _selection == drag.Selection &&
        ReferenceEquals(_newTabShortcuts, drag.Grid) && drag.Grid.IsVisualAncestorOf(drag.Source) &&
        CanReorderNewTabShortcuts && SavedShortcuts == drag.Order;

    private CuiContainerControl? FindShortcutDropTarget(ShortcutDrag drag, PointerEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is not { } root || root.InputHitTest(args.GetPosition(root)) is not Visual hit ||
            FindShortcutDragCard(hit) is not { } target || !drag.Grid.IsVisualAncestorOf(target) ||
            GetShortcutDragCardId(target) is not { } id ||
            id == drag.SourceId || FindShortcutIndex(drag.Order, id) < 0)
            return null;
        return target;
    }

    private static CuiContainerControl? FindShortcutDragCard(Visual hit) =>
        hit.GetSelfAndVisualAncestors().OfType<CuiContainerControl>()
            .FirstOrDefault(card => card.Name == "home-newtab-shortcut-card");

    private static string? GetShortcutDragCardId(CuiContainerControl card) =>
        card.DataContext is ICuiBindingContext binding && binding.TryGetValue("shortcut.Id", out var id) ? id as string : null;

    private void EndNewTabShortcutDrag(bool cancelPress = true)
    {
        if (_shortcutDrag is not { } drag) return;
        _shortcutDrag = null;
        drag.Handle.PointerCaptureLost -= OnShortcutPointerCaptureLost;
        // Release the original capture owner, even if the authored grid has been replaced.
        drag.Grid.PointerCaptureLost -= OnShortcutPointerCaptureLost;
        SetShortcutDragPresentation(drag.Source, null);
        SetShortcutDragPresentation(drag.Target, null);
        if (ReferenceEquals(drag.Pointer.Captured, drag.Grid)) drag.Pointer.Capture(null);
        if (cancelPress && _shortcutDrag is null && drag.Pointer.Captured is Visual captured &&
            (ReferenceEquals(captured, drag.Handle) || drag.Handle.IsVisualAncestorOf(captured)))
            drag.Pointer.Capture(null);
    }

    private static void SetShortcutDragPresentation(CuiContainerControl? card, double? opacity)
    {
        if (card is null) return;
        if (opacity is { } value)
            CuiPropertyPresentation.Set(card, "Opacity", value, CuiPropertyLevel.Instance, ShortcutDragPresentation, order: 3);
        else CuiPropertyPresentation.Remove(card, "Opacity", CuiPropertyLevel.Instance, ShortcutDragPresentation);
    }

    private static ImmutableArray<NewTabShortcutPlacement> ReorderShortcuts(ImmutableArray<NewTabShortcutPlacement> shortcuts,
        string sourceId, string targetId)
    {
        var source = FindShortcutIndex(shortcuts, sourceId);
        var target = FindShortcutIndex(shortcuts, targetId);
        return source < 0 || target < 0 || source == target
            ? shortcuts
            : shortcuts.RemoveAt(source).Insert(target, shortcuts[source]);
    }

    private void FocusNewTabShortcutGrip(string id) => FocusAfterSelection(() =>
    {
        var handle = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(control =>
            AutomationProperties.GetAutomationId(control) == ShortcutDragAutomationPrefix + id);
        if (handle?.Focus(NavigationMethod.Tab) == true) handle.BringIntoView();
    });
}
