using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantMemoryManagementController : IAssistantMemoryManagementPagingController
{
    private sealed record IssuedPage(AssistantConversationBinding Binding, IChatOriginalPersistentMemoryInput Input,
        int Maximum, string Search, AssistantMemoryPageContinuation SourceContinuation);

    public Task<AssistantMemoryView> ReadPageAsync(AssistantConversationBinding binding, int maximum = 32,
        string? searchText = null, AssistantMemoryPageContinuation? sameContinuation = null, CancellationToken token = default) => Run(async scope =>
    {
        var search = (searchText ?? "").Trim();
        if (maximum is < 1 or > 64 || search.Length > 256)
            return new AssistantMemoryView(_issuer, binding, null, [], "Use 1–64 records per page and a search of at most 256 characters.", search);
        IChatOriginalPersistentMemoryInput? input; AssistantMemoryPageContinuation? sourceContinuation = null;
        if (sameContinuation is not null)
        {
            if (!ReferenceEquals(sameContinuation.Issuer, _issuer) || sameContinuation.Original is not IssuedPage issued ||
                !ReferenceEquals(issued.Binding, binding) || issued.Maximum != maximum || issued.Search != search)
                return new AssistantMemoryView(_issuer, binding, null, [], "Refresh this presentation's memory after changing its Assistant, search or page size.", search);
            input = issued.Input; sourceContinuation = issued.SourceContinuation;
        }
        else
        {
            var prepared = await scope.Read(() => _source.PrepareOriginalAssistantMemoryInputWithinSourceAsync(
                binding, binding.Definition, scope.Run, scope.Retain, token)).ConfigureAwait(false);
            if (!prepared.IsPrepared) return new AssistantMemoryView(_issuer, binding, null, [], prepared.Reason, search);
            input = prepared.Input;
        }
        var observed = await scope.Read(() => _source.ReadOriginalManagementPageWithinSourceAsync(input!, binding.Conversation,
            maximum, search, sourceContinuation, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        var next = observed.NextContinuation is { } continuation
            ? new AssistantMemoryPageContinuation(_issuer, new IssuedPage(binding, input!, maximum, search, continuation)) : null;
        return new AssistantMemoryView(_issuer, binding, input, observed.Records, observed.RequiresInitialWrite
            ? "The first approved memory write will initialize the existing Knowledge store. This read performed no setup."
            : "Current permitted active preferences, newest first. Conversation requests select their own bounded subset.", search, next, observed.OriginalPreservedLegacyRecords);
    });
}
