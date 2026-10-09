namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    /// <summary>Pure configured composition availability only. This establishes no
    /// capsule eligibility, actor currentness, restored authority, provider readiness,
    /// resource permission or business admission. Every later cold operation must
    /// independently validate its actual source and current authority.</summary>
    public bool HasOriginalColdResumeComposition(ChatSessionService sameChat)
    {
        if (sameChat is null || !sameChat.HasOriginalCanonicalProcessCoordinator(this)) return false;
        ITaskRunColdRecoveryJournal? journal;
        ITaskRunColdContextAuthority? context;
        TaskRunPermissionAuthority? authority;
        lock (_processProducerGate)
        {
            if (_processProducerAdmissionSealed) return false;
            journal = _coldRecoveryJournal;
            context = _coldRecoveryContext;
            authority = _admissionAuthority as TaskRunPermissionAuthority;
        }
        // Call only the SAME sealed maintained authority's pure captured-reference
        // observation, outside the coordinator lock. No foreign interface callback.
        if (journal is null || context is null || authority is null || authority.IsOriginalAdmissionSealed ||
            !authority.HasOriginalColdRecoveryComposition(journal, context)) return false;
        lock (_processProducerGate)
            return !_processProducerAdmissionSealed && ReferenceEquals(_coldRecoveryJournal, journal) &&
                ReferenceEquals(_coldRecoveryContext, context) && ReferenceEquals(_admissionAuthority, authority);
    }
}
