using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Services;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Verified-profile platform adapter of the SAME canonical repository and records.
/// The shared transport owns every actor/storage/actor original. This creates no Task, provider,
/// permission, remote sync or browser process capability. Branch/turn production APIs require their owner.</summary>
[SupportedOSPlatform("browser")]
public sealed class IndexedDbConversationRepository(BrowserTaskExecutionTransport transport, BrowserTaskActorSource actors)
    : IConversationRepository
{
    internal void DemandPrivateContextCurrent() => transport.DemandPrivateContextCurrent();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IgnoreReadOnlyProperties = true };
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static void RequireId(Guid id) { if (id == Guid.Empty) throw new ArgumentException("A genuine canonical record ID is required."); }
    private static void Validate(Conversation row)
    {
        RequireId(row.Id);
        if (!Enum.IsDefined(row.Mode) || !Enum.IsDefined(row.Kind) || row.Title is null || row.ContainerId == Guid.Empty ||
            row.LessonId == Guid.Empty || row.ParentConversationId == Guid.Empty || row.SpaceId == Guid.Empty)
            throw new InvalidDataException("The canonical conversation record is invalid.");
    }
    private static void Validate(ChatMessage row)
    {
        RequireId(row.Id); RequireId(row.ConversationId);
        if (!Enum.IsDefined(row.Role) || row.Content is null) throw new InvalidDataException("The canonical message is invalid.");
        // MetadataJson is preserved verbatim, including historical malformed text. Never call its computed Metadata getter.
    }
    private static void Validate(ConversationContextEntry row)
    {
        RequireId(row.Id); RequireId(row.ConversationId);
        if (!Enum.IsDefined(row.Kind) || row.Title is null || row.Content is null || row.Evidence is null)
            throw new InvalidDataException("The canonical context record is invalid.");
    }
    private static object Encode<T>(T row, string entity)
    {
        var json = JsonSerializer.Serialize(row, Json);
        using var doc = JsonDocument.Parse(json); var data = doc.RootElement;
        return new
        {
            profileId = "", entity, id = data.GetProperty("id").GetGuid(), json, sha256 = Hash(json), revision = "1",
            createdAt = data.GetProperty("createdAt").GetString(),
            updatedAt = entity == "conversation" ? data.GetProperty("updatedAt").GetString() : null,
            mode = entity == "conversation" ? data.GetProperty("mode").GetInt32() : (int?)null,
            kind = entity is "conversation" or "context" ? data.GetProperty("kind").GetInt32() : (int?)null,
            conversationId = entity == "conversation" ? (Guid?)null : data.GetProperty("conversationId").GetGuid(),
            spaceId = entity == "conversation" && data.GetProperty("spaceId").ValueKind != JsonValueKind.Null ? data.GetProperty("spaceId").GetGuid() : (Guid?)null,
            isTemporary = entity == "conversation" && data.GetProperty("isTemporary").GetBoolean(),
            isArchived = entity == "conversation" && data.GetProperty("isArchived").GetBoolean(),
            isCompacted = entity == "message" && data.GetProperty("isCompacted").GetBoolean()
        };
    }
    private static T Decode<T>(JsonElement row, string entity, AuthenticatedResourceActor actor)
    {
        var json = row.GetProperty("json").GetString() ?? throw new InvalidDataException("No canonical record payload.");
        var revision = row.GetProperty("revision").GetString();
        if (row.GetProperty("profileId").GetString() != actor.ProfileId || row.GetProperty("entity").GetString() != entity ||
            row.GetProperty("sha256").GetString() != Hash(json) || revision is null ||
            !long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1 || value.ToString(CultureInfo.InvariantCulture) != revision)
            throw new InvalidDataException("The actual canonical profile, payload hash or storage revision differs.");
        var result = JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidDataException("No canonical record was decoded.");
        using var doc = JsonDocument.Parse(json); var data = doc.RootElement;
        if (row.GetProperty("id").GetGuid() != data.GetProperty("id").GetGuid() ||
            row.GetProperty("createdAt").GetString() != data.GetProperty("createdAt").GetString())
            throw new InvalidDataException("The actual canonical identity/date metadata differs.");
        if (result is Conversation conversation)
        {
            Validate(conversation);
            if (row.GetProperty("updatedAt").GetString() != data.GetProperty("updatedAt").GetString() ||
                row.GetProperty("mode").GetInt32() != (int)conversation.Mode || row.GetProperty("kind").GetInt32() != (int)conversation.Kind ||
                row.GetProperty("spaceId").GetRawText() != data.GetProperty("spaceId").GetRawText() ||
                row.GetProperty("isTemporary").GetBoolean() != conversation.IsTemporary || row.GetProperty("isArchived").GetBoolean() != conversation.IsArchived)
                throw new InvalidDataException("The actual conversation selection metadata differs.");
        }
        else if (result is ChatMessage message)
        {
            Validate(message);
            if (row.GetProperty("conversationId").GetGuid() != message.ConversationId || row.GetProperty("isCompacted").GetBoolean() != message.IsCompacted)
                throw new InvalidDataException("The actual message linkage/compaction differs.");
        }
        else if (result is ConversationContextEntry entry)
        {
            Validate(entry);
            if (row.GetProperty("conversationId").GetGuid() != entry.ConversationId || row.GetProperty("kind").GetInt32() != (int)entry.Kind)
                throw new InvalidDataException("The actual context linkage/kind differs.");
        }
        return result;
    }
    private async Task<BrowserAuthenticatedStorageReply> Call(string action, object args, CancellationToken token)
    {
        var observed = await transport.InvokeAuthenticatedAsync(actors, action, JsonSerializer.SerializeToElement(args, WireJson), token);
        var reply = observed.Reply;
        if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new IOException("The actual canonical conversation storage reply is malformed; no retry was dispatched.");
        if (!ok.GetBoolean())
        {
            var error = reply.GetProperty("error"); var code = error.GetProperty("code").GetString();
            if (code == "ConversationRevisionConflict") throw new BrowserConversationRevisionConflictException(
                error.GetProperty("id").GetGuid(), error.GetProperty("expectedRevision").GetString()!, error.GetProperty("actualRevision").GetString()!);
            throw new IOException($"The actual canonical conversation storage refused: {code}.");
        }
        return observed;
    }
    private static IReadOnlyList<T> Rows<T>(BrowserAuthenticatedStorageReply actual, string entity) =>
        actual.Reply.GetProperty("value").EnumerateArray().Select(row => Decode<T>(row, entity, actual.Actor)).ToArray();
    private async Task<IReadOnlyList<Conversation>> List(string action, object args, CancellationToken token) =>
        Rows<Conversation>(await Call(action, args, token), "conversation");
    public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) =>
        List("Conversation.Recent", new { mode, limit }, token);
    public Task<IReadOnlyList<Conversation>> GetArchivedAsync(HavenMode? mode, int limit, CancellationToken token) =>
        List("Conversation.Archived", new { mode, limit }, token);
    public Task<IReadOnlyList<Conversation>> GetBySpaceAsync(Guid spaceId, int limit, CancellationToken token)
    { RequireId(spaceId); return List("Conversation.BySpace", new { spaceId, limit = Math.Max(0, limit) }, token); }
    public Task<IReadOnlyList<Conversation>> GetRecentInScopeAsync(ConversationScope scope, int limit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var kind = scope.Kind switch
        {
            ConversationScopeKind.GeneralChat or ConversationScopeKind.ChatGroup => ConversationKind.Chat,
            ConversationScopeKind.StudyQuickChat => ConversationKind.QuickChat,
            ConversationScopeKind.StudyLesson => ConversationKind.LessonChat,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        return List("Conversation.Recent", new { mode = scope.Mode, limit = Math.Max(0, limit),
            scope = new { mode = scope.Mode, kind, containerId = scope.ContainerId, lessonId = scope.LessonId } }, token);
    }
    internal async Task<(Conversation? Conversation, AuthenticatedResourceActor Actor)> ReadOriginalConversationAsync(Guid id, CancellationToken token)
    {
        RequireId(id); var actual = await Call("Conversation.Get", new { id }, token); var row = actual.Reply.GetProperty("value");
        var conversation = row.ValueKind == JsonValueKind.Null ? null : Decode<Conversation>(row, "conversation", actual.Actor);
        if (conversation is not null && conversation.Id != id) throw new InvalidDataException("A different original context was returned.");
        return (conversation, actual.Actor);
    }
    public async Task<Conversation?> GetAsync(Guid id, CancellationToken token) => (await ReadOriginalConversationAsync(id, token)).Conversation;
    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token)
    { RequireId(id); return Rows<ChatMessage>(await Call("Conversation.Messages", new { conversationId = id, contextOnly = false }, token), "message"); }
    public async Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(Guid id, CancellationToken token)
    { RequireId(id); return Rows<ChatMessage>(await Call("Conversation.Messages", new { conversationId = id, contextOnly = true }, token), "message"); }
    public async Task<IReadOnlyList<ConversationContextEntry>> GetContextEntriesAsync(Guid id, CancellationToken token)
    { RequireId(id); return Rows<ConversationContextEntry>(await Call("Conversation.Context", new { conversationId = id }, token), "context"); }
    private async Task<BrowserAuthenticatedStorageReply> Write(string action, object args, CancellationToken token)
    {
        var actual = await Call(action, args, token);
        if (!actual.Reply.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True)
            throw new IOException("No actual complete IndexedDB mutation acknowledgement was received.");
        return actual;
    }
    private async Task<(Conversation Conversation, AuthenticatedResourceActor Actor)> PutConversation(Conversation row, bool insertOnly, CancellationToken token)
    {
        Validate(row);
        var actual = await Write("Conversation.Upsert", new { row = Encode(row, "conversation"), expectedRevision = insertOnly ? "0" : null }, token);
        var returned = actual.Reply.GetProperty("value").GetProperty("rows").EnumerateArray().ToArray();
        if (returned.Length != 1) throw new InvalidDataException("The actual conversation write supplied no unique canonical row.");
        var stored = Decode<Conversation>(returned[0], "conversation", actual.Actor);
        if (stored != (row with { CreatedAt = stored.CreatedAt })) throw new InvalidDataException("The actual written canonical conversation differs.");
        return (stored, actual.Actor);
    }
    public async Task UpsertConversationAsync(Conversation row, CancellationToken token) => _ = await PutConversation(row, false, token);
    internal Task<(Conversation Conversation, AuthenticatedResourceActor Actor)> InsertOriginalTaskConversationAsync(Conversation row, CancellationToken token) => PutConversation(row, true, token);
    public async Task AddMessageAsync(ChatMessage row, CancellationToken token)
    {
        Validate(row); var actual = await Write("Conversation.PutMessage", new { row = Encode(row, "message") }, token);
        var stored = actual.Reply.GetProperty("value").GetProperty("rows");
        if (stored.GetArrayLength() != 1 || Decode<ChatMessage>(stored[0], "message", actual.Actor) != row)
            throw new InvalidDataException("The actual message write differs.");
    }
    public async Task AddContextEntryAsync(ConversationContextEntry row, CancellationToken token)
    {
        Validate(row); var actual = await Write("Conversation.PutContext", new { row = Encode(row, "context") }, token);
        var stored = actual.Reply.GetProperty("value").GetProperty("rows");
        if (stored.GetArrayLength() != 1 || Decode<ConversationContextEntry>(stored[0], "context", actual.Actor) != row)
            throw new InvalidDataException("The actual context write differs.");
    }
    public async Task DetachSpaceAsync(Guid spaceId, CancellationToken token)
    { RequireId(spaceId); _ = await Write("Conversation.DetachSpace", new { spaceId }, token); }
    public async Task DeleteMessageAsync(Guid conversationId, Guid id, CancellationToken token)
    { RequireId(conversationId); RequireId(id); _ = await Write("Conversation.DeleteMessage", new { conversationId, id }, token); }
    public async Task MarkMessagesCompactedAsync(Guid conversationId, IReadOnlyCollection<Guid> messageIds, CancellationToken token)
    { RequireId(conversationId); foreach (var id in messageIds) RequireId(id); _ = await Write("Conversation.CompactMessages", new { conversationId, messageIds }, token); }
    public async Task<bool> DeleteContextEntryAsync(Guid conversationId, Guid id, CancellationToken token)
    {
        RequireId(conversationId); RequireId(id);
        var actual = await Write("Conversation.DeleteContext", new { conversationId, id }, token);
        return actual.Reply.GetProperty("value").GetProperty("removed").GetInt32() == 1;
    }
    public async Task DeleteConversationAsync(Guid id, CancellationToken token)
    { RequireId(id); _ = await Write("Conversation.Delete", new { id }, token); }
}

/// <summary>Actual platform CAS refusal, never a instruction to replay a mutation.</summary>
public sealed class BrowserConversationRevisionConflictException(Guid id, string expectedRevision, string actualRevision)
    : InvalidOperationException("The actual browser conversation revision changed; inspect durable state before retrying.")
{
    public Guid Id { get; } = id;
    public string ExpectedRevision { get; } = expectedRevision;
    public string ActualRevision { get; } = actualRevision;
}
