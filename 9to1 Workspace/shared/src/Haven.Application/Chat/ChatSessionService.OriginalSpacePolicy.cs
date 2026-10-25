using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private IOriginalSpaceRequestPolicySource? _originalSpacePolicySource;

    /// <summary>One-time trusted composition only; registration creates no policy grant.</summary>
    public void BindOriginalSpacePolicySource(IOriginalSpaceRequestPolicySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var previous = Interlocked.CompareExchange(ref _originalSpacePolicySource, source, null);
        if (previous is not null && !ReferenceEquals(previous, source))
            throw new InvalidOperationException("The original Space policy source cannot be replaced.");
    }

    private sealed class OriginalSpacePolicyBinding(IOriginalSpaceRequestPolicySource source,
        IOriginalSpaceRequestPolicyInput input, Conversation conversation, OriginalSpacePolicySnapshot snapshot,
        ChatOrdinaryOriginalInvocation? ordinary, TaskRunInvocationCustody? canonical)
    {
        internal OriginalSpacePolicySnapshot Snapshot { get; } = snapshot;
        internal Action<Action> Acquire => body =>
        {
            if (ordinary is not null) ordinary.InvokeOriginal(body);
            else (canonical?.OriginalProcessProducer ?? throw new InvalidOperationException("The original Space request producer is unavailable."))
                .InvokeOriginalCallback(body);
        };
        private void Retain(Task actual)
        {
            // The existing conservative child seam keeps raw source/cleanup outcomes;
            // calling a wrapper a read never waives an independently accepted failure.
            if (ordinary is not null) ordinary.RetainUnknownMemoryOriginal(actual);
            else (canonical ?? throw new InvalidOperationException("No canonical Space request custody exists."))
                .RetainAdditionalOriginal("spaces.policy", actual);
        }
        internal async Task ValidateAsync(CancellationToken token)
        {
            var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalCallerCallback(Acquire);
            await originals.RunToOriginalSettlementAsync(async () =>
            {
                var actual = originals.Invoke(() =>
                {
                    var read = source.ValidateAsync(input, conversation, Acquire, token);
                    Retain(read); return read;
                });
                await originals.AwaitAsync(actual).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        internal IOriginalSpacePolicyDispatchGuard DispatchGuard => new Guard(this);
        private sealed class Guard(OriginalSpacePolicyBinding binding) : IOriginalSpacePolicyDispatchGuard
        {
            public async Task ValidateAsync(IModelProvider? actualProvider, CancellationToken token)
            {
                await binding.ValidateAsync(token).ConfigureAwait(false);
                if (binding.Snapshot.CapturedContext is not null && actualProvider is not IOriginalPrivateContextModelProvider { IsLocal: true })
                    throw new SpaceRequestPolicyBlockedException("SPACE_CONTEXT_LOCAL_PROVIDER_REQUIRED", "Reviewed Space content has no cloud disclosure grant and requires an actual maintained local provider.");
                var required = binding.Snapshot.Policy.Model.ProviderConstraint;
                if (!string.IsNullOrWhiteSpace(required) && (actualProvider is null ||
                    !string.Equals(actualProvider.Id, required, StringComparison.OrdinalIgnoreCase)))
                    throw new SpaceRequestPolicyBlockedException("SPACE_PROVIDER_POLICY_REQUIRED", "The actual selected provider does not match the effective Space policy.");
            }
        }
    }

    private async Task<OriginalSpacePolicyBinding?> CaptureOriginalSpacePolicyAsync(Conversation conversation,
        GenerationOptions? options, ChatOrdinaryOriginalInvocation? ordinary, TaskRunInvocationCustody? canonical, CancellationToken token)
    {
        if (conversation.SpaceId is not { } id)
        {
            if (OriginalSpacePolicyDispatch.RequiresGuard(options) || options?.RequestedSpacePolicyLineage is not null || options?.RequestedSpaceContextSha256 is not null)
                throw new SpaceRequestPolicyBlockedException("SPACE_POLICY_IDENTITY_CHANGED", "The continued request no longer identifies its original Space.");
            return null; // Existing non-Space workflows keep their own original owners.
        }
        if (id == Guid.Empty || conversation.Id == Guid.Empty)
            throw new SpaceRequestPolicyBlockedException("SPACE_POLICY_IDENTITY_INVALID", "The request needs its actual conversation and Space identities.");
        if (ollama is not IOriginalPrivateContextModelClient)
            throw new SpaceRequestPolicyBlockedException("SPACE_GUARDED_ROUTER_UNAVAILABLE", "This Space request needs the maintained provider router's original dispatch checks.");
        var source = Volatile.Read(ref _originalSpacePolicySource)
            ?? throw new SpaceRequestPolicyBlockedException("SPACE_POLICY_SOURCE_UNAVAILABLE", "The original Home and Space policy source is unavailable.");
        void Acquire(Action body)
        {
            if (ordinary is not null) ordinary.InvokeOriginal(body);
            else (canonical?.OriginalProcessProducer ?? throw new InvalidOperationException("The original Space producer is unavailable."))
                .InvokeOriginalCallback(body);
        }
        void Retain(Task actual)
        {
            if (ordinary is not null) ordinary.RetainUnknownMemoryOriginal(actual);
            else (canonical ?? throw new InvalidOperationException("No original Space request custody exists."))
                .RetainAdditionalOriginal("spaces.policy", actual);
        }
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalCallerCallback(Acquire);
        return await originals.RunToOriginalSettlementAsync(async () =>
        {
            var capture = originals.Invoke(() => { var task = source.CaptureAsync(conversation, OriginalSpaceContextBudget.ForRequest(options?.ContextLimit ?? 32768), Acquire, token); Retain(task); return task; });
            var input = await originals.AwaitAsync(capture).ConfigureAwait(false);
            var read = originals.Invoke(() => { var task = source.ReadAsync(input, conversation, Acquire, token); Retain(task); return task; });
            var snapshot = await originals.AwaitAsync(read).ConfigureAwait(false);
            if (snapshot.Lineage.SpaceId != id)
                throw new SpaceRequestPolicyBlockedException("SPACE_POLICY_SOURCE_IDENTITY_MISMATCH", "The original source returned another Space policy.");
            return new OriginalSpacePolicyBinding(source, input, conversation, snapshot, ordinary, canonical);
        }).ConfigureAwait(false);
    }
}
