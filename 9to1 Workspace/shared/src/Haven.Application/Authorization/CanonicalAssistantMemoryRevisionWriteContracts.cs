using Haven.Core;

namespace Haven.Application;

public enum CanonicalAssistantMemoryMutationKind { Create = 0, Correct = 1, Reject = 2 }

/// <summary>Observations on the SAME private canonical producer intent. The bound
/// Home owner verifies that original issuer before selecting a distinct manual
/// action. Neither this interface, its record ID nor its hash grants authority.</summary>
public interface ICanonicalAssistantMemoryRevisionWriteIntent : ICanonicalAssistantMemoryWriteIntent
{
    CanonicalAssistantMemoryMutationKind MutationKind { get; }
    KnowledgeRecord? OriginalRecord { get; }
    string? ExpectedOriginalRecordSha256 { get; }
}

/// <summary>Optional decision on the SAME source-owned atomic acknowledgment.
/// Applied=false is issued only after a proved no-effect transaction rollback
/// and independently healthy original source custody, never an unknown fault.
/// Consumers must obtain the original creator's exact Task/ack issuer proof first.</summary>
public interface ICanonicalAssistantMemoryWriteDecisionAcknowledgment : ICanonicalAssistantMemoryWriteAcknowledgment
{
    bool Applied { get; }
    string Reason { get; }
}
