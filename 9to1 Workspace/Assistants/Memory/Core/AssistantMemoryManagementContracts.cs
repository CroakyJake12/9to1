using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Memory;

public sealed class AssistantMemoryView
{
    internal AssistantMemoryView(object issuer, AssistantConversationBinding binding,
        IChatOriginalPersistentMemoryInput? input, IReadOnlyList<KnowledgeRecord> records, string reason, string searchText = "", AssistantMemoryPageContinuation? nextContinuation = null,
        IReadOnlyList<KnowledgeRecord>? preservedLegacyRecords = null)
    { Issuer = issuer; Binding = binding; Input = input; Records = records; Reason = reason; SearchText = searchText; NextContinuation = nextContinuation;
      _preservedLegacyRecords = Array.AsReadOnly((preservedLegacyRecords ?? []).ToArray()); }
    private readonly IReadOnlyList<KnowledgeRecord> _preservedLegacyRecords;
    public bool IsPreservedLegacyRecord(KnowledgeRecord sameRecord) =>
        Records.Any(record => ReferenceEquals(record, sameRecord)) &&
        _preservedLegacyRecords.Any(record => ReferenceEquals(record, sameRecord));
    internal object Issuer { get; }
    internal AssistantConversationBinding Binding { get; }
    internal IChatOriginalPersistentMemoryInput? Input { get; }
    public AssistantDefinitionSnapshot Definition => Binding.Definition;
    public bool IsAvailable => Input is not null;
    public IReadOnlyList<KnowledgeRecord> Records { get; }
    public string Reason { get; }
    public string SearchText { get; }
    public AssistantMemoryPageContinuation? NextContinuation { get; }
}
public sealed class AssistantMemoryWritePreview
{
    internal AssistantMemoryWritePreview(object issuer, ICanonicalAssistantMemoryWriteIntent intent)
    { Issuer = issuer; Intent = intent; }
    internal object Issuer { get; }
    internal ICanonicalAssistantMemoryWriteIntent Intent { get; }
    public KnowledgeRecord Candidate => Intent.Candidate;
    public Guid OperationId => Intent.OperationId;
    public CanonicalAssistantMemoryMutationKind MutationKind => (Intent as ICanonicalAssistantMemoryRevisionWriteIntent)?.MutationKind ?? CanonicalAssistantMemoryMutationKind.Create;
    public KnowledgeRecord? OriginalRecord => (Intent as ICanonicalAssistantMemoryRevisionWriteIntent)?.OriginalRecord;
    public string Explanation => MutationKind switch
    {
        CanonicalAssistantMemoryMutationKind.Correct => "Creates the corrected memory and stops recall of its superseded original after separate Home approval. Original records and index history are retained.",
        CanonicalAssistantMemoryMutationKind.Reject => "Stops recall of this memory after separate Home approval. Its original record and index history are retained.",
        _ => "Creates this private local preference for this Assistant after a separate Home approval."
    };
}
public sealed record AssistantMemorySaveResult(bool Saved, KnowledgeRecord? Record, string Reason);
public sealed record AssistantMemoryManagementRecords(IReadOnlyList<KnowledgeRecord> Records, bool RequiresInitialWrite);
public interface IAssistantMemoryManagementController : IAsyncDisposable
{
    bool IsOriginalCanonicalBridge(IAssistantCanonicalBridge bridge);
    Task<AssistantMemoryView> ReadAsync(AssistantConversationBinding actualBinding, CancellationToken token = default);
    Task<AssistantMemoryWritePreview> PrepareAsync(AssistantMemoryView sameView, string title, string summary,
        Guid operationId, CancellationToken token = default);
    Task<AssistantMemorySaveResult> CommitAsync(AssistantMemoryWritePreview samePreview, CancellationToken token = default);
    Task? OriginalClose { get; }
    void DemandExternalOriginalRetirementJoin();
    void RequestRetirement();
    Task CloseAndDrainAsync();
}

public sealed record AssistantMemoryRevisionPreparation(AssistantMemoryWritePreview? Preview, string Reason);
public interface IAssistantMemoryRevisionManagementController
{
    Task<AssistantMemoryRevisionPreparation> PrepareRevisionAsync(AssistantMemoryView sameView,
        KnowledgeRecord sameSelectedRecord, CanonicalAssistantMemoryMutationKind kind, string title,
        string summary, Guid operationId, CancellationToken token = default);
}
