namespace Haven.Application;

/// <summary>Privately issued by the SAME actual Home WRITE source only after its
/// exact atomic child (or owned no-SQL state), accepted source cohort, held Home
/// entry and completion leases have been independently joined and released.
/// These projections and caller implementations confer no authority.</summary>
public interface ICanonicalOriginalWriteSettlementReleasePhase<TIntent, TAcknowledgment>
{
    TIntent OriginalIntent { get; }
    Task<TAcknowledgment>? OriginalAtomicSqlTask { get; }
}

/// <summary>Optional custody participant on the SAME configured canonical creator.
/// Implementations must validate the phase against their genuinely bound Home
/// issuer and SAME private invocation, including exact child-or-noSQL provenance.
/// Release joins cached original Den/native revision pins; no Home IO, new SQL or
/// definition/membership reads may occur. A failed release is never replayed.</summary>
public interface ICanonicalOriginalWriteSettlementPinOwner<TIntent, TAcknowledgment>
{
    // This joins the privately retained setup/dispatch barrier, never its encompassing
    // creation driver. Late accepted acquisitions must be captured before it settles.
    Task WaitOriginalSettlementDispatchWithinSourceAsync(
        TIntent sameOriginalIntent, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsOwnedOriginalSettlementDispatch(TIntent sameOriginalIntent,
        Task sameOriginalWaitTask, Task<TAcknowledgment>? sameOriginalAtomicSqlTask);
    Task ReleaseOriginalSettlementPinsWithinSourceAsync(
        ICanonicalOriginalWriteSettlementReleasePhase<TIntent, TAcknowledgment> sameHomePhase,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Only the privately retained SAME release Task whose whole original pin
    // cohort independently closed healthy. Task status/interface metadata alone
    // cannot replace this issuer proof or acknowledge foreign/mixed failures.
    bool IsOwnedOriginalSettlementPinRelease(
        ICanonicalOriginalWriteSettlementReleasePhase<TIntent, TAcknowledgment> sameHomePhase,
        Task sameOriginalReleaseTask);
}
