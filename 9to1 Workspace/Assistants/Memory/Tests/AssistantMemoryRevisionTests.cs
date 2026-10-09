using System.Diagnostics;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Approved_correction_retains_old_index_and_excludes_it_from_actual_scoped_and_generic_recall() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var old = await rig.AddAsync(binding, "violet preference original");
        var unrelated = await rig.AddAsync(binding, "violet unrelated active preference", scope: "global");
        var indexBefore = await CaptureOriginalIndex(rig, old.Id);
        await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var view = await management.ReadAsync(binding, Token); var selected = Assert.Single(view.Records);
        var preparation = await management.PrepareRevisionAsync(view, selected, CanonicalAssistantMemoryMutationKind.Correct,
            "Updated preference", "emerald corrected preference", Guid.NewGuid(), Token);
        var preview = Assert.IsType<AssistantMemoryWritePreview>(preparation.Preview);
        var commit = management.CommitAsync(preview, Token);
        await DecideMemoryRevision(rig, commit, HomeCanonicalAssistantMemoryWriteSource.CorrectAction, HomeApprovalChoice.Accept);
        var result = await commit; Assert.True(result.Saved); Assert.NotEqual(old.Id, result.Record!.Id);
        Assert.Equal(old.Id, result.Record.SupersedesId); Assert.Equal(KnowledgeRecordStatus.Corrected, result.Record.Status);
        Assert.Equal(indexBefore, await CaptureOriginalIndex(rig, old.Id));
        Assert.Equal(KnowledgeRecordStatus.Superseded, (await rig.Knowledge.GetAsync(old.Id, Token))!.Status);
        var input = await rig.PrepareAsync(binding);
        Assert.Equal(result.Record.Id, Assert.Single(await rig.Memory.ReadOriginalWithinSourceAsync(input.Input!, binding.Conversation, null, Scope, rig.Retain, Token)).Id);
        Assert.DoesNotContain(await rig.Knowledge.GetActiveLearnMeAsync(8, Token), record => record.Id == old.Id);
        Assert.Empty((await Recall(rig, old.Id, "violet")).Citations);
        Assert.NotEmpty((await Recall(rig, unrelated.Id, "violet")).Citations);
        Assert.NotEmpty((await Recall(rig, result.Record.Id, "emerald")).Citations);
        Assert.Equal(1, await CountRevisionReceipt(rig, preview.OperationId));
    });

    [Fact]
    public Task Reject_requires_its_own_Home_decision_preserves_declined_preview_then_retains_rejected_history() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var old = await rig.AddAsync(binding, "amber old preference");
        await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var view = await management.ReadAsync(binding, Token); var selected = Assert.Single(view.Records);
        var before = await rig.CaptureMemoryAsync(); var indexBefore = await CaptureOriginalIndex(rig, old.Id);
        var declined = (await management.PrepareRevisionAsync(view, selected, CanonicalAssistantMemoryMutationKind.Reject,
            "", "", Guid.NewGuid(), Token)).Preview!;
        var first = management.CommitAsync(declined, Token);
        var firstRequest = await DecideMemoryRevision(rig, first, HomeCanonicalAssistantMemoryWriteSource.RejectAction, HomeApprovalChoice.Decline);
        Assert.False((await first).Saved); Assert.False((await management.CommitAsync(declined, Token)).Saved);
        Assert.Equal(before, await rig.CaptureMemoryAsync()); Assert.Equal(0, await CountRevisionReceipt(rig, declined.OperationId));
        var accepted = (await management.PrepareRevisionAsync(view, selected, CanonicalAssistantMemoryMutationKind.Reject,
            "", "", Guid.NewGuid(), Token)).Preview!;
        var second = management.CommitAsync(accepted, Token);
        var secondRequest = await DecideMemoryRevision(rig, second, HomeCanonicalAssistantMemoryWriteSource.RejectAction, HomeApprovalChoice.Accept);
        Assert.NotEqual(firstRequest, secondRequest); Assert.True((await second).Saved);
        Assert.Empty((await management.ReadAsync(binding, Token)).Records);
        Assert.Equal(indexBefore, await CaptureOriginalIndex(rig, old.Id));
        Assert.Equal(KnowledgeRecordStatus.Rejected, (await rig.Knowledge.GetAsync(old.Id, Token))!.Status);
        Assert.Empty((await Recall(rig, old.Id, "amber")).Citations);
        Assert.Equal(1, await CountRevisionReceipt(rig, accepted.OperationId));
    });

    [Fact]
    public Task Reopen_reconstructs_scoped_owner_and_keeps_rejected_content_ineligible_without_deleting_history() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var original = await rig.AddAsync(binding, "retained after reopen");
        var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var view = await management.ReadAsync(binding, Token);
        var preview = (await management.PrepareRevisionAsync(view, Assert.Single(view.Records), CanonicalAssistantMemoryMutationKind.Reject,
            "", "", Guid.NewGuid(), Token)).Preview!;
        var commit = management.CommitAsync(preview, Token);
        await DecideMemoryRevision(rig, commit, HomeCanonicalAssistantMemoryWriteSource.RejectAction, HomeApprovalChoice.Accept);
        Assert.True((await commit).Saved); await management.CloseAndDrainAsync();
        var retained = await CaptureOriginalIndex(rig, original.Id);
        await rig.ReopenAsync();
        var currentBinding = await rig.Bridge.OpenConversationAsync(binding.Definition.Identity, binding.Conversation.Id, Token);
        await using var current = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        Assert.Empty((await current.ReadAsync(currentBinding, Token)).Records);
        Assert.Empty((await Recall(rig, original.Id, "retained")).Citations);
        Assert.Equal(retained, await CaptureOriginalIndex(rig, original.Id));
        Assert.Equal(1, await CountRevisionReceipt(rig, preview.OperationId));
    });

    [Fact]
    public Task Stale_raw_snapshot_after_preview_rolls_back_with_exact_no_effect_decision_and_no_replay() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var old = await rig.AddAsync(binding, "earlier preference");
        await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var view = await management.ReadAsync(binding, Token);
        var preview = (await management.PrepareRevisionAsync(view, Assert.Single(view.Records), CanonicalAssistantMemoryMutationKind.Correct,
            "Replacement", "replacement preference", Guid.NewGuid(), Token)).Preview!;
        // A genuine concurrent stored edit changes a column captured by the original
        // producer. Do not infer staleness from a fabricated revision or mutable DTO.
        await ExecuteRevisionFixture(rig, "ALTER TABLE knowledge_record_details ADD COLUMN retained_extension TEXT;");
        await ExecuteRevisionFixture(rig, "UPDATE knowledge_record_details SET retained_extension='concurrent metadata' WHERE id=$id;", old.Id);
        var before = await rig.CaptureMemoryAsync();
        var commit = management.CommitAsync(preview, Token);
        await DecideMemoryRevision(rig, commit, HomeCanonicalAssistantMemoryWriteSource.CorrectAction, HomeApprovalChoice.Accept);
        var result = await commit; Assert.False(result.Saved); Assert.Contains("changed", result.Reason);
        Assert.Equal(before, await rig.CaptureMemoryAsync()); Assert.Equal(0, await CountRevisionReceipt(rig, preview.OperationId));
        Assert.False((await management.CommitAsync(preview, Token)).Saved);
        Assert.Equal(AssistantMemoryWriteState.Declined, management.ObserveOriginalWrite(preview).State);
        Assert.NotEmpty((await Recall(rig, old.Id, "earlier")).Citations);
    });

    [Fact]
    public Task Foreign_record_view_invalid_kind_and_missing_selection_decline_without_authority_or_effect() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); await rig.AddAsync(binding, "owned preference");
        await using var first = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        await using var other = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var view = await first.ReadAsync(binding, Token); var selected = Assert.Single(view.Records); var before = await rig.CaptureMemoryAsync();
        Assert.Null((await other.PrepareRevisionAsync(view, selected, CanonicalAssistantMemoryMutationKind.Reject, "", "", Guid.NewGuid(), Token)).Preview);
        Assert.Null((await first.PrepareRevisionAsync(view, selected with { }, CanonicalAssistantMemoryMutationKind.Reject, "", "", Guid.NewGuid(), Token)).Preview);
        Assert.Null((await first.PrepareRevisionAsync(view, selected, (CanonicalAssistantMemoryMutationKind)999, "", "", Guid.NewGuid(), Token)).Preview);
        Assert.Null((await first.PrepareRevisionAsync(view, selected, CanonicalAssistantMemoryMutationKind.Correct, "", "", Guid.NewGuid(), Token)).Preview);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    });

    [Fact]
    public Task Late_SQL_failure_rolls_back_correction_state_new_record_and_index_but_remains_unknown_close_failure() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); await rig.AddAsync(binding, "preserved original");
        var input = await rig.PrepareAsync(binding);
        var records = await rig.Memory.ReadOriginalWithinSourceAsync(input.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        var prepared = await rig.Memory.PrepareOriginalRevisionWithinSourceAsync(input.Input!, binding.Conversation, Assert.Single(records),
            CanonicalAssistantMemoryMutationKind.Correct, "Correction", "new corrected content", Guid.NewGuid(), Scope, rig.Retain, Token);
        Assert.NotNull(prepared.Intent);
        await ExecuteRevisionFixture(rig, "CREATE TRIGGER retained_fail_revision_receipt BEFORE INSERT ON settings WHEN NEW.key LIKE 'canonical.sqlite.assistant-memory.revision.v1.%' BEGIN SELECT RAISE(ABORT,'actual late revision receipt refusal'); END;");
        var before = await rig.CaptureMemoryAsync();
        var commit = rig.Memory.CommitOriginalWriteWithinSourceAsync(prepared.Intent!, Scope, rig.Retain, Token);
        await DecideMemoryRevision(rig, commit, HomeCanonicalAssistantMemoryWriteSource.CorrectAction, HomeApprovalChoice.Accept);
        await Assert.ThrowsAnyAsync<Exception>(() => commit); Assert.False(rig.Memory.IsAcknowledgedOriginalWriteRefusal(commit));
        Assert.Equal(before, await rig.CaptureMemoryAsync()); Assert.Equal(0, await CountRevisionReceipt(rig, prepared.Intent!.OperationId));
        var memoryClose = rig.Memory.CloseAndDrainAsync(); await Assert.ThrowsAnyAsync<Exception>(() => memoryClose); rig.ExpectedMemoryClose = memoryClose;
        var homeClose = rig.OriginalMemoryWrites.CloseAndDrainOriginalAsync(); await Assert.ThrowsAnyAsync<Exception>(() => homeClose); rig.ExpectedWriteClose = homeClose;
        var storeClose = rig.Store.CloseAndDrainAsync(); await Assert.ThrowsAnyAsync<Exception>(() => storeClose); rig.ExpectedStoreClose = storeClose;
    });

    [Fact]
    public Task Ordinary_legacy_recall_needs_no_lifecycle_schema_but_initialized_missing_schema_refuses() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var record = await rig.AddAsync(binding, "ordinary active recall", scope: "global");
        Assert.NotEmpty((await Recall(rig, record.Id, "ordinary")).Citations);
        await using (var connection = await rig.Database.OpenAsync(Token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE name='retrieval_source_states';";
            Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(Token)));
        }
        await ExecuteRevisionFixture(rig, "INSERT INTO settings(key,value,updated_at) VALUES('canonical.retrieval.source-states.v1','1','fixture');");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Recall(rig, record.Id, "ordinary"));
        Assert.Equal(KnowledgeRecordStatus.Active, (await rig.Knowledge.GetAsync(record.Id, Token))!.Status);
    });

    private static Task<RetrievalResult> Recall(Rig rig, Guid record, string text) =>
        new RetrievalIndexService(rig.Database, new LocalHashEmbeddingService()).SearchAsync(
            new(text, [new(RetrievalScopeKind.Collection, record)], IncludeVectorSearch: false), Token);
    private static async Task ExecuteRevisionFixture(Rig rig, string sql, Guid? id = null)
    {
        await using var connection = await rig.Database.OpenAsync(Token); await using var command = connection.CreateCommand();
        command.CommandText = sql; if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString());
        await command.ExecuteNonQueryAsync(Token);
    }
    private static async Task<long> CountRevisionReceipt(Rig rig, Guid operation)
    {
        await using var connection = await rig.Database.OpenAsync(Token); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM settings WHERE key=$key;";
        command.Parameters.AddWithValue("$key", "canonical.sqlite.assistant-memory.revision.v1." + operation.ToString("N"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(Token));
    }
    private static async Task<string> CaptureOriginalIndex(Rig rig, Guid record)
    {
        await using var connection = await rig.Database.OpenAsync(Token); var result = new List<string>();
        foreach (var sql in new[] { "SELECT * FROM retrieval_documents WHERE source_type='knowledge' AND source_id=$id ORDER BY id;",
            "SELECT c.* FROM retrieval_chunks c JOIN retrieval_documents d ON d.id=c.document_id WHERE d.source_type='knowledge' AND d.source_id=$id ORDER BY c.id;" })
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("$id", record.ToString());
            await using var reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token)) result.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue).ToArray()));
        }
        return JsonSerializer.Serialize(result);
    }
    private static async Task<string> DecideMemoryRevision(Rig rig, Task actual, string action, HomeApprovalChoice choice)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            var state = await rig.Permissions.GetSnapshotAsync(cancellationToken: Token);
            var matches = state.PendingRequests.Where(value => value.Scope.ActionName == action).ToArray();
            if (matches.Length != 0)
            {
                var request = Assert.Single(matches); Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.True((await rig.Permissions.DecideAsync(request.RequestId, choice, cancellationToken: Token)).Succeeded);
                return request.RequestId;
            }
            if (actual.IsCompleted) { await actual; throw new InvalidOperationException("Revision completed without its actual distinct Home approval."); }
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The genuine memory revision Home action was not observed.");
    }
}
