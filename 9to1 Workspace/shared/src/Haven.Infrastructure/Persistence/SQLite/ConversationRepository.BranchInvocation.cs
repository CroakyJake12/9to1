using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository
{
    private ISqliteConnectionFactory BranchInvocationFactory => factory;
    private sealed record AdmittedBranchInput(Guid BranchId, ChatMessage Message, Guid TurnId);
    private sealed record BranchMessageWriteReceipt(Guid BranchId, Guid? TurnId);

    public IConversationBranchInvocation BindCurrentInvocation(Guid conversationId)
    {
        if (conversationId == Guid.Empty) throw new ArgumentException("A conversation identity is required.", nameof(conversationId));
        return new BranchInvocation(this, conversationId, null);
    }

    public IConversationBranchInvocation BindInvocation(Guid conversationId, Guid branchId)
    {
        if (conversationId == Guid.Empty) throw new ArgumentException("A conversation identity is required.", nameof(conversationId));
        if (branchId == Guid.Empty) throw new ArgumentException("A branch identity is required.", nameof(branchId));
        return new BranchInvocation(this, conversationId, branchId);
    }

    private static async Task<Guid> AdmitExistingTurnAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid branchId, ChatMessage message, CancellationToken cancellationToken)
    {
        Guid? turnId = null;
        Guid? previousAssistant = null;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT id,assistant_message_id FROM conversation_turns
                 WHERE conversation_id=$conversationId AND branch_id=$branchId AND user_message_id=$userMessageId LIMIT 2;
                """;
            find.Parameters.AddWithValue("$conversationId", message.ConversationId.ToString());
            find.Parameters.AddWithValue("$branchId", branchId.ToString());
            find.Parameters.AddWithValue("$userMessageId", message.Id.ToString());
            await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                turnId = Guid.Parse(reader.GetString(0));
                previousAssistant = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1));
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("The original user input has ambiguous turn ownership.");
            }
        }
        // Regeneration Here removes its completed turn while retaining the user message;
        // a fork instead copies that turn with an assistant that is not mapped to the fork.
        // Repair only this verified input, preserving every message and other branch's turn.
        if (turnId is null)
            return await InsertUserTurnAsync(connection, transaction, branchId, message, cancellationToken).ConfigureAwait(false);
        if (previousAssistant is { } previous)
        {
            await using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = """
                UPDATE conversation_turns SET assistant_message_id=NULL
                 WHERE id=$turnId AND assistant_message_id=$previous
                   AND NOT EXISTS(SELECT 1 FROM conversation_branch_messages WHERE branch_id=$branchId AND message_id=$previous);
                """;
            clear.Parameters.AddWithValue("$turnId", turnId.Value.ToString());
            clear.Parameters.AddWithValue("$previous", previous.ToString());
            clear.Parameters.AddWithValue("$branchId", branchId.ToString());
            if (await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The original input still has an active response on this branch.");
        }
        return turnId.Value;
    }

    private sealed class BranchInvocation(ConversationRepository owner, Guid conversationId, Guid? requestedBranchId) : IConversationBranchInvocation, IConversationBranchPromptContextSource
    {
        private readonly SemaphoreSlim _operations = new(1, 1);
        private int _closed;
        private AdmittedBranchInput? _input;
        public Guid ConversationId { get; } = conversationId;
        public Guid BranchId => Volatile.Read(ref _input)?.BranchId ?? requestedBranchId ?? throw new InvalidOperationException("The current branch is not known until the original input commits.");

        public async Task AdmitExistingUserInputAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
                if (_input is not null || requestedBranchId is null || message.ConversationId != ConversationId || message.Role != MessageRole.User)
                    throw new InvalidOperationException("The original user input cannot be admitted for this invocation.");
                await ConversationProductionSchema.EnsureAsync(owner.BranchInvocationFactory, cancellationToken).ConfigureAwait(false);
                await using var connection = await owner.BranchInvocationFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var current = await GetCurrentBranchIdAsync(connection, transaction, ConversationId, cancellationToken).ConfigureAwait(false);
                if (current != BranchId) throw new InvalidOperationException("The original regeneration branch is no longer current.");
                var history = await ReadBranchMessagesAsync(connection, transaction, ConversationId, BranchId, true, cancellationToken).ConfigureAwait(false);
                if (history.LastOrDefault(item => item.Role == MessageRole.User) != message)
                    throw new InvalidOperationException("The original regeneration input no longer matches the canonical branch input.");
                // A mapped response is still a completed turn, even if an old database lacks
                // its turn row. Only an explicitly unmapped regeneration may be admitted.
                if (history.SkipWhile(item => item.Id != message.Id).Skip(1).Any(item => item.Role == MessageRole.Assistant))
                    throw new InvalidOperationException("The original input still has a response on this branch.");
                var turnId = await AdmitExistingTurnAsync(connection, transaction, BranchId, message, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _input, new(BranchId, message, turnId));
            }
            finally { _operations.Release(); }
        }

        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) =>
            AddMessageCoreAsync(message, [], cancellationToken);

        public Task AddUserMessageWithAttachmentsAsync(ChatMessage message, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(attachmentIds);
            if (message.Role != MessageRole.User)
                throw new InvalidOperationException("Only the original user input can claim pending attachments.");
            return AddMessageCoreAsync(message, attachmentIds.ToArray(), cancellationToken);
        }

        private async Task AddMessageCoreAsync(ChatMessage message, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
                if (message.ConversationId != ConversationId) throw new InvalidOperationException("The message belongs to another conversation.");
                if (_input is null && message.Role != MessageRole.User)
                    throw new InvalidOperationException("The original user input must be admitted before response persistence.");
                if (_input is not null && message.Role == MessageRole.User)
                    throw new InvalidOperationException("This invocation has already admitted its original user input.");
                var receipt = await owner.AddMessageCoreAsync(message, _input?.BranchId ?? requestedBranchId, requireCurrentBranch: _input is null,
                    cancellationToken, attachmentIds, createOnly: true, originalInput: _input).ConfigureAwait(false);
                if (_input is null)
                {
                    Volatile.Write(ref _input, new(receipt.BranchId, message,
                        receipt.TurnId ?? throw new InvalidOperationException("The committed input has no turn identity.")));
                }
            }
            finally { _operations.Release(); }
        }

        public async Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(CancellationToken cancellationToken)
        {
            await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
                if (_input is null) throw new InvalidOperationException("The original branch invocation has not admitted user input.");
                return await owner.ReadMessagesAsync(ConversationId, includeCompacted: false, cancellationToken, BranchId).ConfigureAwait(false);
            }
            finally { _operations.Release(); }
        }

        public async Task<ConversationPromptContext> GetPromptContextAsync(CancellationToken cancellationToken)
        {
            await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
                if (_input is null) throw new InvalidOperationException("The original branch invocation has not admitted user input.");
                return await owner.ReadPromptContextAsync(ConversationId, BranchId, cancellationToken).ConfigureAwait(false);
            }
            finally { _operations.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _closed, 1);
            await _operations.WaitAsync().ConfigureAwait(false);
            _operations.Release();
        }
    }
}
