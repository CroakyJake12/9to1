using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private static async Task<SpaceUserMessageGraphResult?> DeriveOriginalSpaceUserMessageAsync(
        OriginalSpacePolicyBinding? policy, Conversation conversation, ChatMessage input,
        ChatOrdinaryOriginalInvocation? ordinary, TaskRunInvocationCustody? canonical, CancellationToken token)
    {
        if (policy?.Snapshot.UserMessageGraph is not { } graph) return null;
        if (conversation.SpaceId != graph.SpaceId || input.ConversationId != conversation.Id)
            throw new SpaceRequestPolicyBlockedException("SPACE_GRAPH_INPUT_MISMATCH", "The graph must derive this original conversation's user message.");
        // A graph consumes only the SAME source's captured snapshot. Revalidate its
        // physical read witnesses before deriving with a body, and the existing
        // provider-dispatch guard revalidates again after the graph has completed.
        if (graph.ReferencedContextIds.Count != 0) await policy.ValidateAsync(token).ConfigureAwait(false);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalCallerCallback(policy.Acquire);
        return await originals.RunToOriginalSettlementAsync(async () =>
        {
            var actual = originals.Invoke(() =>
            {
                var task = graph.ExecuteWithCapturedPolicyAsync(input, policy.Snapshot, token);
                if (ordinary is not null) ordinary.RetainUnknownMemoryOriginal(task);
                else (canonical ?? throw new InvalidOperationException("The original Space graph request owner is unavailable."))
                    .RetainAdditionalOriginal("spaces.user-message-graph", task);
                return task;
            });
            return await originals.AwaitAsync(actual).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
