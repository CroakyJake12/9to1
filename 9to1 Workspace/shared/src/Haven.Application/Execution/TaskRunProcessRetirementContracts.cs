namespace Haven.Application;

/// <summary>The actual configured producer owner. A stop request or successful drain is
/// neither task permission nor the host's final-clean acknowledgment.</summary>
public interface ITaskRunProcessRetirementParticipant
{
    /// <summary>Pure preflight. An admitted original cannot join its own process drain.</summary>
    void DemandExternalOriginalProcessJoin();
    /// <summary>Seals admission and publishes request originals; performs no caller join.</summary>
    void RequestOriginalProcessRetirement();
    /// <summary>The same actual whole close task. Unknown, canceled or faulted originals remain failures.</summary>
    Task CloseAndSuspendOriginalProducersAsync();
}
