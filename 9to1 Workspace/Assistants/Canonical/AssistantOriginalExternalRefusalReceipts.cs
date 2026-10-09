using System.Runtime.CompilerServices;

namespace HavenOS.Apps.Assistants.Canonical;

// Terminal settlement metadata only. This issues no actor/resource/native authorization.
// The producing configured owner must prove exact healthy pre-effect refusal BEFORE issue.
// Every borrower of the SAME original Task can then preserve that same acknowledged outcome
// without accepting another Task carrying the same cause object or exception type.
internal static class AssistantOriginalExternalRefusalReceipts
{
    private sealed record Receipt(Exception Cause);
    private static readonly ConditionalWeakTable<Task, Receipt> Issued = new();

    internal static bool Publish(Task sameActual, Exception sameBareCause)
    {
        if (!sameActual.IsFaulted || sameBareCause is AggregateException ||
            sameActual.Exception is not { InnerExceptions.Count: 1 } payload ||
            !ReferenceEquals(payload.InnerExceptions[0], sameBareCause)) return false;
        var receipt = Issued.GetValue(sameActual, _ => new(sameBareCause));
        return ReferenceEquals(receipt.Cause, sameBareCause);
    }

    // Pure weak-metadata observation, safe inside borrower bookkeeping locks. No producer
    // callback/query, source acquisition, Close probing or exception-container flattening.
    internal static bool IsAcknowledgedOriginal(Task sameActual) => sameActual.IsFaulted &&
        Issued.TryGetValue(sameActual, out var receipt) && sameActual.Exception is { InnerExceptions.Count: 1 } payload &&
        ReferenceEquals(payload.InnerExceptions[0], receipt.Cause);
}
