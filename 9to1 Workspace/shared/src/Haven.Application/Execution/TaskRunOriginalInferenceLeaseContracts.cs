namespace Haven.Application;

// Access to the SAME issuer's existing scoped LOCAL model-use validation.
// Interface presence alone never establishes issued admission or current permission.
public interface ITaskRunOriginalInferenceLeaseSource
{
    Task RevalidateOriginalInferenceWithinSourceAsync(TaskRunAttemptAdmission sameAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
}
