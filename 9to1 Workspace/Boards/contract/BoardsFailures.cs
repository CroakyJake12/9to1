namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// The canonical Boards failure identities. The specification's Boards "Failure behaviour" clause lists
/// exactly these twelve codes, "plus the global error contract"; the global equivalents used below
/// (<see cref="BoardsFailureCode.MissingSourceSection"/> and <see cref="BoardsFailureCode.InvalidArgument"/>)
/// come from that global list and are named here so one place owns the vocabulary.
/// </summary>
public enum BoardsFailureCode
{
    BoardNotFound,
    PageNotFound,
    BlockNotFound,
    UnsupportedPageType,
    UnsupportedBlockType,
    InvalidLayoutTransition,
    RevisionConflict,
    PermissionDenied,
    SourceIngestionFailed,
    ScopedChatExternalAccessDenied,
    DependencyUnavailable,
    SharedEngineIncompatible,

    // Global error-contract equivalents used by Boards operations (specification "Actions" clause).
    MissingSourceSection,
    InvalidArgument,
}

/// <summary>
/// A structured Boards failure: the code says what failed, the operation and target identify the action and
/// object, and <see cref="Retryable"/> plus <see cref="RetainedState"/> say what the caller may do next.
///
/// The specification requires these identities rather than prose-only failures ("Errors identify
/// recoverability and the affected operation without leaking forbidden source details"). The message stays
/// human-readable because the app shows it: the code is what automation, Spaces and tests match on, so a
/// reworded sentence must never change what a caller can rely on.
///
/// This derives from <see cref="InvalidOperationException"/> so every existing caller and test that catches
/// the operation-level failure keeps working while the structure is added.
/// </summary>
public sealed class BoardsFailureException : InvalidOperationException
{
    public BoardsFailureException(BoardsFailureCode code, string operation, string? targetId, string message,
        bool retryable = false, string retainedState = "the board document is unchanged")
        : base(message)
    {
        Code = code;
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        TargetId = targetId;
        Retryable = retryable;
        RetainedState = retainedState;
    }

    public BoardsFailureCode Code { get; }

    /// <summary>The canonical operation the failure belongs to, e.g. "Boards.Block.SetLayout".</summary>
    public string Operation { get; }

    /// <summary>The stable board/page/block id the caller supplied, when one was supplied.</summary>
    public string? TargetId { get; }

    public bool Retryable { get; }

    /// <summary>What the caller still has after the failure, so "failed" never means "your edit is gone".</summary>
    public string RetainedState { get; }

    /// <summary>One-line, code-first text the app can show and a test can assert on.</summary>
    public string Describe() => Retryable
        ? $"{Code} ({Operation}): {Message} [{TargetId ?? "no target"} — retained: {RetainedState}, retryable]"
        : $"{Code} ({Operation}): {Message} [{TargetId ?? "no target"} — retained: {RetainedState}]";
}
