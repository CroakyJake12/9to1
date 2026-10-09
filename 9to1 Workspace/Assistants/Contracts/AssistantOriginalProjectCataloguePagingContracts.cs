namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>Opaque display-page cursor from the SAME configured saved-project source.
/// It is not a project choice, Home READ receipt or execution permission.</summary>
public sealed class AssistantOriginalProjectCatalogueContinuation
{
    internal AssistantOriginalProjectCatalogueContinuation(object issuer, object original)
    { Issuer = issuer; Original = original; }
    internal object Issuer { get; }
    internal object Original { get; }
}

public interface IAssistantOriginalProjectCataloguePagingOwner
{
    Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesPageAsync(int maximum,
        AssistantOriginalProjectCatalogueContinuation? continuation, string? searchText,
        CancellationToken token = default);
}
