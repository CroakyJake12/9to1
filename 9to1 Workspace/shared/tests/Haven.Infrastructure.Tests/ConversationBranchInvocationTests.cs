using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual canonical SQLite branch persistence, independent of Windows speech hosts.</summary>
public sealed class ConversationBranchInvocationTests
{
    [Fact]
    public async Task Current_invocation_creates_and_pins_a_root_only_with_its_committed_first_input()
    {
        await using var rig = await Rig.CreateAsync(createRoot: false);
        await using var invocation = rig.Repository.BindCurrentInvocation(rig.Conversation.Id);
        Assert.Throws<InvalidOperationException>(() => invocation.BranchId);
        Assert.Empty(await rig.Production.GetBranchesAsync(rig.Conversation.Id, CancellationToken.None));
        var user = rig.Message(MessageRole.User, "first input");
        await invocation.AddMessageAsync(user, CancellationToken.None);
        var branch = Assert.Single(await rig.Production.GetBranchesAsync(rig.Conversation.Id, CancellationToken.None));
        Assert.Equal(branch.Id, invocation.BranchId);
        Assert.Equal(user, Assert.Single(await invocation.GetContextMessagesAsync(CancellationToken.None)));
        var response = rig.Message(MessageRole.Assistant, "first response");
        await invocation.AddMessageAsync(response, CancellationToken.None);
        var turn = Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, branch.Id, CancellationToken.None));
        Assert.Equal(user.Id, turn.UserMessageId);
        Assert.Equal(response.Id, turn.AssistantMessageId);
    }

    [Fact]
    public async Task Current_invocation_selects_the_branch_at_input_commit_and_ignores_later_selection_changes()
    {
        await using var rig = await Rig.CreateAsync();
        var seed = rig.Message(MessageRole.User, "seed");
        await rig.Repository.AddMessageAsync(seed, CancellationToken.None);
        await using var invocation = rig.Repository.BindCurrentInvocation(rig.Conversation.Id);
        var acceptedBranch = await rig.Production.CreateBranchAsync(rig.Conversation.Id, rig.Root.Id, seed.Id,
            "Selected before admission", ConversationBranchReason.Manual, CancellationToken.None);
        var user = rig.Message(MessageRole.User, "accepted on selected branch");
        await invocation.AddMessageAsync(user, CancellationToken.None);
        Assert.Equal(acceptedBranch.Id, invocation.BranchId);
        await rig.Production.SetCurrentBranchAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None);
        var elsewhere = rig.Message(MessageRole.User, "another tab");
        await rig.Repository.AddMessageAsync(elsewhere, CancellationToken.None);
        Assert.Equal([seed.Id, user.Id], (await invocation.GetContextMessagesAsync(CancellationToken.None)).Select(item => item.Id));
        var response = rig.Message(MessageRole.Assistant, "original response");
        await invocation.AddMessageAsync(response, CancellationToken.None);
        Assert.DoesNotContain(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None), item => item.Id == response.Id);
        var turns = await rig.Production.GetTurnsAsync(rig.Conversation.Id, acceptedBranch.Id, CancellationToken.None);
        Assert.Equal(response.Id, Assert.Single(turns, item => item.UserMessageId == user.Id).AssistantMessageId);
        Assert.Null(Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None),
            item => item.UserMessageId == elsewhere.Id).AssistantMessageId);
    }

    [Fact]
    public async Task A_later_user_on_the_same_branch_cannot_steal_the_original_response_turn()
    {
        await using var rig = await Rig.CreateAsync();
        await using var invocation = rig.Repository.BindCurrentInvocation(rig.Conversation.Id);
        var accepted = rig.Message(MessageRole.User, "original input");
        await invocation.AddMessageAsync(accepted, CancellationToken.None);
        var next = rig.Message(MessageRole.User, "concurrent input");
        await rig.Repository.AddMessageAsync(next, CancellationToken.None);
        var response = rig.Message(MessageRole.Assistant, "original response");
        await invocation.AddMessageAsync(response, CancellationToken.None);
        var turns = await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None);
        Assert.Equal(response.Id, Assert.Single(turns, item => item.UserMessageId == accepted.Id).AssistantMessageId);
        Assert.Null(Assert.Single(turns, item => item.UserMessageId == next.Id).AssistantMessageId);
        Assert.Equal([accepted.Id, next.Id, response.Id],
            (await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None)).Select(item => item.Id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Regeneration_restores_only_the_exact_unanswered_turn_after_here_or_fork(bool fork, bool safeProduction)
    {
        await using var rig = await Rig.CreateAsync();
        var user = rig.Message(MessageRole.User, "original input");
        var previous = rig.Message(MessageRole.Assistant, "previous completed response");
        await rig.Repository.AddMessageAsync(user, CancellationToken.None);
        await rig.Repository.AddMessageAsync(previous, CancellationToken.None);
        var originalTurn = Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None));
        IConversationProductionRepository production = safeProduction
            ? new SafeConversationProductionRepository(rig.Factory, rig.Repository, rig.Production)
            : rig.Production;
        var versioning = new ConversationVersioningService(rig.Repository, production);
        var branch = await versioning.PrepareRegenerationAsync(rig.Conversation.Id, previous.Id, true,
            fork ? ResponseRegenerationMode.NewBranch : ResponseRegenerationMode.Here, CancellationToken.None);
        await using var invocation = rig.Repository.BindInvocation(rig.Conversation.Id, branch.Id);
        await invocation.AdmitExistingUserInputAsync(user, CancellationToken.None);
        var admitted = Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, branch.Id, CancellationToken.None));
        Assert.Equal(user.Id, admitted.UserMessageId);
        Assert.Null(admitted.AssistantMessageId);
        var response = rig.Message(MessageRole.Assistant, "regenerated response");
        await invocation.AddMessageAsync(response, CancellationToken.None);
        var completed = Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, branch.Id, CancellationToken.None));
        Assert.Equal(admitted.Id, completed.Id);
        Assert.Equal(response.Id, completed.AssistantMessageId);
        Assert.Equal([user.Id, response.Id], (await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None)).Select(item => item.Id));
        Assert.True(await rig.ContainsStoredMessageAsync(previous.Id));
        if (fork)
            Assert.Equal(originalTurn, Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_mapped_completed_response_is_never_cleared_even_if_its_turn_row_is_missing(bool removeTurn)
    {
        await using var rig = await Rig.CreateAsync();
        var user = rig.Message(MessageRole.User, "completed input");
        var response = rig.Message(MessageRole.Assistant, "completed response");
        await rig.Repository.AddMessageAsync(user, CancellationToken.None);
        await rig.Repository.AddMessageAsync(response, CancellationToken.None);
        var before = await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None);
        if (removeTurn)
        {
            await using var connection = await rig.Factory.OpenAsync(CancellationToken.None);
            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM conversation_turns WHERE branch_id=$branch;";
            delete.Parameters.AddWithValue("$branch", rig.Root.Id.ToString());
            Assert.Equal(1, await delete.ExecuteNonQueryAsync());
        }
        await using var invocation = rig.Repository.BindInvocation(rig.Conversation.Id, rig.Root.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AdmitExistingUserInputAsync(user, CancellationToken.None));
        Assert.Equal([user.Id, response.Id], (await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None)).Select(item => item.Id));
        var after = await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None);
        if (removeTurn) Assert.Empty(after);
        else Assert.Equal(before, after);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.GetContextMessagesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Editing_the_accepted_input_refuses_and_rolls_back_the_old_response()
    {
        await using var rig = await Rig.CreateAsync();
        await using var invocation = rig.Repository.BindCurrentInvocation(rig.Conversation.Id);
        var user = rig.Message(MessageRole.User, "accepted input");
        await invocation.AddMessageAsync(user, CancellationToken.None);
        var versioning = new ConversationVersioningService(rig.Repository, rig.Production);
        await versioning.EditUserMessageAsync(rig.Conversation.Id, user.Id, "edited meanwhile", MessageEditMode.OverwriteCurrentBranch, CancellationToken.None);
        var stale = rig.Message(MessageRole.Assistant, "response to old input");
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AddMessageAsync(stale, CancellationToken.None));
        Assert.False(await rig.ContainsStoredMessageAsync(stale.Id));
        Assert.Equal("edited meanwhile", Assert.Single(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None)).Content);
        Assert.Null(Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None)).AssistantMessageId);
    }

    [Fact]
    public async Task A_completed_bound_turn_cannot_be_replaced_by_another_response()
    {
        await using var rig = await Rig.CreateAsync();
        await using var invocation = rig.Repository.BindCurrentInvocation(rig.Conversation.Id);
        var user = rig.Message(MessageRole.User, "input");
        var first = rig.Message(MessageRole.Assistant, "completed");
        await invocation.AddMessageAsync(user, CancellationToken.None);
        await invocation.AddMessageAsync(first, CancellationToken.None);
        var second = rig.Message(MessageRole.Assistant, "must not replace");
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AddMessageAsync(second, CancellationToken.None));
        Assert.False(await rig.ContainsStoredMessageAsync(second.Id));
        Assert.Equal(first.Id, Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None)).AssistantMessageId);
    }

    [Fact]
    public async Task A_current_invocation_cannot_replace_an_existing_user_or_claim_its_branch()
    {
        await using var rig = await Rig.CreateAsync();
        var user = rig.Message(MessageRole.User, "original stored input");
        await rig.Repository.AddMessageAsync(user, CancellationToken.None);
        await using var invocation = rig.Repository.BindCurrentInvocation(rig.Conversation.Id);
        await Assert.ThrowsAsync<SqliteException>(() => invocation.AddMessageAsync(user with { Content = "replacement" }, CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => invocation.BranchId);
        Assert.Equal(user, Assert.Single(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None)));
        Assert.Single(await rig.Production.GetTurnsAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Original_user_and_all_pending_attachments_are_committed_together_on_the_bound_branch()
    {
        await using var rig = await Rig.CreateAsync();
        var first = await rig.AttachmentAsync(rig.Root.Id);
        var unassigned = await rig.AttachmentAsync(null);
        await using var invocation = rig.Repository.BindInvocation(rig.Conversation.Id, rig.Root.Id);
        var user = rig.Message(MessageRole.User, "attached request");
        await invocation.AddUserMessageWithAttachmentsAsync(user, [first.Id, unassigned.Id], CancellationToken.None);
        var actual = await rig.Production.GetAttachmentsAsync(rig.Conversation.Id, user.Id, CancellationToken.None);
        Assert.Equal(2, actual.Count);
        Assert.All(actual, item => { Assert.Equal(user.Id, item.MessageId); Assert.Equal(rig.Root.Id, item.BranchId); });
        Assert.Equal(user, Assert.Single(await invocation.GetContextMessagesAsync(CancellationToken.None)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other-branch")]
    [InlineData("already-claimed")]
    public async Task Attachment_ownership_failure_rolls_back_entire_user_write_and_prior_attachment_updates(string reason)
    {
        await using var rig = await Rig.CreateAsync();
        var valid = await rig.AttachmentAsync(rig.Root.Id);
        var invalid = Guid.NewGuid();
        if (reason == "other-branch")
        {
            var other = await rig.Production.CreateBranchAsync(rig.Conversation.Id, rig.Root.Id, null,
                "Other branch", ConversationBranchReason.Manual, CancellationToken.None);
            invalid = (await rig.AttachmentAsync(other.Id)).Id;
            await rig.Production.SetCurrentBranchAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None);
        }
        else if (reason == "already-claimed")
        {
            var earlier = rig.Message(MessageRole.User, "earlier");
            await rig.Repository.AddMessageAsync(earlier, CancellationToken.None);
            invalid = (await rig.AttachmentAsync(rig.Root.Id, earlier.Id)).Id;
        }
        await using var invocation = rig.Repository.BindInvocation(rig.Conversation.Id, rig.Root.Id);
        var refused = rig.Message(MessageRole.User, "must not persist");
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AddUserMessageWithAttachmentsAsync(refused,
            [valid.Id, invalid], CancellationToken.None));
        Assert.DoesNotContain(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None), item => item.Id == refused.Id);
        Assert.Null((await rig.Production.GetAttachmentsAsync(rig.Conversation.Id, null, CancellationToken.None)).Single(item => item.Id == valid.Id).MessageId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.GetContextMessagesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Another_hosts_branch_switch_does_not_retarget_accepted_history_or_response()
    {
        await using var rig = await Rig.CreateAsync();
        await using var invocation = rig.Repository.BindInvocation(rig.Conversation.Id, rig.Root.Id);
        var user = rig.Message(MessageRole.User, "original request");
        await invocation.AddMessageAsync(user, CancellationToken.None);
        var other = await rig.Production.CreateBranchAsync(rig.Conversation.Id, rig.Root.Id, user.Id,
            "Other host", ConversationBranchReason.Manual, CancellationToken.None);
        await rig.Production.SetCurrentBranchAsync(rig.Conversation.Id, other.Id, CancellationToken.None);
        var elsewhere = rig.Message(MessageRole.User, "other branch request");
        await rig.Repository.AddMessageAsync(elsewhere, CancellationToken.None);

        Assert.Equal([user.Id], (await invocation.GetContextMessagesAsync(CancellationToken.None)).Select(item => item.Id));
        var response = rig.Message(MessageRole.Assistant, "original answer");
        await invocation.AddMessageAsync(response, CancellationToken.None);
        Assert.DoesNotContain(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None), item => item.Id == response.Id);
        await rig.Production.SetCurrentBranchAsync(rig.Conversation.Id, rig.Root.Id, CancellationToken.None);
        Assert.Equal([user.Id, response.Id], (await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None)).Select(item => item.Id));
    }

    [Fact]
    public async Task Branch_changed_before_first_input_is_refused_without_any_new_message()
    {
        await using var rig = await Rig.CreateAsync();
        var seed = rig.Message(MessageRole.User, "seed");
        await rig.Repository.AddMessageAsync(seed, CancellationToken.None);
        await using var invocation = rig.Repository.BindInvocation(rig.Conversation.Id, rig.Root.Id);
        var other = await rig.Production.CreateBranchAsync(rig.Conversation.Id, rig.Root.Id, seed.Id,
            "Other", ConversationBranchReason.Manual, CancellationToken.None);
        await rig.Production.SetCurrentBranchAsync(rig.Conversation.Id, other.Id, CancellationToken.None);
        var rejected = rig.Message(MessageRole.User, "must not write");
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AddMessageAsync(rejected, CancellationToken.None));
        Assert.DoesNotContain(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None), item => item.Id == rejected.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.GetContextMessagesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Regeneration_admits_exact_edited_branch_input_without_inserting_another_user()
    {
        await using var rig = await Rig.CreateAsync();
        var user = rig.Message(MessageRole.User, "original");
        await rig.Repository.AddMessageAsync(user, CancellationToken.None);
        var versioning = new ConversationVersioningService(rig.Repository, rig.Production);
        var edited = await versioning.EditUserMessageAsync(rig.Conversation.Id, user.Id, "edited", MessageEditMode.NewBranch, CancellationToken.None);
        await using var invocation = rig.Repository.BindInvocation(rig.Conversation.Id, edited.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AdmitExistingUserInputAsync(user, CancellationToken.None));
        var canonical = Assert.Single(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None));
        Assert.Equal("edited", canonical.Content);
        await invocation.AdmitExistingUserInputAsync(canonical, CancellationToken.None);
        await invocation.AddMessageAsync(rig.Message(MessageRole.Assistant, "regenerated"), CancellationToken.None);
        var messages = await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None);
        Assert.Single(messages, item => item.Role == MessageRole.User);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task Invocation_refuses_cross_conversation_duplicate_input_and_reuse_after_close()
    {
        await using var rig = await Rig.CreateAsync();
        var invocation = rig.Repository.BindInvocation(rig.Conversation.Id, rig.Root.Id);
        var user = rig.Message(MessageRole.User, "request");
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AddMessageAsync(user with { ConversationId = Guid.NewGuid() }, CancellationToken.None));
        await invocation.AddMessageAsync(user, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AddMessageAsync(rig.Message(MessageRole.User, "second input"), CancellationToken.None));
        await invocation.DisposeAsync();
        await invocation.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => invocation.AddMessageAsync(rig.Message(MessageRole.Assistant, "late"), CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => invocation.GetContextMessagesAsync(CancellationToken.None));
        Assert.Single(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_joins_an_admitted_database_operation_and_refuses_late_work(bool currentBranch)
    {
        await using var rig = await Rig.CreateAsync();
        var gate = new HeldFactory(rig.Factory);
        var repository = new ConversationRepository(gate);
        var invocation = currentBranch ? repository.BindCurrentInvocation(rig.Conversation.Id)
            : repository.BindInvocation(rig.Conversation.Id, rig.Root.Id);
        var write = invocation.AddMessageAsync(rig.Message(MessageRole.User, "held"), CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = invocation.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        gate.Release.TrySetResult();
        await Task.WhenAll(write, close).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => invocation.GetContextMessagesAsync(CancellationToken.None));
        Assert.Single(await rig.Repository.GetMessagesAsync(rig.Conversation.Id, CancellationToken.None));
    }

    private sealed class HeldFactory(ISqliteConnectionFactory original) : ISqliteConnectionFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await original.OpenAsync(cancellationToken);
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public TestFactory Factory { get; } = new();
        public ConversationRepository Repository { get; }
        public ConversationProductionRepository Production { get; }
        public Conversation Conversation { get; }
        public ConversationBranch Root { get; private set; } = null!;
        private Rig()
        {
            Repository = new(Factory); Production = new(Factory, Repository);
            var now = DateTimeOffset.UtcNow;
            Conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Branch contract", null, null, false, false, now, now);
        }
        public static async Task<Rig> CreateAsync(bool createRoot = true)
        {
            var rig = new Rig();
            await rig.Factory.InitializeAsync();
            await rig.Repository.UpsertConversationAsync(rig.Conversation, CancellationToken.None);
            if (createRoot) rig.Root = await rig.Production.EnsureRootBranchAsync(rig.Conversation.Id, CancellationToken.None);
            return rig;
        }
        public ChatMessage Message(MessageRole role, string text) => new(Guid.NewGuid(), Conversation.Id, role, text, role == MessageRole.Assistant ? "Haven" : null, null, null, DateTimeOffset.UtcNow);
        public Task<MessageAttachment> AttachmentAsync(Guid? branch, Guid? message = null)
        {
            var now = DateTimeOffset.UtcNow;
            return Production.UpsertAttachmentAsync(new MessageAttachment(Guid.NewGuid(), Conversation.Id, message, branch,
                "selected.txt", "selected.txt", "text/plain", MessageAttachmentKind.PlainText, 4, "fixture-hash",
                AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted, "text", "{}", now, now), CancellationToken.None);
        }
        public async Task<bool> ContainsStoredMessageAsync(Guid messageId)
        {
            await using var connection = await Factory.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM messages WHERE id=$id;";
            command.Parameters.AddWithValue("$id", messageId.ToString());
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) != 0;
        }
        public ValueTask DisposeAsync() { Factory.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class TestFactory : ISqliteConnectionFactory, IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "haven-branch-" + Guid.NewGuid().ToString("N") + ".db");
        public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        {
            SqliteProviderBootstrap.EnsureInitialized();
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, ForeignKeys = true, Pooling = false }.ToString());
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        public async Task InitializeAsync()
        {
            await using var connection = await OpenAsync(CancellationToken.None);
            await using (var ledger = connection.CreateCommand())
            {
                ledger.CommandText = "CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);";
                await ledger.ExecuteNonQueryAsync();
            }
            foreach (var migration in Migrations.All)
            {
                await using var command = connection.CreateCommand(); command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync();
            }
            await ConversationProductionSchema.EnsureAsync(this, CancellationToken.None);
        }
        public void Dispose() => File.Delete(_path);
    }
}
