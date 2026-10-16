using Haven.Application;
using Haven.Core;
using NineToOne.Dulche.Context;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    internal const string UcmlMetadataPrefix = "ucml.v1:";

    internal Task<ICanonicalAssistantMemoryWriteIntent> PrepareOriginalUcmlImportWithinSourceAsync(
        IChatOriginalPersistentMemoryInput input, Conversation conversation, UcmlDocument document,
        Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        // A defensive roundtrip freezes caller-owned lists before asynchronous permission work.
        var frozen = UcmlCodec.Parse(UcmlCodec.Serialize(document));
        return PrepareOriginalWriteCoreAsync(input, conversation, frozen.Metadata.Title, frozen.Markdown,
            operationId, scope, retain, token, frozen);
    }

    internal static void DemandUcmlImport(UcmlDocument document, string actualDefinitionId) =>
        AssistantUcmlImportPolicy.Validate(document, actualDefinitionId);

    internal static UcmlDocument? ReadUcmlDocument(KnowledgeRecord record)
    {
        if (!record.LearnedBecause.StartsWith(UcmlMetadataPrefix, StringComparison.Ordinal)) return null;
        var document = new UcmlDocument(UcmlCodec.ParseMetadata(record.LearnedBecause[UcmlMetadataPrefix.Length..]), record.Summary);
        UcmlCodec.Validate(document);
        if (document.Metadata.Id != record.Id || document.Metadata.Title != record.Title ||
            document.Metadata.Category != record.Topic || document.Metadata.Confidence != record.Confidence)
            throw new InvalidOperationException("Canonical UCML provenance no longer matches its memory record.");
        return document;
    }

    internal async Task<UcmlDocument> ExportOriginalUcmlWithinSourceAsync(IChatOriginalPersistentMemoryInput input,
        Conversation conversation, KnowledgeRecord selected, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var current = await ReadOriginalRevisionSnapshotAsync(DemandInput(input), conversation, selected.Id,
            scope, retain, token).ConfigureAwait(false);
        if (current is null || current.Record.UpdatedAt != selected.UpdatedAt || current.Record.Summary != selected.Summary)
            throw new UnauthorizedAccessException("Refresh the current permitted memory before exporting it.");
        return ReadUcmlDocument(current.Record) ?? throw new NotSupportedException("This memory has no UCML provenance.");
    }

    internal Task<IReadOnlyList<UcmlDocument>> RevalidateOriginalUcmlSelectionWithinSourceAsync(
        IChatOriginalPersistentMemoryInput input, Conversation conversation, IReadOnlyList<UcmlDocument> selected,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => RunAsync<IReadOnlyList<UcmlDocument>>(scope, retain, async source =>
    {
        if (selected.Count > 8) throw new ArgumentOutOfRangeException(nameof(selected));
        var original = DemandInput(input);
        await ValidateMembershipAsync(original, conversation, null, source, token).ConfigureAwait(false);
        return await WithLeaseAsync<IReadOnlyList<UcmlDocument>>(original.Actor, source, async lease =>
        {
            await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
            var current = new List<UcmlDocument>();
            // One fresh protected SQLite read transaction supplies the final snapshot.
            // Rejected/corrected/deleted records from earlier scan pages are not disclosed.
            foreach (var document in selected)
            {
                var record = await source.Read(() => _knowledge.ReadOriginalAssistantMemoryRecordAsync(
                    lease, original.ReadScope, document.Metadata.Id, token)).ConfigureAwait(false);
                if (record is not null && ReadUcmlDocument(record.Record) is { } fresh &&
                    UcmlCodec.Serialize(fresh) == UcmlCodec.Serialize(document)) current.Add(fresh);
            }
            await DemandPermissionAsync(original, lease, source, token).ConfigureAwait(false);
            await ValidateMembershipAsync(original, conversation, null, source, token).ConfigureAwait(false);
            return Array.AsReadOnly(current.ToArray());
        }, token).ConfigureAwait(false);
    });
}
