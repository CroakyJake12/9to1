using Haven.Application;

namespace HavenOS.Apps.Spaces.Builder;

/// <summary>Policy gate for an entire resource category in a UserCreated Space. Available does
/// not imply permission granted; effective state is resolved against Home/Dulche policy.</summary>
public enum SpaceBuilderCategoryState
{
    Inherited,
    Enabled,
    Disabled
}

/// <summary>One policy rule for a specific resource inside a Space. Mandatory + Blocked is a
/// validation failure, never a bypass.</summary>
public sealed record SpaceBuilderResourceRule(
    Guid ResourceId,
    bool Mandatory,
    bool Allowed,
    bool Blocked,
    string Source);

/// <summary>Draft configuration for one canonical Space. Draft and Active are separate revisions
/// of the SAME SpaceID; an invalid draft never displaces the last known-good active revision.</summary>
public sealed record SpaceBuilderDraft(
    string Name,
    string Description,
    string IconKey,
    string Instructions,
    SpaceBuilderCategoryState Skills,
    SpaceBuilderCategoryState Plugins,
    SpaceBuilderCategoryState Apps,
    SpaceBuilderCategoryState Models,
    SpaceBuilderCategoryState Memory,
    IReadOnlyList<SpaceBuilderResourceRule> SkillRules,
    IReadOnlyList<SpaceBuilderResourceRule> PluginRules,
    IReadOnlyList<SpaceBuilderResourceRule> AppRules,
    IReadOnlyList<SpaceBuilderResourceRule> ModelRules)
{
    public static SpaceBuilderDraft Empty { get; } = new("", "", "", "",
        SpaceBuilderCategoryState.Inherited, SpaceBuilderCategoryState.Inherited,
        SpaceBuilderCategoryState.Inherited, SpaceBuilderCategoryState.Inherited,
        SpaceBuilderCategoryState.Inherited, [], [], [], []);

    internal static SpaceBuilderDraft From(SpaceDefinition space) =>
        new(space.Name, space.Description, space.IconKey, space.Instructions,
            SpaceBuilderCategoryState.Inherited, SpaceBuilderCategoryState.Inherited,
            SpaceBuilderCategoryState.Inherited, SpaceBuilderCategoryState.Inherited,
            SpaceBuilderCategoryState.Inherited, [], [], [], []);
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
