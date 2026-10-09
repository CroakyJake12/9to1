using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed partial class AssistantMiniComputerSource
{
    internal Task<CanonicalMiniComputerCatalogIdentityPreparation> PrepareIdentitySetupWithinSourceAsync(
        AssistantConversationBinding binding, Guid operationId, Action<Action> scope, Action<Task> retain,
        CancellationToken token) => _originals.Run(scope, retain, async source =>
    {
        var membership = RequireMembership(binding);
        var current = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (!current.Definition.Configuration.Enabled || current.Definition.Configuration.Archived)
            return new CanonicalMiniComputerCatalogIdentityPreparation(null, "Enable this current Assistant before reviewing Mini Computer setup.");
        var prepared = await source.Read(() => _catalog.PrepareOriginalIdentitySetupWithinSourceAsync(current.Actor,
            operationId, source.Run, source.Retain, token)).ConfigureAwait(false);
        var after = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (after.Actor != current.Actor) throw new UnauthorizedAccessException("The actual Home actor changed during catalogue setup review.");
        return prepared;
    });
    internal Task<AssistantMiniComputerIdentitySetupResult> ExecuteIdentitySetupWithinSourceAsync(
        AssistantConversationBinding binding, ICanonicalMiniComputerCatalogIdentityIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Run(scope, retain, async source =>
    {
        var membership = RequireMembership(binding);
        var current = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (!current.Definition.Configuration.Enabled || current.Definition.Configuration.Archived ||
            current.Actor != intent.Actor || !_catalog.IsIssuedOriginalIdentityIntent(intent))
            return new AssistantMiniComputerIdentitySetupResult(false, "Review setup again from the current enabled Assistant and Home profile.");
        var actual = await source.Read(() => _catalog.ExecuteOriginalIdentitySetupWithinSourceAsync(intent,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        return new AssistantMiniComputerIdentitySetupResult(actual.Applied, actual.Reason);
    });
    internal AssistantMiniComputerPendingObservation ObserveIdentitySetup(ICanonicalMiniComputerCatalogIdentityIntent intent)
    {
        if (!_catalog.IsIssuedOriginalIdentityIntent(intent)) return new(null, "This catalogue setup preview is unavailable.", false);
        var actual = _catalog.ObserveOriginalIdentitySetup(intent);
        return new(actual.RequestId, actual.IsPending ? "Waiting for this exact catalogue setup decision in Home."
            : "Review the original catalogue setup result.", actual.IsPending);
    }
}
