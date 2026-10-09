using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Assistants;

/// <summary>Revokes reads as well as actions. Old queued CUI callbacks cannot redisplay private values.</summary>
public sealed class BrowserAssistantsBindingFence : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly ICuiBindingContext _bindings;
    private readonly ICuiActionDispatcher _actions;
    private readonly INotifyPropertyChanged? _notifications;
    private volatile bool _revoked;

    public BrowserAssistantsBindingFence(ICuiBindingContext bindings, ICuiActionDispatcher actions)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _notifications = bindings as INotifyPropertyChanged;
        if (_notifications is not null) _notifications.PropertyChanged += Changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        if (_revoked || !_bindings.TryGetValue(path, out value)) return false;
        if (!_revoked) return true;
        value = null;
        return false;
    }
    public bool TrySetValue(string path, object? value) => !_revoked &&
        _bindings is ICuiWritableBindingContext writable && writable.TrySetValue(path, value);
    public bool? IsActionAvailable(string command) => !_revoked &&
        _actions is ICuiActionAvailability availability && availability.IsActionAvailable(command) == true;
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _revoked
            ? ValueTask.FromException(new UnauthorizedAccessException("The previous Assistants context has been revoked."))
            : _actions.DispatchAsync(command, parameter, cancellationToken);
    }
    public void Revoke() => _revoked = true;
    private void Changed(object? sender, PropertyChangedEventArgs args)
    { if (!_revoked) PropertyChanged?.Invoke(this, args); }
    public void Dispose()
    {
        Revoke();
        if (_notifications is not null) _notifications.PropertyChanged -= Changed;
    }
}
