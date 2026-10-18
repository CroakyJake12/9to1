using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Spaces.Builder;

/// <summary>The host supplies synchronous draft edits and owns/awaits asynchronous commands.
/// Two-way text changes never launch an unobserved async operation.</summary>
public sealed class SpaceDetailsCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly Func<SpaceDetailsState> _state;
    private readonly Action<SpaceDetailsField, string> _edit;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Func<bool> _isCurrent;
    private readonly Func<bool> _canGoBack;

    public SpaceDetailsCuiBindings(Func<SpaceDetailsState> state, Action<SpaceDetailsField, string> edit,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Func<bool>? isCurrent = null)
        : this(state, edit, dispatch, isCurrent, null) { }

    public SpaceDetailsCuiBindings(Func<SpaceDetailsState> state, Action<SpaceDetailsField, string> edit,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Func<bool>? isCurrent, Func<bool>? canGoBack)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _edit = edit ?? throw new ArgumentNullException(nameof(edit));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isCurrent = isCurrent ?? (() => true);
        _canGoBack = canGoBack ?? (() => false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public bool TryGetValue(string path, out object? value)
    {
        var state = _state();
        value = path switch
        {
            "details.name" => state.Draft.Name,
            "details.description" => state.Draft.Description,
            "details.icon" => state.Draft.IconKey,
            "details.instructions" => state.Draft.Instructions,
            "details.canEdit" => _isCurrent() && state.CanEdit,
            "details.canSave" => _isCurrent() && state.CanSave,
            "details.canDiscard" => _isCurrent() && state.CanDiscard,
            "details.canReload" => _isCurrent() && state.CanReload,
            "details.canGoBack" => _isCurrent() && _canGoBack(),
            "details.status" => state.StatusMessage,
            _ => null
        };
        return value is not null;
    }

    public bool TrySetValue(string path, object? value)
    {
        if (!_isCurrent() || !_state().CanEdit || value is not string text) return false;
        SpaceDetailsField? field = path switch
        {
            "details.name" => SpaceDetailsField.Name,
            "details.description" => SpaceDetailsField.Description,
            "details.icon" => SpaceDetailsField.IconKey,
            "details.instructions" => SpaceDetailsField.Instructions,
            _ => null
        };
        if (field is null) return false;
        _edit(field.Value, text);
        return true;
    }

    public bool? IsActionAvailable(string command) => command switch
    {
        "space-details.save" => _isCurrent() && _state().CanSave,
        "space-details.discard" => _isCurrent() && _state().CanDiscard,
        "space-details.reload" => _isCurrent() && _state().CanReload,
        "space-details.back" => _isCurrent() && _canGoBack(),
        _ => null
    };

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true)
            throw new InvalidOperationException("This Space details command is unavailable.");
        await _dispatch(command, parameter, cancellationToken);
    }
}
