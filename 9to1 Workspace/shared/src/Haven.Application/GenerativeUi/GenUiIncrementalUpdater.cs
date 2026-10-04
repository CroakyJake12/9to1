using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Extended incremental GenUI operations beyond basic patches.
/// Supports structured state mutations that preserve user selections,
/// input state, scroll position, focus and ongoing interactions.
/// </summary>
public enum GenUiIncrementalOperation
{
    SetState,
    PatchState,
    AddComponent,
    RemoveComponent,
    ReplaceComponent,
    MoveComponent,
    UpdateProperties,
    SetSelection,
    SetError,
    SetProgress,
    DismissSurface
}

/// <summary>
/// A structured incremental operation on a GenUI instance.
/// Unlike raw patches, these operations carry semantic meaning
/// that the renderer can use to animate transitions smoothly.
/// </summary>
public sealed record GenUiIncrementalChange(
    Guid ChangeId,
    Guid InstanceId,
    GenUiIncrementalOperation Operation,
    string? TargetComponentId,
    string? SourceContainerId,
    string? DestinationContainerId,
    int? Position,
    string? PropertyKey,
    JsonElement? Value,
    string? ErrorMessage,
    double? ProgressValue,
    DateTimeOffset Timestamp)
{
    /// <summary>Converts to the underlying patch representation for store application.</summary>
    public GenUiStatePatch ToPatch() => Operation switch
    {
        GenUiIncrementalOperation.SetState or GenUiIncrementalOperation.PatchState =>
            new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                "state", PropertyKey ?? string.Empty, Value, Timestamp),
        GenUiIncrementalOperation.UpdateProperties =>
            new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                TargetComponentId ?? string.Empty, PropertyKey ?? string.Empty, Value, Timestamp),
        GenUiIncrementalOperation.SetError =>
            new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                TargetComponentId ?? string.Empty, "error", Value, Timestamp),
        GenUiIncrementalOperation.SetProgress =>
            new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                TargetComponentId ?? string.Empty, "value",
                ProgressValue.HasValue ? JsonSerializer.SerializeToElement(ProgressValue.Value) : Value, Timestamp),
        _ => new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                TargetComponentId ?? string.Empty, PropertyKey ?? string.Empty, Value, Timestamp)
    };
}

/// <summary>
/// Applies incremental changes to GenUI instances while preserving
/// unrelated user state. Changes are idempotent and ordered.
/// </summary>
public sealed class GenUiIncrementalUpdater
{
    private readonly GenUiInstanceStore _instances;

    public GenUiIncrementalUpdater(GenUiInstanceStore instances) => _instances = instances;

    public event EventHandler<GenUiIncrementalChange>? ChangeApplied;

    /// <summary>
    /// Applies a single incremental change. Returns true if the change
    /// was applied (idempotent; duplicate change IDs are skipped).
    /// </summary>
    public bool Apply(GenUiIncrementalChange change)
    {
        switch (change.Operation)
        {
            case GenUiIncrementalOperation.DismissSurface:
                return _instances.Remove(change.InstanceId);

            case GenUiIncrementalOperation.AddComponent:
            case GenUiIncrementalOperation.RemoveComponent:
            case GenUiIncrementalOperation.ReplaceComponent:
            case GenUiIncrementalOperation.MoveComponent:
                return ApplyStructuralChange(change);

            default:
                var applied = _instances.ApplyPatch(change.ToPatch());
                if (applied) ChangeApplied?.Invoke(this, change);
                return applied;
        }
    }

    /// <summary>
    /// Applies a batch of incremental changes atomically.
    /// </summary>
    public IReadOnlyList<bool> ApplyBatch(IEnumerable<GenUiIncrementalChange> changes)
    {
       var batch = changes.ToArray();
       if (batch.Length == 0) return [];
       if (batch.Any(change => change.Operation is GenUiIncrementalOperation.AddComponent
          or GenUiIncrementalOperation.RemoveComponent
          or GenUiIncrementalOperation.ReplaceComponent
          or GenUiIncrementalOperation.MoveComponent
          or GenUiIncrementalOperation.DismissSurface))
          throw new InvalidOperationException("Atomic batches currently support state/property/progress/error changes only.");

       var results = _instances.ApplyPatchesAtomically(batch.Select(change => change.ToPatch()).ToArray());
       for (var i = 0; i < batch.Length; i++)
          if (results[i]) ChangeApplied?.Invoke(this, batch[i]);
       return results;
    }
    private bool ApplyStructuralChange(GenUiIncrementalChange change)
    {
        var applied = _instances.ApplyDocumentChange(change.ChangeId, change.InstanceId,
            document => change.Operation switch
            {
                GenUiIncrementalOperation.AddComponent => AddComponentToDocument(document, change),
                GenUiIncrementalOperation.RemoveComponent => RemoveComponentFromDocument(document, change),
                GenUiIncrementalOperation.ReplaceComponent => ReplaceComponentInDocument(document, change),
                GenUiIncrementalOperation.MoveComponent => MoveComponentInDocument(document, change),
                _ => document
            }, change.Timestamp);
        if (applied) ChangeApplied?.Invoke(this, change);
        return applied;
    }

    private static GenUiDocument AddComponentToDocument(GenUiDocument document, GenUiIncrementalChange change)
    {
        if (change.Value is null || change.Value.Value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("An added component is required.");
        var component = JsonSerializer.Deserialize<GenUiComponent>(change.Value!.Value.GetRawText());
        if (component is null) throw new InvalidOperationException("The added component is invalid.");

        if (change.DestinationContainerId is null)
        {
            return document with
            {
                Root = document.Root with
                {
                    Children = [.. document.Root.Children, component]
                }
            };
        }

        if (FindComponent(document.Root, change.DestinationContainerId) is null)
            throw new InvalidOperationException($"Destination container '{change.DestinationContainerId}' does not exist.");
        return document with { Root = AddChildToContainer(document.Root, change.DestinationContainerId, component, change.Position) };
    }

    private static GenUiDocument RemoveComponentFromDocument(GenUiDocument document, GenUiIncrementalChange change)
    {
        if (string.IsNullOrWhiteSpace(change.TargetComponentId) || change.TargetComponentId == document.Root.ComponentId) throw new InvalidOperationException("A non-root component ID is required for removal.");
        if (FindComponent(document.Root, change.TargetComponentId) is null) throw new InvalidOperationException("The component to remove does not exist.");
        return document with { Root = RemoveChild(document.Root, change.TargetComponentId) };
    }

    private static GenUiDocument ReplaceComponentInDocument(GenUiDocument document, GenUiIncrementalChange change)
    {
        if (string.IsNullOrWhiteSpace(change.TargetComponentId) || change.Value is null || change.Value.Value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("A target ID and replacement component are required.");
        var replacement = JsonSerializer.Deserialize<GenUiComponent>(change.Value!.Value.GetRawText());
        if (replacement is null || replacement.ComponentId != change.TargetComponentId) throw new InvalidOperationException("A replacement must preserve the target component ID.");
        if (FindComponent(document.Root, change.TargetComponentId) is null) throw new InvalidOperationException("The component to replace does not exist.");
        return document with { Root = ReplaceChild(document.Root, change.TargetComponentId, replacement) };
    }

    private static GenUiDocument MoveComponentInDocument(GenUiDocument document, GenUiIncrementalChange change)
    {
        if (string.IsNullOrWhiteSpace(change.TargetComponentId) || string.IsNullOrWhiteSpace(change.DestinationContainerId) || change.TargetComponentId == document.Root.ComponentId)
            throw new InvalidOperationException("A movable component and destination container are required.");
        var component = FindComponent(document.Root, change.TargetComponentId) ?? throw new InvalidOperationException("The component to move does not exist.");
        var destination = FindComponent(document.Root, change.DestinationContainerId) ?? throw new InvalidOperationException("The destination container does not exist.");
        if (Contains(component, destination.ComponentId)) throw new InvalidOperationException("A component cannot be moved into itself or one of its descendants.");
        var root = RemoveChild(document.Root, change.TargetComponentId);
        return document with { Root = AddChildToContainer(root, change.DestinationContainerId, component, change.Position) };
    }

    private static bool Contains(GenUiComponent root, string componentId) =>
        root.ComponentId.Equals(componentId, StringComparison.Ordinal) || root.Children.Any(child => Contains(child, componentId));

    private static GenUiComponent AddChildToContainer(GenUiComponent root, string containerId, GenUiComponent child, int? position)
    {
        if (root.ComponentId.Equals(containerId, StringComparison.Ordinal))
        {
            var children = root.Children.ToList();
            if (position.HasValue && position.Value >= 0 && position.Value <= children.Count)
                children.Insert(position.Value, child);
            else
                children.Add(child);
            return root with { Children = children };
        }
        return root with { Children = root.Children.Select(c => AddChildToContainer(c, containerId, child, position)).ToArray() };
    }

    private static GenUiComponent RemoveChild(GenUiComponent root, string targetId)
    {
        var children = root.Children
            .Where(c => !c.ComponentId.Equals(targetId, StringComparison.Ordinal))
            .Select(c => RemoveChild(c, targetId))
            .ToArray();
        return root with { Children = children };
    }

    private static GenUiComponent ReplaceChild(GenUiComponent root, string targetId, GenUiComponent replacement)
    {
        if (root.ComponentId.Equals(targetId, StringComparison.Ordinal)) return replacement;
        return root with { Children = root.Children.Select(c => ReplaceChild(c, targetId, replacement)).ToArray() };
    }

    private static GenUiComponent? FindComponent(GenUiComponent root, string targetId)
    {
        if (root.ComponentId.Equals(targetId, StringComparison.Ordinal)) return root;
        foreach (var child in root.Children)
        {
            var found = FindComponent(child, targetId);
            if (found is not null) return found;
        }
        return null;
    }
}
