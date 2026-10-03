using System.Text.Json;

namespace NineToOne.Dulche.Den;

/// <summary>Exposure bindings are distinct from permission. Unresolved targets, external callers
/// or copied IDs cannot authorize discovery, context access, a run or an external action.</summary>
public enum AgentAvailabilityScope { Global, Surface, Space, ProjectOrEntity }
public sealed record AgentAvailabilityBinding(Guid BindingId, AgentAvailabilityScope Scope, string? TargetId = null);

/// <summary>References original owning-app artifacts with pinned identity/revision/access scope.
/// No source content, credentials, attachment copy or authority is encoded in this declaration.</summary>
public sealed record AgentKnowledgeReference(Guid ReferenceId, string OwningApp, string CanonicalId,
    string Revision, string AccessScope, string? DisplayName = null, string? ContentHash = null);

/// <summary>Reference to the existing Den Workflow entity; an entry is not a new private graph.
/// The current graph/owner capability contract must admit its exact schema, scope and revision.</summary>
public sealed record DenAgentGraphReference(string DenId, string NamespaceId, string WorkflowId,
    long DefinitionRevision, string? EntryId = null);

public enum AgentQuickActionConfirmationPolicy { Inherit, ConfirmBeforeRun }
/// <summary>One named manual entry on the same canonical Agent. Instructions or a named graph
/// entry is declared explicitly; neither creates an Automation or grants executable capability.</summary>
public sealed record AgentQuickActionDefinition(Guid QuickActionId, string Label,
    string? Instruction = null, DenAgentGraphReference? GraphEntry = null,
    string? IconReference = null, string? Description = null, JsonElement? InputSchema = null,
    IReadOnlyList<AgentKnowledgeReference>? ContextRequirements = null,
    AgentQuickActionConfirmationPolicy ConfirmationPolicy = AgentQuickActionConfirmationPolicy.Inherit,
    int Order = 0);

/// <summary>Memory storage/retention declaration reuses existing Den memory scope/frequency.
/// The actual host must re-admit each read/write; metadata never gives a contact owner memory.</summary>
public sealed record AgentMemoryPolicy(string NamespaceId, MemoryScopeKind ScopeKind, string ScopeId,
    MemoryFrequency ReadFrequency = MemoryFrequency.Never,
    MemoryFrequency WriteFrequency = MemoryFrequency.Never, TimeSpan? MaximumRetention = null);

/// <summary>Descriptive sharing targets only. Canonical namespace/org membership and current
/// ACLs decide actual access. Saving or copying this object never performs a Share operation.</summary>
public sealed record AgentSharingMetadata(string ScopeId, IReadOnlyList<string> PrincipalIds,
    string? OrganisationId = null, string? TeamId = null);

/// <summary>Must agree with Enabled at validation/activation. Null preserves an older explicit
/// development record's behavior; assigning Active does not supply original Home execution authority.</summary>
public enum AgentDefinitionLifecycle { Draft, Active, Paused, Archived }
