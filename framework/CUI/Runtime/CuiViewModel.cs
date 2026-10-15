using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Observable binding context for CUI controls.
/// Implements ICuiBindingContext for the CUI language model
/// and INotifyPropertyChanged for live Avalonia bindings.
/// </summary>
public sealed class CuiViewModel : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action<object?>> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _actionAvailability = new(StringComparer.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Set a property and notify listeners.</summary>
    public void Set(string property, object? value, [CallerMemberName] string? caller = null)
    {
        if (_properties.TryGetValue(property, out var current) && Equals(current, value))
            return;
        _properties[property] = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    /// <summary>Get a property value.</summary>
    public object? Get(string property) =>
        _properties.TryGetValue(property, out var val) ? val : null;

    /// <summary>Register a command handler.</summary>
    public void On(string command, Action<object?> handler) =>
        _commands[command] = handler;

    /// <summary>Declare whether a registered command can actually serve its route.</summary>
    public void SetActionAvailability(string command, bool available) =>
        _actionAvailability[command] = available;

    public bool HasAction(string command) => _commands.ContainsKey(command);

    public bool? IsActionAvailable(string command) =>
        _actionAvailability.TryGetValue(command, out var available) ? available : null;

    /// <summary>ICuiBindingContext: resolve a binding path.</summary>
    public bool TryGetValue(string path, out object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // An exact key always wins. A view model can legitimately hold both "Conversation.Title"
        // and "Status.Message"; resolving a dotted path by its last segment alone made those two
        // indistinguishable, so whichever was published last silently answered both bindings.
        // Checking the exact key first is what lets a document disambiguate, and it changes
        // nothing for a model that stores flat keys: a flat key is either found on the exact
        // attempt or still resolved by the fallback exactly as before.
        if (_properties.TryGetValue(path, out value))
            return true;

        var lastDot = path.LastIndexOf('.');
        if (lastDot < 0)
            return false;

        return _properties.TryGetValue(path[(lastDot + 1)..], out value);
    }

    /// <summary>ICuiWritableBindingContext: update a bound value and notify the runtime.</summary>
    public bool TrySetValue(string path, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Mirrors TryGetValue: a write must land on the same key a read resolved, otherwise a
        // two-way binding would read one property and silently write a different one.
        if (!_properties.ContainsKey(path))
        {
            var lastDot = path.LastIndexOf('.');
            if (lastDot < 0)
                return false;
            path = path[(lastDot + 1)..];
            if (!_properties.ContainsKey(path))
                return false;
        }

        Set(path, value);
        return true;
    }

    /// <summary>Dispatch a command by name (sync).</summary>
    public void Dispatch(string command, object? parameter = null)
    {
        if (!_commands.TryGetValue(command, out var handler))
            throw new InvalidOperationException($"CUI command '{command}' has no registered handler.");
        handler(parameter);
    }

    /// <summary>ICuiActionDispatcher: async dispatch.</summary>
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatch(command, parameter);
        return ValueTask.CompletedTask;
    }

    /// <summary>Convenience: add items to an observable collection property.</summary>
    public ObservableCollection<T> GetOrCreateList<T>(string property)
    {
        if (_properties.TryGetValue(property, out var existing) && existing is ObservableCollection<T> list)
            return list;

        var newList = new ObservableCollection<T>();
        _properties[property] = newList;
        return newList;
    }
}
