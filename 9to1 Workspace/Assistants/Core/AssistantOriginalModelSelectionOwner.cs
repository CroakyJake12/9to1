using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

/// <summary>
/// Catalogue adapter over the existing provider registry, privacy preferences and model
/// permission evaluator. Choices are current observations, never provider/resource grants.
/// Conversion is the actual host's maintained compatibility-descriptor method; this class
/// creates no routing policy, fallback engine, credential store or model fixture.
/// </summary>
public sealed partial class AssistantOriginalModelSelectionOwner : IAssistantOriginalModelSelectionOwner
{
    private readonly IModelProviderRegistry _providers;
    private readonly IPrivacyPreferenceStore _privacy;
    private readonly ModelPermissionEvaluator _permissions;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly Func<ProviderModelDescriptor, ModelDescriptor> _toCompatibilityDescriptor;

    public AssistantOriginalModelSelectionOwner(
        IModelProviderRegistry sameProviders,
        IPrivacyPreferenceStore samePrivacy,
        ModelPermissionEvaluator samePermissions,
        IAuthenticatedResourceActorSource sameActors,
        Func<ProviderModelDescriptor, ModelDescriptor> sameCompatibilityDescriptor)
    {
        _providers = sameProviders ?? throw new ArgumentNullException(nameof(sameProviders));
        _privacy = samePrivacy ?? throw new ArgumentNullException(nameof(samePrivacy));
        _permissions = samePermissions ?? throw new ArgumentNullException(nameof(samePermissions));
        _actors = sameActors ?? throw new ArgumentNullException(nameof(sameActors));
        _toCompatibilityDescriptor = sameCompatibilityDescriptor ?? throw new ArgumentNullException(nameof(sameCompatibilityDescriptor));
    }

    public async Task<IReadOnlyList<AssistantModelChoice>> ReadAvailableOriginalAsync(
        AssistantDefinitionSnapshot definition, Conversation conversation, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(conversation);
        token.ThrowIfCancellationRequested();
        var actor = await AwaitOriginalAsync(_actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
        if (!ValidPersonalActor(actor))
            throw new AssistantCommandRefusedException("A current authenticated personal Home actor is required for the Assistant model catalogue.");
        var privacy = _privacy.Current;
        var safeMode = RuntimeSafetyState.IsSafeMode;
        var allowRemote = definition.Configuration.Model.AllowCloud && !privacy.LocalOnlyMode && !safeMode;
        // The canonical registry filters providers BEFORE contacting them. Filtering a
        // returned remote list would already have violated local-only catalogue privacy.
        var catalogue = await AwaitOriginalAsync(_providers.GetModelsAsync(new ModelCataloguePolicy(AllowLocal: true,
            AllowRemote: allowRemote), token)).ConfigureAwait(false);
        await DemandUnchangedActorAndPrivacyAsync(actor!, privacy, safeMode, token).ConfigureAwait(false);
        var permissionPolicy = await AwaitOriginalAsync(_permissions.GetOriginalPolicyAsync(token)).ConfigureAwait(false);
        await DemandUnchangedActorAndPrivacyAsync(actor!, privacy, safeMode, token).ConfigureAwait(false);

        var choices = BuildAvailableOriginalChoices(definition, catalogue, permissionPolicy, allowRemote, token);
        await DemandUnchangedActorAndPrivacyAsync(actor!, privacy, safeMode, token).ConfigureAwait(false);
        return choices;
    }

    private async Task DemandUnchangedActorAndPrivacyAsync(AuthenticatedResourceActor actor,
        PrivacyPreferences privacy, bool safeMode, CancellationToken token)
    {
        var currentActor = await AwaitOriginalAsync(_actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
        // Read privacy AFTER the actor await as well: that source may yield while
        // the user's local-only or safe-mode policy changes.
        if (currentActor != actor || _privacy.Current != privacy || RuntimeSafetyState.IsSafeMode != safeMode)
            throw new AssistantCommandRefusedException("The current actor or privacy policy changed during model discovery. Refresh under the current owner.");
    }
    private static async Task<T> AwaitOriginalAsync<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted && actual.Exception!.InnerExceptions.Count > 1) { throw actual.Exception!; }
    }
    private static bool ValidPersonalActor(AuthenticatedResourceActor? actor) => actor is not null
        && !string.IsNullOrWhiteSpace(actor.ActorId) && !string.IsNullOrWhiteSpace(actor.ProfileId)
        && !string.IsNullOrWhiteSpace(actor.AuthenticationRevision) && actor.AccountId is null && actor.OrganisationId is null;
    private static bool Preferred(AssistantModelChoice choice, AssistantModelPreferences preference) =>
        (string.IsNullOrWhiteSpace(preference.ProviderId) || choice.ProviderId.Equals(preference.ProviderId, StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(preference.ModelId)
        && (choice.Model.Name.Equals(preference.ModelId, StringComparison.OrdinalIgnoreCase)
            || choice.Model.Name.Equals(choice.ProviderId + ":" + preference.ModelId, StringComparison.OrdinalIgnoreCase));
}
