using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    private sealed record ManagementContinuation(Input Input, string Search, int Maximum,
        KnowledgeLibraryService.AssistantMemoryPagePosition Position);

    public Task<AssistantMemoryManagementPage> ReadOriginalManagementPageWithinSourceAsync(
        IChatOriginalPersistentMemoryInput input, Conversation conversation, int maximum, string search,
        AssistantMemoryPageContinuation? sameContinuation, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunAsync(scope, retain, async source =>
        {
            var original = DemandInput(input);
            if (maximum is < 1 or > 64 || search is null || search.Length > 256)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            ManagementContinuation? after = null;
            if (sameContinuation is not null)
            {
                if (!ReferenceEquals(sameContinuation.Issuer, this) || sameContinuation.Original is not ManagementContinuation issued ||
                    !ReferenceEquals(issued.Input, original) || issued.Search != search || issued.Maximum != maximum)
                    throw new UnauthorizedAccessException("Use the SAME scoped memory page and search continuation.");
                after = issued;
            }
            await ValidateMembershipAsync(original, conversation, null, source, token).ConfigureAwait(false);
            return await WithLeaseAsync(original.Actor, source, async lease =>
            {
                await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
                var ready = await source.Read(() => KnowledgeLibraryService.IsOriginalAssistantMemorySchemaReadyAsync(lease, token)).ConfigureAwait(false);
                var page = ready ? await source.Read(() => _knowledge.ReadAssistantMemoryPageWithinSourceAsync(
                    lease, original.ReadScope, maximum, search, after?.Position, token)).ConfigureAwait(false) : null;
                await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
                await ValidateMembershipAsync(original, conversation, null, source, token).ConfigureAwait(false);
                var next = page?.Next is { } position
                    ? new AssistantMemoryPageContinuation(this, new ManagementContinuation(original, search, maximum, position)) : null;
                var records = page?.Records ?? Array.Empty<KnowledgeRecord>();
                return new AssistantMemoryManagementPage(records, !ready, next)
                {
                    OriginalPreservedLegacyRecords = original.Legacy is null ? [] :
                        Array.AsReadOnly(records.Where(record => record.Scope == "agent").ToArray())
                };
            }, token).ConfigureAwait(false);
        });
}
