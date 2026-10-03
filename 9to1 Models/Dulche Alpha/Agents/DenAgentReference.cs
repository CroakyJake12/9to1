namespace Dulche.Runtime.Agents;

/// <summary>Identity and revision observations only. These values cannot grant access to a Den,
/// namespace, Agent, run or tool, and must not be substituted for a privately admitted Home context.</summary>
public sealed record DenAgentReference(string DenId, string NamespaceId, string AgentId, long DefinitionRevision);

