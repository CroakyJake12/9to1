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
        // Support dotted paths: "ViewModel.Count" → lookup "Count"
        var lastDot = path.LastIndexOf('.');
        var key = lastDot >= 0 ? path[(lastDot + 1)..] : path;
        return _properties.TryGetValue(key, out value);
    }

    /// <summary>ICuiWritableBindingContext: update a bound value and notify the runtime.</summary>
    public bool TrySetValue(string path, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var lastDot = path.LastIndexOf('.');
        var key = lastDot >= 0 ? path[(lastDot + 1)..] : path;
        if (!_properties.ContainsKey(key))
            return false;
        Set(key, value);
        return true;
    }

    /// <summary>Dispatch a command by name (sync).</summary>
    public void Dispatch(string command, object? parameter = null)
    {
        if (_commands.TryGetValue(command, out var handler))
            handler(parameter);
    }

    /// <summary>ICuiActionDispatcher: async dispatch.</summary>
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
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
