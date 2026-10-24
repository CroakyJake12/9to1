using System;
using System.Runtime.CompilerServices;
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
    private static readonly ConditionalWeakTable<Control, SharedHost> Hosts = new();
    private static readonly object HostsGate = new();
    private readonly SharedHost? _shared;
    private bool _disposed;

    private CuiKeyboardTooltipHost(SharedHost? shared) => _shared = shared;

    /// <summary>Acquire the shared keyboard tooltip behavior for one CUI root. The first attachment owns
    /// its lifecycle; repeated attachments return inert handles so one root never installs duplicate
    /// focus or Escape handlers.</summary>
    public static CuiKeyboardTooltipHost Attach(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        lock (HostsGate)
        {
            if (Hosts.TryGetValue(root, out _)) return new CuiKeyboardTooltipHost(null);
            var shared = new SharedHost(root);
            Hosts.Add(root, shared);
            return new CuiKeyboardTooltipHost(shared);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shared?.Release();
    }

    /// <summary>Observe an authored control that may be hosted in a separate PopupRoot. Duplicate
    /// registrations are ignored, and the owning loader withdraws the registration when that control retires.</summary>
    public void ObserveControl(Control control) => _shared?.ObserveControl(control);
    public void UnobserveControl(Control control) => _shared?.UnobserveControl(control);

    private sealed class SharedHost
    {
        private readonly Control _root;
        private readonly HashSet<Control> _observedControls = new(ReferenceEqualityComparer.Instance);
        private Control? _open;
        private PlacementMode _originalPlacement;
        private bool _disposed;
        private bool _treeDetached;

        public SharedHost(Control root)
        {
            _root = root;
            _root.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
            _root.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
            // Bubble after the focused popup/control gets its chance to consume Escape; a tooltip
            // dismisses only when no more-specific focused surface handled the key.
            _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
            _root.AttachedToVisualTree += OnAttachedToVisualTree;
            _root.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        public void Release()
        {
            lock (HostsGate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            try { Retire(); }
            finally
            {
                lock (HostsGate)
                {
                    if (Hosts.TryGetValue(_root, out var current) && ReferenceEquals(current, this))
                        Hosts.Remove(_root);
                }
            }
        }

        public void ObserveControl(Control control)
        {
            if (_disposed || !_observedControls.Add(control)) return;
            control.AddHandler(InputElement.GotFocusEvent, ObservedGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
            control.AddHandler(InputElement.LostFocusEvent, ObservedLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
            control.DetachedFromVisualTree += ObservedDetachedFromVisualTree;
        }

        public void UnobserveControl(Control control)
        {
            if (!_observedControls.Remove(control)) return;
            control.RemoveHandler(InputElement.GotFocusEvent, ObservedGotFocus);
            control.RemoveHandler(InputElement.LostFocusEvent, ObservedLostFocus);
            control.DetachedFromVisualTree -= ObservedDetachedFromVisualTree;
            if (ReferenceEquals(_open, control)) Close();
        }

        private void OnGotFocus(object? sender, FocusChangedEventArgs e)
        {
            if (_disposed || _treeDetached) return;
            // Pointer navigation is owned by the pinned pointer tooltip service. Genuine keyboard focus
            // (Tab/Directional) and programmatic focus (Unspecified) show the retained tip here, so a
            // keyboard user beneath a stationary mouse still gets the label.
            if (e.NavigationMethod == NavigationMethod.Pointer) return;
            if (e.Source is not InputElement element || element is not Control control)
                return;
            if (!_observedControls.Contains(control)) return;
            if (ReferenceEquals(_open, control) && ToolTip.GetIsOpen(control)) return;
            Close();
            if (!control.IsEffectivelyEnabled)
                return;
            if (ToolTip.GetTip(control) is null)
                return;
            if (!control.IsAttachedToVisualTree())
                return;
            OpenForKeyboard(control);
        }

        private void ObservedGotFocus(object? sender, FocusChangedEventArgs e)
        {
            if (_disposed || _treeDetached || sender is not Control control || !ReferenceEquals(e.Source, control)) return;
            var controlRoot = TopLevel.GetTopLevel(control);
            if (controlRoot is not null && !ReferenceEquals(controlRoot, TopLevel.GetTopLevel(_root)))
                OnGotFocus(sender, e);
        }

        private void ObservedLostFocus(object? sender, FocusChangedEventArgs e)
        {
            if (_disposed || sender is not Control control || !ReferenceEquals(e.Source, control)) return;
            var controlRoot = TopLevel.GetTopLevel(control);
            if (controlRoot is not null && !ReferenceEquals(controlRoot, TopLevel.GetTopLevel(_root)))
                OnLostFocus(sender, e);
        }

        private void ObservedDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (ReferenceEquals(sender, _open)) Close();
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
            control.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
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
            if (_disposed || e.Handled || e.Key != Key.Escape || _open is not { } control
                || !ToolTip.GetIsOpen(control)) return;
            Close();
            e.Handled = true;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => _treeDetached = false;

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _treeDetached = true;
            Close();
        }

        private void Close()
        {
            var control = _open;
            _open = null;
            if (control is null) return;
            control.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            if (ToolTip.GetIsOpen(control))
            {
                // Notify genuine closing handlers while the attached IsOpen property is still true,
                // mirroring the pinned vendor tooltip's close contract (its own Close raises
                // ToolTipClosingEvent only while GetIsOpen is still true). Merely flipping IsOpen to
                // false already nulls AdornedControl before the vendor close path runs, so that event
                // would never fire otherwise. On retirement all focus handlers have been unsubscribed,
                // so a closing callback cannot reopen a tip in this host.
                control.RaiseEvent(new RoutedEventArgs(ToolTip.ToolTipClosingEvent));
                ToolTip.SetIsOpen(control, false);
            }
            // Restore pointer behavior after a keyboard-close.
            if (_originalPlacement == PlacementMode.Pointer && ToolTip.GetPlacement(control) != PlacementMode.Pointer)
                ToolTip.SetPlacement(control, PlacementMode.Pointer);
        }

        private void Retire()
        {
            _root.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
            _root.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
            foreach (var control in _observedControls)
            {
                control.RemoveHandler(InputElement.GotFocusEvent, ObservedGotFocus);
                control.RemoveHandler(InputElement.LostFocusEvent, ObservedLostFocus);
                control.DetachedFromVisualTree -= ObservedDetachedFromVisualTree;
            }
            _observedControls.Clear();
            _root.AttachedToVisualTree -= OnAttachedToVisualTree;
            _root.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            Close();
        }
    }
}
