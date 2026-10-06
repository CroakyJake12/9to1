namespace Haven.Application;

public sealed partial class ChatSessionService
{
    internal void DemandOriginalCanonicalProcessCoordinator(TaskExecutionCoordinator coordinator)
    {
        if (!ReferenceEquals(taskCoordinator, coordinator))
            throw new InvalidOperationException("The Agent's original Chat producer must use the same canonical process coordinator.");
    }
}
