using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Application;
using HavenOS.Apps.Assistants.NativeUI;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Migration;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

// The Desktop owning graph already references both actual modules. The maintained
// Memory rig still has no Migration project reference or alternative authority.
public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Rendered_migrated_memory_uses_actual_source_origin_and_keeps_legacy_rows_read_only() => RunMigratedAsync(async rig =>
    {
        KnowledgeRecord? preserved = null;
        var binding = await CreateMigratedMemoryBindingAsync(rig, async id =>
            preserved = await AddPreservedLegacyMemoryAsync(rig, id, "Preserved visible preference"));
        Assert.NotNull(preserved);
        var own = await rig.AddAsync(binding, "Current Assistant preference");
        var before = await rig.CaptureMemoryAsync();
        await WithActualMemorySurface(rig, async (window, surface, unusedWorkspace, management) =>
        {
            await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
            await surface.Bindings.DispatchAsync("assistants.memory.open", null, Token);
            await FlushNativeMemoryUi(window);
            Assert.True(surface.MemoryBindings.TryGetValue("MemoryRecords", out var value));
            var rows = Assert.IsAssignableFrom<IReadOnlyList<AssistantsMemoryCuiBindings.MemoryRow>>(value);
            var legacyRow = Assert.Single(rows, row => row.Id == preserved.Id);
            var currentRow = Assert.Single(rows, row => row.Id == own.Id);
            Assert.True(surface.MemoryBindings.TryGetItemValue(legacyRow, "Origin", out var origin));
            Assert.Equal("Preserved saved Agent memory · read-only", origin);
            Assert.True(surface.MemoryBindings.TryGetItemValue(legacyRow, "CanChange", out var legacyChange)); Assert.Equal(false, legacyChange);
            Assert.True(surface.MemoryBindings.TryGetItemValue(currentRow, "CanChange", out var currentChange)); Assert.Equal(true, currentChange);
            Assert.False(surface.MemoryBindings.TryGetItemValue(new AssistantsMemoryCuiBindings.MemoryRow(legacyRow.Original), "Origin", out _));
            var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
            Assert.Contains("Preserved saved Agent memory · read-only", labels);
            var correct = window.GetVisualDescendants().OfType<Button>().Where(button => button.Content as string == "Correct this memory").ToArray();
            var reject = window.GetVisualDescendants().OfType<Button>().Where(button => button.Content as string == "Stop recalling this memory").ToArray();
            Assert.Equal(rows.Count, correct.Length); Assert.Equal(rows.Count, reject.Length);
            var legacyIndex = Array.FindIndex(rows.ToArray(), row => ReferenceEquals(row, legacyRow));
            var ownIndex = Array.FindIndex(rows.ToArray(), row => ReferenceEquals(row, currentRow));
            Assert.False(correct[legacyIndex].IsEnabled); Assert.False(reject[legacyIndex].IsEnabled);
            Assert.True(correct[ownIndex].IsEnabled); Assert.True(reject[ownIndex].IsEnabled);
            await Assert.ThrowsAsync<InvalidOperationException>(() => surface.MemoryBindings.DispatchAsync(
                "assistants.memory.correct", legacyRow, Token).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => surface.MemoryBindings.DispatchAsync(
                "assistants.memory.reject", legacyRow, Token).AsTask());
            await surface.MemoryBindings.DispatchAsync("assistants.memory.correct", currentRow, Token);
            await FlushNativeMemoryUi(window);
            Assert.False(correct[ownIndex].IsEnabled); Assert.False(reject[ownIndex].IsEnabled);
            await surface.MemoryBindings.DispatchAsync("assistants.memory.discard", null, Token);
            await FlushNativeMemoryUi(window);
            Assert.True(correct[ownIndex].IsEnabled); Assert.False(correct[legacyIndex].IsEnabled);
            // The actual producer independently declines even when called without UI.
            var view = await management.ReadAsync(binding, Token);
            var original = Assert.Single(view.Records, record => record.Id == preserved.Id);
            Assert.True(view.IsPreservedLegacyRecord(original));
            Assert.False(view.IsPreservedLegacyRecord(original with { }));
            var outcome = await management.PrepareRevisionAsync(view, original,
                CanonicalAssistantMemoryMutationKind.Reject, original.Title, original.Summary, Guid.NewGuid(), Token);
            Assert.Null(outcome.Preview); Assert.Contains("read-only", outcome.Reason);
            Assert.Equal(before, await rig.CaptureMemoryAsync());
        });
    });

    [Fact]
    public Task Current_explicit_migration_recalls_preserved_memory_after_real_import_and_reopen() => RunMigratedAsync(async rig =>
    {
        KnowledgeRecord? expected = null; string? before = null;
        var binding = await CreateMigratedMemoryBindingAsync(rig, async id =>
        {
            expected = await AddPreservedLegacyMemoryAsync(rig, id, "Preserved original local preference");
            await AddPreservedLegacyMemoryAsync(rig, Guid.NewGuid().ToString("D"), "Unrelated saved Agent");
            await AddPreservedLegacyMemoryAsync(rig, id, "Unadmitted project", project: "foreign-project");
            await AddPreservedLegacyMemoryAsync(rig, id, "Foreign app", app: "foreign-app");
            await AddPreservedLegacyMemoryAsync(rig, id, "Sensitive original", privacy: KnowledgePrivacyClass.Sensitive);
            await AddPreservedLegacyMemoryAsync(rig, id, "Expired original", expires: DateTimeOffset.UtcNow.AddDays(-1));
            await AddPreservedLegacyMemoryAsync(rig, id, "Global original", scope: "global");
            before = await rig.CaptureMemoryAsync();
        });
        Assert.NotNull(expected); Assert.Equal(before, await rig.CaptureMemoryAsync());
        var sourceBefore = await ReadLegacyInstructionsAsync(rig, binding.Definition.Identity.DefinitionId);
        var prepared = await rig.PrepareAsync(binding); Assert.True(prepared.IsPrepared, prepared.Reason);
        var records = await rig.Memory.ReadOriginalWithinSourceAsync(prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        var actual = Assert.Single(records); Assert.Equal(expected.Id, actual.Id); Assert.Equal(expected.Summary, actual.Summary);
        Assert.Equal(expected.PrivacyClass, actual.PrivacyClass); Assert.Equal(expected.Sources, actual.Sources); Assert.Equal("agent", actual.Scope);
        await rig.Memory.ValidateOriginalWithinSourceAsync(prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
        Assert.Equal(sourceBefore, await ReadLegacyInstructionsAsync(rig, binding.Definition.Identity.DefinitionId));

        await rig.ReopenAsync();
        var reopened = await rig.Bridge.OpenConversationAsync(binding.Definition.Identity, binding.Conversation.Id, Token);
        var fresh = await rig.PrepareAsync(reopened); Assert.True(fresh.IsPrepared, fresh.Reason);
        Assert.False(rig.Memory.IsIssuedOriginalInput(prepared.Input!));
        Assert.Equal(expected.Id, Assert.Single(await rig.Memory.ReadOriginalWithinSourceAsync(
            fresh.Input!, reopened.Conversation, null, Scope, rig.Retain, Token)).Id);
        // An ordinary saved configuration revision retains genuine migration lineage,
        // while a fresh canonical binding/marker is still required.
        var edited = await rig.Bridge.UpdateAsync(reopened.Definition.Identity, reopened.Definition.Revision,
            reopened.Definition.Configuration with { Name = "Current migrated preference owner" }, Guid.NewGuid(), Token);
        var current = await rig.Bridge.OpenConversationAsync(edited.Identity, reopened.Conversation.Id, Token);
        var currentInput = await rig.PrepareAsync(current); Assert.True(currentInput.IsPrepared, currentInput.Reason);
        Assert.Equal(expected.Id, Assert.Single(await rig.Memory.ReadOriginalWithinSourceAsync(
            currentInput.Input!, current.Conversation, null, Scope, rig.Retain, Token)).Id);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    });

    [Fact]
    public Task Real_legacy_import_and_classification_do_not_grant_assistant_memory() => RunMigratedAsync(async rig =>
    {
        var binding = await CreateMigratedMemoryBindingAsync(rig);
        await rig.AddAsync(binding, "Retained but unadmitted legacy preference", scope: "agent");
        var before = await rig.CaptureMemoryAsync();
        var prepared = await rig.PrepareAsync(binding);
        Assert.False(prepared.IsPrepared); Assert.Null(prepared.Input); Assert.Contains("Home", prepared.Reason);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    }, importMemory: false);

    [Fact]
    public Task Changed_original_saved_agent_revokes_prepared_legacy_read_without_rewriting_memory() => RunMigratedAsync(async rig =>
    {
        var binding = await CreateMigratedMemoryBindingAsync(rig);
        await rig.AddAsync(binding, "Preserved preference", scope: "agent");
        var before = await rig.CaptureMemoryAsync();
        var prepared = await rig.PrepareAsync(binding); Assert.True(prepared.IsPrepared, prepared.Reason);
        await ExecuteLegacyMemorySqlAsync(rig, "UPDATE agents SET instructions='Actual later legacy change' WHERE id=$id;", binding.Definition.Identity.DefinitionId);
        var read = rig.Memory.ReadOriginalWithinSourceAsync(prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        await Assert.ThrowsAnyAsync<Exception>(() => read); Assert.True(read.IsFaulted);
        var close = rig.Memory.CloseAndDrainAsync(); await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.True(close.IsFaulted); rig.ExpectedMemoryClose = close;
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    });

    [Fact]
    public Task Composed_legacy_source_does_not_turn_a_matching_agent_id_into_provenance() => RunMigratedAsync(async rig =>
    {
        var binding = await rig.CreateAsync();
        await SeedLegacyMemoryAgentAsync(rig, binding.Definition.Identity.DefinitionId);
        await ImportLegacyMemoryStoreAsync(rig);
        await rig.AddAsync(binding, "Matching GUID without canonical migration", scope: "agent");
        var expected = await rig.AddAsync(binding, "Actual full identity memory");
        var before = await rig.CaptureMemoryAsync();
        var prepared = await rig.PrepareAsync(binding); Assert.True(prepared.IsPrepared, prepared.Reason);
        Assert.Equal(expected.Id, Assert.Single(await rig.Memory.ReadOriginalWithinSourceAsync(
            prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token)).Id);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    });

    private static async Task RunMigratedAsync(Func<Rig, Task> test, bool importMemory = true)
    {
        var rig = new Rig((store, profiles) => new LegacySavedAgentSqliteSource(store, profiles));
        var failures = new List<Exception>();
        try { await rig.InitializeAsync(true, importMemory); await test(rig); } catch (Exception failure) { failures.Add(failure); }
        try { await rig.CloseAsync(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual migrated memory fixture retained at " + rig.Root, failures);
    }

    private static async Task<AssistantConversationBinding> CreateMigratedMemoryBindingAsync(Rig rig, Func<string, Task>? seedOriginalMemories = null)
    {
        var id = Guid.NewGuid(); await SeedLegacyMemoryAgentAsync(rig, id.ToString("D"));
        if (seedOriginalMemories is not null) await seedOriginalMemories(id.ToString("D"));
        await ImportLegacyMemoryStoreAsync(rig);
        var migration = new LegacyAgentMigrationController((LegacySavedAgentSqliteSource)rig.OriginalLegacyEvidence!,
            rig.Home, rig.Authority, rig.Bridge);
        AssistantConversationBinding? binding = null; var failures = new List<Exception>();
        try
        {
            var preview = await migration.PreviewAsync(id, Token);
            var classified = await migration.ClassifyAsync(preview, ConfiguredIdentityKind.Assistant,
                preview.SuggestedConfiguration with { Memory = new(true), Model = new(AllowCloud: false) }, Guid.NewGuid(), Token);
            Assert.Equal(id.ToString("D"), classified.Definition.Identity.DefinitionId);
            binding = await rig.Bridge.CreateConversationAsync(classified.Definition.Identity, classified.Definition.Revision,
                Guid.NewGuid(), "Migrated local memory", Guid.NewGuid(), Token);
        }
        catch (Exception failure) { failures.Add(failure); }
        try { await migration.CloseAndDrainAsync(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
        return binding ?? throw new InvalidOperationException("Actual canonical migration returned no binding.");
    }

    private static async Task ImportLegacyMemoryStoreAsync(Rig rig)
    {
        var legacy = (LegacySavedAgentSqliteSource)rig.OriginalLegacyEvidence!;
        var setup = new HomeLocalStoreSetupSession(LegacySavedAgentSqliteSource.LegacyResourceKind,
            legacy, rig.Profiles, rig.Ownership, legacy);
        var inspected = await setup.InspectAsync(Token); Assert.False(inspected.IsOwned);
        var request = await setup.RequestImportAsync(Token);
        Assert.True((await rig.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
        await setup.CompleteImportAsync(Token);
    }
    private static async Task<KnowledgeRecord> AddPreservedLegacyMemoryAsync(Rig rig, string agentId, string summary,
        string scope = "agent", string? app = null, string? project = null,
        KnowledgePrivacyClass privacy = KnowledgePrivacyClass.Private, DateTimeOffset? expires = null)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new KnowledgeRecord(Guid.NewGuid(), KnowledgeCategory.LearnMe, "Original migration memory",
            summary, summary, privacy, 1, true, now, now, expires, "Existing explicit saved Agent preference",
            [new("retained-original", "Original local source", "conversation", null, null, now, now, null, "User")],
            Scope: scope, Origin: KnowledgeOrigin.Explicit, IsUserLocked: true, AppId: app, ProjectId: project, AgentId: agentId);
        return await rig.Knowledge.UpsertAsync(record, summary, Token);
    }
    private static Task SeedLegacyMemoryAgentAsync(Rig rig, string id) => ExecuteLegacyMemorySqlAsync(rig, """
        INSERT INTO agents(id,name,description,instructions,icon_key,preferred_model,detection_rules,permissions_json,is_built_in,is_enabled,updated_at)
        VALUES($id,'Fictional retained helper','Original saved Agent','Preserved original instructions','helper','','','{}',0,1,$now);
        """, id);
    private static async Task ExecuteLegacyMemorySqlAsync(Rig rig, string sql, string id)
    {
        await using var connection = await rig.Database.OpenAsync(Token); await using var command = connection.CreateCommand();
        command.CommandText = sql; command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(Token);
    }
    private static async Task<string> ReadLegacyInstructionsAsync(Rig rig, string id)
    {
        await using var connection = await rig.Database.OpenAsync(Token); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT instructions FROM agents WHERE id=$id;"; command.Parameters.AddWithValue("$id", id);
        return (string)(await command.ExecuteScalarAsync(Token))!;
    }
}
