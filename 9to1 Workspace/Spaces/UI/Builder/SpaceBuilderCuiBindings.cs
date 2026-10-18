using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace HavenOS.Apps.Spaces.Builder;

/// <summary>Host supplies synchronous draft edits and owns/awaits asynchronous commands.</summary>
public sealed class SpaceBuilderCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly Func<SpaceBuilderState> _state;
    private readonly Action<SpaceBuilderField, string> _edit;
    private readonly Action<SpaceBuilderCategory, SpaceCapabilityCategoryState> _setCategory;
    private readonly Action<SpaceCapabilityKind, string> _detachCapability;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Func<bool> _isCurrent;
    private readonly Func<bool> _canGoBack;
    private static readonly string[] CategoryOptions = ["Inherited", "Enabled", "Disabled"];

    public SpaceBuilderCuiBindings(Func<SpaceBuilderState> state, Action<SpaceBuilderField, string> edit,
        Action<SpaceBuilderCategory, SpaceCapabilityCategoryState> setCategory,
        Action<SpaceCapabilityKind, string> detachCapability,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Func<bool>? isCurrent = null)
        : this(state, edit, setCategory, detachCapability, dispatch, isCurrent, null) { }

    public SpaceBuilderCuiBindings(Func<SpaceBuilderState> state, Action<SpaceBuilderField, string> edit,
        Action<SpaceBuilderCategory, SpaceCapabilityCategoryState> setCategory,
        Action<SpaceCapabilityKind, string> detachCapability,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Func<bool>? isCurrent, Func<bool>? canGoBack)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _edit = edit ?? throw new ArgumentNullException(nameof(edit));
        _setCategory = setCategory ?? throw new ArgumentNullException(nameof(setCategory));
        _detachCapability = detachCapability ?? throw new ArgumentNullException(nameof(detachCapability));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isCurrent = isCurrent ?? (() => true);
        _canGoBack = canGoBack ?? (() => false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private int CategoryIndex(SpaceBuilderCategory category) => category switch
    {
        SpaceBuilderCategory.Skills => (int)_state().Draft.CategoryPolicy.Skills,
        SpaceBuilderCategory.Plugins => (int)_state().Draft.CategoryPolicy.Plugins,
        SpaceBuilderCategory.Apps => (int)_state().Draft.CategoryPolicy.Apps,
        SpaceBuilderCategory.Models => (int)_state().Draft.CategoryPolicy.Models,
        SpaceBuilderCategory.Memory => (int)_state().Draft.CategoryPolicy.Memory,
        _ => 0
    };

    public bool TryGetValue(string path, out object? value)
    {
        var state = _state();
        value = path switch
        {
            "builder.name" => state.Draft.Name,
            "builder.description" => state.Draft.Description,
            "builder.icon" => state.Draft.IconKey,
            "builder.instructions" => state.Draft.Instructions,
            "builder.capabilities" => state.Draft.CapabilityBindings.Select(b => new
            {
                id = b.CapabilityId,
                kind = b.Kind.ToString(),
                version = b.VersionPin ?? "(unpinned)"
            }).ToArray(),
            "builder.hasNoCapabilities" => state.Draft.CapabilityBindings.Count == 0,
            "builder.category.options" => CategoryOptions,
            "builder.category.skills" => CategoryIndex(SpaceBuilderCategory.Skills),
            "builder.category.plugins" => CategoryIndex(SpaceBuilderCategory.Plugins),
            "builder.category.apps" => CategoryIndex(SpaceBuilderCategory.Apps),
            "builder.category.models" => CategoryIndex(SpaceBuilderCategory.Models),
            "builder.category.memory" => CategoryIndex(SpaceBuilderCategory.Memory),
            "builder.canEdit" => _isCurrent() && state.CanEdit,
            "builder.canSave" => _isCurrent() && state.CanSave,
            "builder.canDiscard" => _isCurrent() && state.CanDiscard,
            "builder.canReload" => _isCurrent() && state.CanReload,
            "builder.canGoBack" => _isCurrent() && _canGoBack(),
            "builder.status" => state.StatusMessage,
            _ => null
        };
        return value is not null;
    }

    public bool TrySetValue(string path, object? value)
    {
        if (!_isCurrent() || !_state().CanEdit) return false;
        SpaceBuilderField? field = path switch
        {
            "builder.name" => SpaceBuilderField.Name,
            "builder.description" => SpaceBuilderField.Description,
            "builder.icon" => SpaceBuilderField.IconKey,
            "builder.instructions" => SpaceBuilderField.Instructions,
            _ => null
        };
        if (field is not null && value is string text)
        {
            _edit(field.Value, text);
            return true;
        }
        SpaceBuilderCategory? cat = path switch
        {
            "builder.category.skills" => SpaceBuilderCategory.Skills,
            "builder.category.plugins" => SpaceBuilderCategory.Plugins,
            "builder.category.apps" => SpaceBuilderCategory.Apps,
            "builder.category.models" => SpaceBuilderCategory.Models,
            "builder.category.memory" => SpaceBuilderCategory.Memory,
            _ => null
        };
        if (cat is not null && value is int idx && idx >= 0 && idx <= 2)
        {
            _setCategory(cat.Value, (SpaceCapabilityCategoryState)idx);
            return true;
        }
        return false;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is null) return false;
        var type = item.GetType();
        var prop = type.GetProperty(path, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (prop is not null)
        {
            value = prop.GetValue(item);
            return true;
        }
        return false;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    public bool? IsActionAvailable(string command) => command switch
    {
        "space-builder.save" => _isCurrent() && _state().CanSave,
        "space-builder.discard" => _isCurrent() && _state().CanDiscard,
        "space-builder.reload" => _isCurrent() && _state().CanReload,
        "space-builder.back" => _isCurrent() && _canGoBack(),
        "space-builder.capability.remove" => _isCurrent() && _state().CanEdit,
        _ => null
    };

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true)
            throw new InvalidOperationException("This Space Builder command is unavailable.");
        if (command == "space-builder.capability.remove" && parameter is string capId)
        {
            var kind = _state().Draft.CapabilityBindings.FirstOrDefault(b => b.CapabilityId == capId)?.Kind ?? SpaceCapabilityKind.Plugin;
            _detachCapability(kind, capId);
            return;
        }
        await _dispatch(command, parameter, cancellationToken);
    }
}
