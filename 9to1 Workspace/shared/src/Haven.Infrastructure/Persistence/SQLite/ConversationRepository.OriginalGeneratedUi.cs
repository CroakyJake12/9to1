using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Haven.Core;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository
{
    private readonly ConditionalWeakTable<OriginalGeneratedMessage, object> _originalGeneratedMessages = new();
    public bool HasOriginalGeneratedUiFactory(ISqliteConnectionFactory same) => ReferenceEquals(factory, same);
    public sealed class OriginalGeneratedMessage
    {
        internal OriginalGeneratedMessage(Guid conversation, Guid message, Guid? branch, string content, ResourceStoreIdentity identity,
            (HavenMode Mode, ConversationKind Kind, Guid? Container, Guid? Space, Guid? Lesson, bool Temporary) scope)
        { ConversationId = conversation; MessageId = message; BranchId = branch; Content = content; OriginalStoreIdentity = identity; Scope = scope; }
        internal readonly (HavenMode Mode, ConversationKind Kind, Guid? Container, Guid? Space, Guid? Lesson, bool Temporary) Scope;
        public Guid ConversationId { get; }
        public Guid MessageId { get; }
        public Guid? BranchId { get; }
        public string Content { get; }
        public ResourceStoreIdentity OriginalStoreIdentity { get; }
    }
    public bool IsIssuedOriginalGeneratedMessage(OriginalGeneratedMessage same) => same is not null && _originalGeneratedMessages.TryGetValue(same, out _);
    public bool MatchesOriginalGeneratedUiConversationScope(OriginalGeneratedMessage same, Conversation expected) =>
        IsIssuedOriginalGeneratedMessage(same) && same.ConversationId == expected.Id && same.Scope.Mode == expected.Mode &&
        same.Scope.Kind == expected.Kind && same.Scope.Container == expected.ContainerId && same.Scope.Space == expected.SpaceId &&
        same.Scope.Lesson == expected.LessonId && same.Scope.Temporary == expected.IsTemporary;
    public async Task<OriginalGeneratedMessage> ReadOriginalGeneratedMessageWithinSourceAsync(
        CanonicalSqliteOriginalStoreOwner sameStore, CanonicalSqliteOriginalStoreLease sameLease,
        Guid conversationId, Guid messageId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(this, scope, retain);
        source.Run(() =>
        {
            if (!sameStore.HasOriginalDatabase(factory) || !sameStore.IsIssuedOriginalLease(sameLease) || conversationId == Guid.Empty || messageId == Guid.Empty)
                throw new UnauthorizedAccessException("Use the SAME configured repository/database and its exact original native lease.");
        });
        // The maintained current-branch and nearest-version projection is reused
        // under the actual protected connection. No schema Ensure or ordinary Open.
        var conversations = await Query(c =>
        {
            c.CommandText = "SELECT mode,kind,container_id,space_id,lesson_id,is_temporary FROM conversations WHERE id=$conversation LIMIT 2;";
            c.Parameters.AddWithValue("$conversation", conversationId.ToString("D"));
        }, r => ((HavenMode)r.GetInt32(0), (ConversationKind)r.GetInt32(1), r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
            r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)), r.IsDBNull(4) ? (Guid?)null : Guid.Parse(r.GetString(4)), r.GetInt64(5) != 0), 1).ConfigureAwait(false);
        if (conversations.Count != 1) throw new InvalidDataException("The exact canonical conversation is absent under the protected lease.");
        var branches = await Query(c =>
        {
            c.CommandText = "SELECT id FROM conversation_branches WHERE conversation_id=$conversation AND is_current=1 LIMIT 2;";
            c.Parameters.AddWithValue("$conversation", conversationId.ToString("D"));
        }, r => r.GetString(0), 1).ConfigureAwait(false);
        Guid? branch = branches.Count == 0 ? null : Guid.Parse(branches[0]);
        var messages = await Query(c =>
        {
            c.CommandText = branch is null
                ? "SELECT content FROM messages WHERE id=$message AND conversation_id=$conversation AND is_compacted=0;"
                : """
                    WITH RECURSIVE ancestry(id,depth) AS (
                        SELECT $branch,0
                        UNION ALL
                        SELECT b.parent_branch_id,ancestry.depth+1 FROM conversation_branches b
                          JOIN ancestry ON b.id=ancestry.id WHERE b.parent_branch_id IS NOT NULL AND ancestry.depth<256
                    )
                    SELECT COALESCE((SELECT v.content FROM message_versions v JOIN ancestry a ON a.id=v.branch_id
                        WHERE v.message_id=m.id AND v.is_current=1 ORDER BY a.depth LIMIT 1),m.content)
                      FROM conversation_branch_messages bm JOIN messages m ON m.id=bm.message_id
                     WHERE bm.branch_id=$branch AND m.id=$message AND m.conversation_id=$conversation AND m.is_compacted=0;
                    """;
            c.Parameters.AddWithValue("$message", messageId.ToString("D")); c.Parameters.AddWithValue("$conversation", conversationId.ToString("D"));
            if (branch is { } id) c.Parameters.AddWithValue("$branch", id.ToString("D"));
        }, r => r.GetString(0), 1).ConfigureAwait(false);
        if (messages.Count != 1 || System.Text.Encoding.UTF8.GetByteCount(messages[0]) > GenerativeUiContractValidator.MaximumJsonBytes * 4)
            throw new InvalidDataException("The exact current projected message is absent or exceeds its returned content bound.");
        if (branch is { } actualBranch)
        {
            // A bounded ancestry cannot silently truncate a corrupt cycle/deep tree.
            var ancestry = await Query(c =>
            {
                c.CommandText = """
                    WITH RECURSIVE ancestry(id,parent,depth,path,cycle) AS (
                        SELECT id,parent_branch_id,0,'|'||id||'|',0 FROM conversation_branches WHERE id=$branch AND conversation_id=$conversation
                        UNION ALL
                        SELECT b.id,b.parent_branch_id,a.depth+1,a.path||b.id||'|',instr(a.path,'|'||b.id||'|')>0
                          FROM conversation_branches b JOIN ancestry a ON b.id=a.parent
                         WHERE a.parent IS NOT NULL AND a.depth<256 AND a.cycle=0 AND b.conversation_id=$conversation
                    ) SELECT id,parent,depth,cycle FROM ancestry;
                    """;
                c.Parameters.AddWithValue("$branch", actualBranch.ToString("D")); c.Parameters.AddWithValue("$conversation", conversationId.ToString("D"));
            }, r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt64(2), r.GetInt64(3)), 257).ConfigureAwait(false);
            if (ancestry.Count == 0 || ancestry.Any(row => row.Item4 != 0 || row.Item3 >= 256 || row.Item2 is not null && !ancestry.Any(parent => parent.Item1 == row.Item2)))
                throw new InvalidDataException("The actual current message ancestry is cyclic, incomplete or exceeds its bounded depth.");
        }
        await source.Read(() => sameLease.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        await source.JoinAllAsync().ConfigureAwait(false);
        return source.Invoke(() =>
        {
            var actual = new OriginalGeneratedMessage(conversationId, messageId, branch, messages[0], sameLease.OriginalIdentity, conversations[0]);
            _originalGeneratedMessages.Add(actual, this); return actual;
        });

        async Task<List<T>> Query<T>(Action<SqliteCommand> configure, Func<SqliteDataReader,T> project, int maximum)
        {
            SqliteCommand? command = null; SqliteDataReader? reader = null; Task<SqliteDataReader>? rawReader = null;
            var errors = new List<Exception>(); var result = new List<T>();
            try
            {
                source.Run(() => sameLease.InvokeOriginalSource(() => command = sameLease.Connection.CreateCommand()));
                source.Run(() => sameLease.InvokeOriginalSource(() => { command!.Transaction = sameLease.Transaction; configure(command); }));
                try { await source.Read(async () =>
                { reader = await sameLease.ReadOriginalSourceAsync(() => rawReader = command!.ExecuteReaderAsync(token)).ConfigureAwait(false); }).ConfigureAwait(false); }
                finally
                {
                    if (rawReader is not null) try { reader = await rawReader.ConfigureAwait(false); }
                        catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, rawReader, cause); }
                }
                while (await source.Read(() => sameLease.ReadOriginalSourceAsync(() => reader!.ReadAsync(token))).ConfigureAwait(false))
                {
                    source.Run(() => sameLease.InvokeOriginalSource(() => result.Add(project(reader!))));
                    if (result.Count > maximum) throw new InvalidDataException("The original message projection exceeded its bounded row count.");
                }
            }
            catch (Exception cause) { errors.Add(cause); }
            var closes = new List<Task>();
            foreach (var actual in new IAsyncDisposable?[] { reader,command })
                if (actual is not null) try
                {
                    Task? close = null;
                    try { source.Run(() => { close = sameLease.CloseOriginalResourceAsync(actual); closes.Add(close); source.Retain(close); }); }
                    catch (Exception cause) { errors.Add(cause); }
                }
                catch (Exception cause) { errors.Add(cause); }
            foreach (var raw in closes) try { await raw.ConfigureAwait(false); }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            CanonicalSqliteOriginalStoreOwner.Throw(errors); return result;
        }
    }
}
