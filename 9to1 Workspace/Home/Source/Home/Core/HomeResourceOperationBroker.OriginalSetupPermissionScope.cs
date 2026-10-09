using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    private Task<HomePermissionOperationResult> RecordOriginalSetupExecutionAsync(HomeOwnershipOriginalSourceCallbacks? source,
        string requestId, HomeExecutionOutcome outcome, CancellationToken token) => source is null
        ? permissions.RecordExecutionAsync(requestId, outcome, token)
        : source.ReadAsync(() => permissions.RecordImportExecutionWithinOriginalSourceAsync(requestId, outcome, source.Run, source.Retain, token, source.UnexpectedCallbackSink));
    internal Task<HomePermissionOperationResult> CompleteSetupExecutionWithinOriginalSourceAsync(HomeResourceExecutionCapability capability,
        HomeExecutionOutcome outcome, Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalSetupCoreEnvelopeAsync(scope, retain, source => CompleteExecutionOriginalSetupCoreAsync(capability, outcome, token, source), retainUnexpectedCallback);
    internal Task<HomePermissionOperationResult> AbortUnclaimedSetupExecutionWithinOriginalSourceAsync(HomeResourceExecutionCapability capability,
        Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalSetupCoreEnvelopeAsync(scope, retain, source => AbortUnclaimedOriginalSetupCoreAsync(capability, token, source), retainUnexpectedCallback);
    internal Task<bool> RetireSetupPreparedReviewWithinOriginalSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalSetupCoreEnvelopeAsync(scope, retain, source => RetirePreparedReviewOriginalSetupCoreAsync(prepared, token, source), retainUnexpectedCallback);

    // The envelope retains and independently joins the exact core Task. Nested
    // original reads use a separate child cohort, forwarding every raw Task to
    // the envelope before callbacks. A child can therefore never enroll its
    // enclosing core into its own read join and wait on that same parent.
    private static Task<T> RunOriginalSetupCoreEnvelopeAsync<T>(Action<Action> scope, Action<Task> retain,
        Func<HomeOwnershipOriginalSourceCallbacks, Task<T>> core, Action<Exception>? retainUnexpectedCallback = null) =>
        RunColdProjectBrokerAsync(scope, retain, envelope => envelope.ReadAsync(() =>
            core(new HomeOwnershipOriginalSourceCallbacks(envelope.Run, envelope.Retain, retainUnexpectedCallback))), retainUnexpectedCallback);
}
