namespace CakeOS.Apps.Boards.App;

public enum BoardsHomeBoardCreationOutcome
{
    CancelledBeforeCreate,
    CreateFailedBeforeCommit,
    HomeAuditPending,
    OutcomeUnknown,
    CommittedWithUncertainAudit,
    Opened,
    OpenedWithAuditWarning,
    CreatedOpenFailed
}

public enum BoardsHomeBoardCreationOperation
{
    Create,
    RetrySettlement,
    RetryOpenExisting,
    InspectOutcome,
    OpenRecordedExisting
}

public sealed record BoardsHomeBoardCreationResult(
    BoardsHomeBoardCreationOutcome Outcome,
    string? Code = null,
    string? Message = null);

/// <summary>Explicitly distinguishes first creation from reopening a retained post-commit receipt.</summary>
public sealed record BoardsHomeBoardCreationRequest(
    ReadOnlyMemory<byte> ValidatedDraft,
    BoardsHomeBoardCreationOperation Operation);

/// <summary>
/// Home-owned operation for creating a new Files board and opening its exact
/// authorized session. The typed result distinguishes cancellation/failure
/// before commit from a committed artifact whose Browse session still needs opening.
/// </summary>
public interface IBoardsHomeBoardCreationHost
{
    bool CanRetryOpenCreatedBoard { get; }
    bool CanRetryCreationSettlement { get; }
    bool CanInspectUnknownCreation { get; }
    bool CanOpenRecordedBoard { get; }
    bool HasUnresolvedCreation { get; }
    Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
        CancellationToken cancellationToken = default);
    Task<BoardsHomeBoardCreationResult> RetryCreationSettlementAsync(
        CancellationToken cancellationToken = default);
    Task<BoardsHomeBoardCreationResult> InspectUnknownCreationAsync(
        CancellationToken cancellationToken = default);
    Task<BoardsHomeBoardCreationResult> OpenRecordedBoardAsync(
        CancellationToken cancellationToken = default);
    Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(
        CancellationToken cancellationToken = default);
}
