using Haven.Application;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed partial class AssistantMiniComputerSource
{
    public Task WaitOriginalSettlementDispatchWithinSourceAsync(ICanonicalMiniComputerOperationIntent value,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(value);
        lock (_gate)
        {
            if (!_invocations.TryGetValue(intent, out var invocation)) throw new UnauthorizedAccessException("No original VM invocation owns this intent.");
            return invocation.DispatchWait ??= _originals.Cleanup(invocation.Parent, scope, retain, async source =>
            {
                await source.Read(() => invocation.DispatchSettled.Task).ConfigureAwait(false);
                if (invocation.Raw is { } raw && (!raw.IsCompleted || !IsOriginalOperationTask(intent, raw)))
                    throw new UnauthorizedAccessException("The SAME actual provider child has not settled.");
                await source.JoinRawAsync().ConfigureAwait(false);
                lock (_gate) invocation.DispatchHealthy = true;
            });
        }
    }
    public bool IsOwnedOriginalSettlementDispatch(ICanonicalMiniComputerOperationIntent value,
        Task sameWait, Task<ICanonicalMiniComputerOperationAcknowledgment>? sameRaw)
    {
        lock (_gate) return value is Intent intent && ReferenceEquals(intent.Owner, this) &&
            _invocations.TryGetValue(intent, out var invocation) && ReferenceEquals(invocation.DispatchWait, sameWait) &&
            sameWait.IsCompletedSuccessfully && invocation.DispatchHealthy && invocation.DispatchSettled.Task.IsCompletedSuccessfully &&
            ReferenceEquals(invocation.Raw, sameRaw) && (sameRaw is null || sameRaw.IsCompleted && IsOriginalOperationTask(intent, sameRaw));
    }
    public Task ReleaseOriginalSettlementPinsWithinSourceAsync(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerOperationIntent, ICanonicalMiniComputerOperationAcknowledgment> phase,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var invocation = RequireRelease(phase);
        lock (_gate)
        {
            if (invocation.Phase is not null && !ReferenceEquals(invocation.Phase, phase))
                throw new UnauthorizedAccessException("The original Home VM settlement phase cannot be replaced.");
            invocation.Phase = phase;
            return invocation.Release ??= _originals.Cleanup(invocation.Parent, scope, retain, async source =>
            {
                if (!ReferenceEquals(RequireRelease(phase), invocation)) throw new UnauthorizedAccessException();
                // Home's held entry and completion are already independently released.
                // Both original pins close independently before audit is permitted.
                if (invocation.Den is { } den)
                    try { await source.Read(() => invocation.DenClose = den.CloseAndDrainAsync()).ConfigureAwait(false); }
                    catch (Exception cause) { source.Remember(cause); }
                if (invocation.Catalog is { } catalog)
                    try { await source.Read(catalog.CloseAndDrainOriginalAsync).ConfigureAwait(false); }
                    catch (Exception cause) { source.Remember(cause); }
                await source.JoinRawAsync().ConfigureAwait(false);
                lock (_gate) invocation.ReleaseHealthy = true;
            });
        }
    }
    private Invocation RequireRelease(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerOperationIntent, ICanonicalMiniComputerOperationAcknowledgment> phase)
    {
        if (_operations?.IsIssuedOriginalSettlementReleasePhase(phase) != true)
            throw new UnauthorizedAccessException("The SAME actual Home owner must issue the settled VM phase.");
        var intent = RequireIntent(phase.OriginalIntent);
        lock (_gate)
        {
            if (!_invocations.TryGetValue(intent, out var invocation) || !invocation.DispatchHealthy ||
                invocation.DispatchWait?.IsCompletedSuccessfully != true ||
                !ReferenceEquals(invocation.Raw, phase.OriginalAtomicSqlTask) ||
                invocation.Raw is { } raw && (!raw.IsCompleted || !IsOriginalOperationTask(intent, raw)))
                throw new UnauthorizedAccessException("The exact actual provider dispatch or original no-dispatch state must settle before pin release.");
            return invocation;
        }
    }
    public bool IsOwnedOriginalSettlementPinRelease(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerOperationIntent, ICanonicalMiniComputerOperationAcknowledgment> phase,
        Task sameRelease)
    {
        if (_operations?.IsIssuedOriginalSettlementReleasePhase(phase) != true || phase.OriginalIntent is not Intent intent || !ReferenceEquals(intent.Owner, this)) return false;
        lock (_gate) return _invocations.TryGetValue(intent, out var invocation) && ReferenceEquals(invocation.Phase, phase) &&
            ReferenceEquals(invocation.Release, sameRelease) && sameRelease.IsCompletedSuccessfully && invocation.ReleaseHealthy &&
            (invocation.Den is null || invocation.DenClose?.IsCompletedSuccessfully == true) &&
            (invocation.Catalog is null || invocation.Catalog.OriginalClose?.IsCompletedSuccessfully == true);
    }
}
