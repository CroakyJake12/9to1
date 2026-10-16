using Haven.Core;

namespace Haven.Infrastructure;

// This interface is internal to the canonical persistence layer and its trusted product
// assembly. IDs/owner text never grant access: the issued Home scope is checked first.
internal interface IAssistantMemoryRecordProtection
{
    Task<KnowledgeRecord> ProtectAsync(KnowledgeRecord record, string ownerId, string storageScope, CancellationToken token);
    Task<KnowledgeRecord> UnprotectAsync(KnowledgeRecord record, string ownerId, string storageScope, CancellationToken token);
}

public sealed partial class KnowledgeLibraryService
{
    private static async Task<KnowledgeRecord> HydrateAssistantMemoryAsync(AssistantOriginalReadScope scope,
        KnowledgeRecord stored, CancellationToken token)
    {
        if (!stored.LearnedBecause.StartsWith("ucml.v1:", StringComparison.Ordinal)) return stored;
        var protection = scope.Protection ?? throw new UnauthorizedAccessException("UCML requires the original per-user credential custody owner.");
        return await protection.UnprotectAsync(stored, scope.ProtectionOwnerId, scope.StorageScope, token).ConfigureAwait(false);
    }
}
