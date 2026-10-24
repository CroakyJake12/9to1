using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Source-owned initial-input metadata lineage. A command READ is never cast into
/// an initial input. A separate genuine initial-input READ is created and closed, and existing
/// validation/cold capture use a fresh READ against its effective identity. No old audit mutates.</summary>
public sealed partial class HomeColdProjectReadReconciliation : ITaskRunOriginalProjectMetadataInputSource
{
    private sealed class MetadataInputOperation { internal Task? Driver; }
    private sealed class MetadataInputSlot
    {
        internal MetadataInputSuccessor? Accepted;
        internal MetadataInputSuccessor? Pending;
    }
    private sealed class MetadataInputSuccessor(HomeColdProjectReadReconciliation owner, MetadataInputOperation operation,
        Work original, Work after, MetadataRebindResult rebind, TaskRunColdProjectIdentity before,
        TaskRunColdProjectIdentity afterIdentity, Task actualClose) : ITaskRunOriginalProjectMetadataInputSuccessor
    {
        internal HomeColdProjectReadReconciliation Owner => owner;
        internal MetadataInputOperation Operation => operation;
        internal Work Original => original;
        internal Work After => after;
        public ITaskRunColdOriginalProjectInput OriginalInput => original;
        public ITaskRunColdOriginalProjectInput VerifiedAfterInput => after;
        public IDeveloperOriginalProjectMetadataRebindResult OriginalMetadataRebind => rebind;
        public TaskRunColdProjectIdentity BeforeIdentity => before;
        public TaskRunColdProjectIdentity AfterIdentity => afterIdentity;
        public Task OriginalAfterClose => actualClose;
    }
    private sealed record MetadataInputBasis(TaskRunColdProjectIdentity Identity, MetadataInputSuccessor? Successor);
    private readonly ConditionalWeakTable<Work, MetadataInputSlot> _metadataInputSlots = new();
    private readonly ConditionalWeakTable<Work, object> _metadataVerificationInputs = new();
    private readonly ConditionalWeakTable<MetadataInputSuccessor, MetadataInputOperation> _metadataInputResults = new();

    /// <summary>Data comparison only, never issuance. Reference JSON may differ in casing/
    /// formatting across the existing source roles; typed project/root/repository/container/
    /// actor fields remain exact, and the old initial input's own raw context stays unchanged.</summary>
    internal static bool SameMetadataInputResource(TaskRunColdProjectIdentity expected, TaskRunColdProjectIdentity observed) =>
        expected with { SavedWorkspaceDocumentSha256 = observed.SavedWorkspaceDocumentSha256,
            OriginalProjectContextJson = observed.OriginalProjectContextJson } == observed;

    private MetadataRebindResult RequireOriginalInputMetadataRebind(IDeveloperOriginalProjectMetadataRebindResult value, bool live)
    {
        if (value is not MetadataRebindResult actual || !IsOwnedOriginalMetadataRebindResult(actual) ||
            actual.Operation.Driver?.IsCompletedSuccessfully != true || live && !IsIssuedOriginalMetadataRebindResult(actual))
            throw new UnauthorizedAccessException("The SAME genuine Home metadata rebind result is required.");
        var preparation = actual.OriginalPreparation as MetadataRebindPreparation
            ?? throw new UnauthorizedAccessException("The original Home metadata preparation is missing.");
        if (!preparation.Store.IsIssuedOriginalMetadataTransition(actual.OriginalTransition, actual.OriginalCommittedValue))
            throw new UnauthorizedAccessException("The original canonical metadata receipt/value no longer qualifies.");
        return actual;
    }

    public ITaskRunColdOriginalProjectInput GetOriginalInitialInputForMetadataRebind(
        IDeveloperOriginalProjectMetadataRebindResult sameRebind)
    {
        var rebind = RequireOriginalInputMetadataRebind(sameRebind, true);
        var context = rebind.OriginalPreparation.OriginalCommandRead.OriginalDescriptor.OriginalConversation.Id;
        Work[] candidates;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(HomeColdProjectReadReconciliation));
            candidates = _issued.Select(pair => pair.Key).Where(work => work.Material is null && work._command is null &&
                !_metadataVerificationInputs.TryGetValue(work, out _) && work.Driver?.IsCompletedSuccessfully == true &&
                work.Conversation.Id == context).ToArray();
        }
        var selected = new List<Work>();
        foreach (var candidate in candidates)
        {
            if (!candidate.IsLive && candidate.ObserveSuccessfullySettledOriginalInputClose() is null) continue;
            var basis = ObserveMetadataInputBasis(candidate);
            if (SameMetadataInputResource(basis.Identity, rebind.OriginalPreparation.BeforeIdentity) &&
                StringComparer.Ordinal.Equals(basis.Identity.SavedWorkspaceDocumentSha256, rebind.OriginalPreparation.BeforeIdentity.SavedWorkspaceDocumentSha256))
                selected.Add(candidate);
        }
        return selected.Count == 1 ? selected[0]
            : throw new UnauthorizedAccessException("Retain the exact original initial input; no unique source-owned context/project/before observation exists.");
    }

    private MetadataInputBasis ObserveMetadataInputBasis(Work original)
    {
        MetadataInputSuccessor? accepted;
        lock (_gate)
        {
            if (!_metadataInputSlots.TryGetValue(original, out var slot)) return new(original.Identity, null);
            if (slot.Pending is { } pending)
            {
                if (pending.Operation.Driver?.IsCompletedSuccessfully == true)
                { slot.Accepted = pending; slot.Pending = null; }
                else throw new InvalidOperationException("Join/inspect the original metadata-input lineage driver before another validation or capture.");
            }
            accepted = slot.Accepted;
        }
        if (accepted is null) return new(original.Identity, null);
        DemandHealthyMetadataInputSuccessor(accepted);
        return new(accepted.AfterIdentity, accepted);
    }
    private void DemandMetadataInputBasis(Work original, MetadataInputBasis expected)
    {
        var current = ObserveMetadataInputBasis(original);
        if (!ReferenceEquals(current.Successor, expected.Successor) || current.Identity != expected.Identity)
            throw new UnauthorizedAccessException("The source-owned effective project identity changed during its fresh READ.");
    }
    private void DemandHealthyMetadataInputSuccessor(MetadataInputSuccessor value)
    {
        lock (_gate)
            if (!ReferenceEquals(value.Owner, this) || !_metadataInputResults.TryGetValue(value, out var operation) ||
                !ReferenceEquals(operation, value.Operation) || operation.Driver?.IsCompletedSuccessfully != true)
                throw new UnauthorizedAccessException("No SAME successful original metadata-input successor exists.");
        _ = RequireInput(value.Original, false); _ = RequireInput(value.After, false);
        if (!ReferenceEquals(value.After.ObserveSuccessfullySettledOriginalInputClose(), value.OriginalAfterClose) ||
            value.After.Identity != value.AfterIdentity)
            throw new UnauthorizedAccessException("The genuine after initial-input source did not close with its exact healthy observation.");
        _ = RequireOriginalInputMetadataRebind(value.OriginalMetadataRebind, false);
    }
    public bool IsIssuedOriginalInitialInputMetadataSuccessor(ITaskRunOriginalProjectMetadataInputSuccessor value)
    {
        if (value is not MetadataInputSuccessor actual) return false;
        try { DemandHealthyMetadataInputSuccessor(actual); return true; }
        catch (Exception cause) when (cause is UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    public Task<ITaskRunOriginalProjectMetadataInputSuccessor> PrepareOriginalInitialInputMetadataSuccessorWithinSourceAsync(
        ITaskRunColdOriginalProjectInput sameOriginalInput, IDeveloperOriginalProjectMetadataRebindResult sameRebind,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var original = RequireInput(sameOriginalInput, false);
        if (!original.IsLive && original.ObserveSuccessfullySettledOriginalInputClose() is null)
            throw new UnauthorizedAccessException("The exact original initial input is neither live nor healthily closed.");
        var rebind = RequireOriginalInputMetadataRebind(sameRebind, true);
        var basis = ObserveMetadataInputBasis(original);
        if (original.Conversation.Id != rebind.OriginalPreparation.OriginalCommandRead.OriginalDescriptor.OriginalConversation.Id ||
            !SameMetadataInputResource(basis.Identity, rebind.OriginalPreparation.BeforeIdentity) ||
            !StringComparer.Ordinal.Equals(basis.Identity.SavedWorkspaceDocumentSha256, rebind.OriginalPreparation.BeforeIdentity.SavedWorkspaceDocumentSha256))
            throw new UnauthorizedAccessException("The metadata transition does not start from this original input's effective context/project/actor.");
        var operation = new MetadataInputOperation();
        var driver = StartOperation<ITaskRunOriginalProjectMetadataInputSuccessor>(scope, retain, async sources =>
        {
            Work? fresh = null; Task? preparation = null, close = null;
            TaskRunColdProjectIdentity? afterIdentity = null; Exception? primary = null; var cleanup = new List<Exception>();
            try
            {
                sources.Invoke(() =>
                {
                    DemandMetadataInputBasis(original, basis);
                    _ = RequireOriginalInputMetadataRebind(rebind, true);
                    fresh = ReserveAdmitted(null);
                    lock (_gate) _metadataVerificationInputs.Add(fresh, new object());
                    return true;
                });
                var after = fresh ?? throw new InvalidOperationException("The actual after initial-input child was not retained.");
                sources.Invoke(() =>
                {
                    preparation = after.StartInput(original.Conversation, original.Container,
                        original.Identity.OriginalProjectContextJson, sources.Scope, sources.Retain, token,
                        rebind.AfterIdentity.SavedWorkspaceDocumentSha256, basis.Identity.RegisteredRootFingerprint);
                    sources.Retain(preparation); return true;
                });
                await sources.Await(preparation ?? throw new InvalidOperationException("No genuine after initial-input preparation Task exists.")).ConfigureAwait(false);
                sources.Invoke(() =>
                {
                    if (!IsIssuedOriginalProjectInput(after) || after._command is not null || after.Material is not null ||
                        !SameMetadataInputResource(basis.Identity, after.Identity) ||
                        !SameMetadataInputResource(rebind.AfterIdentity, after.Identity) ||
                        !StringComparer.Ordinal.Equals(after.Identity.SavedWorkspaceDocumentSha256, rebind.AfterIdentity.SavedWorkspaceDocumentSha256))
                        throw new UnauthorizedAccessException("The separately approved initial-input after READ changed resource identity.");
                    afterIdentity = after.Identity; return true;
                });
            }
            catch (Exception cause) { primary = cause; }
            finally
            {
                if (preparation is not null)
                    try { await sources.Await(preparation).ConfigureAwait(false); }
                    catch (Exception cause) { cleanup.Add(cause); }
                if (fresh is not null)
                {
                    var closes = new List<Task>();
                    sources.AcquireClose(() => { close = fresh.CloseOwned(); return close; }, closes);
                    if (close is null) cleanup.Add(new InvalidOperationException("The exact after initial-input close was not acquired."));
                    else try { await sources.Await(close).ConfigureAwait(false); } catch (Exception cause) { cleanup.Add(cause); }
                    if (!fresh.CanPruneSuccessfulClose)
                        cleanup.Add(new InvalidOperationException("The actual after initial-input cohort did not close healthily."));
                }
            }
            if (cleanup.Count != 0)
                throw new AggregateException("Metadata initial-input READ and independent source close failed.",
                    (primary is null ? Enumerable.Empty<Exception>() : new[] { primary }).Concat(cleanup));
            if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            var actualAfter = fresh ?? throw new InvalidOperationException("No actual after initial-input exists.");
            var actualClose = close ?? throw new InvalidOperationException("No actual healthy after close exists.");
            var identity = afterIdentity ?? throw new InvalidOperationException("No verified after initial-input identity exists.");
            return sources.Invoke(() =>
            {
                DemandMetadataInputBasis(original, basis);
                _ = RequireOriginalInputMetadataRebind(rebind, true);
                if (!ReferenceEquals(actualAfter.ObserveSuccessfullySettledOriginalInputClose(), actualClose))
                    throw new UnauthorizedAccessException("The actual after initial-input close is not the SAME healthy source close.");
                var result = new MetadataInputSuccessor(this, operation, original, actualAfter, rebind, basis.Identity, identity, actualClose);
                lock (_gate)
                {
                    var slot = _metadataInputSlots.GetValue(original, _ => new MetadataInputSlot());
                    if (slot.Pending is not null || !ReferenceEquals(slot.Accepted, basis.Successor))
                        throw new UnauthorizedAccessException("Another metadata-input successor won the source CAS; no old identity was overwritten.");
                    _metadataInputResults.Add(result, operation); slot.Pending = result;
                }
                return (ITaskRunOriginalProjectMetadataInputSuccessor)result;
            });
        });
        operation.Driver = driver; return driver;
    }

    private async Task ValidateEffectiveOriginalProjectInputAsync(Work original, TaskRunColdChatInput exact,
        Context sources, CancellationToken token)
    {
        original.DemandExactInput(exact);
        var basis = ObserveMetadataInputBasis(original);
        if (basis.Successor is null) await original.ValidateFreshShortReadAsync(sources, token).ConfigureAwait(false);
        else await ValidateClosedInputThroughFreshOriginalReadAsync(original, sources, token, basis.Identity).ConfigureAwait(false);
        DemandMetadataInputBasis(original, basis); original.DemandExactInput(exact);
    }
}
