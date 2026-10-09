using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantOriginalDevelopmentOwner
{
    private async Task<AuthenticatedResourceActor> ReadOriginalTaskActorWithinSourceAsync(
        Sources sources, CancellationToken token)
    {
        var observed = await sources.Take(() => _tasks.ObserveOriginalTaskActorWithinSourceAsync(
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        var issued = false;
        sources.Scope(() => issued = _tasks.IsIssuedOriginalTaskActorObservation(observed, _actors));
        if (!issued)
            throw new AssistantCommandRefusedException("The configured Task authority and project input must use the SAME actual Task actor source.");
        return observed.IsConfigured && observed.Actor is { } actor ? actor :
            throw new AssistantCommandRefusedException("The actual current Task actor is unavailable.");
    }
}
