using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Migration.Tests;

/// <summary>Actual SQLite + file Home profile/ownership + Den + existing bridge controls.
/// These checks authorize no model, tool execution or native/browser workflow claim.</summary>
public sealed partial class LegacySavedAgentMigrationTests
{
    [Fact]
    public Task Preview_reads_actual_disabled_legacy_rows_without_seeding_or_classifying() => RunAsync(async rig =>
    {
        var before = await rig.CaptureLegacyAsync();
        var page = await rig.Controller.ListPageAsync(token: Token);
        Assert.Equal(2, page.Items.Count); Assert.All(page.Items, row => Assert.Equal(LegacyAgentMigrationState.Unselected, row.State));
        var preview = await rig.Controller.PreviewAsync(rig.Second, Token);
        Assert.False(preview.Definition.IsEnabled); Assert.True(preview.CanClassify); Assert.Null(preview.PendingClassification);
        Assert.Empty((await rig.Bridge.ListAsync(Token)).Definitions);
        Assert.Equal(before, await rig.CaptureLegacyAsync());
    });

    [Theory]
    [InlineData(ConfiguredIdentityKind.Assistant)]
    [InlineData(ConfiguredIdentityKind.Specialist)]
    public Task Explicit_classification_preserves_guid_original_fields_history_memory_and_references(ConfiguredIdentityKind kind) => RunAsync(async rig =>
    {
        var before = await rig.CaptureLegacyAsync();
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        Assert.Equal(1, preview.Preserved.Runs); Assert.Equal(1, preview.Preserved.ScopedMemories);
        var run = Assert.Single((await rig.Controller.ReadPreservedLinksAsync(preview, "runs", token: Token)).Items);
        Assert.Equal(rig.RunId.ToString("D"), run.Id); Assert.Equal(rig.ConversationId.ToString("D"), run.RelatedId);
        var conversations = new ConversationRepository(rig.Database);
        Assert.Equal("Existing original conversation", (await conversations.GetAsync(rig.ConversationId, Token))!.Title);
        Assert.Equal("Existing original message", Assert.Single(await conversations.GetMessagesAsync(rig.ConversationId, Token)).Content);
        var memory = Assert.Single((await rig.Controller.ReadPreservedLinksAsync(preview, "memories", token: Token)).Items);
        Assert.Equal(rig.MemoryId.ToString("D"), memory.Id);
        Assert.Equal("Existing personal memory", (await rig.Knowledge.GetAsync(rig.MemoryId, Token))!.Summary);
        Assert.Equal("canonical://existing-resource", Assert.Single((await rig.Controller.ReadPreservedLinksAsync(preview, "references", token: Token)).Items).RelatedId);
        var result = await rig.Controller.ClassifyAsync(preview, kind, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        Assert.Equal(rig.First.ToString("D"), result.Definition.Identity.DefinitionId); Assert.Equal(kind, result.Definition.Kind);
        Assert.True(result.LegacySourceRetained); Assert.Equal(2, result.Definition.Revision);
        Assert.Equal("Original role and instructions", (await rig.Bridge.GetAsync(result.Definition.Identity, Token)).Configuration.Instructions);
        var home = await rig.Factory.OpenAsync(Token);
        var row = await home.Den.GetAsync<AgentDefinitionRecord>("personal", rig.First.ToString("D"), Token);
        var metadata = row!.ExtensionData!["assistants.savedAgentMigration.v1"];
        var raw = metadata.GetProperty("originalDefinitionJson").GetString()!;
        Assert.Contains("unknown_future_configuration", raw); Assert.Contains("retained extension", raw);
        Assert.Equal(before, await rig.CaptureLegacyAsync());
        var retainedMemory = await rig.Knowledge.GetAsync(rig.MemoryId, Token);
        Assert.Equal(rig.First.ToString("D"), retainedMemory!.AgentId);
        Assert.Equal(KnowledgePrivacyClass.Private, retainedMemory.PrivacyClass);
        Assert.Equal(KnowledgeOrigin.Explicit, retainedMemory.Origin);
        Assert.Equal(LegacyAgentMigrationState.Unselected, (await rig.Controller.PreviewAsync(rig.Second, Token)).State);
    });

    [Fact]
    public Task Stale_source_preview_refuses_without_den_write_and_joins_cleanly() => RunAsync(async rig =>
    {
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        await rig.ExecuteAsync("UPDATE agents SET instructions='Concurrent edit' WHERE id=$id;", ("$id", rig.First.ToString("D")));
        var actual = rig.Controller.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        Assert.Equal("SourceChanged", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => actual)).Code);
        Assert.True(rig.Controller.IsAcknowledgedOriginalCommandRefusal(actual));
        Assert.Empty((await rig.Bridge.ListAsync(Token)).Definitions);
        var home = await rig.Factory.OpenAsync(Token);
        Assert.Empty(await home.Den.ListAsync<AgentDefinitionRecord>("personal", Token));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Unrelated_den_id_collision_is_not_adopted_or_overwritten(bool otherRecordType) => RunAsync(async rig =>
    {
        var home = await rig.Factory.OpenAsync(Token);
        DenRecord collision = otherRecordType
            ? new SessionRecord { Id = rig.First.ToString("D"), NamespaceId = "personal", ConversationId = Guid.NewGuid().ToString("D") }
            : new AgentDefinitionRecord { Id = rig.First.ToString("D"), NamespaceId = "personal", DisplayName = "Unrelated", Version = "1" };
        var foreign = await home.Den.SaveAsync(collision, 0, "collision-test", Token);
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        Assert.Equal(LegacyAgentMigrationState.Conflict, preview.State); Assert.False(preview.CanClassify);
        var actual = rig.Controller.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => actual);
        Assert.True(rig.Controller.IsAcknowledgedOriginalCommandRefusal(actual));
        Assert.Equal(JsonSerializer.Serialize(foreign, DenJson.Options), JsonSerializer.Serialize(await home.Den.GetAsync<DenRecord>("personal", foreign.Id, Token), DenJson.Options));
    });

    [Fact]
    public Task Foreign_preview_and_specialist_personal_proactivity_refuse_without_effect() => RunAsync(async rig =>
    {
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        var other = rig.NewController();
        var foreign = other.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        Assert.Equal("ForeignPreview", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => foreign)).Code);
        Assert.True(other.IsAcknowledgedOriginalCommandRefusal(foreign));
        var personal = rig.Controller.ClassifyAsync(preview, ConfiguredIdentityKind.Specialist,
            preview.SuggestedConfiguration with { Proactive = new(Enabled: true, AllowCheckIns: true) }, Guid.NewGuid(), Token);
        Assert.Equal("SpecialistProactivityConflict", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => personal)).Code);
        Assert.True(rig.Controller.IsAcknowledgedOriginalCommandRefusal(personal));
        Assert.Empty((await rig.Bridge.ListAsync(Token)).Definitions);
    });

    [Fact]
    public Task Source_identity_and_migrated_definition_reopen_and_undo_restores_original_state() => RunAsync(async rig =>
    {
        var before = await rig.CaptureLegacyAsync();
        var sourceIdentity = await rig.Source.GetStoreIdentityAsync(Token);
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        var saved = await rig.Controller.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        await rig.ReopenAsync();
        Assert.Equal(sourceIdentity.StoreId, (await rig.Source.GetStoreIdentityAsync(Token)).StoreId);
        Assert.Equal(saved.Definition.Identity, Assert.Single((await rig.Bridge.ListAsync(Token)).Definitions).Identity);
        var reopened = await rig.Controller.PreviewAsync(rig.First, Token);
        Assert.NotNull(reopened.ClassifiedDefinition);
        Assert.Equal(saved.Definition.Identity, reopened.ClassifiedDefinition.Identity);
        var recovery = await rig.Controller.ReadRecoveryAsync(reopened.ClassifiedDefinition.Identity, Token);
        Assert.True(recovery.CanUndo, recovery.Reason);
        var recovered = await rig.Controller.UndoAsync(recovery, Guid.NewGuid(), Token);
        Assert.True(recovered.OriginalSourceRetained); Assert.True(recovered.ClassifiedDefinitionRemoved);
        Assert.Empty((await rig.Bridge.ListAsync(Token)).Definitions);
        Assert.Equal(LegacyAgentMigrationState.Recovered, (await rig.Controller.PreviewAsync(rig.First, Token)).State);
        Assert.Equal(before, await rig.CaptureLegacyAsync());
    });

    [Fact]
    public Task Later_configuration_edit_refuses_undo_and_keeps_changed_definition() => RunAsync(async rig =>
    {
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        var migrated = await rig.Controller.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        var recovery = await rig.Controller.ReadRecoveryAsync(migrated.Definition.Identity, Token);
        var edited = await rig.Bridge.UpdateAsync(migrated.Definition.Identity, migrated.Definition.Revision,
            migrated.Definition.Configuration with { Description = "Later user configuration" }, Guid.NewGuid(), Token);
        var actual = rig.Controller.UndoAsync(recovery, Guid.NewGuid(), Token);
        Assert.Equal("RevisionConflict", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => actual)).Code);
        Assert.True(rig.Controller.IsAcknowledgedOriginalCommandRefusal(actual));
        Assert.False((await rig.Controller.ReadRecoveryAsync(edited.Identity, Token)).CanUndo);
        Assert.Equal("Later user configuration", (await rig.Bridge.GetAsync(edited.Identity, Token)).Configuration.Description);
    });

    [Fact]
    public Task Continuing_conversation_prevents_removing_its_identity_during_undo() => RunAsync(async rig =>
    {
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        var migrated = await rig.Controller.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        var recovery = await rig.Controller.ReadRecoveryAsync(migrated.Definition.Identity, Token);
        var conversation = await rig.Bridge.CreateConversationAsync(migrated.Definition.Identity, migrated.Definition.Revision,
            Guid.NewGuid(), "Continuing conversation", Guid.NewGuid(), Token);
        var actual = rig.Controller.UndoAsync(recovery, Guid.NewGuid(), Token);
        Assert.Equal("HistoryChanged", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => actual)).Code);
        Assert.True(rig.Controller.IsAcknowledgedOriginalCommandRefusal(actual));
        Assert.False((await rig.Controller.ReadRecoveryAsync(migrated.Definition.Identity, Token)).CanUndo);
        Assert.Equal(conversation.Conversation.Id, Assert.Single(await rig.Bridge.ReadConversationsAsync(migrated.Definition.Identity, token: Token)).ConversationId);
    });

    [Fact]
    public Task Paging_is_bounded_and_does_not_duplicate_rows() => RunAsync(async rig =>
    {
        var first = await rig.Controller.ListPageAsync(maximum: 1, token: Token);
        Assert.Single(first.Items); Assert.NotNull(first.NextCursor);
        var second = await rig.Controller.ListPageAsync(first.NextCursor, 1, Token);
        Assert.Single(second.Items); Assert.Null(second.NextCursor);
        Assert.NotEqual(first.Items[0].LegacyAgentId, second.Items[0].LegacyAgentId);
    });

    [Fact]
    public Task Interrupted_stage_retains_original_choice_and_resumes_only_by_explicit_same_operation() => RunAsync(async rig =>
    {
        var home = await rig.Factory.OpenAsync(Token);
        var gate = new FailAfterStageAuthority(rig.Authority, home.Den, rig.First);
        var interrupted = rig.NewController(gate);
        var preview = await interrupted.PreviewAsync(rig.First, Token);
        var operation = Guid.NewGuid(); gate.Armed = true;
        var actual = interrupted.ClassifyAsync(preview, ConfiguredIdentityKind.Specialist, preview.SuggestedConfiguration, operation, Token);
        Assert.Same(gate.Failure, await Assert.ThrowsAsync<IOException>(() => actual));
        Assert.False(interrupted.IsAcknowledgedOriginalCommandRefusal(actual));
        var close = interrupted.CloseAndDrainAsync();
        Assert.Same(gate.Failure, Assert.Single((await Assert.ThrowsAsync<AggregateException>(() => close)).InnerExceptions));
        rig.ExpectedControllerClose = close;
        var staged = await rig.Controller.PreviewAsync(rig.First, Token);
        Assert.Equal(LegacyAgentMigrationState.Staged, staged.State); Assert.True(staged.CanClassify);
        Assert.Equal(operation, staged.PendingClassification!.OperationId);
        Assert.Equal(ConfiguredIdentityKind.Specialist, staged.PendingClassification.ConfirmedKind);
        var wrong = rig.Controller.ClassifyAsync(staged, ConfiguredIdentityKind.Assistant, staged.SuggestedConfiguration, Guid.NewGuid(), Token);
        Assert.Equal("StagedOperationMismatch", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => wrong)).Code);
        Assert.True(rig.Controller.IsAcknowledgedOriginalCommandRefusal(wrong));
        var resumed = await rig.Controller.ClassifyAsync(staged, staged.PendingClassification.ConfirmedKind,
            staged.SuggestedConfiguration, staged.PendingClassification.OperationId, Token);
        Assert.Equal(rig.First.ToString("D"), resumed.Definition.Identity.DefinitionId);
        Assert.Equal(2, resumed.Definition.Revision); Assert.Equal(ConfiguredIdentityKind.Specialist, resumed.Definition.Kind);
    });

    [Fact]
    public Task Actual_pending_session_writer_before_undo_keeps_the_assistant_and_conversation() => RunAsync(async rig =>
    {
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        var migrated = await rig.Controller.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        var recovery = await rig.Controller.ReadRecoveryAsync(migrated.Definition.Identity, Token);
        var conversations = new HeldConversationRepository(new ConversationRepository(rig.Database), Guid.NewGuid(), holdBeforeInsert: true);
        var bridge = rig.NewBridge(conversations);
        var creation = bridge.CreateConversationAsync(migrated.Definition.Identity, migrated.Definition.Revision,
            conversations.Target, "Held canonical creation", Guid.NewGuid(), Token);
        try
        {
            await conversations.Entered.Task.WaitAsync(Token);
            var home = await rig.Factory.OpenAsync(Token);
            var pending = Assert.Single((await home.Den.ListAsync<SessionRecord>("personal", Token))
                .Where(value => value.ConversationId == conversations.Target.ToString("D")));
            Assert.Equal("pending", pending.ExtensionData!["assistants.membership.v1"].GetProperty("publication").GetString());
            var undo = rig.Controller.UndoAsync(recovery, Guid.NewGuid(), Token);
            Assert.Equal("HistoryChanged", (await Assert.ThrowsAsync<LegacyAgentMigrationRefusedException>(() => undo)).Code);
            Assert.True(rig.Controller.IsAcknowledgedOriginalCommandRefusal(undo));
            Assert.Equal(migrated.Definition.Identity, (await rig.Bridge.GetAsync(migrated.Definition.Identity, Token)).Identity);
        }
        finally { conversations.Release.TrySetResult(); }
        var created = await creation;
        Assert.Equal(conversations.Target, created.Conversation.Id);
        Assert.NotNull(await new ConversationRepository(rig.Database).GetAsync(conversations.Target, Token));
        Assert.Equal(1, conversations.PhysicalInsertCalls);
    });

    [Fact]
    public Task Undo_writer_before_stale_session_reservation_retains_pending_without_conversation_adoption() => RunAsync(async rig =>
    {
        var preview = await rig.Controller.PreviewAsync(rig.First, Token);
        var migrated = await rig.Controller.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant, preview.SuggestedConfiguration, Guid.NewGuid(), Token);
        var recovery = await rig.Controller.ReadRecoveryAsync(migrated.Definition.Identity, Token);
        var conversations = new HeldConversationRepository(new ConversationRepository(rig.Database), Guid.NewGuid(), holdBeforeInsert: false);
        var bridge = rig.NewBridge(conversations);
        var creation = bridge.CreateConversationAsync(migrated.Definition.Identity, migrated.Definition.Revision,
            conversations.Target, "Held canonical lookup", Guid.NewGuid(), Token);
        try
        {
            await conversations.Entered.Task.WaitAsync(Token);
            var undone = await rig.Controller.UndoAsync(recovery, Guid.NewGuid(), Token);
            Assert.True(undone.ClassifiedDefinitionRemoved);
        }
        finally { conversations.Release.TrySetResult(); }
        await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => creation);
        var home = await rig.Factory.OpenAsync(Token);
        var pending = Assert.Single((await home.Den.ListAsync<SessionRecord>("personal", Token))
            .Where(value => value.ConversationId == conversations.Target.ToString("D")));
        Assert.Equal("pending", pending.ExtensionData!["assistants.membership.v1"].GetProperty("publication").GetString());
        Assert.Null(await new ConversationRepository(rig.Database).GetAsync(conversations.Target, Token));
        Assert.Equal(0, conversations.PhysicalInsertCalls);
        Assert.Empty((await rig.Bridge.ListAsync(Token)).Definitions);
    });

    [Fact]
    public async Task Actual_sqlite_source_requires_its_own_home_import_even_when_den_is_owned()
    {
        var rig = new Rig(); var failures = new List<Exception>();
        try
        {
            await rig.InitializeAsync(true, importSource: false);
            var actual = rig.Controller.ListPageAsync(token: Token);
            var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => actual);
            Assert.False(rig.Controller.IsAcknowledgedOriginalCommandRefusal(actual));
            var close = rig.Controller.CloseAndDrainAsync();
            var retained = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.Same(refusal, Assert.Single(retained.InnerExceptions));
            // Explicitly observed denial is retained, not globally waived by its exception type.
            rig.ExpectedControllerClose = close;
            var home = await rig.Factory.OpenAsync(Token);
            Assert.Empty(await home.Den.ListAsync<AgentDefinitionRecord>("personal", Token));
        }
        catch (Exception error) { failures.Add(error); }
        finally { try { await rig.CloseAsync(); } catch (Exception error) { failures.Add(error); } }
        Finish(rig, failures);
    }

    /// <summary>Holds an actual canonical source boundary while preserving its SAME physical
    /// SQLite repository and insertion acknowledgement. No synthetic rows or successful writes.</summary>
    private sealed class HeldConversationRepository(ConversationRepository original, Guid target, bool holdBeforeInsert)
        : IConversationRepository, IConversationCreateOnlyRepository
    {
        internal Guid Target => target;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int PhysicalInsertCalls;
        public async Task<Conversation?> GetAsync(Guid id, CancellationToken token)
        {
            var actual = original.GetAsync(id, token); var result = await actual;
            if (id == target && !holdBeforeInsert) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return result;
        }
        public async Task<bool> TryCreateConversationAsync(Conversation conversation, CancellationToken token)
        {
            if (conversation.Id == target && holdBeforeInsert) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            Interlocked.Increment(ref PhysicalInsertCalls);
            return await original.TryCreateConversationAsync(conversation, token);
        }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => original.GetRecentAsync(mode, limit, token);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => original.GetMessagesAsync(id, token);
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) => original.UpsertConversationAsync(value, token);
        public Task AddMessageAsync(ChatMessage value, CancellationToken token) => original.AddMessageAsync(value, token);
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => original.DeleteConversationAsync(id, token);
    }
    private sealed class FailAfterStageAuthority(IResourceStoreOwnershipReceiptAuthority original, DulcheDen den, Guid id)
        : IResourceStoreOriginalScopedOwnershipAuthority
    {
        internal bool Armed;
        internal readonly IOException Failure = new("Deliberately held source failed after the actual stage was saved.");
        public ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedAsync(string kind, string storeId, CancellationToken token) =>
            original.GetVerifiedAsync(kind, storeId, token);
        public ValueTask<bool> IsCurrentAsync(VerifiedResourceStoreOwnership captured, AuthenticatedResourceActor actor, CancellationToken token) =>
            original.IsCurrentAsync(captured, actor, token);
        public ValueTask<VerifiedResourceStoreOwnership?> GetVerifiedWithinOriginalSourceAsync(string kind, string storeId,
            Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            ((IResourceStoreOriginalScopedOwnershipAuthority)original).GetVerifiedWithinOriginalSourceAsync(kind, storeId, scope, retain, token);
        public async ValueTask<bool> IsCurrentWithinOriginalSourceAsync(VerifiedResourceStoreOwnership captured, AuthenticatedResourceActor actor,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var callbacks = new LegacySavedAgentSqliteSource.Callbacks(scope, retain);
            var permitted = await callbacks.Read(() => ((IResourceStoreOriginalScopedOwnershipAuthority)original)
                .IsCurrentWithinOriginalSourceAsync(captured, actor, scope, retain, token).AsTask());
            if (permitted && Armed)
            {
                var row = await callbacks.Read(() => den.GetAsync<AgentDefinitionRecord>("personal", id.ToString("D"), token));
                if (row?.ExtensionData?.ContainsKey("assistants.savedAgentMigration.v1") == true &&
                    !row.ExtensionData.ContainsKey("assistants.definition.v1"))
                { Armed = false; throw Failure; }
            }
            return permitted;
        }
    }

}
