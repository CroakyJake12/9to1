using System.Runtime.CompilerServices;
using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge : IAssistantOriginalConfigurationModelCatalogueOwner
{
    private readonly ConditionalWeakTable<AssistantOriginalConfigurationModelCatalogue, object> _configurationModelCatalogues = new();

    public Task<AssistantOriginalConfigurationModelCatalogue> ReadOriginalConfigurationModelsWithinSourceAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token) => _originals.Admit(async () =>
    {
        var callbacks = CaptureConfigurationCapabilityCallbacks(originalSynchronousScope, retainOriginalTask);
        var current = await ReadCurrentConfigurationModelDefinitionAsync(identity, expectedDefinitionRevision, callbacks, token).ConfigureAwait(false);
        var choices = _models is IAssistantOriginalConfigurationModelSelectionOwner source
            ? await CaptureOriginalConfigurationCapabilitySourceAsync(() => source.ReadAvailableConfigurationOriginalWithinSourceAsync(
                current.Definition, current.Actor, callbacks.Scope, callbacks.Retain, token), callbacks.Scope, callbacks.Retain).ConfigureAwait(false)
            : Array.Empty<AssistantModelChoice>();
        var fresh = await ReadCurrentConfigurationModelDefinitionAsync(identity, expectedDefinitionRevision, callbacks, token).ConfigureAwait(false);
        if (fresh.Actor != current.Actor) throw new AssistantCommandRefusedException("The Home profile changed during model discovery.");
        var frozen = Array.AsReadOnly(choices.ToArray());
        var result = new AssistantOriginalConfigurationModelCatalogue(this, current.Definition, current.Actor, frozen,
            _models is not IAssistantOriginalConfigurationModelSelectionOwner
                ? "Model discovery is not configured on this host. Your saved preferences are preserved."
                : frozen.Count == 0
                    ? "No models are currently available under this Assistant's saved model and privacy preferences. Review those preferences, save, then refresh."
                    : "Choose a preferred model from this Assistant's current catalogue, then save configuration.");
        _configurationModelCatalogues.Add(result, new object());
        return result;
    });

    public bool IsIssuedOriginalConfigurationModels(AssistantOriginalConfigurationModelCatalogue sameCatalogue) =>
        sameCatalogue is not null && ReferenceEquals(sameCatalogue.Issuer, this) && _configurationModelCatalogues.TryGetValue(sameCatalogue, out _);

    public Task RevalidateOriginalConfigurationModelWithinSourceAsync(AssistantOriginalConfigurationModelCatalogue sameCatalogue,
        AssistantModelChoice sameModel, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken token) => _originals.Admit(async () =>
    {
        if (!IsIssuedOriginalConfigurationModels(sameCatalogue) || !sameCatalogue.Models.Any(value => ReferenceEquals(value, sameModel)))
            throw new UnauthorizedAccessException("Choose an exact model row issued by this saved Assistant's catalogue.");
        var callbacks = CaptureConfigurationCapabilityCallbacks(originalSynchronousScope, retainOriginalTask);
        var current = await ReadCurrentConfigurationModelDefinitionAsync(sameCatalogue.Definition.Identity,
            sameCatalogue.Definition.Revision, callbacks, token).ConfigureAwait(false);
        if (current.Actor != sameCatalogue.Actor) throw new AssistantCommandRefusedException("The Home profile changed. Refresh models.");
        if (_models is not IAssistantOriginalConfigurationModelSelectionOwner source)
            throw new AssistantCommandRefusedException("The original model catalogue source is unavailable.");
        var available = await CaptureOriginalConfigurationCapabilitySourceAsync(() => source.ReadAvailableConfigurationOriginalWithinSourceAsync(
            current.Definition, current.Actor, callbacks.Scope, callbacks.Retain, token), callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        var fresh = await ReadCurrentConfigurationModelDefinitionAsync(sameCatalogue.Definition.Identity,
            sameCatalogue.Definition.Revision, callbacks, token).ConfigureAwait(false);
        if (fresh.Actor != current.Actor || !available.Any(value => value.ProviderId == sameModel.ProviderId && value.Model.Name == sameModel.Model.Name))
            throw new AssistantCommandRefusedException("That model is no longer available under the current saved preferences. Refresh models.");
        return true;
    });

    private async Task<(AssistantDefinitionSnapshot Definition, AuthenticatedResourceActor Actor)> ReadCurrentConfigurationModelDefinitionAsync(
        AssistantIdentity identity, long expectedRevision, (Action<Action> Scope, Action<Task> Retain) callbacks, CancellationToken token)
    {
        var home = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            _membership.OpenHomeWithinSourceAsync(callbacks.Scope, callbacks.Retain, token), callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        var row = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            _membership.DefinitionWithinSourceAsync(home, identity, callbacks.Scope, callbacks.Retain, token), callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        var definition = Snapshot(home, row);
        if (definition.Revision != expectedRevision || definition.Kind != ConfiguredIdentityKind.Assistant)
            throw new AssistantCommandRefusedException("Review models from the current saved Assistant revision.");
        return (definition, home.Actor);
    }
}
