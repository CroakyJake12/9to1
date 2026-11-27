using Haven.Core;

namespace Haven.Application;

/// <summary>The effective version selected by the actual branch reader. Null means the
/// original row has no version; it never means any version is acceptable.</summary>
public sealed record ConversationSourceVersion(Guid MessageId, Guid? VersionId);

/// <summary>A retained replacement for exact source messages. Raw bodies remain in history;
/// prompt exclusion requires matching source coverage in the branch being read.</summary>
public sealed record ConversationCompaction(ConversationContextEntry Summary, IReadOnlyList<ChatMessage> OriginalMessages)
{
    public Guid? SourceBranchId { get; init; }
    public IReadOnlyList<ConversationSourceVersion>? SourceVersions { get; init; }

    public ConversationCompaction Snapshot(Guid conversationId)
    {
        ArgumentNullException.ThrowIfNull(Summary);
        ArgumentNullException.ThrowIfNull(OriginalMessages);
        var originals = OriginalMessages.ToArray();
        if (conversationId == Guid.Empty || Summary.Id == Guid.Empty || Summary.ConversationId != conversationId ||
            Summary.Kind != ContextEntryKind.CompactSummary || string.IsNullOrWhiteSpace(Summary.Content) ||
            SourceBranchId == Guid.Empty || originals.Length == 0 || originals.Any(message => message is null || message.Id == Guid.Empty ||
                message.ConversationId != conversationId || message.IsCompacted ||
                message.Role is not (MessageRole.User or MessageRole.Assistant)) ||
            originals.Select(message => message.Id).Distinct().Count() != originals.Length)
            throw new ArgumentException("A compaction must retain a nonempty summary of exact included messages from this conversation.");
        var versions = SourceVersions?.ToArray();
        if (versions is not null && (versions.Any(version => version is null || version.MessageId == Guid.Empty || version.VersionId == Guid.Empty) ||
            versions.Select(version => version.MessageId).Distinct().Count() != originals.Length || versions.Length != originals.Length ||
            !versions.Select(version => version.MessageId).ToHashSet().SetEquals(originals.Select(message => message.Id))))
            throw new ArgumentException("Compaction coverage must identify each original source version exactly once.");
        return this with { OriginalMessages = Array.AsReadOnly(originals),
            SourceVersions = versions is null ? null : Array.AsReadOnly(versions) };
    }
}

/// <summary>The canonical store commits replacement text, exact source coverage and inclusion
/// flags in one transaction. Failure leaves raw prompt context available.</summary>
public interface IConversationCompactionRepository
{
    Task<ConversationCompactionReceipt> CommitCompactionAsync(Guid conversationId,
        ConversationCompaction compaction, CancellationToken cancellationToken);
}

public sealed record ConversationCompactionReceipt(Guid ConversationId, ConversationContextEntry Summary,
    IReadOnlyList<Guid> SourceMessageIds, DateTimeOffset CompactedAt);

public interface IConversationPromptContextRepository
{
    Task<ConversationPromptContext> GetPromptContextAsync(Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>Optional context projection on the SAME admitted branch invocation. It must keep
/// that invocation's admission, operation gate and original branch identity.</summary>
public interface IConversationBranchPromptContextSource
{
    Task<ConversationPromptContext> GetPromptContextAsync(CancellationToken cancellationToken);
}

/// <summary>An immutable read of one branch. Only applicable, nonoverlapping replacements
/// may exclude source rows; OriginalMessages always retains every raw row and its real flags.</summary>
public sealed class ConversationPromptContext
{
    public ConversationPromptContext(Guid conversationId, Guid? branchId, IReadOnlyList<ChatMessage> originalMessages,
        IReadOnlyList<ConversationSourceVersion> sourceVersions, IReadOnlyList<ConversationCompaction> compactions)
    {
        ArgumentNullException.ThrowIfNull(originalMessages);
        ArgumentNullException.ThrowIfNull(sourceVersions);
        ArgumentNullException.ThrowIfNull(compactions);
        var history = originalMessages.ToArray();
        var versions = sourceVersions.ToArray();
        if (conversationId == Guid.Empty || branchId == Guid.Empty ||
            history.Any(message => message is null || message.Id == Guid.Empty || message.ConversationId != conversationId) ||
            history.Select(message => message.Id).Distinct().Count() != history.Length ||
            versions.Any(version => version is null || version.MessageId == Guid.Empty || version.VersionId == Guid.Empty) ||
            versions.Select(version => version.MessageId).Distinct().Count() != history.Length || versions.Length != history.Length ||
            !versions.Select(version => version.MessageId).ToHashSet().SetEquals(history.Select(message => message.Id)))
            throw new ArgumentException("Prompt context must preserve the exact original conversation, messages and effective versions.");
        var byId = history.ToDictionary(message => message.Id);
        var versionById = versions.ToDictionary(version => version.MessageId, version => version.VersionId);
        var replacements = compactions.Select(compaction => compaction.Snapshot(conversationId)).ToArray();
        var covered = new HashSet<Guid>();
        var summaryIds = new HashSet<Guid>();
        foreach (var replacement in replacements)
        {
            if (!summaryIds.Add(replacement.Summary.Id)) throw new ArgumentException("A summary cannot be included more than once.");
            foreach (var source in replacement.OriginalMessages)
                if (!covered.Add(source.Id) || !byId.TryGetValue(source.Id, out var actual) || !actual.IsCompacted ||
                    source != (actual with { IsCompacted = false }))
                    throw new ArgumentException("A replacement cannot exclude absent, changed or already covered branch messages.");
            if (replacement.SourceVersions is { } sourceVersion &&
                sourceVersion.Any(version => versionById[version.MessageId] != version.VersionId))
                throw new ArgumentException("A replacement no longer matches its effective source versions.");
        }
        ConversationId = conversationId; BranchId = branchId;
        OriginalMessages = Array.AsReadOnly(history);
        SourceVersions = Array.AsReadOnly(versions);
        Compactions = Array.AsReadOnly(replacements);
        Messages = Array.AsReadOnly(history.Where(message => !covered.Contains(message.Id))
            .Select(message => message with { IsCompacted = false }).ToArray());
    }

    public Guid ConversationId { get; }
    public Guid? BranchId { get; }
    public IReadOnlyList<ChatMessage> OriginalMessages { get; }
    public IReadOnlyList<ConversationSourceVersion> SourceVersions { get; }
    public IReadOnlyList<ConversationCompaction> Compactions { get; }
    public IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>Capture the original branch/version observation BEFORE summary generation.
    /// Commit must revalidate it; reading the latest version after generation is insufficient.</summary>
    public ConversationCompaction BindCompaction(ConversationContextEntry summary, IReadOnlyList<ChatMessage> sources)
    {
        var included = Messages.ToDictionary(message => message.Id);
        if (sources.Any(source => !included.TryGetValue(source.Id, out var actual) || actual != source))
            throw new InvalidOperationException("The selected compaction source is not included in this original branch read.");
        var ids = sources.Select(source => source.Id).ToHashSet();
        return new ConversationCompaction(summary, sources)
        {
            SourceBranchId = BranchId,
            SourceVersions = SourceVersions.Where(version => ids.Contains(version.MessageId)).ToArray()
        }.Snapshot(ConversationId);
    }
}

/// <summary>Session-only ordinary chat context, frozen by the actual page before Send. This
/// carries data, never routing, resource or tool authority, and is never serialized.</summary>
public sealed class TemporaryConversationContext
{
    public TemporaryConversationContext(Conversation original, IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ConversationCompaction> compactions)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(compactions);
        if (original.IsArchived || !original.IsTemporary || original.Mode != HavenMode.Chat || original.Kind != ConversationKind.Chat)
            throw new ArgumentException("Only an original ordinary Temporary Chat can own this live context.");
        Original = original;
        Context = new(original.Id, null, history, history.Select(message => new ConversationSourceVersion(message.Id, null)).ToArray(), compactions);
        if (Context.Compactions.Any(compaction => compaction.SourceBranchId is not null || compaction.SourceVersions is not null))
            throw new ArgumentException("A Temporary replacement cannot borrow persisted branch coverage.");
    }

    public Conversation Original { get; }
    public ConversationPromptContext Context { get; }

    public ConversationPromptContext ForConversation(Conversation conversation)
    {
        if (conversation.IsArchived != Original.IsArchived || !conversation.IsTemporary || conversation.Id != Original.Id || conversation.CreatedAt != Original.CreatedAt ||
            conversation.Mode != Original.Mode || conversation.Kind != Original.Kind || conversation.SpaceId != Original.SpaceId ||
            conversation.ContainerId != Original.ContainerId || conversation.LessonId != Original.LessonId ||
            conversation.ParentConversationId != Original.ParentConversationId)
            throw new InvalidOperationException("The live Temporary context belongs to a different original conversation.");
        return Context;
    }
}
