namespace CakeOS.Apps.Boards.App;

public enum BoardsHomeBoardCreationOutcome
{
    CancelledBeforeCreate,
    CreateFailedBeforeCommit,
    Opened,
    CreatedOpenFailed
}

public sealed record BoardsHomeBoardCreationResult(
    BoardsHomeBoardCreationOutcome Outcome,
    string? Code = null,
    string? Message = null);

/// <summary>Explicitly distinguishes first creation from reopening a retained post-commit receipt.</summary>
public sealed record BoardsHomeBoardCreationRequest(
    ReadOnlyMemory<byte> ValidatedDraft,
    bool RetryOpenExisting);

/// <summary>
/// Home-owned operation for creating a new Files board and opening its exact
/// authorized session. The typed result distinguishes cancellation/failure
/// before commit from a committed artifact whose Browse session still needs opening.
/// </summary>
public interface IBoardsHomeBoardCreationHost
{
    bool CanRetryOpenCreatedBoard { get; }
    Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
        CancellationToken cancellationToken = default);
    Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(
        CancellationToken cancellationToken = default);
}
