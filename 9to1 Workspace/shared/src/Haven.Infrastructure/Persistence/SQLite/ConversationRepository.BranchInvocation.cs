using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository
{
    private ISqliteConnectionFactory BranchInvocationFactory => factory;

    public IConversationBranchInvocation BindInvocation(Guid conversationId, Guid branchId)
    {
        if (conversationId == Guid.Empty) throw new ArgumentException("A conversation identity is required.", nameof(conversationId));
        if (branchId == Guid.Empty) throw new ArgumentException("A branch identity is required.", nameof(branchId));
        return new BranchInvocation(this, conversationId, branchId);
    }

    private sealed class BranchInvocation(ConversationRepository owner, Guid conversationId, Guid branchId) : IConversationBranchInvocation
    {
        private readonly SemaphoreSlim _operations = new(1, 1);
        private int _closed;
        private bool _admitted;
        public Guid ConversationId { get; } = conversationId;
        public Guid BranchId { get; } = branchId;

        public async Task AdmitExistingUserInputAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);
            await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
                if (_admitted || message.ConversationId != ConversationId || message.Role != MessageRole.User)
                    throw new InvalidOperationException("The original user input cannot be admitted for this invocation.");
                await ConversationProductionSchema.EnsureAsync(owner.BranchInvocationFactory, cancellationToken).ConfigureAwait(false);
                await using var connection = await owner.BranchInvocationFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var current = await GetCurrentBranchIdAsync(connection, transaction, ConversationId, cancellationToken).ConfigureAwait(false);
                if (current != BranchId) throw new InvalidOperationException("The original regeneration branch is no longer current.");
                var history = await ReadBranchMessagesAsync(connection, transaction, ConversationId, BranchId, true, cancellationToken).ConfigureAwait(false);
                if (history.LastOrDefault(item => item.Role == MessageRole.User) != message)
                    throw new InvalidOperationException("The original regeneration input no longer matches the canonical branch input.");
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _admitted = true;
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
                if (!_admitted && message.Role != MessageRole.User)
                    throw new InvalidOperationException("The original user input must be admitted before response persistence.");
                if (_admitted && message.Role == MessageRole.User)
                    throw new InvalidOperationException("This invocation has already admitted its original user input.");
                await owner.AddMessageCoreAsync(message, BranchId, requireCurrentBranch: !_admitted, cancellationToken, attachmentIds).ConfigureAwait(false);
                _admitted = true;
            }
            finally { _operations.Release(); }
        }

        public async Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(CancellationToken cancellationToken)
        {
            await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
                if (!_admitted) throw new InvalidOperationException("The original branch invocation has not admitted user input.");
                return await owner.ReadMessagesAsync(ConversationId, includeCompacted: false, cancellationToken, BranchId).ConfigureAwait(false);
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
