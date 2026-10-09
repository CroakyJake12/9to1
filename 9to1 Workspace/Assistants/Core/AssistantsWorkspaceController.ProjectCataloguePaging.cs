using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantsWorkspaceController
{
    public Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesPageAsync(int maximum = 32,
        AssistantOriginalProjectCatalogueContinuation? continuation = null, string? searchText = null,
        CancellationToken token = default) => CommandAsync(() => ObserveSourceAsync(() =>
            _bridge is IAssistantOriginalProjectCataloguePagingOwner owner
                ? owner.ReadOriginalProjectCandidatesPageAsync(maximum, continuation, searchText, token)
                : Task.FromException<AssistantOriginalProjectCatalogue>(new AssistantCommandRefusedException(
                    "The genuine saved-project paging source is unavailable."))), false, token);
}
