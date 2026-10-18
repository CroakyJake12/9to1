using Haven.Application;

namespace HavenOS.Apps.Spaces.Builder;

public enum SpaceDetailsField { Name, Description, IconKey, Instructions }

public sealed record SpaceDetailsDraft(string Name, string Description, string IconKey, string Instructions)
{
    public static SpaceDetailsDraft Empty { get; } = new("", "", "", "");
    internal static SpaceDetailsDraft From(SpaceDefinition space) =>
        new(space.Name, space.Description, space.IconKey, space.Instructions);
}

/// <summary>Four editable details of one existing Space, not a separate Space configuration
/// or a graph Draft/Active revision. Unexposed configuration stays with SpaceRegistry.</summary>
public sealed record SpaceDetailsState(
    Guid SpaceId, long? LoadedRevision, SpaceDetailsDraft Draft, bool IsLoaded,
    bool IsReadOnly, bool IsLoading, bool IsSaving, bool HasChanges, bool HasConflict, string StatusMessage)
{
    public bool IsBusy => IsLoading || IsSaving;
    public bool CanEdit => IsLoaded && !IsReadOnly && !IsLoading;
    public bool CanSave => CanEdit && !IsBusy && HasChanges && !HasConflict;
    public bool CanDiscard => IsLoaded && !IsBusy && (HasChanges || HasConflict);
    public bool CanReload => !IsBusy && !HasChanges;
}
