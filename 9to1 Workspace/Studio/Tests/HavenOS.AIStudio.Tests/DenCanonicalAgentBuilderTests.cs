using System.Text.Json;
using Dulche.Runtime.Agents;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class DenCanonicalAgentBuilderTests
{
    [Fact]
    public async Task Create_open_edit_and_reopen_use_one_Den_record_and_preserve_avatar_and_execution_declarations()
    {
        await using var f = await Fixture.CreateAsync();
        var config = new DenAgentBuilderConfiguration
        {
            ModelPolicy = new(false, "exact-model", "exact-provider", false, false, new HashSet<string>(StringComparer.Ordinal) { "cap.exact.one", "cap.exact.two" }),
            Budget = new(MaxTokens: 500, MaxSteps: 20, MaxToolCalls: 3),
            ToolIds = ["tool.explicit"], SkillIds = ["skill.explicit"], PluginIds = ["plugin.explicit"],
            McpCapabilityIds = ["mcp.explicit"], AllowedPermissions = ["permission.upper-bound"]
        };
        var created = await f.Builder.CreateAsync(Draft(config), default);
        Assert.True(created.IsSuccess); Assert.NotEqual(Guid.Empty, created.Value!.AgentId);
        var id = created.Value.AgentId.ToString("D");
        var original = (await f.Den.GetAsync<AgentDefinitionRecord>("personal", id))!;
        var presentation = new AgentPresentationDefinition(1, AgentIconPresentation.Static, "asset:static", "Canonical Agent",
            "idle", [new("idle", "Idle", "asset:static", false)], [], []);
        await f.Den.SaveAsync(original with { Presentation = presentation }, original.Revision, "assign-presentation");
        var opened = await f.Builder.OpenAsync(created.Value.AgentId, default);
        Assert.True(opened.IsSuccess); Assert.Equal("Purpose", opened.Value!.Description);
        Assert.True(opened.Value.IsDraft);
        var reopenedConfig = DenCanonicalAgentBuilderAdapter.DecodeConfiguration(opened.Value.ConfigurationJson);
        Assert.Equal(new[] { "cap.exact.one", "cap.exact.two" }, reopenedConfig.ModelPolicy.RequiredCapabilities!.Order(StringComparer.Ordinal));
        var before = (await f.Den.GetAsync<AgentDefinitionRecord>("personal", id))!;
        var changed = opened.Value with { Description = "Revised purpose", Instructions = "Revised instructions" };
        var saved = await f.Builder.UpdateDraftAsync(created.Value.AgentId, opened.Value.DefinitionRevision, changed, default);
        Assert.True(saved.IsSuccess); Assert.True(saved.Value!.DefinitionRevision > opened.Value.DefinitionRevision);
        var reopened = await f.Builder.OpenAsync(created.Value.AgentId, default);
        Assert.True(reopened.IsSuccess); Assert.Equal("Revised purpose", reopened.Value!.Description);
        Assert.Equal("Revised instructions", reopened.Value.Instructions);
        var after = (await f.Den.GetAsync<AgentDefinitionRecord>("personal", id))!;
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.NamespaceId, after.NamespaceId);
        Assert.Equal(JsonSerializer.Serialize(before.Presentation), JsonSerializer.Serialize(after.Presentation));
        Assert.Equal(before.ToolIds, after.ToolIds); Assert.Equal(before.SkillIds, after.SkillIds);
        Assert.Equal(before.PluginIds, after.PluginIds); Assert.Equal(before.McpCapabilityIds, after.McpCapabilityIds);
        Assert.Equal(before.AllowedPermissions, after.AllowedPermissions); Assert.Equal(before.Enabled, after.Enabled);
        Assert.False(after.Enabled); Assert.Single(await f.Den.ListAsync<AgentDefinitionRecord>("personal"));
        var stale = await f.Builder.UpdateDraftAsync(created.Value.AgentId, opened.Value.DefinitionRevision, changed, default);
        Assert.False(stale.IsSuccess); Assert.Equal("Conflict", stale.Error!.Code);
        Assert.Equal(JsonSerializer.Serialize(after), JsonSerializer.Serialize(await f.Den.GetAsync<AgentDefinitionRecord>("personal", id)));
    }

    [Fact]
    public async Task Schema_denial_revoked_write_and_cancellation_do_not_create_a_duplicate_or_mutate_the_Den()
    {
        await using var f = await Fixture.CreateAsync();
        var unknown = await f.Builder.CreateAsync(Draft(new()) with { ConfigurationJson = "{\"unrecognisedOption\":true}" }, default);
        Assert.False(unknown.IsSuccess); Assert.Equal("InvalidRecord", unknown.Error!.Code);
        var negative = await f.Builder.CreateAsync(Draft(new()) with { ConfigurationJson = "{\"budget\":{\"maxTokens\":-1}}" }, default);
        Assert.False(negative.IsSuccess); Assert.Equal("InvalidRecord", negative.Error!.Code);
        var unboundedCapabilities = await f.Builder.CreateAsync(Draft(new()) with { ConfigurationJson = JsonSerializer.Serialize(new DenAgentBuilderConfiguration
        { ModelPolicy = new(true, RequiredCapabilities: Enumerable.Range(0, 129).Select(i => "cap." + i).ToHashSet()) }, StudioJson.Options) }, default);
        Assert.False(unboundedCapabilities.IsSuccess); Assert.Equal("InvalidRecord", unboundedCapabilities.Error!.Code);
        var ambiguousProvider = await f.Builder.CreateAsync(Draft(new()) with { ConfigurationJson = "{\"modelPolicy\":{\"inherit\":false,\"modelId\":\"exact-model\",\"providerId\":\" provider \"}}" }, default);
        Assert.False(ambiguousProvider.IsSuccess); Assert.Equal("InvalidRecord", ambiguousProvider.Error!.Code);
        f.Policy.AllowWrite = false;
        var denied = await f.Builder.CreateAsync(Draft(new()), default);
        Assert.False(denied.IsSuccess); Assert.Equal("Forbidden", denied.Error!.Code);
        Assert.Empty(await f.Den.ListAsync<AgentDefinitionRecord>("personal"));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Builder.CreateAsync(Draft(new()), canceled.Token));
        Assert.Empty(await f.Den.ListAsync<AgentDefinitionRecord>("personal"));
    }

    [Fact]
    public async Task A_bound_adapter_refuses_actor_or_selected_store_replacement_and_does_not_follow_a_copied_UUID()
    {
        await using var f = await Fixture.CreateAsync();
        await using var other = await Fixture.CreateAsync();
        var created = (await f.Builder.CreateAsync(Draft(new()), default)).Value!;
        var before = JsonSerializer.Serialize(await f.Den.GetAsync<AgentDefinitionRecord>("personal", created.AgentId.ToString("D")));
        var originalSession = f.Session;
        f.Session = f.Session with { Actor = f.Session.Actor with { AuthenticationRevision = "changed-session" } };
        var actorDenied = await f.Builder.OpenAsync(created.AgentId, default);
        Assert.False(actorDenied.IsSuccess); Assert.Equal("Forbidden", actorDenied.Error!.Code);
        f.Session = other.Session;
        var storeDenied = await f.Builder.UpdateDraftAsync(created.AgentId, created.DefinitionRevision,
            Draft(new()) with { AgentId = created.AgentId, DefinitionRevision = created.DefinitionRevision }, default);
        Assert.False(storeDenied.IsSuccess); Assert.Equal("Forbidden", storeDenied.Error!.Code);
        Assert.Empty(await other.Den.ListAsync<AgentDefinitionRecord>("personal"));
        f.Session = originalSession;
        Assert.Equal(before, JsonSerializer.Serialize(await f.Den.GetAsync<AgentDefinitionRecord>("personal", created.AgentId.ToString("D"))));
    }

    [Fact]
    public async Task Authoring_does_not_claim_registry_validation_preview_activation_or_sharing_authority()
    {
        await using var f = await Fixture.CreateAsync();
        var created = (await f.Builder.CreateAsync(Draft(new() { ToolIds = ["unknown.registry.id"] }), default)).Value!;
        var before = JsonSerializer.Serialize(await f.Den.GetAsync<AgentDefinitionRecord>("personal", created.AgentId.ToString("D")));
        var validation = await f.Builder.ValidateAsync(created.AgentId, created.DefinitionRevision, default);
        Assert.True(validation.IsSuccess); Assert.False(validation.Value!.IsValid);
        Assert.Contains("execution unverified", validation.Value.ValidationScope);
        var preview = await f.Builder.PreviewAsync(created.AgentId, "synthetic test", new(null, true, new Dictionary<string, bool>()), default);
        var activation = await f.Builder.ActivateAsync(created.AgentId, created.DefinitionRevision, default);
        var share = await f.Builder.ShareAsync(created.AgentId, "another-principal", "owner", default);
        Assert.False(preview.IsSuccess); Assert.False(activation.IsSuccess); Assert.False(share.IsSuccess);
        Assert.Equal("CapabilityUnavailable", preview.Error!.Code);
        Assert.Equal("CapabilityUnavailable", activation.Error!.Code);
        Assert.Equal("CapabilityUnavailable", share.Error!.Code);
        Assert.Equal(before, JsonSerializer.Serialize(await f.Den.GetAsync<AgentDefinitionRecord>("personal", created.AgentId.ToString("D"))));
    }

    [Fact]
    public async Task Cancellation_after_successful_create_retains_known_ID_and_revision_without_an_ordinary_retry_failure()
    {
        await using var f = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        f.AfterCurrentRead = count => { if (count == 2) cancellation.Cancel(); };
        var known = await Assert.ThrowsAsync<CanonicalAgentCommittedObservationException>(() =>
            f.Builder.CreateAsync(Draft(new()), cancellation.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(known.InnerException);
        Assert.Equal(CanonicalAgentCommitKind.CreatedDraft, known.Receipt.Kind);
        Assert.NotEqual(Guid.Empty, known.Receipt.AgentId);
        var actual = Assert.Single(await f.Den.ListAsync<AgentDefinitionRecord>("personal"));
        Assert.Equal(known.Receipt.AgentId.ToString("D"), actual.Id);
        Assert.Equal(known.Receipt.DefinitionRevision, actual.Revision);
        Assert.Contains("do not repeat", known.Message);
        Assert.DoesNotContain("Instructions", known.Message);
    }

    [Fact]
    public async Task Actor_change_after_successful_update_retains_known_revision_without_returning_definition_content()
    {
        await using var f = await Fixture.CreateAsync();
        var created = (await f.Builder.CreateAsync(Draft(new()), default)).Value!;
        var opened = (await f.Builder.OpenAsync(created.AgentId, default)).Value!;
        f.CurrentReadCount = 0;
        f.AfterCurrentRead = count =>
        {
            if (count == 4) f.Session = f.Session with { Actor = f.Session.Actor with { AuthenticationRevision = "replacement-session" } };
        };
        var known = await Assert.ThrowsAsync<CanonicalAgentCommittedObservationException>(() =>
            f.Builder.UpdateDraftAsync(created.AgentId, opened.DefinitionRevision,
                opened with { Instructions = "Private revised instructions" }, default));
        Assert.IsType<DenException>(known.InnerException);
        Assert.Equal(DenErrorCode.Forbidden, ((DenException)known.InnerException!).Code);
        Assert.Equal(CanonicalAgentCommitKind.UpdatedDraft, known.Receipt.Kind);
        Assert.Equal(created.AgentId, known.Receipt.AgentId);
        var actual = Assert.Single(await f.Den.ListAsync<AgentDefinitionRecord>("personal"));
        Assert.Equal(known.Receipt.DefinitionRevision, actual.Revision);
        Assert.True(actual.Revision > opened.DefinitionRevision);
        Assert.Equal("Private revised instructions", actual.Instructions);
        Assert.DoesNotContain("Private revised instructions", known.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_list_actor_or_same_ID_store_switch_never_returns_a_publishable_observation(bool switchStore)
    {
        await using var f = await Fixture.CreateAsync();
        await using var other = await Fixture.CreateAsync();
        var original = f.Session; var reads = 0; var published = false;
        // Real permitted empty Den listing does not prove its original Home session still current.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            var observation = await StudioDenObservationBoundary.ReadAgentsAsync(ct =>
            {
                ct.ThrowIfCancellationRequested();
                if (++reads == 2)
                    f.Session = switchStore ? original with { Den = other.Den } :
                        original with { Actor = original.Actor with { AuthenticationRevision = "post-list-replacement" } };
                return Task.FromResult(f.Session);
            }, original.DenId);
            published = true;
            Assert.Empty(observation.Agents);
        });
        Assert.False(published); Assert.Equal(2, reads);
        f.Session = original;
        var accepted = await StudioDenObservationBoundary.ReadAgentsAsync(_ => Task.FromResult(f.Session), original.DenId);
        Assert.Empty(accepted.Agents); Assert.True(StudioDenObservationBoundary.SameSession(original, accepted.Session));
        Assert.Empty(await f.Den.ListAsync<AgentDefinitionRecord>("personal"));
        Assert.Empty(await other.Den.ListAsync<AgentDefinitionRecord>("personal"));
    }

    [Fact]
    public async Task Original_host_failure_still_drains_original_preview_and_retains_every_distinct_failure()
    {
        var clearFailure = new InvalidOperationException("synthetic clear failure");
        var hostFailure = new InvalidOperationException("synthetic original host failure");
        var previewFailure = new InvalidOperationException("synthetic original preview failure");
        var fieldCleared = false; var calls = new List<string>();
        var result = await Assert.ThrowsAsync<AggregateException>(() => StudioOwnedCleanup.RunAsync(
            [() => { fieldCleared = true; calls.Add("clear"); throw clearFailure; }, () => calls.Add("second-clear")],
            () => { Assert.True(fieldCleared); calls.Add("original-host"); throw hostFailure; },
            () => { Assert.True(fieldCleared); calls.Add("original-preview"); return ValueTask.FromException(previewFailure); }));
        Assert.Equal(new[] { "clear", "second-clear", "original-host", "original-preview" }, calls);
        Assert.Equal(3, result.InnerExceptions.Count);
        Assert.Same(clearFailure, result.InnerExceptions[0]); Assert.Same(hostFailure, result.InnerExceptions[1]);
        Assert.Same(previewFailure, result.InnerExceptions[2]);
    }

    [Fact]
    public async Task Opened_agent_session_refuses_a_same_actor_same_Den_ID_replacement_store()
    {
        await using var f = await Fixture.CreateAsync();
        await using var other = await Fixture.CreateAsync();
        var created = (await f.Builder.CreateAsync(Draft(new()), default)).Value!;
        var opened = await f.Builder.OpenAsync(created.AgentId, default);
        Assert.True(opened.IsSuccess);
        var retainedEditorSession = f.Session;
        var before = JsonSerializer.Serialize(await f.Den.GetAsync<AgentDefinitionRecord>("personal", created.AgentId.ToString("D")));
        var replaced = retainedEditorSession with { Den = other.Den };
        Assert.Equal(retainedEditorSession.Actor, replaced.Actor);
        Assert.Equal(retainedEditorSession.DenId, replaced.DenId);
        Assert.NotSame(retainedEditorSession.Den.Store, replaced.Den.Store);
        Assert.Throws<UnauthorizedAccessException>(() =>
            StudioDenObservationBoundary.RequireSameSession(retainedEditorSession, replaced));
        StudioDenObservationBoundary.RequireSameSession(retainedEditorSession, f.Session);
        Assert.Equal(before, JsonSerializer.Serialize(await f.Den.GetAsync<AgentDefinitionRecord>("personal", created.AgentId.ToString("D"))));
        Assert.Empty(await other.Den.ListAsync<AgentDefinitionRecord>("personal"));
    }

    private static CanonicalAgentDefinition Draft(DenAgentBuilderConfiguration config) =>
        new(Guid.Empty, "Canonical Agent", "Purpose", "Instructions", DenCanonicalAgentBuilderAdapter.EncodeConfiguration(config), 0, true);
    private sealed class Policy : IDenAccessPolicy
    {
        public bool AllowWrite { get; set; } = true;
        public ValueTask<bool> IsAllowedAsync(string principal, string ns, string id, DenPermission permission, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(principal == "owner" && ns == "personal" &&
                (permission == DenPermission.Read || (permission == DenPermission.Write && AllowWrite)));
        }
    }
    // This is explicit synthetic authoring-session input, not a Home ownership/execution proof.
    private sealed class Fixture(string root, DenStore store, DulcheDen den, Policy policy) : IAsyncDisposable
    {
        public DulcheDen Den { get; } = den;
        public Policy Policy { get; } = policy;
        public HomePersonalDenSession Session { get; set; } = new(new AuthenticatedResourceActor("owner", "local-profile", null, null, "session-1"), store.Manifest.DenId, den);
        public DenCanonicalAgentBuilderAdapter Builder { get; private set; } = null!;
        public int CurrentReadCount { get; set; }
        public Action<int>? AfterCurrentRead { get; set; }
        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-canonical-agent-builder-" + Guid.NewGuid().ToString("N"));
            var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            try
            {
                var policy = new Policy(); var den = new DulcheDen(store, policy, "owner");
                var f = new Fixture(root, store, den, policy);
                f.Builder = new(ct =>
                {
                    ct.ThrowIfCancellationRequested(); f.AfterCurrentRead?.Invoke(++f.CurrentReadCount);
                    return Task.FromResult(f.Session);
                }); return f;
            }
            catch { await store.DisposeAsync(); Directory.Delete(root, true); throw; }
        }
        public async ValueTask DisposeAsync() { await store.DisposeAsync(); Directory.Delete(root, true); }
    }
}
