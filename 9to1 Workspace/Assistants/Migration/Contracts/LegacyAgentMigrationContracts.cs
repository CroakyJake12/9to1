using System.Runtime.CompilerServices;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

[assembly: InternalsVisibleTo("HavenOS.Apps.Assistants.Migration")]
[assembly: InternalsVisibleTo("HavenOS.Assistants.Migration.Tests")]
[assembly: InternalsVisibleTo("HavenOS.Assistants.NativeUI.Tests")]

namespace HavenOS.Apps.Assistants.Migration;

public enum LegacyAgentMigrationState { Unselected, Staged, Classified, Recovered, Conflict }
public sealed record LegacyAgentMigrationItem(Guid LegacyAgentId, string Name, string Description,
    bool Enabled, bool BuiltIn, LegacyAgentMigrationState State);
public sealed record LegacyAgentMigrationPage(IReadOnlyList<LegacyAgentMigrationItem> Items, string? NextCursor);
public sealed record LegacyAgentLink(string Kind, string Id, string? RelatedId, string? Title);
public sealed record LegacyAgentLinkPage(IReadOnlyList<LegacyAgentLink> Items, string? NextCursor);
public sealed record LegacyAgentPreservationSummary(long Runs, long ScopedMemories,
    bool RunHistoryAvailable, bool MemoryMetadataAvailable);
public sealed record LegacyAgentPendingClassification(Guid OperationId, ConfiguredIdentityKind ConfirmedKind);

/// <summary>Issued by the actual migration presentation after current source ownership checks.
/// IDs, suggested configuration and preserved links convey no execution or resource grants.</summary>
public sealed class LegacyAgentMigrationPreview
{
    internal LegacyAgentMigrationPreview(object owner, object original, Guid id, string sourceRevision,
        AgentDefinition definition, AssistantConfiguration configuration, LegacyAgentMigrationState state,
        long destinationRevision, LegacyAgentPreservationSummary preserved, IReadOnlyList<string> notes,
        bool canClassify, LegacyAgentPendingClassification? pendingClassification = null,
        AssistantDefinitionSnapshot? classifiedDefinition = null)
    { Owner = owner; Original = original; LegacyAgentId = id; SourceRevision = sourceRevision;
      Definition = definition; SuggestedConfiguration = configuration; State = state;
      DestinationRevision = destinationRevision; Preserved = preserved; CompatibilityNotes = notes;
      CanClassify = canClassify; PendingClassification = pendingClassification; ClassifiedDefinition = classifiedDefinition; }
    internal object Owner { get; }
    internal object Original { get; }
    public Guid LegacyAgentId { get; }
    public string SourceRevision { get; }
    public AgentDefinition Definition { get; }
    public AssistantConfiguration SuggestedConfiguration { get; }
    public LegacyAgentMigrationState State { get; }
    public long DestinationRevision { get; }
    public LegacyAgentPreservationSummary Preserved { get; }
    public IReadOnlyList<string> CompatibilityNotes { get; }
    public bool CanClassify { get; }
    public LegacyAgentPendingClassification? PendingClassification { get; }
    public AssistantDefinitionSnapshot? ClassifiedDefinition { get; }
    // No inferred or preselected kind: the user makes this semantic decision.
}

public sealed record LegacyAgentMigrationResult(AssistantDefinitionSnapshot Definition,
    Guid LegacyAgentId, string SourceRevision, Guid OperationId, bool LegacySourceRetained);

public sealed class LegacyAgentRecoveryPreview
{
    internal LegacyAgentRecoveryPreview(object owner, object original, AssistantIdentity identity,
        long revision, string name, ConfiguredIdentityKind kind, bool canUndo, string reason)
    { Owner = owner; Original = original; Identity = identity; Revision = revision; Name = name;
      Kind = kind; CanUndo = canUndo; Reason = reason; }
    internal object Owner { get; }
    internal object Original { get; }
    public AssistantIdentity Identity { get; }
    public long Revision { get; }
    public string Name { get; }
    public ConfiguredIdentityKind Kind { get; }
    public bool CanUndo { get; }
    public string Reason { get; }
}

public sealed record LegacyAgentRecoveryResult(AssistantIdentity Identity, long Revision,
    bool OriginalSourceRetained, bool ClassifiedDefinitionRemoved);

public sealed class LegacyAgentMigrationRefusedException : InvalidOperationException
{
    internal LegacyAgentMigrationRefusedException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

/// <summary>Optional per-view presentation over the SAME source, Home Den and canonical bridge.
/// Native/web callers render this contract; they do not depend on SQLite. Retire/join this owner
/// before retiring its borrowed canonical bridge. Setup uses the existing Home import UI.</summary>
public interface ILegacyAgentMigrationController
{
    Task<LegacyAgentMigrationPage> ListPageAsync(string? cursor = null, int maximum = 30,
        CancellationToken token = default);
    Task<LegacyAgentMigrationPreview> PreviewAsync(Guid legacyAgentId, CancellationToken token = default);
    Task<LegacyAgentLinkPage> ReadPreservedLinksAsync(LegacyAgentMigrationPreview preview,
        string kind, string? cursor = null, int maximum = 30, CancellationToken token = default);
    Task<LegacyAgentMigrationResult> ClassifyAsync(LegacyAgentMigrationPreview preview,
        ConfiguredIdentityKind confirmedKind, AssistantConfiguration confirmedConfiguration,
        Guid operationId, CancellationToken token = default);
    Task<LegacyAgentRecoveryPreview> ReadRecoveryAsync(AssistantIdentity identity,
        CancellationToken token = default);
    Task<LegacyAgentRecoveryResult> UndoAsync(LegacyAgentRecoveryPreview preview,
        Guid operationId, CancellationToken token = default);
    bool IsOriginalCanonicalBridge(IAssistantCanonicalBridge actualBridge);
    bool IsAcknowledgedOriginalCommandRefusal(Task actualCommand);
    void RequestRetirement();
    void DemandExternalOriginalRetirementJoin();
    Task? OriginalClose { get; }
    Task CloseAndDrainAsync();
}
