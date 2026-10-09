namespace Haven.Application;

public sealed partial class ChatSessionService
{
    // Immutable constructor pairing only; no source read or process admission.
    internal bool HasOriginalCanonicalProcessCoordinator(TaskExecutionCoordinator sameCoordinator) =>
        ReferenceEquals(taskCoordinator, sameCoordinator);
}
