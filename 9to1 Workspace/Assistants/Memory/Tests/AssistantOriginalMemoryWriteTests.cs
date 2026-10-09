using System.Diagnostics;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Individually_approved_memory_write_commits_same_knowledge_index_and_receipt() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync();
        await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var initial = await management.ReadAsync(binding, Token);
        Assert.True(initial.IsAvailable); Assert.Empty(initial.Records);
        Assert.Contains("first approved", initial.Reason);
        var input = await rig.PrepareAsync(binding);
        var intent = await rig.Memory.PrepareOriginalWriteWithinSourceAsync(input.Input!, binding.Conversation,
            "Preferred detail", "Use short concrete examples", Guid.NewGuid(), Scope, rig.Retain, Token);
        var write = rig.Memory.CommitOriginalWriteWithinSourceAsync(intent, Scope, rig.Retain, Token);
        await DecideMemoryWrite(rig, write, HomeApprovalChoice.Accept);
        var receipt = await write;
        Assert.Same(intent, receipt.OriginalIntent); Assert.Equal(intent.Candidate.Id, receipt.Record.Id);
        Assert.Same(write, rig.Memory.CommitOriginalWriteWithinSourceAsync(intent, Scope, rig.Retain, Token));
        var records = await rig.Memory.ReadOriginalWithinSourceAsync(input.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        var saved = Assert.Single(records); Assert.Equal(intent.Candidate.Id, saved.Id);
        Assert.Equal(intent.OriginalStorageScope, saved.Scope); Assert.Equal(KnowledgePrivacyClass.Private, saved.PrivacyClass);
        await using var connection = await rig.Database.OpenAsync(Token); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM knowledge_records WHERE id=$record)+(SELECT COUNT(*) FROM retrieval_documents WHERE id=$document)+(SELECT COUNT(*) FROM settings WHERE key=$key);";
        command.Parameters.AddWithValue("$record", saved.Id.ToString()); command.Parameters.AddWithValue("$document", receipt.OriginalRetrievalDocument.Id.ToString());
        command.Parameters.AddWithValue("$key", "canonical.sqlite.assistant-memory.create.v1." + intent.OperationId.ToString("N"));
        Assert.Equal(3L, Convert.ToInt64(await command.ExecuteScalarAsync(Token)));
    });

    [Fact]
    public Task Individual_Home_decline_has_zero_writes_and_issuer_proved_terminal_refusal() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); await rig.AddAsync(binding, "preserved baseline", scope: "global");
        var before = await rig.CaptureMemoryAsync(); var input = await rig.PrepareAsync(binding);
        var intent = await rig.Memory.PrepareOriginalWriteWithinSourceAsync(input.Input!, binding.Conversation,
            "Declined preference", "Do not persist this declined draft", Guid.NewGuid(), Scope, rig.Retain, Token);
        var actual = rig.Memory.CommitOriginalWriteWithinSourceAsync(intent, Scope, rig.Retain, Token);
        await DecideMemoryWrite(rig, actual, HomeApprovalChoice.Decline);
        await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.True(rig.Memory.IsAcknowledgedOriginalWriteRefusal(actual));
        Assert.Equal(before, await rig.CaptureMemoryAsync());
        Assert.False(rig.Memory.IsAcknowledgedOriginalWriteRefusal(Task.FromException(actual.Exception!.InnerExceptions[0])));
        await rig.Memory.CloseAndDrainAsync();
    });

    [Fact]
    public Task Actual_Den_revision_pin_serializes_later_configuration_write() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var home = await rig.Home.OpenAsync(Token);
        var definition = (await home.Den.GetAsync<AgentDefinitionRecord>(binding.Definition.Identity.NamespaceId,
            binding.Definition.Identity.DefinitionId, Token))!;
        var session = (await home.Den.GetAsync<SessionRecord>(binding.Definition.Identity.NamespaceId,
            binding.DenSessionId, Token))!;
        var pin = await home.Den.Store.PinOriginalAssistantRevisionsAsync(definition, session,
            home.Den.AccessPolicy, home.Den.PrincipalId, Scope, rig.Retain, null, Token);
        var update = rig.Bridge.UpdateAsync(binding.Definition.Identity, binding.Definition.Revision,
            binding.Definition.Configuration with { Description = "later configuration" }, Guid.NewGuid(), Token);
        pin.DemandOriginalPinnedRevisions(); Assert.False(update.IsCompleted);
        var close = pin.CloseAndDrainAsync(); Assert.Same(close, pin.CloseAndDrainAsync()); await close;
        Assert.Equal(binding.Definition.Revision + 1, (await update).Revision);
        Assert.Throws<ObjectDisposedException>(pin.DemandOriginalPinnedRevisions);
    });

    private static async Task DecideMemoryWrite(Rig rig, Task actual, HomeApprovalChoice choice, Action<string>? observeOriginalRequest = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            var snapshot = await rig.Permissions.GetSnapshotAsync(cancellationToken: Token);
            var requests = snapshot.PendingRequests.Where(value => value.Scope.ActionName == HomeCanonicalAssistantMemoryWriteSource.WriteAction).ToArray();
            if (requests.Length != 0)
            {
                var request = Assert.Single(requests); Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.True(request.Policy.RequiresPerActionApproval);
                observeOriginalRequest?.Invoke(request.RequestId);
                Assert.True((await rig.Permissions.DecideAsync(request.RequestId, choice, cancellationToken: Token)).Succeeded); return;
            }
            if (actual.IsCompleted) { await actual; throw new InvalidOperationException("The write completed without its actual manual approval request."); }
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The actual Home memory write review was not observed.");
    }
}
