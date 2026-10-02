namespace Haven.Application.NodeGraph;

/// <summary>Outcome metadata, not a publication or run grant. Null Published means unknown/unstarted,
/// never permission to replay. AuditRecorded reflects only actual durable issuer acknowledgement.</summary>
public sealed record GraphPublicationOutcome(bool? Published, GraphPublicationReceipt? Receipt, bool AuditRecorded, string Code);
