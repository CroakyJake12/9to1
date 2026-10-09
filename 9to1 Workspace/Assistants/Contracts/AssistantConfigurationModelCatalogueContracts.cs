using Haven.Application;

namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>One current saved-definition catalogue observation. Rows are preferences,
/// not model, tool, conversation or resource execution authority.</summary>
public sealed class AssistantOriginalConfigurationModelCatalogue
{
    internal AssistantOriginalConfigurationModelCatalogue(object issuer, AssistantDefinitionSnapshot definition,
        AuthenticatedResourceActor actor, IReadOnlyList<AssistantModelChoice> models, string detail)
    { Issuer = issuer; Definition = definition; Actor = actor; Models = models; Detail = detail; }
    internal object Issuer { get; }
    public AssistantDefinitionSnapshot Definition { get; }
    public AuthenticatedResourceActor Actor { get; }
    public IReadOnlyList<AssistantModelChoice> Models { get; }
    public string Detail { get; }
}

public interface IAssistantOriginalConfigurationModelCatalogueOwner
{
    Task<AssistantOriginalConfigurationModelCatalogue> ReadOriginalConfigurationModelsWithinSourceAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalConfigurationModels(AssistantOriginalConfigurationModelCatalogue sameCatalogue);
    Task RevalidateOriginalConfigurationModelWithinSourceAsync(AssistantOriginalConfigurationModelCatalogue sameCatalogue,
        AssistantModelChoice sameModel, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken token);
}

/// <summary>Optional source port on the SAME configured model selection owner;
/// a definition and expected actor remain observations that its caller must validate.</summary>
public interface IAssistantOriginalConfigurationModelSelectionOwner : IAssistantOriginalModelSelectionOwner
{
    Task<IReadOnlyList<AssistantModelChoice>> ReadAvailableConfigurationOriginalWithinSourceAsync(
        AssistantDefinitionSnapshot definition, AuthenticatedResourceActor expectedActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
