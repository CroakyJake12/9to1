namespace HavenOS.Apps.Assistants.Canonical;

// A source-observed durable Pending membership, not a successful binding and not
// an enclosing pre-effect refusal. Keep SAME operation/IDs and original source cause.
internal sealed class AssistantCompatibleTaskPublicationPendingException(
    Guid originalConversationId, Guid originalOperationId, long originalMembershipRevision,
    Exception? originalCause = null) : InvalidOperationException(
        "The compatible project Task publication remains pending. Preserve the SAME conversation and operation; inspect its source-owned recovery before another attempt.", originalCause)
{
    internal Guid OriginalConversationId { get; } = originalConversationId;
    internal Guid OriginalOperationId { get; } = originalOperationId;
    internal long OriginalMembershipRevision { get; } = originalMembershipRevision;
}
