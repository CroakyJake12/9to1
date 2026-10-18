using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;
using static HavenOS.Apps.Spaces.Tests.HavenChatSpaceBackendAcceptanceTests;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class HavenChatSpaceBackendScopeTests
{
    [Fact]
    public async Task Separate_backends_create_fresh_conversations_in_immutable_selected_spaces()
    {
        var rig = await Rig.CreateAsync();
        var first = rig.Backend;
        var other = await rig.Registry.CreateAsync("Other");
        var second = rig.For(await rig.ContextAsync(other.Id));
        await rig.Registry.RenameAsync(rig.Space.Id, "Renamed");
        var a = await first.CreateChatAsync(default);
        var b = await second.CreateChatAsync(default);
        var a2 = await first.CreateChatAsync(default);
        Assert.Equal(rig.Space.Id, a.SpaceId);
        Assert.Equal(other.Id, b.SpaceId);
        Assert.Equal(a.SpaceId, a2.SpaceId);
        Assert.Equal(3, new[] { a.Id, b.Id, a2.Id }.Distinct().Count());
        Assert.Equal(rig.Space.Id, first.SpaceId);
    }

    [Fact]
    public async Task Study_creation_preserves_mode_and_existing_lesson_identity()
    {
        var rig = await Rig.CreateAsync();
        var study = rig.For((await new SpacesModel(rig.Registry).CreateOpenActionAsync(SpaceScope.Study)).Context);
        var created = await study.CreateChatAsync(default);
        Assert.Equal(SpaceRegistry.StudySpaceId, created.SpaceId);
        Assert.Equal(HavenMode.Study, created.Mode);
        Assert.Equal(ConversationKind.QuickChat, created.Kind);
        var lesson = created with { Kind = ConversationKind.LessonChat, ContainerId = Guid.NewGuid(), LessonId = Guid.NewGuid() };
        rig.Rows[lesson.Id] = lesson;
        var reopened = await study.GetConversationAsync(lesson.Id, default);
        Assert.Equal(lesson, reopened.Conversation);
    }

    [Fact]
    public async Task Scoped_history_is_filtered_before_limit_and_never_uses_global_general_history()
    {
        var rig = await Rig.CreateAsync();
        var other = Guid.NewGuid();
        for (var i = 0; i < 110; i++) rig.Add(other, DateTimeOffset.UtcNow.AddMinutes(i));
        var target = rig.Add(rig.Space.Id, DateTimeOffset.UtcNow.AddYears(-1));
        var rows = await rig.Backend.GetRecentChatsAsync(1, default);
        Assert.Equal(target.Id, Assert.Single(rows).Id);
        Assert.Equal((rig.Space.Id, 1), Assert.Single(rig.SpaceQueries));
        Assert.Equal(0, rig.GeneralQueries);
    }

    [Fact]
    public async Task Built_in_Chat_merges_legacy_rows_without_changing_their_saved_identity()
    {
        var rig = await Rig.CreateAsync();
        var chat = rig.For((await new SpacesModel(rig.Registry).CreateOpenActionAsync(SpaceScope.Chat)).Context);
        var legacy = rig.Add(SpaceRegistry.ChatSpaceId) with { SpaceId = null };
        rig.Rows[legacy.Id] = legacy;
        var scoped = rig.Add(SpaceRegistry.ChatSpaceId);
        var rows = await chat.GetRecentChatsAsync(10, default);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Id == legacy.Id && row.SpaceId is null);
        Assert.Contains(rows, row => row.Id == scoped.Id);
        Assert.Null((await chat.GetConversationAsync(legacy.Id, default)).Conversation.SpaceId);
        Assert.Null(rig.Rows[legacy.Id].SpaceId);
        Assert.Empty(await rig.Backend.GetRecentChatsAsync(10, default));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("safety")]
    [InlineData("draft")]
    [InlineData("import")]
    [InlineData("remove")]
    [InlineData("send")]
    [InlineData("branch")]
    [InlineData("switch")]
    [InlineData("regenerate")]
    [InlineData("copy")]
    [InlineData("delete")]
    [InlineData("forget")]
    [InlineData("edit")]
    public async Task Foreign_conversations_are_rejected_before_any_content_safety_or_mutation_boundary(string operation)
    {
        var rig = await Rig.CreateAsync();
        var foreign = rig.Add(Guid.NewGuid());
        var id = foreign.Id;
        Func<Task> act = operation switch
        {
            "get" => async () => { await rig.Backend.GetConversationAsync(id, default); },
            "safety" => async () => { await rig.Backend.GetSafetySnapshotAsync(id, default); },
            "draft" => () => rig.Backend.SaveDraftAsync(id, null, "secret", [], default),
            "import" => async () => { await rig.Backend.ImportAttachmentAsync(id, null, "unused", default); },
            "remove" => () => rig.Backend.RemoveAttachmentAsync(id, null, Guid.NewGuid(), default),
            "send" => () => rig.Backend.SendAsync(new(foreign, "secret", rig.Model, []), null, default),
            "branch" => async () => { await rig.Backend.CreateBranchAsync(id, Guid.NewGuid(), null, default); },
            "switch" => () => rig.Backend.SwitchBranchAsync(id, Guid.NewGuid(), default),
            "regenerate" => () => rig.Backend.RegenerateAsync(id, Guid.NewGuid(), rig.Model, default),
            "copy" => async () => { await rig.Backend.GetMessageContentAsync(id, Guid.NewGuid(), default); },
            "delete" => () => rig.Backend.DeleteMessageAsync(id, Guid.NewGuid(), default),
            "forget" => () => rig.Backend.ForgetMessageAsync(id, Guid.NewGuid(), default),
            "edit" => () => rig.Backend.EditUserMessageAsync(id, Guid.NewGuid(), "edit", default, default),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(rig.SensitiveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_content_is_not_published_after_conversation_moves_or_space_is_archived(bool archive)
    {
        var rig = await Rig.CreateAsync();
        var row = rig.Add(rig.Space.Id);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.MessageRead = async () => { reached.TrySetResult(); await release.Task; return []; };
        var pending = rig.Backend.GetConversationAsync(row.Id, default);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (archive) await rig.Registry.SetArchivedAsync(rig.Space.Id, true);
            else rig.Rows[row.Id] = row with { SpaceId = Guid.NewGuid() };
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Late_content_cannot_publish_another_branches_draft_or_messages()
    {
        var rig = await Rig.CreateAsync();
        var row = rig.Add(rig.Space.Id);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.MessageRead = async () => { reached.TrySetResult(); await release.Task; return []; };
        var pending = rig.Backend.GetConversationAsync(row.Id, default);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            rig.Branches[row.Id] = rig.Branch(row.Id) with { Id = Guid.NewGuid() };
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Model_inventory_await_cannot_dispatch_a_conversation_that_has_moved()
    {
        var rig = await Rig.CreateAsync();
        var row = rig.Add(rig.Space.Id);
        rig.InventoryRead = () => { rig.Rows[row.Id] = row with { SpaceId = Guid.NewGuid() }; return Task.FromResult(new ChatSpaceModelInventory([rig.Model])); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Backend.SendAsync(new(row, "send", rig.Model, [], BranchId: rig.Branch(row.Id).Id), null, default));
        Assert.DoesNotContain("ExecuteAsync", rig.SensitiveCalls);
    }

    [Fact]
    public async Task Safety_read_checks_membership_again_after_the_service_returns()
    {
        var rig = await Rig.CreateAsync();
        var row = rig.Add(rig.Space.Id);
        rig.SnapshotRead = id =>
        {
            rig.Rows[id] = rig.Rows[id] with { SpaceId = Guid.NewGuid() };
            return Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 1));
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Backend.GetSafetySnapshotAsync(row.Id, default));
        Assert.Contains("GetSnapshotAsync", rig.SensitiveCalls);
    }

    [Fact]
    public async Task An_archived_space_cannot_publish_a_late_model_inventory_or_create_another_chat()
    {
        var rig = await Rig.CreateAsync();
        rig.InventoryRead = async () =>
        {
            await rig.Registry.SetArchivedAsync(rig.Space.Id, true);
            return new([rig.Model]);
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Backend.GetModelInventoryAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Backend.CreateChatAsync(default));
        Assert.Empty(rig.Rows);
    }

    [Fact]
    public async Task Drafts_remain_keyed_by_conversation_and_branch_across_space_backends()
    {
        var rig = await Rig.CreateAsync();
        var other = await rig.Registry.CreateAsync("Other");
        var otherBackend = rig.For(await rig.ContextAsync(other.Id));
        var a = rig.Add(rig.Space.Id); var b = rig.Add(other.Id);
        var aBranch = rig.Branch(a.Id).Id; var bBranch = rig.Branch(b.Id).Id;
        await rig.Backend.SaveDraftAsync(a.Id, aBranch, "Draft A", [], default);
        await otherBackend.SaveDraftAsync(b.Id, bBranch, "Draft B", [], default);
        Assert.Equal("Draft A", (await rig.Backend.GetConversationAsync(a.Id, default)).Draft!.Content);
        Assert.Equal("Draft B", (await otherBackend.GetConversationAsync(b.Id, default)).Draft!.Content);
        Assert.Equal(2, rig.Drafts.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Backend.SaveDraftAsync(a.Id, bBranch, "cross branch", [], default));
        Assert.Equal("Draft A", rig.Drafts[(a.Id, aBranch)].Content);
    }

    [Fact]
    public async Task Delete_requires_current_canonical_mutation_safety()
    {
        var rig = await Rig.CreateAsync();
        var row = rig.Add(rig.Space.Id);
        var message = new ChatMessage(Guid.NewGuid(), row.Id, MessageRole.User, "keep", null, null, null, DateTimeOffset.UtcNow);
        rig.MessageRead = () => Task.FromResult<IReadOnlyList<ChatMessage>>([message]);
        rig.Locked = true;
        await Assert.ThrowsAsync<ConversationSafetyLockException>(() => rig.Backend.DeleteMessageAsync(row.Id, message.Id, default));
        Assert.Contains("chat.delete-message", rig.SensitiveCalls);
        Assert.DoesNotContain("DeleteMessageAsync", rig.SensitiveCalls);
    }

    [Fact]
    public async Task Constructor_rejects_scope_record_mismatch_and_preserves_real_seven_parameter_overload()
    {
        var rig = await Rig.CreateAsync();
        var context = await rig.ContextAsync(rig.Space.Id);
        Assert.Throws<InvalidOperationException>(() => rig.For(context with { Definition = context.Definition with { Scope = SpaceScope.Chat } }));
        Assert.Contains(typeof(HavenChatSpaceBackend).GetConstructors(), constructor => constructor.GetParameters().Length == 7);
    }

    [Fact]
    public async Task Attachment_claimed_while_safety_waits_is_preserved()
    {
        var rig = await Rig.CreateAsync();
        var row = rig.Add(rig.Space.Id);
        var branch = rig.Branch(row.Id);
        var attachment = PendingAttachment(row.Id, branch.Id);
        rig.Attachments.Add(attachment);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.SafetyAdmission = () => { entered.TrySetResult(); return release.Task; };
        var removal = rig.Backend.RemoveAttachmentAsync(row.Id, branch.Id, attachment.Id, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            rig.Attachments[0] = attachment with { MessageId = Guid.NewGuid() };
            release.TrySetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => removal.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.NotNull(Assert.Single(rig.Attachments).MessageId);
            Assert.Equal(0, rig.PendingDeleteCalls);
            Assert.Equal(0, rig.UnconditionalDeleteCalls);
        }
        finally
        {
            release.TrySetResult();
            try { await removal.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Attachment_removal_uses_atomic_owner_boundary_and_reports_a_lost_race(bool admitted)
    {
        var rig = await Rig.CreateAsync();
        var row = rig.Add(rig.Space.Id);
        var branch = rig.Branch(row.Id);
        var attachment = PendingAttachment(row.Id, branch.Id);
        rig.Attachments.Add(attachment);
        rig.PendingDeleteResult = admitted;
        var removal = rig.Backend.RemoveAttachmentAsync(row.Id, branch.Id, attachment.Id, default);
        if (admitted) await removal;
        else await Assert.ThrowsAsync<InvalidOperationException>(() => removal);
        Assert.Equal(1, rig.PendingDeleteCalls);
        Assert.Equal((attachment.Id, row.Id, branch.Id), rig.PendingDeleteOwner);
    }

    private static MessageAttachment PendingAttachment(Guid conversationId, Guid branchId) => new(
        Guid.NewGuid(), conversationId, null, branchId, "pending.txt", "pending.txt", "text/plain",
        MessageAttachmentKind.PlainText, 1, "hash", AttachmentProcessingState.Ready,
        AttachmentAnalysisMethod.TextExtracted, "draft", "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class Rig
    {
        internal readonly SpaceRegistry Registry = new(new MemorySettingsStore());
        internal SpaceDefinition Space = null!;
        internal HavenChatSpaceBackend Backend = null!;
        internal readonly Dictionary<Guid, Conversation> Rows = [];
        internal readonly Dictionary<(Guid, Guid?), ConversationDraft> Drafts = [];
        internal readonly Dictionary<Guid, ConversationBranch> Branches = [];
        internal readonly List<(Guid, int)> SpaceQueries = [];
        internal readonly List<string> SensitiveCalls = [];
        internal int GeneralQueries;
        internal bool Locked;
        internal readonly List<MessageAttachment> Attachments = [];
        internal Func<Task>? SafetyAdmission;
        internal int PendingDeleteCalls;
        internal int UnconditionalDeleteCalls;
        internal bool PendingDeleteResult = true;
        internal (Guid, Guid, Guid) PendingDeleteOwner;
        internal Func<Task<IReadOnlyList<ChatMessage>>> MessageRead = () => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        internal Func<Task<ChatSpaceModelInventory>>? InventoryRead;
        internal Func<Guid, Task<ConversationSafetySnapshot>>? SnapshotRead;
        internal readonly ModelDescriptor Model = new("local", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);

        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            rig.Space = await rig.Registry.CreateAsync("Selected");
            rig.Backend = rig.For(await rig.ContextAsync(rig.Space.Id));
            return rig;
        }
        internal async Task<SpacesContext> ContextAsync(Guid id) =>
            (await new SpacesModel(Registry).CreateOpenActionAsync(SpaceScope.ForCustom(id))).Context;
        internal Conversation Add(Guid spaceId, DateTimeOffset? updated = null)
        {
            var now = updated ?? DateTimeOffset.UtcNow;
            var row = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Same title", null, null, false, false, now, now, SpaceId: spaceId);
            Rows[row.Id] = row;
            return row;
        }
        internal ConversationBranch Branch(Guid id)
        {
            if (!Branches.TryGetValue(id, out var value))
                Branches[id] = value = new(Guid.NewGuid(), id, null, null, "Main", ConversationBranchReason.Root, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            return value;
        }
        internal HavenChatSpaceBackend For(SpacesContext context)
        {
            var conversations = StrictProxy.Create<IConversationRepository>((method, args) =>
            {
                var id = args.Length > 0 && args[0] is Guid guid ? guid : Guid.Empty;
                switch (method)
                {
                    case nameof(IConversationRepository.GetAsync): return Task.FromResult(Rows.GetValueOrDefault(id));
                    case nameof(IConversationRepository.GetBySpaceAsync):
                        var limit = (int)args[1]!; SpaceQueries.Add((id, limit));
                        return Task.FromResult<IReadOnlyList<Conversation>>(Rows.Values.Where(row => row.SpaceId == id).OrderByDescending(row => row.UpdatedAt).Take(limit).ToArray());
                    case nameof(IConversationRepository.GetUnassignedGeneralChatsAsync):
                        return Task.FromResult<IReadOnlyList<Conversation>>(Rows.Values.Where(row => row.SpaceId is null && ConversationScope.GeneralChat.Matches(row)).OrderByDescending(row => row.UpdatedAt).Take((int)args[0]!).ToArray());
                    case nameof(IConversationRepository.GetRecentInScopeAsync): GeneralQueries++; throw new InvalidOperationException("Global history is forbidden in this scoped rig.");
                    case nameof(IConversationRepository.UpsertConversationAsync):
                        var row = (Conversation)args[0]!; Rows[row.Id] = row; return Task.CompletedTask;
                    case nameof(IConversationRepository.GetMessagesAsync): SensitiveCalls.Add(method); return MessageRead();
                    case nameof(IConversationRepository.DeleteMessageAsync): SensitiveCalls.Add(method); return Task.CompletedTask;
                    default: throw new InvalidOperationException("Unexpected repository call " + method);
                }
            });
            var production = StrictProxy.Create<IConversationProductionRepository>((method, args) =>
            {
                SensitiveCalls.Add(method);
                var id = args.Length > 0 && args[0] is Guid guid ? guid : Guid.Empty;
                return method switch
                {
                    nameof(IConversationProductionRepository.GetCurrentBranchAsync) => Task.FromResult<ConversationBranch?>(Branch(id)),
                    nameof(IConversationProductionRepository.EnsureRootBranchAsync) => Task.FromResult(Branch(id)),
                    nameof(IConversationProductionRepository.GetBranchesAsync) => Task.FromResult<IReadOnlyList<ConversationBranch>>([Branch(id)]),
                    nameof(IConversationProductionRepository.GetAttachmentsAsync) => Task.FromResult<IReadOnlyList<MessageAttachment>>(Attachments.Where(item => item.ConversationId == id).ToArray()),
                    nameof(IConversationProductionRepository.GetDraftAsync) => Task.FromResult(Drafts.GetValueOrDefault((id, (Guid?)args[1]))),
                    nameof(IConversationProductionRepository.SaveDraftAsync) => SaveDraft((ConversationDraft)args[0]!),
                    _ => throw new InvalidOperationException("Unexpected production call " + method)
                };
            });
            var catalog = StrictProxy.Create<IChatSpaceModelCatalog>((method, _) => InventoryRead?.Invoke() ?? Task.FromResult(new ChatSpaceModelInventory([Model])));
            var safety = StrictProxy.Create<IConversationSafetyService>((method, args) =>
            {
                SensitiveCalls.Add(method == nameof(IConversationSafetyService.EnsureMayActAsync) ? (string)args[1]! : method);
                if (method == nameof(IConversationSafetyService.GetSnapshotAsync) && SnapshotRead is not null)
                    return SnapshotRead((Guid)args[0]!);
                if (method != nameof(IConversationSafetyService.EnsureMayActAsync)) throw new InvalidOperationException("Unexpected safety read.");
                return Locked ? Task.FromException(new ConversationSafetyLockException((Guid)args[0]!, (string)args[1]!)) : SafetyAdmission?.Invoke() ?? Task.CompletedTask;
            });
            var turns = StrictProxy.Create<IChatSpaceTurnExecutor>((method, _) => { SensitiveCalls.Add(method); return Task.CompletedTask; });
            return new(conversations, production,
                StrictProxy.Create<IConversationVersioningService>((method, _) => throw new InvalidOperationException(method)),
                StrictProxy.Create<IMessageAttachmentService>((method, args) =>
                {
                    if (method == nameof(IMessageAttachmentService.DeleteAsync))
                    {
                        UnconditionalDeleteCalls++;
                        Attachments.RemoveAll(item => item.Id == (Guid)args[0]!);
                        return Task.CompletedTask;
                    }
                    if (method != nameof(IMessageAttachmentService.TryDeletePendingAsync)) throw new InvalidOperationException(method);
                    PendingDeleteCalls++;
                    PendingDeleteOwner = ((Guid)args[0]!, (Guid)args[1]!, (Guid)args[2]!);
                    return Task.FromResult(PendingDeleteResult);
                }),
                catalog, turns, safety, context, Registry);
        }
        private Task SaveDraft(ConversationDraft draft) { Drafts[(draft.ConversationId, draft.BranchId)] = draft; return Task.CompletedTask; }
    }
}
