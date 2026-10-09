using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Historical observation of one acknowledged import's actual successful audit.
/// It is not a current ownership receipt, an import permission or a redispatch instruction.</summary>
public sealed class HomeStoreImportAuditRecoveryObservation
{
    internal HomeStoreImportAuditRecoveryObservation(AuthenticatedResourceActor actor,
        string requestId, HomeLocalStoreBinding binding)
    { Actor = actor; RequestId = requestId; Binding = binding; }

    public AuthenticatedResourceActor Actor { get; }
    public string RequestId { get; }
    public HomeLocalStoreBinding Binding { get; }
}

public sealed partial class HomeLocalStoreOwnership
{
    private enum OriginalImportOperation { Complete, RetryAudit }
    private sealed class OriginalImportInvocation(OriginalImportOperation operation, string requestId)
    {
        public OriginalImportOperation Operation { get; } = operation;
        public string RequestId { get; } = requestId;
        public UnauthorizedAccessException? PreEffectRefusal { get; set; }
        public HomeStoreImportAuditPendingException? PendingCause { get; set; }
        public ImportCompletion? Completion { get; set; }
        public bool AuditGateReleased { get; set; }
        public HomeStoreImportAuditRecoveryObservation? RecoveryObservation { get; set; }
    }
    private readonly ConditionalWeakTable<Task<HomeLocalStoreBinding>, OriginalImportInvocation> _originalImportInvocations = new();

    /// <summary>Observes only a direct pre-effect refusal of this owner's SAME canonical
    /// CompleteImport invocation/task. Request-removal, execution, binding and body errors
    /// are not inferred from their type or an earlier invocation's exception.</summary>
    public bool TryObserveOriginalPreEffectRefusal(Task<HomeLocalStoreBinding> originalCanonicalTask,
        Exception originalCause) =>
        originalCanonicalTask is not null && originalCause is not null &&
        _originalImportInvocations.TryGetValue(originalCanonicalTask, out var invocation) &&
        invocation.Operation == OriginalImportOperation.Complete &&
        ReferenceEquals(invocation.PreEffectRefusal, originalCause) &&
        HasOnlyOriginalCause(originalCanonicalTask, originalCause);

    /// <summary>Correlates this owner's exact failed canonical task/pending cause with
    /// the SAME private completion and an actual successful canonical Retry task/result.
    /// Audit success is recorded only after its actual gate release. Foreign tasks, replayed
    /// causes and caller-created results refuse; this observation grants no current access.</summary>
    public bool TryObserveOriginalImportAuditRecovery(Task<HomeLocalStoreBinding> originalPendingCanonicalTask,
        HomeStoreImportAuditPendingException originalPending,
        Task<HomeLocalStoreBinding> originalSuccessfulRetryCanonicalTask,
        AuthenticatedResourceActor originalActor, out HomeStoreImportAuditRecoveryObservation? observation)
    {
        observation = null;
        if (originalPendingCanonicalTask is null || originalPending is null ||
            originalSuccessfulRetryCanonicalTask is null || originalActor is null ||
            !_originalImportInvocations.TryGetValue(originalPendingCanonicalTask, out var pendingInvocation) ||
            !_originalImportInvocations.TryGetValue(originalSuccessfulRetryCanonicalTask, out var recoveryInvocation) ||
            !ReferenceEquals(pendingInvocation.PendingCause, originalPending) ||
            !HasOnlyOriginalCause(originalPendingCanonicalTask, originalPending) ||
            recoveryInvocation.Operation != OriginalImportOperation.RetryAudit ||
            !originalSuccessfulRetryCanonicalTask.IsCompletedSuccessfully ||
            pendingInvocation.Completion is not { } completion ||
            !ReferenceEquals(recoveryInvocation.Completion, completion) ||
            recoveryInvocation.RecoveryObservation is not { } recovered ||
            pendingInvocation.RequestId != originalPending.RequestId ||
            recoveryInvocation.RequestId != pendingInvocation.RequestId ||
            recovered.RequestId != pendingInvocation.RequestId ||
            completion.Actor != originalActor || recovered.Actor != originalActor ||
            !ReferenceEquals(originalPending.Binding, completion.Binding) ||
            !ReferenceEquals(originalSuccessfulRetryCanonicalTask.Result, completion.Binding) ||
            !ReferenceEquals(recovered.Binding, completion.Binding))
            return false;
        observation = recovered;
        return true;
    }

    private static bool HasOnlyOriginalCause(Task originalTask, Exception originalCause)
    {
        if (!originalTask.IsFaulted) return false;
        var failure = originalTask.Exception;
        return failure is not null && failure.InnerExceptions.Count == 1 &&
            ReferenceEquals(failure.InnerExceptions[0], originalCause);
    }

    private static UnauthorizedAccessException RetainOriginalPreEffectRefusal(
        OriginalImportInvocation invocation, string message)
    {
        var original = new UnauthorizedAccessException(message);
        invocation.PreEffectRefusal = original;
        return original;
    }

    private static HomeStoreImportAuditPendingException RetainOriginalImportAuditPending(
        OriginalImportInvocation invocation, string requestId, ImportCompletion completion, Exception originalCause)
    {
        var original = new HomeStoreImportAuditPendingException(requestId, completion.Binding, originalCause);
        invocation.PendingCause = original;
        return original;
    }
}
