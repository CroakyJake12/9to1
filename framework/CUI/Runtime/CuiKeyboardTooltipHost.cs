using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Bounded canonical CUI keyboard-focus tooltip capability. The pinned vendor tooltip service only
/// reacts to pointer events, so authored CUI tooltips otherwise never appear for keyboard
/// navigation. This host observes keyboard focus across a loaded CUI root and shows the SAME
/// retained <see cref="ToolTip"/> popup (the presentation/transparency seam the loader configures
/// per authored tooltip) when a tooltipped control gains genuine keyboard or programmatic focus. It
/// closes the tip on focus departure, Escape, visual-tree detach and explicit retirement, and always
/// disposes its event subscriptions. No programmatic force-open surface is exposed.
/// </summary>
public sealed class CuiKeyboardTooltipHost : IDisposable
{
    private readonly Control _root;
    private Control? _open;
    private PlacementMode _originalPlacement;
    private bool _disposed;

    private CuiKeyboardTooltipHost(Control root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _root.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        _root.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _root.DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    /// <summary>Attach a keyboard-focus tooltip host to an authored CUI root.</summary>
    public static CuiKeyboardTooltipHost Attach(Control root) => new(root);

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_disposed) return;
        // Pointer navigation is owned by the pinned pointer tooltip service. Genuine keyboard focus
        // (Tab/Directional) and programmatic focus (Unspecified) show the retained tip here, so a
        // keyboard user beneath a stationary mouse still gets the label.
        if (e.NavigationMethod == NavigationMethod.Pointer) return;
        if (e.Source is not InputElement element || element is not Control control)
            return;
        Close();
        if (!control.IsEffectivelyEnabled)
            return;
        if (ToolTip.GetTip(control) is null)
            return;
        if (!control.IsAttachedToVisualTree())
            return;
        OpenForKeyboard(control);
    }

    private void OpenForKeyboard(Control control)
    {
        // Preserve an authored placement; only the default Pointer placement is switched to a
        // control-anchored placement for keyboard display so the label appears near the focused
        // rail button rather than at a stale cursor position. Pointer behavior is restored on close.
        var placement = ToolTip.GetPlacement(control);
        if (placement == PlacementMode.Pointer)
        {
            ToolTip.SetPlacement(control, PlacementMode.Right);
            _originalPlacement = PlacementMode.Pointer;
        }
        else
        {
            _originalPlacement = placement;
        }
        ToolTip.SetIsOpen(control, true);
        _open = control;
    }

    private void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_disposed) return;
        // Only close the tip bearing the control that actually lost focus, so a moving-focus
        // transition never tears down the newly focused control's own tip regardless of whether
        // the old control's LostFocus or the new control's GotFocus is raised first.
        if (ReferenceEquals(e.Source, _open)) Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_disposed) return;
        if (e.Key == Key.Escape) Close();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => Close();

    private void Close()
    {
        var control = _open;
        _open = null;
        if (control is null) return;
        if (ToolTip.GetIsOpen(control))
        {
            // Notify genuine closing handlers while the attached IsOpen property is still true,
            // mirroring the pinned vendor tooltip's close contract (its own Close raises
            // ToolTipClosingEvent only while GetIsOpen is still true). Merely flipping IsOpen to
            // false already nulls AdornedControl before the vendor close path runs, so that event
            // would never fire otherwise. On retirement the host has already unsubscribed its focus
            // handlers before this runs, so a closing callback that reentrantly focuses another
            // control cannot reopen a tip here.
            control.RaiseEvent(new RoutedEventArgs(ToolTip.ToolTipClosingEvent));
            ToolTip.SetIsOpen(control, false);
        }
        // Restore pointer behavior after a keyboard-close.
        if (_originalPlacement == PlacementMode.Pointer && ToolTip.GetPlacement(control) != PlacementMode.Pointer)
            ToolTip.SetPlacement(control, PlacementMode.Pointer);
    }

    /// <summary>
    /// Detach from the root and dispose every subscription; any open tip is closed. Retirement is
    /// marked (and every handler unsubscribed) BEFORE the tip is closed so a closing callback — for
    /// example one that reentrantly focuses another control — cannot reopen a tip during disposal.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        _root.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        _root.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _root.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        Close();
    }
}
