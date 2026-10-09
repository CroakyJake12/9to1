using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>Source-owned display metadata for a saved configured identity before
/// it has a conversation. Neither its rows nor selected preferences grant execution.</summary>
public sealed class AssistantOriginalConfigurationCapabilityCatalogue
{
    internal AssistantOriginalConfigurationCapabilityCatalogue(object issuer, object original,
        AssistantDefinitionSnapshot definition, AuthenticatedResourceActor actor,
        CapabilityOriginalCatalogueState state, string detail, IReadOnlyList<CapabilityDefinition> definitions)
    { Issuer = issuer; Original = original; Definition = definition; Actor = actor; State = state; Detail = detail; Definitions = definitions; }
    internal object Issuer { get; }
    internal object Original { get; }
    public AssistantDefinitionSnapshot Definition { get; }
    public AuthenticatedResourceActor Actor { get; }
    public CapabilityOriginalCatalogueState State { get; }
    public string Detail { get; }
    public IReadOnlyList<CapabilityDefinition> Definitions { get; }
}

public sealed class AssistantOriginalConfigurationCapabilityChoice
{
    internal AssistantOriginalConfigurationCapabilityChoice(object issuer, object original,
        AssistantOriginalConfigurationCapabilityCatalogue catalogue, CapabilityDefinition selected)
    { Issuer = issuer; Original = original; Catalogue = catalogue; Selected = selected; }
    internal object Issuer { get; }
    internal object Original { get; }
    public AssistantOriginalConfigurationCapabilityCatalogue Catalogue { get; }
    public CapabilityDefinition Selected { get; }
    // A preference identifies this one observed row, not every connection sharing
    // a provider or owner key. Runtime admission independently revalidates it.
    public string PreferenceId => Selected.Id.ToString("D");
}

public interface IAssistantOriginalConfigurationCapabilityCatalogueOwner
{
    Task<AssistantOriginalConfigurationCapabilityCatalogue> ReadOriginalConfigurationCatalogueWithinSourceAsync(
        AssistantIdentity actualIdentity, long expectedDefinitionRevision,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalConfigurationCatalogue(AssistantOriginalConfigurationCapabilityCatalogue sameActual);
    Task RevalidateOriginalConfigurationCatalogueWithinSourceAsync(AssistantOriginalConfigurationCapabilityCatalogue sameActual,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    Task<AssistantOriginalConfigurationCapabilityChoice> SelectOriginalConfigurationCapabilityWithinSourceAsync(
        AssistantOriginalConfigurationCapabilityCatalogue sameActual, Guid observedCapabilityId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalConfigurationChoice(AssistantOriginalConfigurationCapabilityChoice sameActual);
    Task RevalidateOriginalConfigurationChoiceWithinSourceAsync(AssistantOriginalConfigurationCapabilityChoice sameActual,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
