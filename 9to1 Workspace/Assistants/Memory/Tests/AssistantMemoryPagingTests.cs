using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Management_pages_beyond_prompt_limit_and_older_selected_record_can_be_really_corrected() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync();
        var older = await rig.AddAsync(binding, "earliest preference to correct");
        var ids = new HashSet<Guid> { older.Id };
        for (var index = 0; index < 36; index++) ids.Add((await rig.AddAsync(binding, "newer preference " + index)).Id);
        await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var first = await management.ReadPageAsync(binding, 16, token: Token);
        Assert.Equal(16, first.Records.Count); Assert.NotNull(first.NextContinuation);
        var second = await management.ReadPageAsync(binding, 16, sameContinuation: first.NextContinuation, token: Token);
        Assert.Equal(16, second.Records.Count); Assert.NotNull(second.NextContinuation);
        var third = await management.ReadPageAsync(binding, 16, sameContinuation: second.NextContinuation, token: Token);
        Assert.Equal(5, third.Records.Count); Assert.Null(third.NextContinuation);
        var all = first.Records.Concat(second.Records).Concat(third.Records).Select(record => record.Id).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count()); Assert.True(ids.SetEquals(all));
        var prepared = await rig.PrepareAsync(binding);
        var promptRecords = await rig.Memory.ReadOriginalWithinSourceAsync(prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        Assert.True(promptRecords.Count <= MemoryInjection.MaximumRecords); Assert.DoesNotContain(promptRecords, record => record.Id == older.Id);
        var selected = Assert.Single(third.Records, record => record.Id == older.Id);
        var revision = await management.PrepareRevisionAsync(third, selected, CanonicalAssistantMemoryMutationKind.Correct,
            "Earlier preference corrected", "corrected older choice", Guid.NewGuid(), Token);
        Assert.NotNull(revision.Preview);
        var commit = management.CommitAsync(revision.Preview!, Token);
        await DecideMemoryRevision(rig, commit, HomeCanonicalAssistantMemoryWriteSource.CorrectAction, HomeApprovalChoice.Accept);
        var result = await commit; Assert.True(result.Saved); Assert.Equal(older.Id, result.Record!.SupersedesId);
        Assert.Equal(KnowledgeRecordStatus.Superseded, (await rig.Knowledge.GetAsync(older.Id, Token))!.Status);
        var searched = await management.ReadPageAsync(binding, searchText: "corrected older", token: Token);
        Assert.Equal(result.Record.Id, Assert.Single(searched.Records).Id);
    });

    [Fact]
    public Task Search_is_literal_scope_filtered_and_continuations_require_same_owner_binding_query_and_limit() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var otherBinding = await rig.CreateAsync();
        var one = await rig.AddAsync(binding, "Prefer 100% examples");
        var two = await rig.AddAsync(binding, "Prefer 100% diagrams");
        await rig.AddAsync(binding, "Prefer plain examples");
        await rig.AddAsync(otherBinding, "Foreign 100% preference");
        await rig.AddAsync(binding, "Global 100% preference", scope: "global");
        await rig.AddAsync(binding, "Expired 100% preference", expires: DateTimeOffset.UtcNow.AddMinutes(-1));
        await using var firstOwner = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        await using var otherOwner = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var before = await rig.CaptureMemoryAsync();
        var first = await firstOwner.ReadPageAsync(binding, 1, " 100% ", token: Token);
        Assert.Equal("100%", first.SearchText); Assert.Single(first.Records); Assert.NotNull(first.NextContinuation);
        var second = await firstOwner.ReadPageAsync(binding, 1, "100%", first.NextContinuation, Token);
        Assert.True(new HashSet<Guid> { one.Id, two.Id }.SetEquals(first.Records.Concat(second.Records).Select(record => record.Id)));
        Assert.Null(second.NextContinuation);
        Assert.False((await otherOwner.ReadPageAsync(binding, 1, "100%", first.NextContinuation, Token)).IsAvailable);
        Assert.False((await firstOwner.ReadPageAsync(otherBinding, 1, "100%", first.NextContinuation, Token)).IsAvailable);
        Assert.False((await firstOwner.ReadPageAsync(binding, 2, "100%", first.NextContinuation, Token)).IsAvailable);
        Assert.False((await firstOwner.ReadPageAsync(binding, 1, "plain", first.NextContinuation, Token)).IsAvailable);
        var current = await rig.Bridge.OpenConversationAsync(binding.Definition.Identity, binding.Conversation.Id, Token);
        Assert.False((await firstOwner.ReadPageAsync(current, 1, "100%", first.NextContinuation, Token)).IsAvailable);
        Assert.Empty((await firstOwner.ReadPageAsync(current, 32, "%' OR 1=1 --", token: Token)).Records);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    });

    [Fact]
    public Task Empty_or_disabled_paged_management_never_initializes_schema_or_reuses_prior_continuation() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var beforeTables = await rig.CountMemoryTablesAsync();
        await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var empty = await management.ReadPageAsync(binding, searchText: "unseen", token: Token);
        Assert.True(empty.IsAvailable); Assert.Empty(empty.Records); Assert.Null(empty.NextContinuation);
        Assert.Equal(beforeTables, await rig.CountMemoryTablesAsync());
        await rig.AddAsync(binding, "first private preference"); await rig.AddAsync(binding, "second private preference");
        var page = await management.ReadPageAsync(binding, 1, token: Token); Assert.NotNull(page.NextContinuation);
        await rig.Bridge.UpdateAsync(binding.Definition.Identity, binding.Definition.Revision,
            binding.Definition.Configuration with { Memory = new(false) }, Guid.NewGuid(), Token);
        var disabled = await rig.Bridge.OpenConversationAsync(binding.Definition.Identity, binding.Conversation.Id, Token);
        var retained = await rig.CaptureMemoryAsync();
        Assert.False((await management.ReadPageAsync(disabled, 1, sameContinuation: page.NextContinuation, token: Token)).IsAvailable);
        var refused = await management.ReadPageAsync(disabled, token: Token);
        Assert.False(refused.IsAvailable); Assert.Empty(refused.Records); Assert.Contains("disabled", refused.Reason);
        Assert.Equal(retained, await rig.CaptureMemoryAsync());
    });
}
