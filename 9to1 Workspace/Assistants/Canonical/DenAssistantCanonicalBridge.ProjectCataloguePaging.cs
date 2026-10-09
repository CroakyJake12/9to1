using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge : IAssistantOriginalProjectCataloguePagingOwner
{
    public Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesPageAsync(int maximum,
        AssistantOriginalProjectCatalogueContinuation? continuation, string? searchText, CancellationToken token = default) =>
        _originals.Admit(async () =>
        {
            _ = await OpenHomeAsync(token).ConfigureAwait(false);
            if (_development is not IAssistantOriginalProjectCataloguePagingOwner owner)
                throw new AssistantCommandRefusedException("The actual saved-project paging source is not configured.");
            var actual = await _originals.Source(() => owner.ReadOriginalProjectCandidatesPageAsync(
                maximum, continuation, searchText, token)).ConfigureAwait(false);
            _ = await OpenHomeAsync(token).ConfigureAwait(false);
            return actual;
        });
}
