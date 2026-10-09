using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.MiniComputer;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed partial class AssistantMiniComputerSource :
    ICanonicalOriginalWriteSettlementPinOwner<ICanonicalMiniComputerOperationIntent, ICanonicalMiniComputerOperationAcknowledgment>
{
    private readonly ConditionalWeakTable<Intent, Invocation> _invocations = new();
    private readonly AsyncLocal<Invocation?> _activeInvocation = new();
    private sealed class Invocation(Intent intent)
    {
        internal Intent Intent { get; } = intent;
        internal Task<OperationOutcome> Parent = null!;
        internal ICanonicalMiniComputerHomeOperationClaim? Claim;
        internal CanonicalMiniComputerCatalogOriginalOwner.Lease? Catalog;
        internal DenStore.AssistantRevisionPin? Den;
        internal Task<ICanonicalMiniComputerOperationAcknowledgment>? Raw;
        internal readonly TaskCompletionSource DispatchSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? DispatchWait, Release, DenClose;
        internal bool DispatchHealthy, ReleaseHealthy;
        internal ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerOperationIntent, ICanonicalMiniComputerOperationAcknowledgment>? Phase;
    }
    internal sealed record OperationOutcome(ICanonicalMiniComputerOperationAcknowledgment? Acknowledgment, string Reason);
    private sealed record Acknowledgment(ICanonicalMiniComputerOperationIntent OriginalIntent,
        string ObservedState, DateTimeOffset ObservedAt, bool IsPending, bool WasDispatched, string Reason)
        : ICanonicalMiniComputerOperationAcknowledgment;

    internal Task<OperationOutcome> ExecuteWithinSourceAsync(ICanonicalMiniComputerOperationIntent value,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(value); TaskCompletionSource? start = null; Invocation invocation;
        lock (_gate)
        {
            if (_invocations.TryGetValue(intent, out invocation!)) return invocation.Parent;
            token.ThrowIfCancellationRequested(); // Only pre-admission caller cancellation.
            invocation = new(intent); _invocations.Add(intent, invocation);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            invocation.Parent = _originals.Run(scope, retain, async source =>
            { await start.Task.ConfigureAwait(false); return await ExecuteBody(invocation, source, CancellationToken.None).ConfigureAwait(false); });
        }
        start.SetResult(); return invocation.Parent;
    }
    private async Task<OperationOutcome> ExecuteBody(Invocation invocation, MiniComputerOriginalInvocation source, CancellationToken token)
    {
        var intent = invocation.Intent;
        var homeOperations = _operations ?? throw new InvalidOperationException("The actual Home VM operation source is unavailable.");
        var prior = _activeInvocation.Value; _activeInvocation.Value = invocation;
        ICanonicalMiniComputerOperationAcknowledgment? result = null; var declined = false;
        try
        {
            Task<ICanonicalMiniComputerHomeOperationClaim>? admission = null;
            try
            {
                invocation.Claim = await source.Read(() => admission = homeOperations.AcquireOriginalOperationWithinSourceAsync(
                    intent, source.Run, source.Retain, claim => invocation.Claim = claim, token), claim => invocation.Claim = claim).ConfigureAwait(false);
            }
            catch
            {
                if (admission is null || invocation.Claim is null || !homeOperations.IsAcknowledgedOriginalOperationRefusal(admission)) throw;
                source.AcknowledgeOriginalRefusal(admission, homeOperations.IsAcknowledgedOriginalOperationRefusal);
                source.AcknowledgeOriginalRefusalOccurrences(homeOperations.IsAcknowledgedOriginalOperationRefusal);
                declined = true;
            }
            if (!declined)
            {
                if (!homeOperations.IsIssuedOriginalOperationClaim(invocation.Claim!, intent)) throw new UnauthorizedAccessException("The SAME exact Home VM action approval is required.");
                await source.Read(() => ValidateOriginalOperationIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
                var home = await source.Read(() => intent.Input.Membership.OpenHomeWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
                if (home.Actor != intent.Actor) throw new UnauthorizedAccessException("The actual Home actor changed before the VM operation.");
                var definition = await source.Read(() => intent.Input.Membership.DefinitionWithinSourceAsync(home,
                    intent.Input.Binding.Definition.Identity, source.Run, source.Retain, token)).ConfigureAwait(false);
                var session = await source.Read(() => home.Den.GetAsync<SessionRecord>(intent.NamespaceId,
                    intent.Input.Binding.DenSessionId, token)).ConfigureAwait(false);
                if (definition.Revision != intent.DefinitionRevision || session is null || session.Revision != intent.Input.Binding.DenSessionRevision)
                    throw new UnauthorizedAccessException("The actual Assistant definition or membership changed.");
                // Canonical catalogue writer reservation -> Den writer pin -> held Home.
                // No Home/Open/ownership IO is permitted below the Den pin.
                invocation.Catalog = await source.Read(() => _catalog.AcquireOriginalProtectedReadWithinSourceAsync(intent.Actor,
                    source.Run, source.Retain, token), actual => invocation.Catalog = actual).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The current protected VM catalogue is unavailable.");
                var catalog = await source.Read(() => _engine.ReadOriginalCatalogWithinSourceAsync(invocation.Catalog,
                    source.Run, source.Retain, token)).ConfigureAwait(false);
                if (catalog.StoreIdentity != intent.OriginalStoreIdentity || catalog.Sha256 != intent.Target.OriginalCatalogSha256)
                    throw new UnauthorizedAccessException("The original VM catalogue changed before dispatch.");
                invocation.Den = await source.Read(() => home.Den.Store.PinOriginalAssistantRevisionsAsync(definition, session,
                    home.Den.AccessPolicy, home.Den.PrincipalId, source.Run, source.Retain,
                    actual => invocation.Den = actual, token), actual => invocation.Den = actual).ConfigureAwait(false);
                await source.Read(() => homeOperations.AcquireOriginalOperationEntryWithinSourceAsync(invocation.Claim!,
                    source.Run, source.Retain, token)).ConfigureAwait(false);
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                invocation.Raw = RunActualProvider(start.Task, invocation, source, homeOperations, token);
                var published = false;
                try
                {
                    source.Run(() => { homeOperations.RetainOriginalOperation(invocation.Claim!, invocation.Raw); source.Retain(invocation.Raw); });
                    published = true;
                }
                finally { start.SetResult(published); }
                result = await invocation.Raw.ConfigureAwait(false);
            }
        }
        catch (Exception cause) { source.Remember(cause); }
        finally
        {
            // This is the exact setup/raw-provider boundary, never the enclosing
            // parent that waits Home settlement. Capture late sources before opening it.
            try { await source.JoinRawAsync().ConfigureAwait(false); } catch { }
            invocation.DispatchSettled.TrySetResult();
            if (invocation.Raw is { } raw)
                try { await source.Read(() => homeOperations.CompleteOriginalOperationWithinSourceAsync(invocation.Claim!, raw,
                    source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false); }
                catch (Exception cause) { source.Remember(cause); }
            if (invocation.Claim is { } claim)
                try { await source.Read(claim.CloseAndDrainOriginalAsync).ConfigureAwait(false); }
                catch (Exception cause) { source.Remember(cause); }
            // Home issues and independently joins the exact release phase before audit.
            // If that healthy boundary is unknown, retain pins; never invent a release.
            if ((invocation.Den is not null || invocation.Catalog is not null) && !invocation.ReleaseHealthy)
                source.Remember(new InvalidOperationException("The actual Home settlement has not acknowledged the original VM revision-pin release."));
            _activeInvocation.Value = prior;
        }
        return new(result, declined ? "Home declined this exact VM action before provider dispatch." : result?.Reason ?? "The original VM outcome is unconfirmed.");
    }
    private async Task<ICanonicalMiniComputerOperationAcknowledgment> RunActualProvider(Task<bool> start,
        Invocation invocation, MiniComputerOriginalInvocation source, ICanonicalMiniComputerHomeOperationSource home,
        CancellationToken token)
    {
        if (!await start.ConfigureAwait(false)) throw new InvalidOperationException("The actual provider task was not admitted by its Home owner.");
        source.Run(() => { DemandOriginalOperation(invocation.Intent); home.DemandOriginalOperation(invocation.Claim!, invocation.Intent); });
        var actual = await source.Read(() => _engine.InvokeOriginalOperationWithinSourceAsync(invocation.Intent.Input.Catalog,
            invocation.Intent, source.Run, source.Retain, token)).ConfigureAwait(false);
        source.Run(() => { DemandOriginalOperation(invocation.Intent); home.DemandOriginalOperation(invocation.Claim!, invocation.Intent); });
        return new Acknowledgment(invocation.Intent, actual.State.State.ToString(), actual.State.ObservedAt,
            actual.IsPending, actual.WasDispatched, actual.Reason);
    }
    public void DemandOriginalOperation(ICanonicalMiniComputerOperationIntent value)
    {
        var intent = RequireIntent(value);
        var invocation = _activeInvocation.Value;
        if (invocation is null || !ReferenceEquals(invocation.Intent, intent) || invocation.Claim is null ||
            invocation.Den is null || invocation.Catalog is null || invocation.DispatchSettled.Task.IsCompleted ||
            _operations?.IsIssuedOriginalOperationClaim(invocation.Claim, intent) != true)
            throw new UnauthorizedAccessException("The SAME live Home VM operation and original revision pins are required.");
        invocation.Catalog.DemandOriginalPinnedCatalog(); invocation.Den.DemandOriginalPinnedRevisions();
    }
    public Task<ICanonicalMiniComputerOperationAcknowledgment> InvokeOriginalOperationWithinSourceAsync(
        ICanonicalMiniComputerOperationIntent value, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(value);
        lock (_gate)
            return _invocations.TryGetValue(intent, out var invocation) && invocation.Raw is { } raw
                ? raw : throw new UnauthorizedAccessException("The canonical producer has not issued this exact provider task.");
    }
    public bool IsOriginalOperationTask(ICanonicalMiniComputerOperationIntent value, Task<ICanonicalMiniComputerOperationAcknowledgment> task)
    { lock (_gate) return value is Intent intent && ReferenceEquals(intent.Owner, this) && _invocations.TryGetValue(intent, out var invocation) && ReferenceEquals(invocation.Raw, task); }
    public bool IsOwnedOriginalOperationAcknowledgment(ICanonicalMiniComputerOperationIntent value,
        ICanonicalMiniComputerOperationAcknowledgment acknowledgment, Task<ICanonicalMiniComputerOperationAcknowledgment> task) =>
        IsOriginalOperationTask(value, task) && task.IsCompletedSuccessfully &&
        acknowledgment is Acknowledgment && ReferenceEquals(task.Result, acknowledgment) && ReferenceEquals(acknowledgment.OriginalIntent, value);
    internal AssistantMiniComputerPendingObservation Observe(ICanonicalMiniComputerOperationIntent value)
    {
        var intent = RequireIntent(value);
        lock (_gate) return _invocations.TryGetValue(intent, out var invocation)
            ? new(invocation.Claim?.OriginalApprovalRequestId, invocation.Parent.IsCompleted ? "Original operation settled; review its actual result." : "Waiting for this exact Home decision or provider operation.", !invocation.Parent.IsCompleted)
            : new(null, "Review this exact VM action before requesting Home approval.", false);
    }
}
