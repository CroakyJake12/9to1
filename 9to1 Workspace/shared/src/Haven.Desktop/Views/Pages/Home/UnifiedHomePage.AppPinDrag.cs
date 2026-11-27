using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private const string AppPinDragPresentation = "Home app pin drag";
    private AppPinDrag? _appPinDrag;
    private sealed record AppPinDrag(IPointer Pointer, Point Start, Button Handle, CuiContainerControl Card,
        Guid Id, IReadOnlyList<ModePin> Order, bool Dragging = false, CuiContainerControl? Target = null);

    private void RegisterAppPinDrag()
    {
        AddHandler(PointerPressedEvent, OnAppPinPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnAppPinMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnAppPinReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        DetachedFromVisualTree += OnAppPinDetached;
    }

    private void StopAppPinDrag()
    {
        EndAppPinDrag();
        RemoveHandler(PointerPressedEvent, OnAppPinPressed);
        RemoveHandler(PointerMovedEvent, OnAppPinMoved);
        RemoveHandler(PointerReleasedEvent, OnAppPinReleased);
        DetachedFromVisualTree -= OnAppPinDetached;
    }

    private void OnAppPinDetached(object? sender, VisualTreeAttachmentEventArgs args) => EndAppPinDrag();
    private bool CanDragAppPins => IsAlive && _section == "apps" && !_appsMutation &&
        _appsState.CanEditPins && _appPinSnapshot.Count > 1;

    private void OnAppPinPressed(object? sender, PointerPressedEventArgs args)
    {
        if (_appPinDrag is not null || !CanDragAppPins ||
            args.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            args.Source is not Visual hit || hit.GetSelfAndVisualAncestors().OfType<Button>()
                .FirstOrDefault(button => button.Name == "home-app-pin-open" && button.IsEffectivelyEnabled) is not { } handle ||
            FindAppPinCard(handle) is not { } card || GetAppPinId(card) is not { } id) return;
        args.Pointer.Capture(handle);
        args.PreventGestureRecognition();
        handle.Focus(NavigationMethod.Pointer);
        _appPinDrag = new(args.Pointer, args.GetPosition(this), handle, card, id, _appPinSnapshot);
        handle.PointerCaptureLost += OnAppPinCaptureLost;
    }

    private void OnAppPinMoved(object? sender, PointerEventArgs args)
    {
        if (_appPinDrag is not { } drag || args.Pointer != drag.Pointer) return;
        if (!CanDragAppPins || !ReferenceEquals(_appPinSnapshot, drag.Order) || !drag.Card.IsAttachedToVisualTree())
        { EndAppPinDrag(); return; }
        if (!drag.Dragging)
        {
            var offset = args.GetPosition(this) - drag.Start;
            if (Math.Abs(offset.X) < 6 && Math.Abs(offset.Y) < 6) return;
            _appPinDrag = drag = drag with { Dragging = true };
            PointerCaptureLost += OnAppPinCaptureLost;
            args.Pointer.Capture(this);
            if (!ReferenceEquals(args.Pointer.Captured, this)) { EndAppPinDrag(); return; }
            SetAppPinDragPresentation(drag.Card, .66);
        }
        var target = FindAppPinDropTarget(args, drag.Id);
        if (!ReferenceEquals(target, drag.Target))
        {
            SetAppPinDragPresentation(drag.Target, null);
            SetAppPinDragPresentation(target, .78);
            _appPinDrag = drag with { Target = target };
        }
        args.Handled = true;
    }

    private void OnAppPinReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_appPinDrag is not { } drag || args.Pointer != drag.Pointer ||
            args.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
        var target = drag.Dragging && CanDragAppPins && ReferenceEquals(_appPinSnapshot, drag.Order)
            ? FindAppPinDropTarget(args, drag.Id) : null;
        EndAppPinDrag(cancelPress: false);
        if (!drag.Dragging) return;
        args.Handled = true;
        if (target is null || GetAppPinId(target) is not { } targetId) return;
        _ = _work.RunAsync(original => ChangeAppPinAsync(drag.Id.ToString("D"), null, original,
            original.Token, targetId), task => _appPinAction = task);
    }

    private CuiContainerControl? FindAppPinDropTarget(PointerEventArgs args, Guid sourceId)
    {
        if (TopLevel.GetTopLevel(this) is not { } root || root.InputHitTest(args.GetPosition(root)) is not Visual hit ||
            FindAppPinCard(hit) is not { } card || GetAppPinId(card) is not { } id || id == sourceId ||
            !_appPinSnapshot.Any(pin => pin.ModeId == id)) return null;
        return card;
    }

    private static CuiContainerControl? FindAppPinCard(Visual hit) => hit.GetSelfAndVisualAncestors()
        .OfType<CuiContainerControl>().FirstOrDefault(control => control.Name == "home-app-pin-card");

    private static Guid? GetAppPinId(CuiContainerControl card) => card.DataContext is ICuiBindingContext binding &&
        binding.TryGetValue("app.pinId", out var value) && Guid.TryParseExact(value as string, "D", out var id) && id != Guid.Empty ? id : null;

    private void OnAppPinCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (_appPinDrag is { } drag && args.Pointer == drag.Pointer &&
            ReferenceEquals(sender, drag.Dragging ? this : drag.Handle)) EndAppPinDrag();
    }

    private void EndAppPinDrag(bool cancelPress = true)
    {
        if (_appPinDrag is not { } drag) return;
        _appPinDrag = null;
        drag.Handle.PointerCaptureLost -= OnAppPinCaptureLost;
        PointerCaptureLost -= OnAppPinCaptureLost;
        if (ReferenceEquals(drag.Pointer.Captured, this) || cancelPress && ReferenceEquals(drag.Pointer.Captured, drag.Handle))
            drag.Pointer.Capture(null);
        SetAppPinDragPresentation(drag.Card, null);
        SetAppPinDragPresentation(drag.Target, null);
    }

    private static void SetAppPinDragPresentation(CuiContainerControl? card, double? opacity)
    {
        if (card is null) return;
        if (opacity is { } value) CuiPropertyPresentation.Set(card, "Opacity", value, CuiPropertyLevel.Instance, AppPinDragPresentation, order: 3);
        else CuiPropertyPresentation.Remove(card, "Opacity", CuiPropertyLevel.Instance, AppPinDragPresentation);
    }
}
