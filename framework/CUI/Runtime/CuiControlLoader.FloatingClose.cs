using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Floating-authored close-gesture wiring. A Floating <see cref="CuiContainerControl"/> (a host that
/// can be moved into an escape portal) carries its referenced authored action as its close gesture:
/// the generic escape portal invokes <see cref="CuiContainerControl.FloatingCloseRequested"/> when the
/// user closes the floating bar (Escape), and this routes the SAME authored action through the loader's
/// <see cref="ObserveActionDispatch"/> admission/ledger pipeline (pending-action task, local toggle
/// variable refresh, cancellation/failure acknowledgement) — never a synthetic button click, a manual
/// visibility toggle, or a second state frame. The wired delegates are retired on conditional/repeat/
/// reload and dispose, together with their invocations. Only the exact loader-owned delegate is ever
/// cleared: HashSet membership never authorizes clearing a replacement owner's handler.
/// </summary>
public sealed partial class CuiControlLoader
{
    /// <summary>Floating-authored hosts wired with a close gesture. Retired on reload/dispose.</summary>
    private readonly HashSet<CuiContainerControl> _floatingCloseRequests = new(ReferenceEqualityComparer.Instance);

    /// <summary>The exact close-gesture delegate this loader installed per host, for identity comparison.</summary>
    private readonly Dictionary<CuiContainerControl, Action> _floatingCloseGestureOf = new(ReferenceEqualityComparer.Instance);

    /// <summary>The exact native-close ledger publisher delegate this loader installed per host, for
    /// identity comparison. The portal additionally holds its own lease so a retired host can never
    /// drop the only publisher before a real close receipt settles.</summary>
    private readonly Dictionary<CuiContainerControl, Action<Task>> _floatingClosePublisherOf = new(ReferenceEqualityComparer.Instance);

    /// <summary>True for a Floating/Overlay-Float authored host that may be escape-portaled.</summary>
    private static bool IsFloatingHost(Control control) =>
        control is CuiContainerControl { Layout: Panel layout } && (layout is CuiOverlayPanel or CuiFloatingLayer);

    /// <summary>Wire the authored close gesture for a floating host: its referenced action becomes its
    /// close route through the SAME ObserveActionDispatch admission/ledger pipeline used by clicked
    /// actions. Only wired when the action is actually dispatchable (local toggle, dispatcher, or owner)
    /// so it stays truthful and never broadly accepted.</summary>
    private void WireFloatingCloseGesture(CuiContainerControl host, Control? contextMenuOwner)
    {
        if (!_wiredActions.Contains(host)
            && TryGetActionInvocation(host, out var invocation)
            && (_actionDispatcher is not null
                || invocation.Command == LocalToggleCommand
                || invocation.Owner is not null
                || ScopedDispatcherFor(host) is not null))
        {
            Action gesture = () => ObserveActionDispatch(host, contextMenuOwner);
            Action<Task> publisher = task => { lock (_actionTasksGate) _pendingActionTasks.Add(task); };
            host.FloatingCloseRequested = gesture;
            host.FloatingCloseLedgerPublisher = publisher;
            _floatingCloseRequests.Add(host);
            _floatingCloseGestureOf[host] = gesture;
            _floatingClosePublisherOf[host] = publisher;
            _wiredActions.Add(host);
        }
    }

    /// <summary>Withdraw the EXACT loader-owned floating close callback and ledger publisher for one
    /// authored control. Comparison is by delegate identity: if a replacement owner has since installed
    /// its own handler on the host, this loader's handler is not cleared. Idempotent.</summary>
    private void RetireFloatingCloseGesture(Control control)
    {
        if (control is not CuiContainerControl host || !_floatingCloseRequests.Remove(host)) return;
        if (_floatingCloseGestureOf.TryGetValue(host, out var gesture) && ReferenceEquals(host.FloatingCloseRequested, gesture))
            host.FloatingCloseRequested = null;
        if (_floatingClosePublisherOf.TryGetValue(host, out var publisher) && ReferenceEquals(host.FloatingCloseLedgerPublisher, publisher))
            host.FloatingCloseLedgerPublisher = null;
        _floatingCloseGestureOf.Remove(host);
        _floatingClosePublisherOf.Remove(host);
    }

    /// <summary>Retire all floating close gestures so a retired/portaled host can never invoke a
    /// disposed or reloaded loader. Identity-compared; idempotent. Portals keep their own publisher
    /// lease, so retiring the host seam never drops the only admission path for an open cycle.</summary>
    private void ClearFloatingCloseGestures()
    {
        foreach (var host in _floatingCloseRequests)
        {
            if (_floatingCloseGestureOf.TryGetValue(host, out var gesture) && ReferenceEquals(host.FloatingCloseRequested, gesture))
                host.FloatingCloseRequested = null;
            if (_floatingClosePublisherOf.TryGetValue(host, out var publisher) && ReferenceEquals(host.FloatingCloseLedgerPublisher, publisher))
                host.FloatingCloseLedgerPublisher = null;
        }
        _floatingCloseRequests.Clear();
        _floatingCloseGestureOf.Clear();
        _floatingClosePublisherOf.Clear();
        _wiredActions.RemoveWhere(IsFloatingHost);
    }
}
