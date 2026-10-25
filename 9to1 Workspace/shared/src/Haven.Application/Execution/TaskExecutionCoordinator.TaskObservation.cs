namespace Haven.Application;

/// <summary>A display scope over the same canonical tasks. Selecting a scope never
/// changes task origin, ownership, execution, or authority to read another context.</summary>
public enum TaskShownScope
{
    TaskSpaceOnly = 0,
    AllTasks = 1
}

/// <summary>Bounded raw IDs from the SAME task store. Candidates and the stable cursor
/// grant no visibility: the coordinator must freshly authorize every selected task.</summary>
public sealed record TaskObservationCandidatePage(IReadOnlyList<Guid> TaskIds, Guid? NextTaskId);

/// <summary>Optional read-only paging; adapters without a real census cannot advertise All Tasks.
/// Ordering is the canonical task key, independent of mutable activity timestamps. New inserts
/// before an issued cursor appear on a fresh first page; this is not an immutable snapshot.</summary>
public interface ITaskExecutionObservationCensus
{
    Task<TaskObservationCandidatePage> ReadTaskCandidatesAsync(int maximumItems, Guid? afterTaskId,
        CancellationToken cancellationToken);
}
