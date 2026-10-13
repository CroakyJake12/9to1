using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Spaces.Storage;
using Xunit;

namespace NineToOne.Web.Spaces.Tests;

/// <summary>Actual managed repository/versioning/recovery owners over a durable
/// controlled wire boundary. Signed authentication and IndexedDB itself are verified
/// separately; fixture actor metadata never grants production authority.</summary>
public sealed class BrowserConversationProductionSessionTests
{
    [Fact]
    public async Task Actual_branch_edit_preserves_original_sources_and_survives_fresh_owner()
    {
        using var fixture = new BrowserConversationProductionFixture();
        var conversation = await fixture.CreateConversation();
        var user = fixture.Message(conversation.Id, MessageRole.User, "Original question");
        var assistant = fixture.Message(conversation.Id, MessageRole.Assistant, "Original answer");
        await fixture.Conversations.AddMessageAsync(user, default);
        await fixture.Conversations.AddMessageAsync(assistant, default);
        var originalBranch = (await fixture.Production.GetCurrentBranchAsync(conversation.Id, default))!;
        var edited = await fixture.Versioning.EditUserMessageAsync(conversation.Id, user.Id, "Revised question", MessageEditMode.NewBranch, default);
        Assert.NotEqual(originalBranch.Id, edited.Id);
        var branchMessages = await fixture.Conversations.GetMessagesAsync(conversation.Id, default);
        Assert.Equal("Revised question", Assert.Single(branchMessages).Content);
        Assert.Equal(user.Id, branchMessages[0].Id);
        var export = await fixture.Production.BuildExportAsync(conversation.Id, default);
        Assert.Equal("Original question", export.Messages.Single(item => item.Id == user.Id).Content);
        Assert.Equal("Original answer", export.Messages.Single(item => item.Id == assistant.Id).Content);
        await fixture.Production.SetCurrentBranchAsync(conversation.Id, originalBranch.Id, default);
        Assert.Equal(new[] { "Original question", "Original answer" }, (await fixture.Conversations.GetMessagesAsync(conversation.Id, default)).Select(item => item.Content));
        await fixture.CloseAsync();
        using var reopened = fixture.Reopen();
        Assert.Equal(2, (await reopened.Production.GetBranchesAsync(conversation.Id, default)).Count);
        await reopened.Production.SetCurrentBranchAsync(conversation.Id, edited.Id, default);
        Assert.Equal("Revised question", Assert.Single(await reopened.Conversations.GetMessagesAsync(conversation.Id, default)).Content);
        await reopened.CloseAsync();
    }

    [Fact]
    public async Task In_place_edit_truncates_visible_branch_but_retains_acknowledged_recovery_versions()
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        var user = fixture.Message(conversation.Id, MessageRole.User, "Question"); var answer = fixture.Message(conversation.Id, MessageRole.Assistant, "Saved answer");
        await fixture.Conversations.AddMessageAsync(user, default); await fixture.Conversations.AddMessageAsync(answer, default);
        await fixture.Versioning.EditUserMessageAsync(conversation.Id, user.Id, "Edited here", MessageEditMode.OverwriteCurrentBranch, default);
        Assert.Equal("Edited here", Assert.Single(await fixture.Conversations.GetMessagesAsync(conversation.Id, default)).Content);
        var export = await fixture.Production.BuildExportAsync(conversation.Id, default);
        Assert.Contains(export.Versions, version => version.MessageId == answer.Id && version.Content == "Saved answer");
        Assert.Contains(export.Versions, version => version.MessageId == user.Id && version.Kind == MessageVersionKind.RecoverySnapshot && version.Content == "Question");
        await fixture.CloseAsync();
    }

    [Fact]
    public async Task Committed_original_message_lost_reply_is_recovered_without_replaying_source_write()
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        var user = fixture.Message(conversation.Id, MessageRole.User, "Exact durable message");
        fixture.FailAfterAction = "Conversation.PutMessage";
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Conversations.AddMessageAsync(user, default));
        Assert.Contains(fixture.LostReply, Leaves(failure));
        Assert.Equal(1, fixture.MessageWrites);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Conversations.GetMessagesAsync(conversation.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Conversations.AddMessageAsync(user, default));
        Assert.Equal(1, fixture.MessageWrites);
        var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => fixture.CloseAsync()); Assert.Contains(fixture.LostReply, Leaves(closeFailure));
        using var reopened = fixture.Reopen();
        Assert.True(await reopened.Production.RecoverOriginalMessageWriteAsync(conversation.Id));
        Assert.Equal(user, Assert.Single(await reopened.Conversations.GetMessagesAsync(conversation.Id, default)));
        Assert.Equal(0, reopened.MessageWrites);
        await reopened.CloseAsync();
    }

    [Fact]
    public async Task Pending_write_with_no_original_effect_cannot_be_inferred_complete_or_branch_switched()
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        var branch = await fixture.Production.EnsureRootBranchAsync(conversation.Id, default);
        var message = fixture.Message(conversation.Id, MessageRole.User, "Never acknowledged");
        await fixture.Production.ReserveOriginalMessageWriteAsync(message, default);
        Assert.False(await fixture.Production.RecoverOriginalMessageWriteAsync(conversation.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Production.SetCurrentBranchAsync(conversation.Id, branch.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Conversations.AddMessageAsync(message, default));
        Assert.Equal(0, fixture.MessageWrites); await fixture.CloseAsync();
    }

    [Fact]
    public async Task Draft_bookmark_and_unknown_extensions_survive_mutation_while_foreign_profile_is_empty()
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        var user = fixture.Message(conversation.Id, MessageRole.User, "Needle in current message"); await fixture.Conversations.AddMessageAsync(user, default);
        var branch = (await fixture.Production.GetCurrentBranchAsync(conversation.Id, default))!;
        fixture.RewriteProduction(conversation.Id, state => state["futureOwnerExtension"] = JsonSerializer.SerializeToElement(new { retained = "exact" }));
        await fixture.Production.SaveDraftAsync(new(conversation.Id, branch.Id, "Unsent draft", "[]", DateTimeOffset.UtcNow), default);
        var bookmark = new MessageBookmark(Guid.NewGuid(), conversation.Id, user.Id, "Useful", "Original note", DateTimeOffset.UtcNow);
        await fixture.Production.UpsertBookmarkAsync(bookmark, default);
        Assert.Single(await fixture.Production.SearchAsync("needle", conversation.Id, 20, default));
        await fixture.CloseAsync();
        using var reopened = fixture.Reopen();
        Assert.Equal("Unsent draft", (await reopened.Production.GetDraftAsync(conversation.Id, branch.Id, default))!.Content);
        Assert.Equal(bookmark, Assert.Single(await reopened.Production.GetBookmarksAsync(conversation.Id, default)));
        Assert.Equal("exact", reopened.ProductionJson(conversation.Id).GetProperty("futureOwnerExtension").GetProperty("retained").GetString());
        await reopened.CloseAsync();
        using var foreign = fixture.Reopen(foreign: true); Assert.Null(await foreign.Conversations.GetAsync(conversation.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => foreign.Production.GetBranchesAsync(conversation.Id, default)); await foreign.CloseAsync();
    }

    [Fact]
    public async Task Three_distinct_confirmed_flags_lock_irreversibly_across_restart_and_duplicate_is_idempotent()
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        var flag = new ConfirmedSafetyFlag(Guid.NewGuid(), "actual fixture confirmation", "test", "abc123", DateTimeOffset.UtcNow);
        Assert.True((await fixture.Production.RecordConfirmedFlagAsync(conversation.Id, flag, default)).Added);
        Assert.False((await fixture.Production.RecordConfirmedFlagAsync(conversation.Id, flag, default)).Added);
        await fixture.Production.RecordConfirmedFlagAsync(conversation.Id, flag with { EventId = Guid.NewGuid() }, default);
        var third = await fixture.Production.RecordConfirmedFlagAsync(conversation.Id, flag with { EventId = Guid.NewGuid() }, default);
        Assert.True(third.LockedNow); await Assert.ThrowsAsync<ConversationSafetyLockException>(() => fixture.Production.EnsureMayActAsync(conversation.Id, "chat.send", default));
        await fixture.CloseAsync();
        using var reopened = fixture.Reopen();
        Assert.Equal(ConversationSafetyState.Locked, (await reopened.Production.GetSnapshotAsync(conversation.Id, default)).State);
        await Assert.ThrowsAsync<ConversationSafetyLockException>(() => reopened.Production.EnsureMayActAsync(conversation.Id, "chat.send", default)); await reopened.CloseAsync();
    }

    [Fact]
    public async Task Malformed_saved_safety_is_refused_without_resetting_or_unlocking()
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        await fixture.Production.EnsureRootBranchAsync(conversation.Id, default);
        fixture.RewriteProduction(conversation.Id, state => state["safety"] = JsonSerializer.SerializeToElement(new { schemaVersion = 1, flags = Array.Empty<object>(), lockedAt = DateTimeOffset.UtcNow }));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Production.GetSnapshotAsync(conversation.Id, default));
        Assert.NotEqual(JsonValueKind.Null, fixture.ProductionJson(conversation.Id).GetProperty("safety").GetProperty("lockedAt").ValueKind); await fixture.CloseAsync();
    }

    [Fact]
    public async Task Raw_delete_and_compaction_refuse_before_source_mutation_when_branch_owner_is_attached()
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        var message = fixture.Message(conversation.Id, MessageRole.User, "Preserved original"); await fixture.Conversations.AddMessageAsync(message, default);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Conversations.DeleteMessageAsync(conversation.Id, message.Id, default));
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Conversations.MarkMessagesCompactedAsync(conversation.Id, [message.Id], default));
        Assert.Equal(message, Assert.Single(await fixture.Conversations.GetMessagesAsync(conversation.Id, default))); await fixture.CloseAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_compacted_history_cannot_be_forked_into_an_unscoped_summary_context(bool summaryWithoutFlags)
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        var message = fixture.Message(conversation.Id, MessageRole.User, "Saved old context") with { IsCompacted = !summaryWithoutFlags };
        await fixture.Conversations.AddMessageAsync(message, default);
        if (summaryWithoutFlags)
            await fixture.Conversations.AddContextEntryAsync(new(Guid.NewGuid(), conversation.Id, ContextEntryKind.CompactSummary,
                "Original global summary", "Includes material after the proposed fork", "Historical compaction", DateTimeOffset.UtcNow), default);
        var branch = (await fixture.Production.GetCurrentBranchAsync(conversation.Id, default))!;
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Production.CreateBranchAsync(conversation.Id, branch.Id, message.Id, "Unsafe fork", ConversationBranchReason.Manual, default));
        Assert.Single(await fixture.Production.GetBranchesAsync(conversation.Id, default)); await fixture.CloseAsync();
    }

    [Fact]
    public async Task Settings_versions_and_privacy_bytes_reopen_without_inventing_default_or_success()
    {
        using var fixture = new BrowserConversationProductionFixture();
        var privacy = PrivacyPreferences.Default with { LocalOnlyMode = true };
        await fixture.Settings.SetAsync("privacy.preferences.v1", privacy, default);
        Assert.Equal(1, (await fixture.Settings.ExportAsync(default)).Version);
        Assert.False(fixture.Settings.HasUnsettledMutation);
        Assert.Equal(1, fixture.Settings.MutationAdmissionGeneration);
        await fixture.CloseAsync();
        using var reopened = fixture.Reopen();
        Assert.True((await reopened.Settings.GetAsync<PrivacyPreferences>("privacy.preferences.v1", default))!.LocalOnlyMode);
        await reopened.Settings.ImportAsync(new() { Settings = new() { ["models.permissions.v1"] = "{}", ["models.personalisation.v1"] = "{}" } }, default);
        var exported = await reopened.Settings.ExportAsync(default); Assert.Equal(2, exported.Version); Assert.Equal(3, exported.Settings.Count);
        await reopened.CloseAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_source_faulted_cancellation_stays_faulted_even_when_owned_token_is_requested(bool delete)
    {
        using var fixture = new BrowserConversationProductionFixture();
        var conversation = await fixture.CreateConversation();
        var branch = await fixture.Production.EnsureRootBranchAsync(conversation.Id, default);
        var draft = new ConversationDraft(conversation.Id, branch.Id, "Keep the actual outcome", "[]", DateTimeOffset.UtcNow);
        if (delete) await fixture.Production.SaveDraftAsync(draft, default);
        using var owned = new CancellationTokenSource();
        var originalFault = new OperationCanceledException("Faulted original writer, not cancellation acknowledgement", owned.Token);
        fixture.LostReply = originalFault;
        fixture.BeforeLostReply = owned.Cancel;
        fixture.FailAfterAction = "ConversationProduction.CompareExchange";
        var actual = delete ? fixture.Production.DeleteDraftAsync(conversation.Id, branch.Id, owned.Token)
            : fixture.Production.SaveDraftAsync(draft, owned.Token);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.True(owned.IsCancellationRequested);
        Assert.True(actual.IsFaulted);
        Assert.False(actual.IsCanceled);
        Assert.Contains(originalFault, Leaves(failure));
        var close = await Assert.ThrowsAnyAsync<Exception>(() => fixture.CloseAsync());
        Assert.Contains(originalFault, Leaves(close));
        using var reopened = fixture.Reopen();
        var saved = await reopened.Production.GetDraftAsync(conversation.Id, branch.Id, default);
        if (delete) Assert.Null(saved); else Assert.Equal(draft, saved);
        await reopened.CloseAsync();
    }

    [Fact]
    public async Task Present_null_model_settings_refuse_while_only_missing_values_are_absent()
    {
        using var fixture = new BrowserConversationProductionFixture();
        Assert.Null(await fixture.Settings.GetAsync<Dictionary<string, string>>("models.permissions.v1", default));
        await fixture.Settings.ImportAsync(new() { Settings = new() { ["models.permissions.v1"] = "null" } }, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Settings.GetAsync<Dictionary<string, string>>("models.permissions.v1", default));
        using var reopened = fixture.Reopen();
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.Settings.GetAsync<Dictionary<string, string>>("models.permissions.v1", default));
        Assert.Equal("null", (await reopened.Settings.ExportAsync(default)).Settings["models.permissions.v1"]);
        await reopened.CloseAsync();
        await fixture.CloseAsync();
    }

    [Fact]
    public async Task Lost_settings_ack_fences_policy_until_new_owner_observes_actual_saved_state()
    {
        using var fixture = new BrowserConversationProductionFixture(); fixture.FailAfterAction = "ConversationProduction.SettingsCompareExchange";
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Settings.SetAsync("privacy.preferences.v1", PrivacyPreferences.Default with { LocalOnlyMode = true }, default));
        Assert.True(fixture.Settings.HasUnsettledMutation); Assert.Equal(1, fixture.Settings.MutationAdmissionGeneration); Assert.Equal(0, fixture.Settings.AcknowledgedMutationGeneration);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.CloseAsync());
        using var reopened = fixture.Reopen(); Assert.True((await reopened.Settings.GetAsync<PrivacyPreferences>("privacy.preferences.v1", default))!.LocalOnlyMode); await reopened.CloseAsync();
    }

    [Fact]
    public async Task Pending_attachment_recovery_keeps_original_kind_limit_after_owner_restart()
    {
        using var fixture = new BrowserConversationProductionFixture(); var conversation = await fixture.CreateConversation();
        fixture.SelectedBytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 0, 0, 0, 0];
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Attachments.ImportAsync(conversation.Id, null, null, "browser-selection:" + Guid.NewGuid(), new(MaxDocumentBytes: 1024, MaxImageBytes: 2), default));
        var pending = Assert.Single(await fixture.Production.GetAttachmentsAsync(conversation.Id, null, default));
        Assert.Equal(AttachmentProcessingState.Pending, pending.ProcessingState);
        Assert.Equal(2, JsonDocument.Parse(pending.MetadataJson).RootElement.GetProperty("processingOptions").GetProperty("MaxImageBytes").GetInt32());
        await fixture.CloseAsync();
        using var reopened = fixture.Reopen();
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.Attachments.RecoverPendingAsync(conversation.Id, pending.Id));
        Assert.Equal(AttachmentProcessingState.Pending, Assert.Single(await reopened.Production.GetAttachmentsAsync(conversation.Id, null, default)).ProcessingState); await reopened.CloseAsync();
    }

    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException { InnerExceptions.Count: > 0 } group
        ? group.InnerExceptions.SelectMany(Leaves) : error.InnerException is { } inner ? new[] { error }.Concat(Leaves(inner)) : [error];
}

internal sealed class BrowserConversationProductionFixture : IDisposable
{
    private readonly string _directory;
    private readonly bool _ownsDirectory;
    private readonly FixtureActor _actor;
    public BrowserTaskExecutionTransport Transport { get; }
    public IndexedDbConversationRepository Conversations { get; }
    public IndexedDbConversationProductionRepository Production { get; }
    public ConversationVersioningService Versioning { get; }
    public BrowserChatSettingsStore Settings { get; }
    public BrowserMessageAttachmentService Attachments { get; }
    public byte[] SelectedBytes { get; set; } = [];
    public string? FailAfterAction { get; set; }
    public Exception LostReply { get; set; } = new IOException("Lost original committed fixture reply");
    public Action? BeforeLostReply { get; set; }
    public int MessageWrites { get; private set; }
    public BrowserConversationProductionFixture() : this(Path.Combine(Path.GetTempPath(), "browser-production-control-" + Guid.NewGuid().ToString("N")), null, true) { }
    private BrowserConversationProductionFixture(string directory, AuthenticatedResourceActor? actor, bool ownsDirectory)
    {
        _directory = directory; _ownsDirectory = ownsDirectory; Directory.CreateDirectory(directory);
        _actor = new(actor ?? CreateActor());
        Transport = new("controlled-original-" + Guid.NewGuid(), (_, _, action, args, _) => Invoke(action, args), (_, _) => { }, _ => Task.CompletedTask);
        Conversations = new(Transport, (IAuthenticatedResourceActorSource)_actor);
        var store = new BrowserConversationProductionStore(Transport, (IAuthenticatedResourceActorSource)_actor);
        Production = new(store, Conversations); Settings = new(store); Attachments = new(store, Production);
        Conversations.AttachOriginalProduction(Production); Versioning = new(Conversations, Production);
    }
    private static AuthenticatedResourceActor CreateActor()
    {
        var id = Guid.NewGuid(); var hash = new string('a', 64);
        return new($"cake-task:{hash}:{id:D}", $"cake-account-profile:{hash}:{id:D}", id, null, "controlled-original-actor-observation");
    }
    public BrowserConversationProductionFixture Reopen(bool foreign = false) => new(_directory, foreign ? CreateActor() : _actor.Actor, false);
    public async Task<Conversation> CreateConversation()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Original Chat", null, null, false, false, now, now);
        await Conversations.UpsertConversationAsync(conversation, default); return conversation;
    }
    public ChatMessage Message(Guid conversationId, MessageRole role, string text) => new(Guid.NewGuid(), conversationId, role, text, null, null, null, DateTimeOffset.UtcNow);
    private string FilePath => Path.Combine(_directory, "actual-wire-fixture.json");
    private Dictionary<string, JsonElement> Load() => File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(FilePath))! : new();
    private void Save(Dictionary<string, JsonElement> rows) => File.WriteAllText(FilePath, JsonSerializer.Serialize(rows));
    private static JsonElement Element(object? value) => JsonSerializer.SerializeToElement(value);
    private static string Key(string profile, string entity, string id) => profile + "|" + entity + "|" + id;
    public JsonElement ProductionJson(Guid id)
    {
        var row = Load()[Key(_actor.Actor.ProfileId, "production", id.ToString("D"))]; return JsonDocument.Parse(row.GetProperty("json").GetString()!).RootElement.Clone();
    }
    public void RewriteProduction(Guid id, Action<Dictionary<string, JsonElement>> rewrite)
    {
        var rows = Load(); var key = Key(_actor.Actor.ProfileId, "production", id.ToString("D")); var row = rows[key];
        var state = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.GetProperty("json").GetString()!)!; rewrite(state);
        var json = JsonSerializer.Serialize(state); var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.GetRawText())!;
        fields["json"] = Element(json); fields["sha256"] = Element(BrowserConversationProductionStore.Hash(json)); rows[key] = Element(fields); Save(rows);
    }
    private Task<string> Invoke(string action, string arguments)
    {
        using var parsed = JsonDocument.Parse(arguments); var args = parsed.RootElement; var profile = args.GetProperty("profileId").GetString()!;
        var rows = Load(); object? value; var write = false;
        JsonElement? One(string entity, string id) => rows.TryGetValue(Key(profile, entity, id), out var found) ? found : null;
        var local = rows.Values.Where(row => row.GetProperty("profileId").GetString() == profile).ToArray();
        if (action is "Conversation.Upsert" or "Conversation.PutMessage" or "Conversation.PutContext")
        {
            var row = args.GetProperty("row"); var entity = row.GetProperty("entity").GetString()!; var id = row.GetProperty("id").GetString()!;
            var previous = One(entity, id); var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.GetRawText())!;
            fields["revision"] = Element(((previous is { } saved ? long.Parse(saved.GetProperty("revision").GetString()!) : 0) + 1).ToString());
            var result = Element(fields); rows[Key(profile, entity, id)] = result; value = new { rows = new[] { result } }; write = true;
            if (action == "Conversation.PutMessage") MessageWrites++;
        }
        else if (action == "Conversation.Get") value = One("conversation", args.GetProperty("id").GetString()!);
        else if (action is "Conversation.Messages" or "Conversation.Context")
        {
            var entity = action == "Conversation.Messages" ? "message" : "context";
            value = local.Where(row => row.TryGetProperty("entity", out var kind) && kind.GetString() == entity && row.GetProperty("conversationId").GetString() == args.GetProperty("conversationId").GetString())
                .OrderBy(row => row.GetProperty("createdAt").GetString(), StringComparer.Ordinal).ToArray();
        }
        else if (action is "Conversation.Recent" or "Conversation.Archived") value = local.Where(row => row.TryGetProperty("entity", out var kind) && kind.GetString() == "conversation").ToArray();
        else if (action == "ConversationProduction.Export") value = local.Where(row => row.TryGetProperty("conversationId", out _) && !row.TryGetProperty("entity", out _) && (!args.TryGetProperty("conversationId", out var id) || id.ValueKind == JsonValueKind.Null || row.GetProperty("conversationId").GetString() == id.GetString())).ToArray();
        else if (action == "ConversationProduction.CompareExchange")
        {
            var id = args.GetProperty("conversationId").GetString()!; var previous = One("production", id); var expected = args.GetProperty("expected").GetString();
            if (previous?.GetProperty("json").GetString() != expected) value = new { exchanged = false, current = previous };
            else
            {
                var result = Element(new { profileId = profile, conversationId = id, json = args.GetProperty("replacement").GetString(), sha256 = args.GetProperty("sha256").GetString(),
                    revision = ((previous is { } old ? long.Parse(old.GetProperty("revision").GetString()!) : 0) + 1).ToString() });
                rows[Key(profile, "production", id)] = result; value = new { exchanged = true, current = result }; write = true;
            }
        }
        else if (action == "ConversationProduction.SettingsExport")
            value = new { profileId = profile, version = One("settingsVersion", "current")?.GetProperty("version").GetInt32() ?? 0,
                rows = local.Where(row => row.TryGetProperty("entity", out var entity) && entity.GetString() == "settings").ToArray() };
        else if (action == "ConversationProduction.SettingsCompareExchange")
        {
            var version = One("settingsVersion", "current")?.GetProperty("version").GetInt32() ?? 0;
            var changes = args.GetProperty("changes").EnumerateArray().ToArray(); write = true;
            if (version != args.GetProperty("expectedVersion").GetInt32() || changes.Any(change => One("settings", change.GetProperty("key").GetString()!)?.GetProperty("json").GetString() != change.GetProperty("expected").GetString()))
                value = new { profileId = profile, exchanged = false, version };
            else
            {
                foreach (var change in changes)
                {
                    var key = change.GetProperty("key").GetString()!;
                    if (change.GetProperty("replacement").ValueKind == JsonValueKind.Null) rows.Remove(Key(profile, "settings", key));
                    else rows[Key(profile, "settings", key)] = Element(new { profileId = profile, entity = "settings", key,
                        json = change.GetProperty("replacement").GetString(), sha256 = change.GetProperty("sha256").GetString(), revision = (version + 1).ToString() });
                }
                rows[Key(profile, "settingsVersion", "current")] = Element(new { profileId = profile, entity = "settingsVersion", version = version + 1 });
                value = new { profileId = profile, exchanged = true, version = version + 1,
                    rows = rows.Values.Where(row => row.GetProperty("profileId").GetString() == profile && row.TryGetProperty("entity", out var entity) && entity.GetString() == "settings").ToArray() };
            }
        }
        else if (action == "ConversationProduction.ImportAttachment")
        {
            var id = args.GetProperty("id").GetString()!;
            var row = Element(new { profileId = profile, entity = "attachment", id, conversationId = args.GetProperty("conversationId").GetString(),
                name = "fixture-original.png", size = SelectedBytes.Length, mediaType = "image/png",
                sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(SelectedBytes)), base64 = Convert.ToBase64String(SelectedBytes) });
            rows[Key(profile, "attachment", id)] = row; value = row; write = true;
        }
        else if (action == "ConversationProduction.ReadAttachment") value = One("attachment", args.GetProperty("id").GetString()!);
        else throw new NotSupportedException("No fixture implementation for " + action);
        if (write) Save(rows);
        if (FailAfterAction == action) { FailAfterAction = null; BeforeLostReply?.Invoke(); return Task.FromException<string>(LostReply); }
        return Task.FromResult(JsonSerializer.Serialize(new { ok = true, committed = write, value }));
    }
    public Task CloseAsync() => Transport.CloseAndDrainAsync();
    public void Dispose() { if (_ownsDirectory) Directory.Delete(_directory, recursive: true); }
    private sealed class FixtureActor(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Actor { get; } = actor;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Actor); }
    }
}
