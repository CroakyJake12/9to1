using Haven.Application;

namespace HavenOS.Apps.Spaces.Builder;

/// <summary>Alias to the canonical category state so the Builder does not invent a second owner.</summary>
using SpaceCapabilityCategoryState = Haven.Application.SpaceCapabilityCategoryState;

/// <summary>One policy rule for a specific resource inside a Space. Mandatory + Blocked is a
/// validation failure, never a bypass.</summary>
/// <summary>Draft configuration for one canonical Space. Draft and Active are separate revisions
/// of the SAME SpaceID; an invalid draft never displaces the last known-good active revision.</summary>
public sealed record SpaceBuilderDraft(
    string Name,
    string Description,
    string IconKey,
    string Instructions,
    Haven.Application.SpaceCapabilityCategoryPolicy CategoryPolicy,
    IReadOnlyList<Haven.Application.SpaceCapabilityBinding> CapabilityBindings)
{
    public static SpaceBuilderDraft Empty { get; } = new("", "", "", "",
        new Haven.Application.SpaceCapabilityCategoryPolicy(), []);

    internal static SpaceBuilderDraft From(SpaceDefinition space) =>
        new(space.Name, space.Description, space.IconKey, space.Instructions,
            space.CapabilityCategoryPolicy ?? new Haven.Application.SpaceCapabilityCategoryPolicy(),
            space.CapabilityBindings?.ToArray() ?? []);
}

public sealed record SpaceBuilderState(
    Guid SpaceId,
    long? DraftRevision,
    long? ActiveRevision,
    SpaceBuilderDraft Draft,
    bool IsLoaded,
    bool IsReadOnly,
    bool IsLoading,
    bool IsSaving,
    bool HasChanges,
    bool HasConflict,
    string StatusMessage)
{
    public bool IsBusy => IsLoading || IsSaving;
    public bool CanEdit => IsLoaded && !IsReadOnly && !IsLoading;
    public bool CanSave => CanEdit && !IsBusy && HasChanges && !HasConflict;
    public bool CanDiscard => IsLoaded && !IsBusy && (HasChanges || HasConflict);
    public bool CanReload => !IsBusy && !HasChanges;
    public bool HasActiveRevision => ActiveRevision is not null;
}
