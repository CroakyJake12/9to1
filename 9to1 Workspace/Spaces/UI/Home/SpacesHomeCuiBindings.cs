using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Home;

/// <summary>
/// CUI binding owner for <c>SpacesHome.cui</c>.
///
/// The authored surface only reads values and raises commands. This class publishes the current
/// controller projection and dispatches every action back through the host so the CUI document never
/// calls domain methods directly.
/// </summary>
public sealed class SpacesHomeCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private static readonly IReadOnlySet<string> KnownCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "spaces.create", "spaces.open", "spaces.rename", "spaces.move", "spaces.fork",
        "spaces.archive", "spaces.add-context", "spaces.clear-search", "spaces.select", "spaces.set-rename-draft",
        "spaces.confirm-rename", "spaces.cancel-rename",
        "spaces.select-move-parent",
        "spaces.edit-extensions", "spaces.edit-graph"
    };

    private readonly CuiViewModel _values = new();
    private readonly Func<SpacesHomeViewState> _state;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Func<SpacesHomeViewState, SpacesHomeViewState, bool>? _isCurrent;
    private SpacesHomeViewState _snapshot = SpacesHomeViewState.Empty;
    private long _revision;

    public SpacesHomeCuiBindings(
        Func<SpacesHomeViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch,
        Func<SpacesHomeViewState, SpacesHomeViewState, bool>? isCurrent = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isCurrent = isCurrent;
        _values.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SpacesHomeViewState Snapshot => Current ? _snapshot : SpacesHomeViewState.Empty;

    private bool Current => _isCurrent?.Invoke(_state(), _snapshot) ?? true;

    public void Refresh()
    {
        _snapshot = _state();
        _values.Set("Revision", _revision++);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool TryGetValue(string path, out object? value)
    {
        var snapshot = _snapshot;
        value = path switch
        {
            "spaces.query" => snapshot.Query,
            "spaces.builtIns" => snapshot.BuiltIns,
            "spaces.customSpaces" => snapshot.CustomSpaces,
            "spaces.moveParentOptions" => snapshot.MoveParentOptions,
            "spaces.pendingRenameSpaceId" => snapshot.PendingRenameSpaceId,
            "spaces.renameDraft" => snapshot.RenameDraft,
            "spaces.selectedMoveParentIndex" => snapshot.SelectedMoveParentIndex,
            "status.message" => snapshot.StatusMessage,
            "selection.name" => snapshot.Selection?.Name ?? string.Empty,
            "selection.description" => snapshot.Selection?.Description ?? string.Empty,
            "selection.isBuiltIn" => snapshot.Selection?.IsBuiltIn ?? false,
            "selection.canEdit" => snapshot.Selection?.CanEdit ?? false,
            "selection.isRenaming" => snapshot.Selection?.IsRenaming ?? false,
            "selection.hasExtensionEditor" => snapshot.Selection?.HasExtensionEditor ?? false,
            "selection.canEditExtensions" => snapshot.Selection?.CanEditExtensions ?? false,
            "selection.canEditGraph" => snapshot.Selection?.CanEditGraph ?? false,
            "selection.parentSpaceId" => snapshot.Selection?.ParentSpaceId,
            "selection.effectiveModelLabel" => snapshot.Selection?.EffectiveModelLabel ?? string.Empty,
            "selection.webAccessLabel" => snapshot.Selection?.WebAccessLabel ?? string.Empty,
            "selection.memoryScopeLabel" => snapshot.Selection?.MemoryScopeLabel ?? string.Empty,
            "selection.contextReferences" => snapshot.Selection?.ContextReferences ?? [],
            "selection.capabilities" => snapshot.Selection?.Capabilities ?? [],
            _ => Unresolved
        };
        return !ReferenceEquals(value, Unresolved);
    }

    public bool TrySetValue(string path, object? value)
    {
        switch (path)
        {
            case "spaces.query":
                _ = _dispatch("spaces.set-query", value, CancellationToken.None);
                return true;
            case "spaces.selectedMoveParentIndex":
                if (value is int index)
                    _ = _dispatch("spaces.select-move-parent", index, CancellationToken.None);
                return true;
            case "spaces.renameDraft":
                _ = _dispatch("spaces.set-rename-draft", value, CancellationToken.None);
                return true;
            default:
                return false;
        }
    }

    public bool? IsActionAvailable(string command)
    {
        if (!KnownCommands.Contains(command))
            return null;

        var snapshot = Snapshot;
        var selection = snapshot.Selection;
        return command switch
        {
            "spaces.rename" or "spaces.move" or "spaces.archive" => selection is { CanEdit: true },
            "spaces.fork" => selection is not null,
            "spaces.add-context" => selection is { CanEdit: true },
            "spaces.confirm-rename" => snapshot.PendingRenameSpaceId.HasValue,
            "spaces.edit-extensions" => selection is { CanEditExtensions: true },
            "spaces.edit-graph" => selection is { CanEditGraph: true },
            _ => true
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (!KnownCommands.Contains(command))
            throw new InvalidOperationException($"Spaces Home CUI command '{command}' is not owned by this binding.");

        await _dispatch(command, parameter, cancellationToken);
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        var snapshot = Snapshot;

        if (item is SpacesHomeItem homeItem && OwnsHomeItem(homeItem))
        {
            value = path.Equals("id", StringComparison.OrdinalIgnoreCase) ? homeItem.Id
                : path.Equals("name", StringComparison.OrdinalIgnoreCase) ? homeItem.Name
                : path.Equals("summary", StringComparison.OrdinalIgnoreCase) ? homeItem.Summary
                : path.Equals("scopeKey", StringComparison.OrdinalIgnoreCase) ? homeItem.ScopeKey
                : path.Equals("isBuiltIn", StringComparison.OrdinalIgnoreCase) ? homeItem.IsBuiltIn
                : path.Equals("parentSpaceId", StringComparison.OrdinalIgnoreCase) ? homeItem.ParentSpaceId
                : path.Equals("position", StringComparison.OrdinalIgnoreCase) ? homeItem.Position
                : path.Equals("revision", StringComparison.OrdinalIgnoreCase) ? homeItem.Revision
                : path.Equals("canEdit", StringComparison.OrdinalIgnoreCase) ? homeItem.CanEdit
                : null;
            return value is not null;
        }

        if (item is SpacesHomeContextReference reference && OwnsContextReference(reference))
        {
            value = path.Equals("id", StringComparison.OrdinalIgnoreCase) ? reference.ContextId
                : path.Equals("label", StringComparison.OrdinalIgnoreCase) ? reference.Label
                : path.Equals("permissionLabel", StringComparison.OrdinalIgnoreCase) ? reference.PermissionLabel
                : path.Equals("indexStateLabel", StringComparison.OrdinalIgnoreCase) ? reference.IndexStateLabel
                : null;
            return value is not null;
        }

        if (item is SpacesHomeCapability capability && snapshot.Selection?.Capabilities.Contains(capability) == true)
        {
            value = path.Equals("id", StringComparison.OrdinalIgnoreCase) ? capability.Id
                : path.Equals("label", StringComparison.OrdinalIgnoreCase) ? capability.Label
                : null;
            return value is not null;
        }

        return false;

        bool OwnsHomeItem(SpacesHomeItem candidate) =>
            snapshot.BuiltIns.Contains(candidate) || snapshot.CustomSpaces.Contains(candidate);

        bool OwnsContextReference(SpacesHomeContextReference candidate) =>
            snapshot.Selection?.ContextReferences.Contains(candidate) == true;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    private static readonly object Unresolved = new();
}
