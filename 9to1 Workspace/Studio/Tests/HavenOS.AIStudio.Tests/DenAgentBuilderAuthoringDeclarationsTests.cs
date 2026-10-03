using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dulche.Runtime.Agents;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class DenAgentBuilderAuthoringDeclarationsTests
{
    [Fact]
    public Task Canonical_authoring_declarations_survive_create_edit_reopen_on_one_ID_with_avatar_and_dependency_provenance() =>
        WithDenAsync(async fixture =>
        {
            using var input = JsonDocument.Parse("{\"type\":\"object\"}");
            var config = Configuration(input.RootElement);
            var created = await fixture.Builder.CreateAsync(Draft(config), default);
            Assert.True(created.IsSuccess); var id = created.Value!.AgentId;
            var before = (await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", id.ToString("D")))!;
            Assert.Equal(AgentDefinitionLifecycle.Draft, before.LifecycleState); Assert.False(before.Enabled);
            var presentation = new AgentPresentationDefinition(1, AgentIconPresentation.Static, "asset:original", "Original avatar",
                "idle", [new("idle", "Idle", "asset:original", false)], [], []);
            await fixture.Den.SaveAsync(before with { Presentation = presentation }, before.Revision, Operation());
            var opened = await fixture.Builder.OpenAsync(id, default);
            Assert.True(opened.IsSuccess); var basis = opened.Value!;
            var decoded = DenCanonicalAgentBuilderAdapter.DecodeConfiguration(basis.ConfigurationJson);
            EqualDeclarations(config, decoded);
            var saved = await fixture.Builder.UpdateDraftAsync(id, basis.DefinitionRevision,
                basis with { Description = "Revised purpose", Instructions = "Revised instructions" }, default);
            Assert.True(saved.IsSuccess); Assert.True(saved.Value!.DefinitionRevision > basis.DefinitionRevision);
            var freshStore = await DenStore.OpenAsync(fixture.Root); fixture.Stores.Add(freshStore);
            var reopenedDen = new DulcheDen(freshStore, fixture.Policy, "owner");
            var actual = (await reopenedDen.GetAsync<AgentDefinitionRecord>("personal", id.ToString("D")))!;
            Assert.Equal(id.ToString("D"), actual.Id); Assert.Equal("personal", actual.NamespaceId);
            Assert.Equal(saved.Value.DefinitionRevision, actual.Revision);
            Assert.Equal("Revised instructions", actual.Instructions); Assert.Equal("Revised purpose", actual.Description);
            Assert.Equal(JsonSerializer.Serialize(presentation), JsonSerializer.Serialize(actual.Presentation));
            Assert.Equal(JsonSerializer.Serialize(config.AvailabilityBindings), JsonSerializer.Serialize(actual.AvailabilityBindings));
            Assert.Equal(JsonSerializer.Serialize(config.KnowledgeReferences), JsonSerializer.Serialize(actual.KnowledgeReferences));
            Assert.Equal(JsonSerializer.Serialize(config.QuickActions), JsonSerializer.Serialize(actual.QuickActions));
            Assert.Equal(config.MemoryPolicy, actual.MemoryPolicy); Assert.Equal(config.GraphReference, actual.GraphReference);
            Assert.Equal(JsonSerializer.Serialize(config.SharingMetadata), JsonSerializer.Serialize(actual.SharingMetadata));
            Assert.Equal(config.LifecycleState, actual.LifecycleState); Assert.False(actual.Enabled);
            Assert.Equal(before.ToolIds, actual.ToolIds); Assert.Equal(before.SkillIds, actual.SkillIds);
            Assert.Equal(before.PluginIds, actual.PluginIds); Assert.Equal(before.McpCapabilityIds, actual.McpCapabilityIds);
            Assert.Equal(before.AllowedPermissions, actual.AllowedPermissions);
            var reopened = await fixture.Builder.OpenAsync(id, default);
            Assert.True(reopened.IsSuccess); EqualDeclarations(config,
                DenCanonicalAgentBuilderAdapter.DecodeConfiguration(reopened.Value!.ConfigurationJson));
            var stale = await fixture.Builder.UpdateDraftAsync(id, basis.DefinitionRevision, basis, default);
            Assert.False(stale.IsSuccess); Assert.Equal("Conflict", stale.Error!.Code);
            Assert.Single(await fixture.Den.ListAsync<AgentDefinitionRecord>("personal"));
            Assert.Equal(0, fixture.Policy.ExecutionChecks);
        });

    [Fact]
    public Task Absent_and_empty_availability_remain_distinct_and_creation_never_activates_or_grants_sharing() =>
        WithDenAsync(async fixture =>
        {
            var legacy = await fixture.Builder.CreateAsync(Draft(new()), default);
            Assert.True(legacy.IsSuccess);
            var legacyRecord = (await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", legacy.Value!.AgentId.ToString("D")))!;
            Assert.Null(legacyRecord.AvailabilityBindings); Assert.Null(legacyRecord.LifecycleState); Assert.False(legacyRecord.Enabled);
            var empty = await fixture.Builder.CreateAsync(Draft(new() { AvailabilityBindings = [], LifecycleState = AgentDefinitionLifecycle.Draft }), default);
            Assert.True(empty.IsSuccess);
            var emptyRecord = (await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", empty.Value!.AgentId.ToString("D")))!;
            Assert.NotNull(emptyRecord.AvailabilityBindings); Assert.Empty(emptyRecord.AvailabilityBindings!); Assert.False(emptyRecord.Enabled);
            var active = await fixture.Builder.CreateAsync(Draft(new() { LifecycleState = AgentDefinitionLifecycle.Active }), default);
            Assert.False(active.IsSuccess); Assert.Equal("InvalidRecord", active.Error!.Code);
            Assert.Equal(2, (await fixture.Den.ListAsync<AgentDefinitionRecord>("personal")).Count);
            var activation = await fixture.Builder.ActivateAsync(empty.Value.AgentId, empty.Value.DefinitionRevision, default);
            Assert.False(activation.IsSuccess); Assert.Equal("CapabilityUnavailable", activation.Error!.Code);
            var sharing = await fixture.Builder.ShareAsync(empty.Value.AgentId, "other-principal", "owner", default);
            Assert.False(sharing.IsSuccess); Assert.Equal("CapabilityUnavailable", sharing.Error!.Code);
            Assert.Equal(0, fixture.Policy.ExecutionChecks);
            Assert.False((await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", emptyRecord.Id))!.Enabled);
        });

    [Fact]
    public Task Current_read_of_saved_declarations_refuses_invalid_policy_or_canonical_extension_overwrite_without_a_save() =>
        WithDenAsync(async fixture =>
        {
            var created = await fixture.Builder.CreateAsync(Draft(new()), default);
            Assert.True(created.IsSuccess); var id = created.Value!.AgentId;
            var original = (await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", id.ToString("D")))!;
            var invalid = await fixture.Den.SaveAsync(original with { CapabilityPolicyJson = "{\"unknownPolicyField\":true}" }, original.Revision, Operation());
            var invalidRead = await fixture.Builder.OpenAsync(id, default);
            Assert.False(invalidRead.IsSuccess); Assert.Equal("InvalidRecord", invalidRead.Error!.Code);
            Assert.Equal(invalid.Revision, (await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", original.Id))!.Revision);
            using var overwrite = JsonDocument.Parse("true");
            var collision = invalid with { CapabilityPolicyJson = null,
                ExtensionData = new Dictionary<string, JsonElement> { ["Enabled"] = overwrite.RootElement } };
            Assert.Equal(DenErrorCode.InvalidRecord,
                Assert.Throws<DenException>(() => DenAgentAuthoringFields.Capture(collision)).Code);
            var validation = await fixture.Builder.ValidateAsync(id, invalid.Revision, default);
            Assert.False(validation.IsSuccess); Assert.Equal("InvalidRecord", validation.Error!.Code);
            Assert.Equal(invalid.Revision, (await fixture.Den.GetAsync<AgentDefinitionRecord>("personal", original.Id))!.Revision);
            Assert.False(original.Enabled); Assert.Equal(0, fixture.Policy.ExecutionChecks);
        });

    private static DenAgentBuilderConfiguration Configuration(JsonElement schema) => new()
    {
        ToolIds = ["tool.original"], SkillIds = ["skill.original"], PluginIds = ["plugin.original"],
        McpCapabilityIds = ["mcp.original"], AllowedPermissions = ["cap.upper-bound"],
        AvailabilityBindings = [new(Guid.NewGuid(), AgentAvailabilityScope.Surface, "Chat")],
        KnowledgeReferences = [new(Guid.NewGuid(), "Files", "canonical-file", "revision-1", "personal")],
        QuickActions = [new(Guid.NewGuid(), "Explain original file", Instruction: "Explain the linked file.", InputSchema: schema,
            ConfirmationPolicy: AgentQuickActionConfirmationPolicy.ConfirmBeforeRun)],
        MemoryPolicy = new("personal", MemoryScopeKind.Agent, "canonical-memory-scope", MemoryFrequency.Sometimes, MemoryFrequency.Never),
        CapabilityPolicy = new(new HashSet<string>(StringComparer.Ordinal) { "cap.upper-bound" }, new HashSet<string>(StringComparer.Ordinal) { "cap.denied" }),
        DelegationPolicy = new(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), false, 1, 0, new()),
        GraphReference = new("original-den", "personal", "original-workflow", 2, "original-entry"),
        SharingMetadata = new("personal", ["declared-principal"], "declared-org", "declared-team"),
        LifecycleState = AgentDefinitionLifecycle.Draft
    };
    private static CanonicalAgentDefinition Draft(DenAgentBuilderConfiguration config) =>
        new(Guid.Empty, "Canonical Agent", "Purpose", "Instructions", DenCanonicalAgentBuilderAdapter.EncodeConfiguration(config), 0, true);
    private static string Operation() => Guid.NewGuid().ToString("N");
    private static void EqualDeclarations(DenAgentBuilderConfiguration expected, DenAgentBuilderConfiguration actual)
    {
        Assert.Equal(JsonSerializer.Serialize(expected.AvailabilityBindings), JsonSerializer.Serialize(actual.AvailabilityBindings));
        Assert.Equal(JsonSerializer.Serialize(expected.KnowledgeReferences), JsonSerializer.Serialize(actual.KnowledgeReferences));
        Assert.Equal(JsonSerializer.Serialize(expected.QuickActions), JsonSerializer.Serialize(actual.QuickActions));
        Assert.Equal(expected.MemoryPolicy, actual.MemoryPolicy); Assert.Equal(expected.GraphReference, actual.GraphReference);
        Assert.Equal(JsonSerializer.Serialize(expected.SharingMetadata), JsonSerializer.Serialize(actual.SharingMetadata));
        Assert.Equal(expected.LifecycleState, actual.LifecycleState);
        Assert.Equal(expected.CapabilityPolicy!.AllowedCapabilities.Order(), actual.CapabilityPolicy!.AllowedCapabilities.Order());
        Assert.Equal(expected.CapabilityPolicy.DeniedCapabilities.Order(), actual.CapabilityPolicy.DeniedCapabilities.Order());
        Assert.Equal(expected.CapabilityPolicy.RequireApprovalForConsequentialActions, actual.CapabilityPolicy.RequireApprovalForConsequentialActions);
        Assert.Equal(expected.DelegationPolicy!.AllowSubagents, actual.DelegationPolicy!.AllowSubagents);
        Assert.Equal(expected.DelegationPolicy.MaximumConcurrency, actual.DelegationPolicy.MaximumConcurrency);
        Assert.Equal(expected.DelegationPolicy.MaximumDepth, actual.DelegationPolicy.MaximumDepth);
        Assert.Equal(expected.DelegationPolicy.Budget, actual.DelegationPolicy.Budget);
        Assert.Empty(actual.DelegationPolicy.AllowedAgentIds); Assert.Empty(actual.DelegationPolicy.AllowedAgentClasses);
    }
    private static async Task WithDenAsync(Func<Fixture, Task> action)
    {
        var fixture = new Fixture(); var failures = new List<Exception>();
        try { await fixture.CreateAsync(); await action(fixture); }
        catch (Exception error) { failures.Add(error); }
        foreach (var store in fixture.Stores.AsEnumerable().Reverse())
            try { await store.DisposeAsync(); }
            catch (Exception error) { if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error); }
        try { if (Directory.Exists(fixture.Root)) Directory.Delete(fixture.Root, true); }
        catch (Exception error) { if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
    // Explicit synthetic current authoring-session input; no production Home/Execute authority.
    private sealed class Fixture
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-den-builder-fields-" + Guid.NewGuid().ToString("N"));
        public List<DenStore> Stores { get; } = [];
        public Policy Policy { get; } = new();
        public DulcheDen Den { get; private set; } = null!;
        public DenCanonicalAgentBuilderAdapter Builder { get; private set; } = null!;
        public async Task CreateAsync()
        {
            var store = await DenStore.CreateAsync(Root, [new("personal", "personal")]); Stores.Add(store);
            Den = new(store, Policy, "owner");
            var session = new HomePersonalDenSession(new AuthenticatedResourceActor("owner", "local-profile", null, null, "fields-session"), store.Manifest.DenId, Den);
            Builder = new(ct => { ct.ThrowIfCancellationRequested(); return Task.FromResult(session); });
        }
    }
    private sealed class Policy : IDenAccessPolicy
    {
        public int ExecutionChecks { get; private set; }
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (permission == DenPermission.Execute) ExecutionChecks++;
            return ValueTask.FromResult(principalId == "owner" && namespaceId == "personal" && permission is DenPermission.Read or DenPermission.Write);
        }
    }
}
