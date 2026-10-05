using System.Runtime.CompilerServices;

namespace HavenOS.Home.Core;

public sealed partial class HomePersonalDenFactory
{
    private sealed class OriginalDenInvocation
    {
        public UnauthorizedAccessException? PreEffectRefusal { get; set; }
    }
    private readonly ConditionalWeakTable<Task<HomePersonalDenSession>, OriginalDenInvocation> _originalDenInvocations = new();

    /// <summary>Observes only the exact refusal directly created by this factory's SAME
    /// canonical OpenAsync invocation/task. A later callback replaying an earlier exception
    /// has a different invocation and refuses. This historical observation grants no access.</summary>
    public bool TryObserveOriginalPreEffectRefusal(Task<HomePersonalDenSession> originalCanonicalTask,
        Exception originalCause)
    {
        if (originalCanonicalTask is null || originalCause is null ||
            !_originalDenInvocations.TryGetValue(originalCanonicalTask, out var invocation) ||
            !ReferenceEquals(invocation.PreEffectRefusal, originalCause) || !originalCanonicalTask.IsFaulted)
            return false;
        var failure = originalCanonicalTask.Exception;
        return failure is not null && failure.InnerExceptions.Count == 1 &&
            ReferenceEquals(failure.InnerExceptions[0], originalCause);
    }

    private static UnauthorizedAccessException RetainOriginalPreEffectRefusal(
        OriginalDenInvocation invocation, string message)
    {
        var original = new UnauthorizedAccessException(message);
        invocation.PreEffectRefusal = original;
        return original;
    }
}
