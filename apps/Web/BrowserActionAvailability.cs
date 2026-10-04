using Avalonia.Controls;
using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.Data;
using Avalonia.Threading;
using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web;

internal static class BrowserActionAvailability
{
    private const string UnavailableTip = "This action requires an available account service or browser capability.";
    public static Observation Observe(Control root, CuiControlLoader loader, CuiDocument document,
        ICuiActionDispatcher actions, ICuiBindingContext bindings) => new(root, loader, document, actions, bindings);

    /// <summary>Capability availability narrows the owning CUI's initial enabled state.</summary>
    public static void ApplyInitial(Button button, CuiControlLoader loader, CuiDocument document, ICuiActionDispatcher actions)
    {
        if (loader.Inspect(button) is null || button.Tag is not string reference) return;
        // Tag is the authored reference; the actual loader dispatches the existing
        // typed Action definition's Command when that reference is an alias.
        var command = document.Actions.TryGetValue(reference, out var definition) ? definition.Command : reference;
        var available = (actions as ICuiActionAvailability)?.IsActionAvailable(command) == true;
        button.IsEnabled = button.IsEnabled && available;
        if (!available) ToolTip.SetTip(button, UnavailableTip);
    }

    /// <summary>Owns reactive native button observers for exactly one rendered surface.</summary>
    internal sealed class Observation : IDisposable
    {
        private readonly Control _root;
        private readonly CuiControlLoader _loader;
        private readonly CuiDocument _document;
        private readonly ICuiActionDispatcher _actions;
        private readonly Dictionary<Button, ButtonState> _buttons = new();
        private readonly List<INotifyPropertyChanged> _contexts = [];
        private bool _disposed;

        public Observation(Control root, CuiControlLoader loader, CuiDocument document,
            ICuiActionDispatcher actions, ICuiBindingContext bindings)
        {
            _root = root; _loader = loader; _document = document; _actions = actions;
            try
            {
                foreach (var source in new object[] { bindings, actions })
                    if (source is INotifyPropertyChanged context && !_contexts.Any(c => ReferenceEquals(c, context)))
                    {
                        _contexts.Add(context);
                        context.PropertyChanged += ContextChanged;
                    }
                Refresh();
            }
            catch { Dispose(); throw; }
        }

        public void Refresh()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed) return;
            var current = _root.GetLogicalDescendants().OfType<Control>().Prepend(_root).OfType<Button>()
                .Where(button => _loader.Inspect(button) is not null && button.Tag is string).ToHashSet();
            foreach (var removed in _buttons.Keys.Except(current).ToArray())
            {
                _buttons[removed].Dispose(); _buttons.Remove(removed);
            }
            foreach (var button in current)
            {
                if (!_buttons.TryGetValue(button, out var state))
                    _buttons.Add(button, state = new(button, _document, _actions));
                state.Refresh();
            }
        }

        private void ContextChanged(object? sender, PropertyChangedEventArgs args)
        {
            // The loader subscribes before this observer and posts at the same
            // priority, so its real bindings/conditional tree refresh first.
            if (Dispatcher.UIThread.CheckAccess()) Refresh();
            else Dispatcher.UIThread.Post(Refresh);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var context in _contexts) context.PropertyChanged -= ContextChanged;
            _contexts.Clear();
            foreach (var state in _buttons.Values) state.Dispose();
            _buttons.Clear();
        }
    }

    private sealed class ButtonState : IDisposable
    {
        private readonly Button _button;
        private readonly CuiDocument _document;
        private readonly ICuiActionDispatcher _actions;
        private IDisposable? _restriction;
        private bool _writing;
        private bool _disposed;
        private bool _ownsTip;

        public ButtonState(Button button, CuiDocument document, ICuiActionDispatcher actions)
        {
            _button = button; _document = document; _actions = actions;
            button.PropertyChanged += NativeChanged;
        }

        private void NativeChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (_disposed || _writing || args.Property != Control.IsEnabledProperty) return;
            Refresh();
        }

        public void Refresh()
        {
            if (_disposed) return;
            _writing = true;
            try
            {
                var reference = _button.Tag as string;
                var command = reference is not null && _document.Actions.TryGetValue(reference, out var definition) ? definition.Command : reference;
                var available = command is not null && (_actions as ICuiActionAvailability)?.IsActionAvailable(command) == true;
                if (!available) Restrict();
                else
                {
                    _restriction?.Dispose(); _restriction = null;
                }
                if (_ownsTip && !Equals(ToolTip.GetTip(_button), UnavailableTip)) _ownsTip = false;
                if (!available && ToolTip.GetTip(_button) is null)
                {
                    ToolTip.SetTip(_button, UnavailableTip); _ownsTip = true;
                }
                else if (available && _ownsTip)
                {
                    ToolTip.SetTip(_button, null); _ownsTip = false;
                }
            }
            catch { Restrict(); throw; }
            finally { _writing = false; }
        }

        private void Restrict()
        {
            if (_restriction is not null && !_button.IsEnabled) return;
            _restriction?.Dispose(); _restriction = null;
            // The native value store retains the owner's local values, bindings
            // and styles below this disposable restriction. Even an equal-false
            // owner assignment remains intact when the restriction is removed.
            _restriction = _button.SetValue(Control.IsEnabledProperty, false, BindingPriority.Animation)
                ?? throw new InvalidOperationException("The native enabled restriction was not disposable.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _button.PropertyChanged -= NativeChanged;
            _restriction?.Dispose(); _restriction = null;
            if (_ownsTip && Equals(ToolTip.GetTip(_button), UnavailableTip)) ToolTip.SetTip(_button, null);
            _ownsTip = false;
        }
    }
}
