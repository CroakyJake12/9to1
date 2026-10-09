using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    // Current Task identity is independent from the Home identity that authorizes
    // Den membership and the assistant.memory store. Neither observation grants the other.
    private async Task<bool> IsOriginalTaskOwnerCurrentAsync(TaskExecutionSnapshot task,
        AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        var actual = await source.Read(() => _tasks.ObserveOriginalTaskActorWithinSourceAsync(
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var issued = false;
        source.Run(() => issued = _tasks.IsIssuedOriginalTaskActorObservation(actual));
        if (!issued) throw new InvalidOperationException("The actual Task owner did not issue its current actor observation.");
        if (!actual.IsConfigured || actual.Actor is not { } actor || task.OwnerBinding is not { } owner) return false;
        return owner.TaskId == task.TaskId && owner.ContextId == task.ContextId && owner.ExecutionId == task.ExecutionId &&
            owner.ActorId == actor.ActorId && owner.ProfileId == actor.ProfileId && owner.AccountId == actor.AccountId &&
            owner.OrganisationId == actor.OrganisationId && owner.AuthenticationRevision == actor.AuthenticationRevision;
    }
}
