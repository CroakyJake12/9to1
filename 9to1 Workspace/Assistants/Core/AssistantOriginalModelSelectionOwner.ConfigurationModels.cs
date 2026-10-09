using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantOriginalModelSelectionOwner : IAssistantOriginalConfigurationModelSelectionOwner
{
    public async Task<IReadOnlyList<AssistantModelChoice>> ReadAvailableConfigurationOriginalWithinSourceAsync(
        AssistantDefinitionSnapshot definition, AuthenticatedResourceActor expectedActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(expectedActor);
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        token.ThrowIfCancellationRequested();
        if (_providers is not IOriginalModelCatalogueSource providers || _actors is not IOriginalScopedResourceActorSource actors)
            throw new AssistantCommandRefusedException("The configured model registry and Home actor must support original source discovery.");
        Task<T> Read<T>(Func<Task<T>> factory) => DenAssistantCanonicalBridge.CaptureOriginalConfigurationCapabilitySourceAsync(
            factory, originalSynchronousScope, retainOriginalTask);
        async Task<AuthenticatedResourceActor?> Actor() => await Read(() => actors.GetCurrentWithinOriginalSourceAsync(
            originalSynchronousScope, retainOriginalTask, token)).ConfigureAwait(false);
        var actor = await Actor().ConfigureAwait(false);
        if (!ValidPersonalActor(actor) || actor != expectedActor)
            throw new AssistantCommandRefusedException("The current personal Home actor does not match this saved Assistant.");
        var policy = await Read(() => Task.FromResult((Privacy: _privacy.Current, SafeMode: RuntimeSafetyState.IsSafeMode))).ConfigureAwait(false);
        var allowRemote = definition.Configuration.Model.AllowCloud && !policy.Privacy.LocalOnlyMode && !policy.SafeMode;
        // SAME registry/provider identities and canonical policy filter, before any
        // provider query. Its optional source owns and retains every actual child.
        var catalogue = await Read(() => providers.GetModelsWithinOriginalSourceAsync(new ModelCataloguePolicy(AllowLocal: true,
            AllowRemote: allowRemote), originalSynchronousScope, retainOriginalTask, token)).ConfigureAwait(false);
        await DemandCurrent().ConfigureAwait(false);
        var permissions = await Read(() => _permissions.GetOriginalPolicyAsync(token)).ConfigureAwait(false);
        await DemandCurrent().ConfigureAwait(false);
        var choices = await Read(() => Task.FromResult(BuildAvailableOriginalChoices(definition, catalogue, permissions, allowRemote, token))).ConfigureAwait(false);
        await DemandCurrent().ConfigureAwait(false);
        return choices;

        async Task DemandCurrent()
        {
            var currentActor = await Actor().ConfigureAwait(false);
            var current = await Read(() => Task.FromResult((Privacy: _privacy.Current, SafeMode: RuntimeSafetyState.IsSafeMode))).ConfigureAwait(false);
            if (currentActor != expectedActor || current.Privacy != policy.Privacy || current.SafeMode != policy.SafeMode)
                throw new AssistantCommandRefusedException("The Home profile or privacy policy changed during model discovery. Refresh models.");
        }
    }

    // One maintained eligibility/compatibility/order implementation shared by
    // existing conversation discovery and the optional saved-definition reader.
    private IReadOnlyList<AssistantModelChoice> BuildAvailableOriginalChoices(AssistantDefinitionSnapshot definition,
        IReadOnlyList<ProviderModelDescriptor> catalogue, ModelPermissionPolicy permissionPolicy, bool allowRemote, CancellationToken token)
    {
        var restricted = definition.Configuration.ToolIds.Select(ModelToolPermissionMap.Map)
            .Where(value => value.HasValue).Select(value => value!.Value).ToHashSet();
        if (definition.Configuration.ComputerUseRequested) restricted.Add(RestrictedModelCapability.ComputerUse);
        var choices = new List<AssistantModelChoice>();
        foreach (var observed in catalogue)
        {
            token.ThrowIfCancellationRequested();
            var provider = _providers.Find(observed.ProviderId);
            if (provider is null || observed.IsLocal != provider.IsLocal || (!observed.IsLocal && !allowRemote)) continue;
            if (ModelPermissionEvaluator.IsBlockedForAny(permissionPolicy, observed, restricted,
                acrossMesh: observed.ProviderId.Equals(MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase))) continue;
            var compatible = _toCompatibilityDescriptor(observed);
            var expectedName = observed.ProviderId.Equals("ollama", StringComparison.OrdinalIgnoreCase)
                ? observed.Name : observed.Key;
            if (compatible.Name != expectedName)
                throw new InvalidOperationException("The maintained model conversion did not preserve its actual provider-qualified identity.");
            choices.Add(new(compatible, observed.ProviderId));
        }
        // A preferred provider/model influences presentation order only. It neither hides
        // canonical alternatives nor authorizes a fallback or a more permissive request.
        var preference = definition.Configuration.Model;
        return Array.AsReadOnly(choices.OrderByDescending(choice => Preferred(choice, preference))
            .ThenBy(choice => choice.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Model.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
