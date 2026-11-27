using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class ConversationCompactionRepositoryTests
{
    [Fact]
    public async Task Ordinary_compaction_commits_one_summary_and_flags_without_replacing_any_raw_message()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        IConversationCompactionRepository owner = new UsageTrackingConversationRepository(database.Conversations, null!, null!, null!, null!);

        var receipt = await owner.CommitCompactionAsync(conversation.Id, replacement, token);

        Assert.Equal(replacement.Summary, receipt.Summary);
        Assert.Equal(history.Take(4).Select(message => message.Id), receipt.SourceMessageIds);
        var retained = await database.Conversations.GetMessagesAsync(conversation.Id, token);
        Assert.Equal(history, retained.Select(message => message with { IsCompacted = false }).ToArray());
        Assert.All(retained.Take(4), message => Assert.True(message.IsCompacted));
        Assert.Equal(history.Skip(4), await database.Conversations.GetContextMessagesAsync(conversation.Id, token));
        Assert.Equal(replacement.Summary, Assert.Single(await database.Conversations.GetContextEntriesAsync(conversation.Id, token)));
        Assert.Equal(receipt.CompactedAt, (await database.Conversations.GetAsync(conversation.Id, token))!.CompactedAt);
    }

    [Fact]
    public async Task Failure_after_first_inclusion_update_rolls_back_summary_flags_and_timestamp_then_allows_retry()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        await database.ExecuteAsync($"""
            CREATE TRIGGER fail_second_compaction BEFORE UPDATE OF is_compacted ON messages
            WHEN NEW.id='{history[1].Id:D}' BEGIN SELECT RAISE(ABORT,'controlled second message failure'); END;
            """, token);

        await Assert.ThrowsAsync<SqliteException>(() => database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token));

        Assert.Equal(history, await database.Conversations.GetContextMessagesAsync(conversation.Id, token));
        Assert.Equal(history, await database.Conversations.GetMessagesAsync(conversation.Id, token));
        Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
        Assert.Equal(conversation, await database.Conversations.GetAsync(conversation.Id, token));
        await database.ExecuteAsync("DROP TRIGGER fail_second_compaction;", token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        Assert.Single(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
    }

    [Fact]
    public async Task Changed_original_message_refuses_summary_publication_before_any_context_is_excluded()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        var edited = history[0] with { Content = "Newer exact source content" };
        await database.Conversations.AddMessageAsync(edited, token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token));

        Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
        var current = await database.Conversations.GetContextMessagesAsync(conversation.Id, token);
        Assert.Equal(history.Length, current.Count);
        Assert.Equal(edited, current.Single(message => message.Id == edited.Id));
        Assert.All(current, message => Assert.False(message.IsCompacted));
    }

    [Fact]
    public async Task Temporary_save_transfers_owned_summary_and_flagged_history_atomically_through_production_adapter()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var request = TemporaryRequest();
        ITemporaryChatSaveRepository owner = new UsageTrackingConversationRepository(database.Conversations, null!, null!, null!, null!);
        Assert.True(owner.SupportsCompactionSummaries);

        var receipt = await owner.SaveTemporaryChatAsync(request, token);

        Assert.False(receipt.Conversation.IsTemporary);
        Assert.Equal(request.Compactions.Select(compaction => compaction.Summary.Id), receipt.CompactionSummaryIds);
        Assert.Equal(request.History, await database.Conversations.GetMessagesAsync(request.Original.Id, token));
        Assert.Equal(request.History.Where(message => !message.IsCompacted),
            await database.Conversations.GetContextMessagesAsync(request.Original.Id, token));
        Assert.Equal(request.Compactions.Single().Summary,
            Assert.Single(await database.Conversations.GetContextEntriesAsync(request.Original.Id, token)));
        Assert.Equal(request.DraftText, (await database.Production.GetDraftAsync(request.Original.Id, receipt.BranchId, token))!.Content);
        Assert.True(request.Original.IsTemporary);
        var context = await database.Conversations.GetPromptContextAsync(request.Original.Id, token);
        Assert.Equal(receipt.BranchId, context.BranchId);
        var replacement = Assert.Single(context.Compactions);
        Assert.Equal(request.Compactions[0].Summary, replacement.Summary);
        Assert.All(replacement.SourceVersions!, source => Assert.NotNull(source.VersionId));
        Assert.Equal(request.History.Where(message => !message.IsCompacted), context.Messages);
    }

    [Fact]
    public async Task Temporary_summary_insert_failure_rolls_back_history_branch_draft_and_conversion()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var request = TemporaryRequest();
        await database.ExecuteAsync("""
            CREATE TRIGGER fail_summary BEFORE INSERT ON conversation_context
            BEGIN SELECT RAISE(ABORT,'controlled summary storage failure'); END;
            """, token);

        await Assert.ThrowsAsync<SqliteException>(() => database.Conversations.SaveTemporaryChatAsync(request, token));

        Assert.Null(await database.Conversations.GetAsync(request.Original.Id, token));
        Assert.Empty(await database.Conversations.GetMessagesAsync(request.Original.Id, token));
        Assert.Empty(await database.Conversations.GetContextEntriesAsync(request.Original.Id, token));
        Assert.Empty(await database.Production.GetBranchesAsync(request.Original.Id, token));
        Assert.True(request.Original.IsTemporary);
        await database.ExecuteAsync("DROP TRIGGER fail_summary;", token);
        await database.Conversations.SaveTemporaryChatAsync(request, token);
        Assert.Single(await database.Conversations.GetContextEntriesAsync(request.Original.Id, token));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    public async Task Temporary_save_refuses_unowned_or_duplicate_replacements_without_writing(string invalid)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var request = TemporaryRequest();
        var replacement = request.Compactions.Single();
        request = request with
        {
            Compactions = invalid switch
            {
                "missing" => [],
                "duplicate" => [replacement, replacement],
                _ => [replacement with { Summary = replacement.Summary with { ConversationId = Guid.NewGuid() } }]
            }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => database.Conversations.SaveTemporaryChatAsync(request, token));

        Assert.Null(await database.Conversations.GetAsync(request.Original.Id, token));
        Assert.Empty(await database.Conversations.GetMessagesAsync(request.Original.Id, token));
        Assert.Empty(await database.Conversations.GetContextEntriesAsync(request.Original.Id, token));
    }

    [Fact]
    public async Task A_foreign_summary_identity_cannot_be_replaced_by_a_Temporary_save()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var request = TemporaryRequest();
        var (other, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(other, history, token);
        var foreign = request.Compactions.Single().Summary with { ConversationId = other.Id, Content = "Foreign retained summary" };
        await database.Conversations.AddContextEntryAsync(foreign, token);

        await Assert.ThrowsAsync<SqliteException>(() => database.Conversations.SaveTemporaryChatAsync(request, token));

        Assert.Null(await database.Conversations.GetAsync(request.Original.Id, token));
        Assert.Empty(await database.Conversations.GetMessagesAsync(request.Original.Id, token));
        Assert.Equal(foreign, Assert.Single(await database.Conversations.GetContextEntriesAsync(other.Id, token)));
        Assert.Equal(history, await database.Conversations.GetContextMessagesAsync(other.Id, token));
    }

    [Fact]
    public async Task Branch_A_only_summary_never_enters_B_and_shared_compacted_rows_fall_back_to_raw_context()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var a = (await database.Production.GetCurrentBranchAsync(conversation.Id, token))!;
        var b = await database.Production.CreateBranchAsync(conversation.Id, a.Id, history[3].Id, "Branch B", ConversationBranchReason.Manual, token);
        var bOnly = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "Only branch B owns this fact", null, null, null, DateTimeOffset.UtcNow);
        await database.Conversations.AddMessageAsync(bOnly, token);
        await database.Production.SetCurrentBranchAsync(conversation.Id, a.Id, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(8).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        var onA = await database.Conversations.GetPromptContextAsync(conversation.Id, token);
        Assert.Equal(replacement.Summary, Assert.Single(onA.Compactions).Summary);
        Assert.Equal(history.Skip(8), onA.Messages);

        await database.Production.SetCurrentBranchAsync(conversation.Id, b.Id, token);
        IConversationPromptContextRepository owner = new UsageTrackingConversationRepository(database.Conversations, null!, null!, null!, null!);
        var onB = await owner.GetPromptContextAsync(conversation.Id, token);

        Assert.Equal(b.Id, onB.BranchId);
        Assert.Empty(onB.Compactions);
        Assert.Equal(history.Take(4).Append(bOnly), onB.Messages);
        Assert.All(onB.Messages, message => Assert.False(message.IsCompacted));
        Assert.All(onB.OriginalMessages.Take(4), message => Assert.True(message.IsCompacted));
        Assert.DoesNotContain(onB.Messages, message => message.Id == history[4].Id);
        await database.Production.SetCurrentBranchAsync(conversation.Id, a.Id, token);
        Assert.Equal(replacement.Summary, Assert.Single((await owner.GetPromptContextAsync(conversation.Id, token)).Compactions).Summary);
    }

    [Fact]
    public async Task Raw_fallback_on_another_branch_can_be_compacted_without_overlapping_summary_injection()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var a = (await database.Production.GetCurrentBranchAsync(conversation.Id, token))!;
        var b = await database.Production.CreateBranchAsync(conversation.Id, a.Id, history[3].Id, "B shared prefix", ConversationBranchReason.Manual, token);
        await database.Production.SetCurrentBranchAsync(conversation.Id, a.Id, token);
        var aReplacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(8).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, aReplacement, token);
        await database.Production.SetCurrentBranchAsync(conversation.Id, b.Id, token);
        var bContext = await database.Conversations.GetPromptContextAsync(conversation.Id, token);
        Assert.All(bContext.OriginalMessages, message => Assert.True(message.IsCompacted));
        var bSummary = CompactionTestDatabase.Replacement(conversation.Id, bContext.Messages).Summary with
        {
            Content = "Only the common four source messages are represented here.",
            CreatedAt = aReplacement.Summary.CreatedAt.AddSeconds(1)
        };
        var bReplacement = bContext.BindCompaction(bSummary, bContext.Messages);

        await database.Conversations.CommitCompactionAsync(conversation.Id, bReplacement, token);

        Assert.Equal(bSummary, Assert.Single((await database.Conversations.GetPromptContextAsync(conversation.Id, token)).Compactions).Summary);
        await database.Production.SetCurrentBranchAsync(conversation.Id, a.Id, token);
        var current = await database.Conversations.GetPromptContextAsync(conversation.Id, token);
        Assert.Equal(bSummary, Assert.Single(current.Compactions).Summary);
        Assert.Equal(history.Skip(4), current.Messages);
        Assert.Equal(2, (await database.Conversations.GetContextEntriesAsync(conversation.Id, token)).Count);
    }

    [Fact]
    public async Task A_complete_unchanged_shared_source_can_use_one_summary_in_its_descendant_branch()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        var b = await database.Production.CreateBranchAsync(conversation.Id, replacement.SourceBranchId!.Value,
            history[7].Id, "Shared sources", ConversationBranchReason.Manual, token);

        var context = await database.Conversations.GetPromptContextAsync(conversation.Id, token);

        Assert.Equal(b.Id, context.BranchId);
        Assert.Equal(replacement.Summary, Assert.Single(context.Compactions).Summary);
        Assert.Equal(history.Skip(4).Take(4), context.Messages);
        Assert.Equal(replacement.SourceVersions, context.Compactions[0].SourceVersions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Edited_effective_branch_version_requires_raw_fallback_even_when_text_is_unchanged(bool sameText)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        var b = await database.Production.CreateBranchAsync(conversation.Id, replacement.SourceBranchId!.Value,
            null, "Edited branch", ConversationBranchReason.EditedUserMessage, token);
        var text = sameText ? history[0].Content : "Only B's revised effective content";
        var version = await database.Production.AddVersionAsync(history[0].Id, b.Id, MessageVersionKind.UserEdit, text, null, true, token);

        var context = await database.Conversations.GetPromptContextAsync(conversation.Id, token);

        Assert.Empty(context.Compactions);
        Assert.Equal(history.Length, context.Messages.Count);
        Assert.Equal(text, context.Messages[0].Content);
        Assert.Equal(version.Id, context.SourceVersions.Single(source => source.MessageId == history[0].Id).VersionId);
        Assert.All(context.Messages, message => Assert.False(message.IsCompacted));
        await database.Production.SetCurrentBranchAsync(conversation.Id, replacement.SourceBranchId.Value, token);
        Assert.Single((await database.Conversations.GetPromptContextAsync(conversation.Id, token)).Compactions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compaction_commit_rechecks_the_branch_and_effective_version_observed_before_generation(bool switchBranch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        if (switchBranch)
            await database.Production.CreateBranchAsync(conversation.Id, replacement.SourceBranchId!.Value, null, "New selection", ConversationBranchReason.Manual, token);
        else
            await database.Production.AddVersionAsync(history[0].Id, replacement.SourceBranchId!.Value,
                MessageVersionKind.UserEdit, history[0].Content, history[0].MetadataJson, true, token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token));

        Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
        var context = await database.Conversations.GetPromptContextAsync(conversation.Id, token);
        Assert.Empty(context.Compactions);
        Assert.Equal(history, context.Messages);
        Assert.All(context.OriginalMessages, message => Assert.False(message.IsCompacted));
    }

    [Fact]
    public async Task Legacy_unscoped_summary_retains_every_raw_prompt_message_instead_of_claiming_coverage()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var legacy = CompactionTestDatabase.Replacement(conversation.Id, history.Take(4).ToArray()).Summary;
        await database.Conversations.AddContextEntryAsync(legacy, token);
        await database.Conversations.MarkMessagesCompactedAsync(conversation.Id, history.Take(4).Select(message => message.Id).ToArray(), token);

        var context = await database.Conversations.GetPromptContextAsync(conversation.Id, token);

        Assert.Empty(context.Compactions);
        Assert.Equal(history, context.Messages);
        Assert.All(context.OriginalMessages.Take(4), message => Assert.True(message.IsCompacted));
        Assert.Equal(legacy, Assert.Single(await database.Conversations.GetContextEntriesAsync(conversation.Id, token)));
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("changed-fingerprint")]
    [InlineData("changed-summary")]
    [InlineData("missing-coverage")]
    [InlineData("cleared-inclusion")]
    public async Task Incomplete_or_changed_retained_coverage_falls_back_to_raw_without_trusting_a_partial_summary(string damage)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        var summary = replacement.Summary.Id.ToString("D");
        await database.ExecuteAsync(damage switch
        {
            "missing-source" => $"DELETE FROM conversation_compaction_sources WHERE summary_id='{summary}' AND source_order=1;",
            "changed-fingerprint" => $"UPDATE conversation_compaction_sources SET message_sha256='unmatched' WHERE summary_id='{summary}' AND source_order=1;",
            "changed-summary" => $"UPDATE conversation_context SET content='An unrelated substituted summary' WHERE id='{summary}';",
            "cleared-inclusion" => $"UPDATE messages SET is_compacted=0 WHERE id='{history[0].Id:D}';",
            _ => $"DELETE FROM conversation_compactions WHERE summary_id='{summary}';"
        }, token);

        var context = await database.Conversations.GetPromptContextAsync(conversation.Id, token);

        Assert.Empty(context.Compactions);
        Assert.Equal(history, context.Messages);
        Assert.Equal(history.Select(message => (message.Id, message.Content)), context.OriginalMessages.Select(message => (message.Id, message.Content)));
        Assert.Single(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
    }

    [Fact]
    public async Task Coverage_storage_failure_rolls_back_replacement_flags_and_provenance_then_retry_owns_one_summary()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(4).ToArray(), token);
        await database.ExecuteAsync("""
            CREATE TRIGGER fail_coverage BEFORE INSERT ON conversation_compaction_sources WHEN NEW.source_order=1
            BEGIN SELECT RAISE(ABORT,'controlled source coverage failure'); END;
            """, token);

        await Assert.ThrowsAsync<SqliteException>(() => database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token));

        Assert.Empty(await database.Conversations.GetContextEntriesAsync(conversation.Id, token));
        Assert.Empty((await database.Conversations.GetPromptContextAsync(conversation.Id, token)).Compactions);
        Assert.Equal(history, await database.Conversations.GetMessagesAsync(conversation.Id, token));
        Assert.Equal(0L, await database.ScalarAsync("SELECT COUNT(*) FROM conversation_compactions;", token));
        Assert.Equal(0L, await database.ScalarAsync("SELECT COUNT(*) FROM conversation_compaction_sources;", token));
        await database.ExecuteAsync("DROP TRIGGER fail_coverage;", token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        Assert.Single((await database.Conversations.GetPromptContextAsync(conversation.Id, token)).Compactions);
        Assert.Equal(4L, await database.ScalarAsync("SELECT COUNT(*) FROM conversation_compaction_sources;", token));
    }

    [Fact]
    public async Task Admitted_branch_context_keeps_its_exact_original_when_current_selection_switches()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        using var database = await CompactionTestDatabase.CreateAsync(token);
        var (conversation, history) = CompactionTestDatabase.History(temporary: false);
        await database.StoreAsync(conversation, history, token);
        var replacement = await database.CaptureCompactionAsync(conversation.Id, history.Take(8).ToArray(), token);
        await database.Conversations.CommitCompactionAsync(conversation.Id, replacement, token);
        var tracked = new UsageTrackingConversationRepository(database.Conversations, null!, null!, null!, null!);
        await using var invocation = tracked.BindInvocation(conversation.Id, replacement.SourceBranchId!.Value);
        var contextSource = Assert.IsAssignableFrom<IConversationBranchPromptContextSource>(invocation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => contextSource.GetPromptContextAsync(token));
        await invocation.AdmitExistingUserInputAsync(history[^2], token);
        var b = await database.Production.CreateBranchAsync(conversation.Id, replacement.SourceBranchId.Value,
            history[3].Id, "Different current branch", ConversationBranchReason.Manual, token);

        var original = await contextSource.GetPromptContextAsync(token);
        var current = await database.Conversations.GetPromptContextAsync(conversation.Id, token);

        Assert.Equal(replacement.SourceBranchId, original.BranchId);
        Assert.Equal(replacement.Summary, Assert.Single(original.Compactions).Summary);
        Assert.Equal(history.Select(message => message.Id), original.OriginalMessages.Select(message => message.Id));
        Assert.Equal(b.Id, current.BranchId);
        Assert.Empty(current.Compactions);
        Assert.Equal(history.Take(4), current.Messages);
    }

    [Fact]
    public void Temporary_context_freezes_exact_data_validates_replacement_coverage_and_is_not_serialized()
    {
        var (conversation, originals) = CompactionTestDatabase.History(temporary: true);
        var replacement = CompactionTestDatabase.Replacement(conversation.Id, originals.Take(4).ToArray());
        var history = originals.Select((message, index) => index < 4 ? message with { IsCompacted = true } : message).ToArray();
        var summaries = new List<ConversationCompaction> { replacement };
        var snapshot = new TemporaryConversationContext(conversation, history, summaries);
        history[0] = history[0] with { Content = "Later caller mutation" }; summaries.Clear();
        Assert.Equal(originals[0].Content, snapshot.Context.OriginalMessages[0].Content);
        Assert.Equal(replacement.Summary, Assert.Single(snapshot.Context.Compactions).Summary);
        Assert.Equal(originals.Skip(4), snapshot.Context.Messages);
        Assert.Same(snapshot.Context, snapshot.ForConversation(conversation with { UpdatedAt = DateTimeOffset.UtcNow }));
        Assert.Throws<InvalidOperationException>(() => snapshot.ForConversation(conversation with { Id = Guid.NewGuid() }));
        Assert.Throws<InvalidOperationException>(() => snapshot.ForConversation(conversation with { IsTemporary = false }));
        Assert.Throws<ArgumentException>(() => new TemporaryConversationContext(conversation, history, [replacement]));
        var json = System.Text.Json.JsonSerializer.Serialize(new GenerationOptions { OriginalTemporaryContext = snapshot });
        Assert.DoesNotContain(nameof(GenerationOptions.OriginalTemporaryContext), json, StringComparison.Ordinal);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<GenerationOptions>(json)!.OriginalTemporaryContext);
    }

    private static TemporaryChatSaveRequest TemporaryRequest()
    {
        var (conversation, history) = CompactionTestDatabase.History(temporary: true);
        var replacement = CompactionTestDatabase.Replacement(conversation.Id, history.Take(4).ToArray());
        return new(conversation, history.Select((message, index) => index < 4 ? message with { IsCompacted = true } : message).ToArray(),
            "Exact unsent draft", []) { Compactions = [replacement] };
    }
}

/// <summary>Private CPU-only SQLite fixture over the same maintained repository/schema.</summary>
internal sealed class CompactionTestDatabase : IAppPaths, IDisposable
{
    private CompactionTestDatabase()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "haven-compaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);
        Database = new SqliteDatabase(this);
        Conversations = new(Database);
        Production = new SafeConversationProductionRepository(Database, Conversations, new ConversationProductionRepository(Database, Conversations));
    }

    public string DataDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "context.db");
    public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
    public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string LegacyStatePath => Path.Combine(DataDirectory, "absent.json");
    internal SqliteDatabase Database { get; }
    internal ConversationRepository Conversations { get; }
    internal IConversationProductionRepository Production { get; }

    internal static async Task<CompactionTestDatabase> CreateAsync(CancellationToken token)
    {
        var fixture = new CompactionTestDatabase();
        try { await new ConversationProductionDatabase(fixture.Database).InitializeAsync(token); return fixture; }
        catch { fixture.Dispose(); throw; }
    }

    internal async Task ExecuteAsync(string sql, CancellationToken token)
    {
        await using var connection = await Database.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }

    internal async Task<long> ScalarAsync(string sql, CancellationToken token)
    {
        await using var connection = await Database.OpenAsync(token);
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture);
    }

    internal async Task<ConversationCompaction> CaptureCompactionAsync(Guid conversation, IReadOnlyList<ChatMessage> sources, CancellationToken token)
    {
        var context = await Conversations.GetPromptContextAsync(conversation, token);
        return context.BindCompaction(Replacement(conversation, sources).Summary, sources);
    }

    internal async Task StoreAsync(Conversation conversation, IReadOnlyList<ChatMessage> history, CancellationToken token)
    {
        await Conversations.UpsertConversationAsync(conversation, token);
        foreach (var message in history) await Conversations.AddMessageAsync(message, token);
    }

    internal static (Conversation Conversation, ChatMessage[] History) History(bool temporary, int count = 12)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Actual compaction history", null, null,
            false, temporary, now, now);
        var history = Enumerable.Range(0, count).Select(index => new ChatMessage(Guid.NewGuid(), conversation.Id,
            index % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
            $"Original retained fact {index}", index % 2 == 0 ? null : "Haven", "owned-model", null, now.AddSeconds(index))).ToArray();
        return (conversation, history);
    }

    internal static ConversationCompaction Replacement(Guid conversation, IReadOnlyList<ChatMessage> source) =>
        new(new(Guid.NewGuid(), conversation, ContextEntryKind.CompactSummary, "Retained summary", "The actual retained replacement.",
            "Original messages remain in history.", DateTimeOffset.UtcNow), source);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true);
    }
}
