using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>An observation of one exact private store state; it grants no resource access.</summary>
public sealed class GenUiOriginalInstanceObservation
{
    internal GenUiOriginalInstanceObservation(object state, object? root, GenUiDocument document, string digest, bool registration)
    { State = state; Root = root; Document = document; Digest = digest; IsRegistration = registration; }
    public GenUiDocument Document { get; }
    public string DocumentSha256 => Digest;
    internal object State { get; }
    internal object? Root { get; }
    internal string Digest { get; }
    internal bool IsRegistration { get; }
}

/// <summary>The exact apply occurrence remains observable even when publication faults.</summary>
public sealed class GenUiOriginalMutationReceipt
{
    internal GenUiOriginalMutationReceipt(GenUiActionResult result, GenUiOriginalInstanceObservation before, Task apply)
    { OriginalResult = result; OriginalPredecessor = before; OriginalApplyTask = apply; }
    public GenUiActionResult OriginalResult { get; }
    public GenUiOriginalInstanceObservation OriginalPredecessor { get; }
    public Task OriginalApplyTask { get; }
    public GenUiOriginalInstanceObservation? OriginalSuccessor { get; internal set; }
    internal bool ApplyIndependentlyJoined { get; set; }
}

public sealed partial class GenUiInstanceStore
{
    private readonly object _originalMutationGate = new();
    private readonly ConditionalWeakTable<InstanceState, OriginalLineage> _originalLineages = new();
    private readonly ConditionalWeakTable<GenUiOriginalInstanceObservation, object> _originalObservations = new();
    private readonly ConditionalWeakTable<GenUiActionResult, GenUiOriginalMutationReceipt> _originalResults = new();

    /// <summary>Called by a source retaining its same created document before publishing any controls.</summary>
    public GenUiOriginalInstanceObservation ObserveOriginalRegisteredDocument(GenUiDocument sameDocument)
    {
        ArgumentNullException.ThrowIfNull(sameDocument);
        if (!_instances.TryGetValue(sameDocument.Origin.InstanceId, out var state))
            throw new InvalidOperationException("The original generated UI instance is not registered.");
        lock (state.Gate)
        {
            DemandLiveState(state, sameDocument);
            var digest = OriginalDigest(sameDocument);
            if (!_originalLineages.TryGetValue(state, out var lineage))
            {
                lineage = new OriginalLineage(new object(), sameDocument, digest);
                _originalLineages.Add(state, lineage);
            }
            if (!ReferenceEquals(lineage.RegistrationDocument, sameDocument) || lineage.RegistrationDigest != digest ||
                !ReferenceEquals(lineage.Document, sameDocument) || lineage.Digest != digest || lineage.PreviousApply is not null)
                throw new InvalidOperationException("An unobserved mutation cannot become original registration provenance.");
            return IssueOriginalObservation(state, lineage.Root, sameDocument, digest, true);
        }
    }

    internal GenUiOriginalInstanceObservation? ObserveOriginalPredecessor(Guid instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var state)) return null;
        lock (state.Gate)
        {
            var document = state.Document;
            DemandLiveState(state, document);
            var digest = OriginalDigest(document);
            object? root = null;
            if (_originalLineages.TryGetValue(state, out var lineage) &&
                ReferenceEquals(lineage.Document, document) && lineage.Digest == digest) root = lineage.Root;
            return IssueOriginalObservation(state, root, document, digest, false);
        }
    }

    public bool IsCurrentOriginalObservation(GenUiOriginalInstanceObservation sameObservation)
    {
        if (sameObservation is null || !_originalObservations.TryGetValue(sameObservation, out _)) return false;
        var state = (InstanceState)sameObservation.State;
        lock (state.Gate)
            return _instances.TryGetValue(sameObservation.Document.Origin.InstanceId, out var current) &&
                ReferenceEquals(current, state) && ReferenceEquals(state.Document, sameObservation.Document) &&
                OriginalDigest(state.Document) == sameObservation.Digest;
    }

    // Destructive cleanup reserves the SAME state and checks the privately issued
    // exact current occurrence inside that reservation. No Guid-only ABA removal.
    public bool RemoveOriginalCurrentObservation(GenUiOriginalInstanceObservation sameCurrent)
    {
        if (sameCurrent is null || !_originalObservations.TryGetValue(sameCurrent, out _)) return false;
        var state = (InstanceState)sameCurrent.State;
        lock (state.Gate)
        {
            if (!_instances.TryGetValue(sameCurrent.Document.Origin.InstanceId, out var actual) || !ReferenceEquals(actual, state) ||
                !ReferenceEquals(state.Document, sameCurrent.Document) || OriginalDigest(state.Document) != sameCurrent.Digest) return false;
            return ((ICollection<KeyValuePair<Guid, InstanceState>>)_instances).Remove(new(sameCurrent.Document.Origin.InstanceId, state));
        }
    }

    public bool IsOriginalContinuation(GenUiOriginalInstanceObservation sameRegistration, GenUiOriginalMutationReceipt sameReceipt)
    {
        if (sameRegistration is null || sameReceipt is null || !sameRegistration.IsRegistration ||
            !_originalObservations.TryGetValue(sameRegistration, out _) ||
            !_originalResults.TryGetValue(sameReceipt.OriginalResult, out var issued) || !ReferenceEquals(issued, sameReceipt) ||
            !sameReceipt.ApplyIndependentlyJoined || !sameReceipt.OriginalApplyTask.IsCompletedSuccessfully ||
            sameReceipt.OriginalSuccessor is not { } after) return false;
        lock (((InstanceState)after.State).Gate)
            return sameRegistration.Root is not null && ReferenceEquals(sameRegistration.State, after.State) &&
                ReferenceEquals(sameRegistration.Root, after.Root) && IsCurrentOriginalObservation(after);
    }

    public bool TryObserveOriginalMutation(GenUiActionResult sameResult, out GenUiOriginalMutationReceipt? sameReceipt)
    {
        ArgumentNullException.ThrowIfNull(sameResult);
        return _originalResults.TryGetValue(sameResult, out sameReceipt);
    }

    internal void AcknowledgeOriginalResultJoin(GenUiActionResult sameResult)
    {
        if (!_originalResults.TryGetValue(sameResult, out var receipt) || !receipt.OriginalApplyTask.IsCompletedSuccessfully)
            throw new InvalidOperationException("The same original apply has not independently completed successfully.");
        lock (((InstanceState)receipt.OriginalPredecessor.State).Gate)
            receipt.ApplyIndependentlyJoined = true;
    }

    internal Task ApplyOriginalResultAsync(GenUiActionResult result, GenUiOriginalInstanceObservation sameBefore, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sameBefore);
        GenUiOriginalMutationReceipt receipt;
        TaskCompletionSource<object?> completion;
        lock (_originalMutationGate)
        {
            if (_originalResults.TryGetValue(result, out var cached))
            {
                if (!ReferenceEquals(cached.OriginalPredecessor, sameBefore))
                    throw new InvalidOperationException("The original result already belongs to another apply occurrence.");
                return cached.OriginalApplyTask;
            }
            token.ThrowIfCancellationRequested();
            if (!_originalObservations.TryGetValue(sameBefore, out _))
                throw new InvalidOperationException("The generated UI predecessor was not issued by this store.");
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            receipt = new(result, sameBefore, completion.Task);
            _originalResults.Add(result, receipt);
        }
        try
        {
            var state = (InstanceState)sameBefore.State;
            GenUiDocument? changed = null;
            lock (state.Gate)
            {
                DemandLiveState(state, sameBefore.Document);
                if (OriginalDigest(state.Document) != sameBefore.Digest || result.Origin != sameBefore.Document.Origin)
                    throw new InvalidOperationException("The original generated UI predecessor changed before its result could apply.");
                if (result.Patches is null || result.Patches.Count > 100 ||
                    result.Patches.Any(patch => patch.InstanceId != result.Origin.InstanceId || patch.PatchId == Guid.Empty))
                    throw new InvalidOperationException("Original result patches must target one original instance with stable IDs.");
                if (result.Status != GenUiActionStatus.Completed && result.Patches.Count > 0 && !IsFailureDisplayOnly(result))
                    throw new InvalidOperationException("A non-completed action cannot mutate generated UI state.");
                var staged = state.Document;
                var stagedIds = new HashSet<Guid>(state.AppliedPatches);
                foreach (var patch in result.Patches)
                {
                    if (!stagedIds.Add(patch.PatchId)) continue;
                    staged = patch.TargetId.Equals("state", StringComparison.Ordinal) ? PatchState(staged, patch) : PatchComponent(staged, patch);
                    GenerativeUiContractValidator.ValidateAndThrow(staged);
                    staged = staged with { UpdatedAt = patch.Timestamp };
                    changed = staged;
                }
                // Register/Remove reserve this SAME state gate until its commit finishes.
                DemandLiveState(state, sameBefore.Document);
                var digest = OriginalDigest(staged);
                if (changed is not null)
                {
                    state.Document = staged;
                    state.AppliedPatches.Clear(); state.AppliedPatches.UnionWith(stagedIds);
                }
                object? root = null;
                if (sameBefore.Root is not null && _originalLineages.TryGetValue(state, out var lineage) &&
                    ReferenceEquals(lineage.Document, sameBefore.Document) && lineage.Digest == sameBefore.Digest &&
                    ReferenceEquals(lineage.Root, sameBefore.Root) &&
                    (lineage.PreviousApply is null || lineage.PreviousApply.ApplyIndependentlyJoined))
                { lineage.Document = staged; lineage.Digest = digest; lineage.PreviousApply = receipt; root = lineage.Root; }
                receipt.OriginalSuccessor = IssueOriginalObservation(state, root, staged, digest, false);
            }
            if (changed is not null) DocumentChanged?.Invoke(this, changed);
            completion.TrySetResult(null);
        }
        catch (Exception cause) { completion.TrySetException(cause); }
        return receipt.OriginalApplyTask;
    }

    private GenUiOriginalInstanceObservation IssueOriginalObservation(InstanceState state, object? root, GenUiDocument document, string digest, bool registration)
    {
        var observation = new GenUiOriginalInstanceObservation(state, root, document, digest, registration);
        _originalObservations.Add(observation, state);
        return observation;
    }
    private void DemandLiveState(InstanceState state, GenUiDocument sameDocument)
    {
        if (!_instances.TryGetValue(sameDocument.Origin.InstanceId, out var current) || !ReferenceEquals(current, state) ||
            !ReferenceEquals(state.Document, sameDocument))
            throw new InvalidOperationException("The original generated UI state was replaced or changed.");
    }
    private static string OriginalDigest(GenUiDocument document) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document)));
    private sealed class OriginalLineage(object root, GenUiDocument document, string digest)
    {
        public object Root { get; } = root;
        public GenUiDocument RegistrationDocument { get; } = document;
        public string RegistrationDigest { get; } = digest;
        public GenUiDocument Document { get; set; } = document;
        public string Digest { get; set; } = digest;
        public GenUiOriginalMutationReceipt? PreviousApply { get; set; }
    }
}
