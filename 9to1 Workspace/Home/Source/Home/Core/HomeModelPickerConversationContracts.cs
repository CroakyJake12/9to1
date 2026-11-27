namespace HavenOS.Home.Core;

/// <summary>Read-only presentation of a canonical Specialist definition. Version identifies
/// the exact definition read; it is not permission to run it or use any of its tools.</summary>
public sealed record HomeModelPickerSpecialist(Guid Id, string Name, string Description, string Version);

/// <summary>An observation of the originating Chat, never a lookup of the currently focused tab.</summary>
public sealed record HomeModelPickerConversationSnapshot(
    Guid ConversationId,
    long ModelSelectionRevision,
    string? SelectedModelName,
    Guid? SelectedSpecialistId,
    string? SelectedSpecialistName,
    string? SelectedSpecialistVersion,
    bool IsSafetyLocked,
    IReadOnlyList<HomeModelPickerSpecialist> Specialists);

/// <summary>Issued by a displayed row with its observed Chat/model/participant state.
/// The original owner rechecks every field and the current canonical definition before applying.</summary>
public sealed record HomeModelPickerSpecialistSelection(
    Guid ConversationId,
    long ModelSelectionRevision,
    Guid? ExpectedSpecialistId,
    string? ExpectedSpecialistVersion,
    Guid? SpecialistId,
    string? DefinitionVersion);

/// <summary>Optional conversation port supplied only by the existing original Chat owner.
/// Implementations retain their real reads and apply through the same Chat action lifetime.</summary>
public interface IHomeModelPickerConversationSource
{
    Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> ReadAsync(CancellationToken cancellationToken = default);
    Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> SelectSpecialistAsync(
        HomeModelPickerSpecialistSelection selection, CancellationToken cancellationToken = default);
}
