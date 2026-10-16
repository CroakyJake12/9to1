using Haven.Core;
using NineToOne.Dulche.Context;

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>Optional portable context operations on the same canonical memory owner.</summary>
public interface IAssistantUcmlMemoryManagementController
{
    Task<AssistantMemoryWritePreview> PrepareUcmlImportAsync(AssistantMemoryView view, string ucml,
        Guid operationId, CancellationToken token = default);
    Task<string> ExportUcmlAsync(AssistantMemoryView view, KnowledgeRecord selected, CancellationToken token = default);
}

/// <summary>Pure admission inspection; this never grants access or prepares a write.</summary>
public static class AssistantUcmlImportPolicy
{
    public static void Validate(UcmlDocument document, string actualDefinitionId)
    {
        UcmlCodec.Validate(document);
        var metadata = document.Metadata;
        if (document.Markdown.Length > 4000 || metadata.Title != metadata.Title.Trim())
            throw new ArgumentException("Split imported UCML into memories of at most 4,000 characters, with trimmed titles.");
        // Imported declarations can narrow an existing grant, never create or expand one.
        // The portable owner label is provenance; the actual destination owner comes only
        // from the authenticated Home actor and the SAME canonical Assistant binding.
        if (metadata.Scope.Kind == "personal" ? metadata.Scope.Id != metadata.OwnerId :
            metadata.Scope.Kind != "assistant" || metadata.Scope.Id != actualDefinitionId)
            throw new UnauthorizedAccessException("Only personal context or this exact Assistant scope can be imported here.");
        if (metadata.Classification != "private" || metadata.Retention != "until-revoked" ||
            metadata.ExpiresAt is not null || metadata.SupersedesId is not null || metadata.Revision != 1 ||
            metadata.Consent.Status != "explicit" ||
            !metadata.Consent.Apps.SequenceEqual(["9to1.assistants"], StringComparer.Ordinal) ||
            !metadata.Consent.Purposes.SequenceEqual(["personalization"], StringComparer.Ordinal) ||
            !metadata.Consent.Recipients.SequenceEqual(["local-model"], StringComparer.Ordinal))
            throw new UnauthorizedAccessException("This local import supports private revision-one records, explicit Assistant personalization consent, local-model recipient and until-revoked retention. Other scopes, expiry and imported revisions need their canonical policy owners.");
    }
}
