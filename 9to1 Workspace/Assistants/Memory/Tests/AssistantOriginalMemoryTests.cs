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
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

/// <summary>Real Home import, Den issuer, SQLite Knowledge Library and retrieval persistence.
/// No model calls, fake record source, schema-on-read or global memory fallback.</summary>
public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Disabled_memory_returns_declined_before_any_source_callback_or_query() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(new() { Name = "Disabled", Memory = new(false) });
        var callbacks = 0; var retained = new List<Task>();
        var preparation = await rig.Memory.PrepareOriginalAssistantMemoryInputWithinSourceAsync(binding,
            binding.Definition, body => { callbacks++; body(); }, retained.Add, Token);
        Assert.False(preparation.IsPrepared); Assert.Null(preparation.Input);
        Assert.Equal(0, callbacks); Assert.Empty(retained);
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task Unsupported_project_or_cloud_expansion_declines_before_memory_IO(bool project, bool cloud) => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(new() { Name = "Scoped", Memory = new(true, project), Model = new(AllowCloud: cloud) });
        var retained = new List<Task>(); var callbacks = 0;
        var result = await rig.Memory.PrepareOriginalAssistantMemoryInputWithinSourceAsync(binding, binding.Definition,
            body => { callbacks++; body(); }, retained.Add, Token);
        Assert.False(result.IsPrepared); Assert.NotEmpty(result.Reason); Assert.Empty(retained); Assert.Equal(0, callbacks);
    });

    [Fact]
    public Task Positive_read_uses_actual_agent_scope_and_preserves_original_records() => RunAsync(async rig =>
    {
        var bankId = Guid.NewGuid();
        var binding = await rig.CreateAsync(new() { Name = "Scoped preference", Memory = new(true), KnowledgeResourceIds = [bankId.ToString("D")] });
        var now = DateTimeOffset.UtcNow;
        await rig.Knowledge.CreateBankAsync(new(bankId, "Unadmitted bank", "Existing scoped bank", "agent", true, "local", "disabled", now, now), Token);
        await rig.AddAsync(binding, "bank is a stored preference, not a source grant", bankId: bankId);
        var expected = await rig.AddAsync(binding, "permitted local preference");
        await rig.AddAsync(binding, "global distractor", scope: "global");
        await rig.AddAsync(binding, "same GUID lacks migrated legacy provenance", scope: "agent");
        await rig.AddAsync(binding, "other identity", agentId: Guid.NewGuid().ToString("D"));
        await rig.AddAsync(binding, "project distractor", project: "ungranted-project");
        await rig.AddAsync(binding, "other app distractor", app: "another-app");
        await rig.AddAsync(binding, "sensitive distractor", privacy: KnowledgePrivacyClass.Sensitive);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.AddAsync(binding, "never learn distractor", privacy: KnowledgePrivacyClass.NeverLearn));
        await rig.AddAsync(binding, "expired distractor", expires: DateTimeOffset.UtcNow.AddDays(-1));
        var before = await rig.CaptureMemoryAsync();
        var prepared = await rig.PrepareAsync(binding); Assert.True(prepared.IsPrepared, prepared.Reason);
        var records = await rig.Memory.ReadOriginalWithinSourceAsync(prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        var actual = Assert.Single(records); Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.AgentId, actual.AgentId); Assert.Equal(expected.Sources, actual.Sources);
        Assert.Equal(expected.PrivacyClass, actual.PrivacyClass); Assert.Equal(expected.Scope, actual.Scope);
        await rig.Memory.ValidateOriginalWithinSourceAsync(prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    });

    [Fact]
    public Task Two_actual_assistants_do_not_share_their_scoped_memory() => RunAsync(async rig =>
    {
        var first = await rig.CreateAsync(); var second = await rig.CreateAsync();
        var firstRecord = await rig.AddAsync(first, "first assistant preference");
        var secondRecord = await rig.AddAsync(second, "second assistant preference");
        var firstInput = await rig.PrepareAsync(first); var secondInput = await rig.PrepareAsync(second);
        Assert.Equal(firstRecord.Id, Assert.Single(await rig.Memory.ReadOriginalWithinSourceAsync(firstInput.Input!, first.Conversation, null, Scope, rig.Retain, Token)).Id);
        Assert.Equal(secondRecord.Id, Assert.Single(await rig.Memory.ReadOriginalWithinSourceAsync(secondInput.Input!, second.Conversation, null, Scope, rig.Retain, Token)).Id);
    });

    [Fact]
    public Task Reopen_reissues_live_permission_and_reads_same_durable_memory_identity() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var memory = await rig.AddAsync(binding, "durable local preference");
        var prior = await rig.PrepareAsync(binding); var before = await rig.CaptureMemoryAsync();
        await rig.ReopenAsync();
        Assert.False(rig.Memory.IsIssuedOriginalInput(prior.Input!));
        var reopened = await rig.Bridge.OpenConversationAsync(binding.Definition.Identity, binding.Conversation.Id, Token);
        var current = await rig.PrepareAsync(reopened); Assert.True(current.IsPrepared, current.Reason);
        Assert.NotSame(prior.Input, current.Input);
        Assert.Equal(memory.Id, Assert.Single(await rig.Memory.ReadOriginalWithinSourceAsync(current.Input!, reopened.Conversation, null, Scope, rig.Retain, Token)).Id);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    });

    [Fact]
    public Task A_separate_canonical_catalogue_import_does_not_grant_memory() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); await rig.AddAsync(binding, "ungranted memory", scope: "agent");
        var result = await rig.PrepareAsync(binding);
        Assert.False(result.IsPrepared); Assert.Null(result.Input); Assert.Contains("Home", result.Reason);
    }, importMemory: false);

    [Fact]
    public Task Editing_configuration_invalidates_prepared_memory_before_read() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); await rig.AddAsync(binding, "revision controlled preference");
        var before = await rig.CaptureMemoryAsync(); var prepared = await rig.PrepareAsync(binding);
        await rig.Bridge.UpdateAsync(binding.Definition.Identity, binding.Definition.Revision,
            binding.Definition.Configuration with { Memory = new(false) }, Guid.NewGuid(), Token);
        var read = rig.Memory.ReadOriginalWithinSourceAsync(prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => read);
        Assert.Contains(failure.Flatten().InnerExceptions, error => error is AssistantCommandRefusedException);
        var close = rig.Memory.CloseAndDrainAsync(); Assert.Same(close, rig.Memory.CloseAndDrainAsync());
        await Assert.ThrowsAsync<AggregateException>(() => close); rig.ExpectedMemoryClose = close;
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    });

    [Fact]
    public Task Missing_memory_schema_is_not_created_by_an_admitted_read() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync();
        await rig.PreserveMemoryTableUnderOlderNameAsync();
        Assert.Equal(1, await rig.CountMemoryTablesAsync());
        var prepared = await rig.PrepareAsync(binding); Assert.True(prepared.IsPrepared, prepared.Reason);
        var read = rig.Memory.ReadOriginalWithinSourceAsync(prepared.Input!, binding.Conversation, null, Scope, rig.Retain, Token);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => read);
        Assert.Contains(failure.Flatten().InnerExceptions, error => error is SqliteException);
        var close = rig.Memory.CloseAndDrainAsync(); await Assert.ThrowsAsync<AggregateException>(() => close); rig.ExpectedMemoryClose = close;
        // The actual shared SQLite owner also retains the original failed SQL source.
        var storeClose = rig.Store.CloseAndDrainAsync(); await Assert.ThrowsAnyAsync<Exception>(() => storeClose);
        Assert.True(storeClose.IsFaulted); rig.ExpectedStoreClose = storeClose;
        Assert.Equal(1, await rig.CountMemoryTablesAsync());
    });

}
