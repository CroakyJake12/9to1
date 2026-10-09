using Haven.Core;
using HavenOS.Apps.Dev;

namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>A source-owned saved canonical context candidate, for display/selection only.
/// It grants no READ, project, container, Task or effect authority.</summary>
public sealed class AssistantOriginalProjectCandidate
{
    internal AssistantOriginalProjectCandidate(object issuer, object original, string title,
        DeveloperProjectReference reference)
    { Issuer = issuer; Original = original; Title = title; Reference = reference; }
    internal object Issuer { get; }
    internal object Original { get; }
    public string Title { get; }
    public DeveloperProjectReference Reference { get; }
}
public sealed record AssistantOriginalProjectCatalogue(IReadOnlyList<AssistantOriginalProjectCandidate> Candidates,
    bool HasMoreSavedContexts, string Message)
{
    public AssistantOriginalProjectCatalogueContinuation? NextContinuation { get; init; }
}

/// <summary>Privately issued only after the actual Home/Files/manual READ and healthy native
/// cleanup. The issuer repeats fresh actor/container/revision/READ checks before use.</summary>
public sealed class AssistantOriginalProjectChoice
{
    internal AssistantOriginalProjectChoice(object issuer, object original, DeveloperResolvedProject project)
    { Issuer = issuer; Original = original; Project = project; }
    internal object Issuer { get; }
    internal object Original { get; }
    public DeveloperResolvedProject Project { get; }
}
public interface IAssistantOriginalProjectSelectionOwner
{
    Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesAsync(int maximum, CancellationToken token);
    Task<AssistantOriginalProjectChoice> AuthorizeOriginalProjectChoiceWithinSourceAsync(AssistantOriginalProjectCandidate candidate,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalProjectChoice(AssistantOriginalProjectChoice choice);
}
