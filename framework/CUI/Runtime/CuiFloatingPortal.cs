using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PopupPositioning = Avalonia.Controls.Primitives.PopupPositioning;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Generic last-resort floating presentation portal.
/// </summary>
/// <remarks>
/// When an authored floating cell's resolved rect must ESCAPE the owning overlay panel's own
/// physical bounds — for example a composer mounted as a bounded native control inside a clipped
/// Haven scene surface — the in-tree cell cannot paint or hit-test outside the composer leaf. This
/// portal re-parents the SAME authored floating host into a light <see cref="Popup"/> hosted by the
/// actual owning top-level, positioned at the resolved rect in the owning window's usable viewport
/// against the exact authored target. It engages ONLY on escape; the in-tree path is otherwise used.
/// </remarks>
/// <remarks>
/// The portal preserves real typed ownership: the popup is adopted as a logical/visual child of the
/// owning overlay panel (resource/accessibility/command ancestry), the SAME authored host instance is
/// hosted (never a copy), and the panel tracks the host by identity so retirement/removal/replacement
/// reconcile the portal. Theme/property inheritance also flows through the popup's placement target.
/// The Escape/close gesture is handled on the retained authored content (which lives in the hosted
/// popup root) and invokes the canonical typed authored-close seam, so authored open/close state is
/// reconciled rather than left selected/open. Lifetime is idempotent/reentrant-safe; the portal never
/// parks the bar at the origin.
/// </remarks>
internal sealed class CuiFloatingPortal : IDisposable
{
    private readonly CuiOverlayPanel _owner;
    private readonly CuiContainerControl _host;
    private readonly Border _cell;
    private readonly Popup _popup;
    private Border? _wrapper;
    private Size _size;
    private bool _dismissing;
    private bool _disposed;
    private bool _adopted;
    private bool _invalidating;
    // Last observed hosted-content signature used to gate per-pass owner invalidation. The wrapper's
    // own bounds are fixed to the solved rect, so an unchanged render pass must NOT dirty the owner
    // again (that would reschedule an endless layout/render loop). Only a genuine change in the
    // authored content's measured size or effective visibility requests replacement.
    private Size _lastContentSize;
    private bool _lastContentVisible;
    private bool _haveContentBaseline;
    private IInputElement? _restoreFocus;
    private readonly EventHandler<AvaloniaPropertyChangedEventArgs> _hostPropertyChanged;
    private readonly EventHandler _layoutUpdated;
    private readonly EventHandler<PointerReleasedEventArgs> _wrapperPointerReleased;
    // The ORIGINAL physical-close publisher lease captured at construction (the loader wires it into
    // the host before any escape portal exists). The portal keeps its own lease so a retired host can
    // never drop the only publisher before this portal's real close receipt settles; release of the
    // portal releases that custody.
    private readonly Action<Task>? _publisherLease;
    // Once-only native-close receipt for the current open cycle, published into the owning loader's
    // original action ledger BEFORE any native Close or reentrant callback. Settled only after the real
    // native close returns/acknowledges. Repeated close in the same cycle joins the SAME receipt and
    // never replays a failed close; a failed close retains its original exception for rethrow.
    private TaskCompletionSource? _closeReceipt;
    private Exception? _closeFailure;

    /// <summary>Create a portal for an authored floating host currently held by a panel-private cell.
    /// <paramref name="cell"/> must be the actual panel-private <see cref="Border"/> wrapper (the
    /// floating-cell contract), never an arbitrary control.</summary>
    public CuiFloatingPortal(CuiOverlayPanel owner, CuiContainerControl host, Border cell)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _publisherLease = host.FloatingCloseLedgerPublisher;
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _hostPropertyChanged = OnHostPropertyChanged;
        _layoutUpdated = (_, _) => OnWrapperLayoutUpdated();
        _wrapperPointerReleased = OnWrapperPointerReleased;
        _popup = new Popup
        {
            PlacementTarget = owner,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupPositioning.PopupAnchor.TopLeft,
            // The offsets are the solved rectangle's top-left. With TopLeft gravity the fork's
            // positioner would subtract width/height (Left/Top gravity), shifting the popup. BottomRight
            // gravity keeps the popup's top-left exactly at the solved offset.
            PlacementGravity = PopupPositioning.PopupGravity.BottomRight,
            PlacementConstraintAdjustment = PopupPositioning.PopupPositionerConstraintAdjustment.None,
            IsLightDismissEnabled = false,   // the authored floating bar does not light-dismiss
            WindowManagerAddShadowHint = false,
        };
        CuiPopupPresentation.RequestTransparency(_popup);
        _popup.Closed += OnClosed;
    }

    /// <summary>The authored floating host moved into this portal.</summary>
    public CuiContainerControl Host => _host;

    /// <summary>The panel-private <see cref="Border"/> cell the host was moved out of.</summary>
    public Border Cell => _cell;

    /// <summary>The popup hosting the portaled content. Internal diagnostic only; used to exercise the
    /// exact direct-close path the panel reconciles.</summary>
    internal Popup Popup => _popup;

    /// <summary>Whether the portal popup is currently open.</summary>
    public bool IsOpen => _popup.IsOpen;

    /// <summary>
    /// Position and open the portal at a resolved rect expressed in the owning overlay panel's local
    /// coordinates. The SAME authored host is re-parented out of the clipped in-tree cell (never a
    /// copy); repeated calls reposition without re-attaching.
    /// </summary>
    public void Show(Rect rect)
    {
        if (_disposed) return;
        _size = rect.Size;
        if (_wrapper is null)
        {
            // Detach the authored host from the clipped cell first, then host it in the portal. The
            // Escape gesture and visible-content/size invalidation are bridged directly on the retained
            // hosted content (inside the popup root) — never on a copied substitute.
            _cell.Child = null;
            _cell.IsVisible = false;
            _wrapper = new Border { Child = _host };
            _wrapper.PointerReleased += _wrapperPointerReleased;
            _popup.Child = _wrapper;
            _owner.AdoptPortalLogically(_popup);
            _adopted = true;
            _host.PropertyChanged += _hostPropertyChanged;
            _wrapper.LayoutUpdated += _layoutUpdated;
            _host.KeyDown += OnPortalKeyDown;
        }
        _wrapper.Width = rect.Width;
        _wrapper.Height = rect.Height;
        _popup.HorizontalOffset = rect.X;
        _popup.VerticalOffset = rect.Y;
        if (!_popup.IsOpen)
        {
            // A new open cycle: the next native close is a fresh, once-only original close.
            _closeReceipt = null;
            _closeFailure = null;
            var ownerTop = TopLevel.GetTopLevel(_owner);
            _restoreFocus = ownerTop?.FocusManager?.GetFocusedElement();
            _popup.Open();
        }
    }

    /// <summary>
    /// The original physical-close ledger publisher lease: the captured constructor lease, or the
    /// host's current publisher as a fallback (kept alive by the portal even after the host seam is
    /// retired, so an open cycle always has a real admission path).
    /// </summary>
    private Action<Task>? Publisher => _publisherLease ?? _host.FloatingCloseLedgerPublisher;

    /// <summary>
    /// Perform the real native popup close exactly once per open cycle and publish its once-only receipt
    /// into the owning loader's original action ledger BEFORE entering native Close. Settled only after
    /// the real Close returns/acknowledges, preserving the exact original error/cancellation. Repeated
    /// close in the same cycle joins the SAME receipt (no replay of a failed close, no double admission);
    /// a failed close rethrows its original error and retains custody (the popup still owns the host).
    /// </summary>
    private void NativeClose()
    {
        // Not guarded by _disposed: disposal still must close the real popup once (the publisher lease
        // may be retired then, which is harmless). Guarded only against double-admission per open cycle.
        if (_closeReceipt is not null)
        {
            if (_closeFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_closeFailure).Throw();
            return;
        }
        if (!_popup.IsOpen) return;
        var receipt = new TaskCompletionSource();
        _closeReceipt = receipt;
        Publisher?.Invoke(receipt.Task);
        Exception? failure = null;
        try { _popup.Close(); }
        catch (Exception error) { failure = error; }
        if (failure is not null) { }
        else if (!_popup.IsOpen) { }
        else failure = new InvalidOperationException("The native popup did not close; its owning host remains retained.");
        if (failure is not null)
        {
            _closeFailure = failure;
            receipt.TrySetException(failure);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        receipt.TrySetResult();
    }

    /// <summary>
    /// Close the portal and restore the SAME authored host to its in-tree cell. Idempotent and
    /// reentrant-safe: nested calls are no-ops; the popup's synchronous Closed callback is guarded by
    /// <see cref="_dismissing"/> so it never double-restores.
    /// </summary>
    public void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        try
        {
            if (_wrapper is not null)
            {
                NativeClose();
                _popup.Child = null;
                _wrapper.Child = null;
                _cell.Child = _host;
            }
            TeardownPortal();
        }
        finally { _dismissing = false; }
    }

    /// <summary>User-initiated Escape on the REAL hosted popup content: invoke the canonical authored
    /// close seam first so authored open/close state is reconciled (not left open, which would reopen on
    /// the next owner arrange), then close and retire.</summary>
    private void OnPortalKeyDown(object? sender, KeyEventArgs e)
    {
        if (_disposed || e.Key != Key.Escape) return;
        e.Handled = true;
        _host.FloatingCloseRequested?.Invoke();
        NativeClose();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_dismissing || _disposed) return;
        // External/native-policy close: admit once through the ORIGINAL publisher now — a real before/
        // after close seam. Do NOT synthesize success inside OnClosed while later teardown callbacks
        // can still fail; settle only after the actual restore/teardown succeeds.
        if (_closeReceipt is null)
        {
            var receipt = new TaskCompletionSource();
            _closeReceipt = receipt;
            Publisher?.Invoke(receipt.Task);
        }
        Exception? failure = null;
        try
        {
            if (_wrapper is not null)
            {
                _popup.Child = null;
                _wrapper.Child = null;
                _cell.Child = _host;
            }
            TeardownPortal();
        }
        catch (Exception error) { failure = error; }
        try { _owner.OnPortalClosed(this); }
        catch (Exception error) { failure ??= error; }
        if (_closeReceipt is not null && !_closeReceipt.Task.IsCompleted)
        {
            if (failure is not null) { _closeFailure = failure; _closeReceipt.TrySetException(failure); }
            else _closeReceipt.TrySetResult();
        }
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty) RequestOwnerReplacement();
    }

    /// <summary>Handle a wrapper layout pass. A layout pass fires for EVERY render/arrange, including
    /// unchanged ones, so the portal must not re-place the owner unless the retained authored content
    /// actually changed shape/size/visibility. The wrapper's own bounds are fixed to the solved rect,
    /// so the only meaningful signal here is the freshly laid-out content signature (the host's actual
    /// measured size and effective visibility, both current at LayoutUpdated time since layout already
    /// ran). Compare against the last applied signature and request replacement only on a real change —
    /// otherwise unchanged geometry would schedule an endless re-layout loop. This read is side-effect
    /// free: it never invalidates/queues layout, so an unchanged pass cannot reschedule another one.</summary>
    private void OnWrapperLayoutUpdated()
    {
        if (_disposed || _wrapper is null) return;
        var size = _host.DesiredSize;
        var visible = _host.IsEffectivelyVisible;

        if (!_haveContentBaseline)
        {
            _lastContentSize = size;
            _lastContentVisible = visible;
            _haveContentBaseline = true;
            return;
        }

        if (size == _lastContentSize && visible == _lastContentVisible)
            return;

        _lastContentSize = size;
        _lastContentVisible = visible;
        RequestOwnerReplacement();
    }

    /// <summary>Bridge a genuine visible-content/size change of the hosted popup subtree (e.g. the
    /// authored INNER format toolbar collapsing when FormattingOpen flips off) to the owning panel's
    /// bounded re-placement/retirement. Coalesced; the arrange pass then re-measures live retained content.
    /// Callers must only invoke this when a real change occurred — never from an unconditional layout pass.</summary>
    private void RequestOwnerReplacement()
    {
        if (_invalidating) return;
        _invalidating = true;
        try { _owner.InvalidateMeasure(); _owner.InvalidateArrange(); }
        finally { _invalidating = false; }
    }

    /// <summary>
    /// Return keyboard focus to the owning surface's primary embedded input after a pointer action on
    /// the portaled floating content (e.g. a toolbar command button). The global pointer-focus
    /// mechanism gives the clicked control keyboard focus, so without this the composer would lose its
    /// editable input after each toolbar interaction. We restore only when the pointer gesture landed
    /// focus on a NON-input control inside this portal, and only to a real visible/focusable input of
    /// the owning compound-focus surface — never when the user is interacting with a genuine input
    /// hosted here. The restore is deferred until the click/action routing settles.
    /// </summary>
    private void OnWrapperPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_disposed || !_popup.IsOpen || e.InitialPressMouseButton != MouseButton.Left) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_popup.IsOpen) return;
            var manager = TopLevel.GetTopLevel(_owner)?.FocusManager;
            var focused = manager?.GetFocusedElement();
            if (focused is null || focused is TextBox || focused is not Visual focusedVisual) return;
            // Only restore when focus currently lives inside this portal's hosted content and is not
            // the primary input itself (the click landed on a transient control such as a command).
            if (!ReferenceEquals(focusedVisual, _host) && !_host.IsVisualAncestorOf(focusedVisual)) return;
            var input = FindOwnerInput();
            if (input is null || !input.IsEffectivelyVisible || !input.Focusable || !input.IsAttachedToVisualTree()) return;
            if (!ReferenceEquals(manager?.GetFocusedElement(), input))
                input.Focus(NavigationMethod.Pointer);
        });
    }

    /// <summary>Locate the owning compound-focus surface's primary visible embedded text input. The
    /// walking is the same visual-ancestor search the overlay panel uses to resolve the surface scope:
    /// nearest compound-focus-enabled authored container, then its first visible/focusable text input
    /// (the composer's editable line; hidden auxiliary inputs such as a closed emoji search are skipped).</summary>
    private TextBox? FindOwnerInput()
    {
        Visual? current = _owner;
        while (current is not null)
        {
            if (current is CuiContainerControl { CompoundFocusEnabled: true } container)
            {
                foreach (var descendant in container.GetVisualDescendants())
                    if (descendant is TextBox box && box.IsEffectivelyVisible && box.Focusable && box.IsAttachedToVisualTree())
                        return box;
                return null;
            }
            current = current.GetVisualParent();
        }
        return null;
    }

    private void TeardownPortal()
    {
        if (_wrapper is not null)
        {
            _wrapper.LayoutUpdated -= _layoutUpdated;
            _wrapper.PointerReleased -= _wrapperPointerReleased;
            _wrapper = null;
        }
        _host.KeyDown -= OnPortalKeyDown;
        _host.PropertyChanged -= _hostPropertyChanged;
        if (_adopted)
        {
            _owner.ReleasePortalLogically(_popup);
            _adopted = false;
        }
        if (_restoreFocus is InputElement element && element.IsAttachedToVisualTree() && element.IsEffectivelyVisible)
            element.Focus();
        _restoreFocus = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _popup.Closed -= OnClosed;
        // Dismiss is intentionally NOT guarded by _disposed so dispose always performs the exact
        // retirement/restore once. _dismissing protects against reentrancy.
        Dismiss();
    }
}
