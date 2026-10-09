using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>Live source-issued continuation. No public constructor, durable grant or saved-ID authority.</summary>
public sealed class AssistantMemoryPageContinuation
{
    internal AssistantMemoryPageContinuation(object issuer, object original) { Issuer = issuer; Original = original; }
    internal object Issuer { get; }
    internal object Original { get; }
}
public sealed record AssistantMemoryManagementPage(IReadOnlyList<KnowledgeRecord> Records,
    bool RequiresInitialWrite, AssistantMemoryPageContinuation? NextContinuation)
{
    // Only the actual source populates this observation after validating its live
    // original lineage. Exact returned record references are retained, never IDs.
    internal IReadOnlyList<KnowledgeRecord> OriginalPreservedLegacyRecords { get; init; } = [];
}
public interface IAssistantMemoryManagementPagingController
{
    Task<AssistantMemoryView> ReadPageAsync(AssistantConversationBinding actualBinding, int maximum = 32,
        string? searchText = null, AssistantMemoryPageContinuation? sameContinuation = null, CancellationToken token = default);
}
