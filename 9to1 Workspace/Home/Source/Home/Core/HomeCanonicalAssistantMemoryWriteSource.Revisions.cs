using Haven.Application;
using Haven.Core;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalAssistantMemoryWriteSource
{
    public const string CorrectAction = "assistants.memory.record.correct";
    public const string RejectAction = "assistants.memory.record.reject";

    internal static bool SupportsOriginalAction(string action) => action is WriteAction or CorrectAction or RejectAction;

    private sealed record OriginalMutation(CanonicalAssistantMemoryMutationKind Kind,
        string Action, KnowledgeRecord? Record, string? ExpectedRecordSha256, string IntentDigest)
    {
        internal string ReviewMessage => Kind switch
        {
            CanonicalAssistantMemoryMutationKind.Create => "Create this explicit private Assistant memory record without changing the definition or conversation history.",
            CanonicalAssistantMemoryMutationKind.Correct => "Correct this exact private Assistant memory revision. Preserve its original record, sources and history.",
            CanonicalAssistantMemoryMutationKind.Reject => "Reject this exact private Assistant memory revision from retrieval. Preserve its original record, sources and history.",
            _ => throw new UnauthorizedAccessException("The original memory mutation is unsupported.")
        };
        internal string AppliedAuditCode => Kind switch
        {
            CanonicalAssistantMemoryMutationKind.Create => "HOME_ASSISTANT_MEMORY_CREATED",
            CanonicalAssistantMemoryMutationKind.Correct => "HOME_ASSISTANT_MEMORY_CORRECTED",
            CanonicalAssistantMemoryMutationKind.Reject => "HOME_ASSISTANT_MEMORY_REJECTED",
            _ => throw new UnauthorizedAccessException("The original memory mutation is unsupported.")
        };
    }

    private static OriginalMutation CaptureOriginalMutation(ICanonicalAssistantMemoryWriteSource sameCreator,
        ICanonicalAssistantMemoryWriteIntent sameIntent)
    {
        // Query the SAME actual configured issuer before reading optional projections.
        // Implementing the interface or sending an enum/hash cannot select a WRITE.
        if (!sameCreator.IsIssuedOriginalWriteIntent(sameIntent))
            throw new UnauthorizedAccessException("The actual memory producer did not issue this mutation intent.");
        var kind = CanonicalAssistantMemoryMutationKind.Create;
        KnowledgeRecord? record = null; string? expected = null;
        if (sameIntent is ICanonicalAssistantMemoryRevisionWriteIntent revision)
        { kind = revision.MutationKind; record = revision.OriginalRecord; expected = revision.ExpectedOriginalRecordSha256; }
        var action = kind switch
        {
            CanonicalAssistantMemoryMutationKind.Create when record is null && expected is null => WriteAction,
            CanonicalAssistantMemoryMutationKind.Correct when record is { Id: var id } && id != Guid.Empty && IsDigest(expected) => CorrectAction,
            CanonicalAssistantMemoryMutationKind.Reject when record is { Id: var id } && id != Guid.Empty && IsDigest(expected) => RejectAction,
            _ => throw new UnauthorizedAccessException("The source-issued memory mutation has no exact original revision observation.")
        };
        var digest = sameCreator.GetOriginalWriteIntentDigest(sameIntent);
        if (!IsDigest(digest)) throw new UnauthorizedAccessException("The actual memory mutation digest is unavailable.");
        return new(kind, action, record, expected, digest);
    }
    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed partial class Claim
    {
        private OriginalMutation _originalMutation = null!;
        internal string? OriginalAction => _originalMutation?.Action;
        private void DemandSameOriginalMutation()
        {
            var current = CaptureOriginalMutation(owner._creator, intent);
            if (_originalMutation is null || current.Kind != _originalMutation.Kind || current.Action != _originalMutation.Action ||
                !ReferenceEquals(current.Record, _originalMutation.Record) || current.ExpectedRecordSha256 != _originalMutation.ExpectedRecordSha256 ||
                current.IntentDigest != _originalMutation.IntentDigest)
                throw new UnauthorizedAccessException("The original memory mutation/record/digest changed after review.");
        }
        private (string Code, bool Applied) CaptureAcknowledgedDecisionAudit(ICanonicalAssistantMemoryWriteAcknowledgment sameAcknowledgment)
        {
            // The settlement caller has already checked SAME raw SQL + actual issuer ACK.
            // A decision is a completed transaction observation, never a permission grant.
            if (sameAcknowledgment is ICanonicalAssistantMemoryWriteDecisionAcknowledgment { Applied: false })
                return ("HOME_ASSISTANT_MEMORY_NOT_APPLIED", false);
            return (_originalMutation.AppliedAuditCode, true);
        }
    }
}
