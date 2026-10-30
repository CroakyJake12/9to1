using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CakeOS.Cui.Runtime;

public sealed partial class CuiControlLoader
{
    private static readonly string[] InteractionEventTypes = ["PointerEnter", "PointerExit", "Focused", "Blurred"];
    private readonly Dictionary<Control, Dictionary<string, OwnedInteractionAction>> _interactionActions = new(ReferenceEqualityComparer.Instance);

    private sealed class OwnedInteractionAction(CuiActionInvocation invocation)
    {
        public CuiActionInvocation Invocation { get; } = invocation;
        public Action? Unwire { get; set; }
    }

    private void ApplyInteractionActionTags(Control control, CuiComponent component)
    {
        foreach (var type in InteractionEventTypes)
        {
            if (CuiActionEventContract.OwnedAction(component, type) is not { } action) continue;
            if (!_interactionActions.TryGetValue(control, out var routes))
                _interactionActions[control] = routes = new(StringComparer.Ordinal);
            routes.Add(type, new(CreateActionInvocation(action.Name, originOnly: true)));
        }
    }

    private void WireInteractionActions(Control control, Control? contextMenuOwner)
    {
        Wire("PointerEnter", InputElement.PointerEnteredEvent);
        Wire("PointerExit", InputElement.PointerExitedEvent);
        // Menu selection handles the bubbling focus notification during its class callback.
        // The focus transition still happened; observe it without changing native handling.
        Wire("Focused", InputElement.GotFocusEvent, observeHandled: true);
        Wire("Blurred", InputElement.LostFocusEvent, observeHandled: true);

        void Wire<TArgs>(string type, RoutedEvent<TArgs> nativeEvent, bool observeHandled = false) where TArgs : RoutedEventArgs
        {
            if (!_interactionActions.TryGetValue(control, out var routes)
                || !routes.TryGetValue(type, out var route) || route.Unwire is not null) return;
            var invocation = route.Invocation;
            if (_actionDispatcher is null && invocation.Owner is null && ScopedDispatcherFor(control) is null
                && invocation.Command is not (LocalToggleCommand or LocalSetCommand)) return;

            EventHandler<TArgs> handler = (_, args) =>
            {
                // Native focus events can bubble from descendants. Pointer transitions are direct.
                // Recheck exact route identity as well as the shared admission guard so a captured
                // retired handler can never act for a replacement control/frame with the same ID.
                if (!ReferenceEquals(args.Source, control) || !control.IsEffectivelyEnabled
                    || !_interactionActions.TryGetValue(control, out var current)
                    || !current.TryGetValue(type, out var admitted) || !ReferenceEquals(admitted, route)) return;
                ObserveActionDispatch(control, contextMenuOwner, invocation);
            };
            control.AddHandler(nativeEvent, handler, handledEventsToo: observeHandled);
            route.Unwire = () => control.RemoveHandler(nativeEvent, handler);
            _wiredActions.Add(control);
        }
    }

    private void RetireInteractionActions(Control control)
    {
        if (!_interactionActions.Remove(control, out var routes)) return;
        foreach (var route in routes.Values) route.Unwire?.Invoke();
    }

    private void ClearInteractionActions()
    {
        foreach (var control in _interactionActions.Keys.ToArray()) RetireInteractionActions(control);
    }
}
