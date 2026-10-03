using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Dulche.Den;

namespace Dulche.Runtime.Agents.Tests;

public sealed class DenAgentRuntimeValidatorTests
{
    [Fact]
    public Task Read_only_authoring_validation_observes_the_same_saved_revision_without_Execute_or_a_run() => WithDen(async f =>
    {
        var result = await f.Validator.ValidateCurrentAsync(f.Reference, CapabilityPlatform.Windows, "user");
        Assert.True(result.Succeeded); Assert.True(result.Value!.ConfigurationResolved);
        Assert.Equal(f.Reference, result.Value.Reference); Assert.Equal("local-test:exact-model", result.Value.Model!.Key);
        Assert.True(result.Value.Model.IsLocal); Assert.Equal(0, f.Remote.Reads);
        Assert.False(await f.Policy.IsAllowedAsync("owner", "personal", f.Record.Id, DenPermission.Execute));
        Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal"));
        var unchanged = Assert.Single(await f.Den.ListAsync<AgentDefinitionRecord>("personal"));
        Assert.Equal(f.Record.Revision, unchanged.Revision); Assert.False(unchanged.Enabled);
    });

    [Fact]
    public Task Unknown_dependencies_do_not_become_valid_from_a_saved_Enabled_flag() => WithDen(async f =>
    {
        var saved = await f.Den.SaveAsync(f.Record with { Enabled = true, ToolIds = ["invented.route"] }, f.Record.Revision, "unknown-dependency");
        var expected = f.Reference with { DefinitionRevision = saved.Revision };
        var result = await f.Validator.ValidateCurrentAsync(expected, CapabilityPlatform.Windows, "user");
        Assert.True(result.Succeeded); Assert.False(result.Value!.ConfigurationResolved);
        Assert.Contains(result.Value.Dependencies.Diagnostics, value => value.Code == "CapabilityNotFound");
        Assert.Null(result.Value.Model); Assert.Equal(0, f.Local.Reads);
        Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal"));
        Assert.Equal(saved.Revision, (await f.Den.GetAsync<AgentDefinitionRecord>("personal", saved.Id))!.Revision);
    });

    [Fact]
    public Task A_real_Den_revision_change_during_model_inventory_invalidates_the_observation() => WithDen(async f =>
    {
        f.Local.AfterRead = async reads =>
        {
            if (reads == 1) await f.Den.SaveAsync(f.Record with { Instructions = "Actual new revision" }, f.Record.Revision, "revision-during-inventory");
        };
        var result = await f.Validator.ValidateCurrentAsync(f.Reference, CapabilityPlatform.Windows, "user");
        Assert.False(result.Succeeded); Assert.Equal(AgentFailureCode.RevisionConflict, result.Error!.Code);
        Assert.Null(result.Value);
        var actual = (await f.Den.GetAsync<AgentDefinitionRecord>("personal", f.Record.Id))!;
        Assert.True(actual.Revision > f.Reference.DefinitionRevision); Assert.Equal("Actual new revision", actual.Instructions);
        Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Fact]
    public Task Actual_read_revocation_during_model_IO_refuses_final_publication() => WithDen(async f =>
    {
        f.Local.AfterRead = reads => { if (reads == 1) f.Policy.Read = false; return Task.CompletedTask; };
        var result = await f.Validator.ValidateCurrentAsync(f.Reference, CapabilityPlatform.Windows, "user");
        Assert.False(result.Succeeded); Assert.Equal(AgentFailureCode.PermissionDenied, result.Error!.Code); Assert.Null(result.Value);
        f.Policy.Read = true;
        Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal"));
        Assert.Equal(f.Record.Revision, (await f.Den.GetAsync<AgentDefinitionRecord>("personal", f.Record.Id))!.Revision);
    });

    [Fact]
    public Task Foreign_namespace_stale_revision_and_missing_inherited_selection_are_explicit_without_model_work() => WithDen(async f =>
    {
        var foreign = await f.Validator.ValidateCurrentAsync(f.Reference with { NamespaceId = "foreign" }, CapabilityPlatform.Windows, "user");
        Assert.Equal(AgentFailureCode.AgentUnavailableInScope, foreign.Error!.Code);
        var stale = await f.Validator.ValidateCurrentAsync(f.Reference with { DefinitionRevision = f.Record.Revision + 1 }, CapabilityPlatform.Windows, "user");
        Assert.Equal(AgentFailureCode.DefinitionRevisionUnavailable, stale.Error!.Code);
        var inherited = await f.Den.SaveAsync(f.Record with { ModelPolicyJson = JsonSerializer.Serialize(
            new AgentModelPolicy(true, RequiredCapabilities: new HashSet<string> { "Text" }), DenJson.Options) }, f.Record.Revision, "inherit-without-selection");
        var result = await f.Validator.ValidateCurrentAsync(f.Reference with { DefinitionRevision = inherited.Revision }, CapabilityPlatform.Windows, "user");
        Assert.True(result.Succeeded); Assert.False(result.Value!.ConfigurationResolved);
        Assert.Contains(result.Value.Diagnostics, value => value.Code == "InheritedSelectionRequired");
        Assert.Equal(0, f.Local.Reads); Assert.Equal(0, f.Remote.Reads);
    });

    [Fact]
    public Task Finite_unmeasured_token_or_cost_budget_is_identified_before_model_work() => WithDen(async f =>
    {
        var saved = await f.Den.SaveAsync(f.Record with { BudgetJson = JsonSerializer.Serialize(
            new AgentBudgetLimits(MaxTokens: 10, MaxCost: 1), DenJson.Options) }, f.Record.Revision, "finite-unmeasured-budget");
        var result = await f.Validator.ValidateCurrentAsync(f.Reference with { DefinitionRevision = saved.Revision }, CapabilityPlatform.Windows, "user");
        Assert.True(result.Succeeded); Assert.False(result.Value!.ConfigurationResolved);
        Assert.Contains(result.Value.Diagnostics, value => value.Code == "UsageMeasurementUnavailable");
        Assert.Equal(0, f.Local.Reads); Assert.Equal(0, f.Remote.Reads);
        Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal"));
    });

    [Fact]
    public Task Mutable_model_feature_revocation_during_final_inventory_cannot_mutate_the_original_observation() => WithDen(async f =>
    {
        f.Local.AfterRead = reads =>
        {
            if (reads == 3) f.Local.ModelCapabilities.Remove(ToolCapability.Text);
            return Task.CompletedTask;
        };
        var result = await f.Validator.ValidateCurrentAsync(f.Reference, CapabilityPlatform.Windows, "user");
        Assert.True(result.Succeeded); Assert.False(result.Value!.ConfigurationResolved); Assert.Null(result.Value.Model);
        Assert.Contains(result.Value.Diagnostics, value => value.Code == "ModelChanged");
        Assert.Equal(3, f.Local.Reads); Assert.DoesNotContain(ToolCapability.Text, f.Local.ModelCapabilities);
        Assert.Empty(await f.Den.ListAsync<AgentRunRecord>("personal"));
        Assert.Equal(f.Record.Revision, (await f.Den.GetAsync<AgentDefinitionRecord>("personal", f.Record.Id))!.Revision);
    });

    private static async Task WithDen(Func<Fixture, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-agent-validation-" + Guid.NewGuid().ToString("N"));
        DenStore? store = null; Exception? primary = null; var cleanup = new List<Exception>();
        try
        {
            store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var policy = new Policy(); var den = new DulcheDen(store, policy, "owner");
            var record = await den.SaveAsync(new AgentDefinitionRecord
            {
                Id = Guid.NewGuid().ToString("D"), NamespaceId = "personal", DisplayName = "Same canonical Agent", Version = "1", Enabled = false,
                ModelPolicyJson = JsonSerializer.Serialize(new AgentModelPolicy(false, "exact-model", "local-test", false, false,
                    new HashSet<string> { "Text" }), DenJson.Options)
            }, 0, "create-definition");
            var local = new Provider("local-test", true); var remote = new Provider("remote-test", false);
            var providers = new ModelProviderRegistry([local, remote]);
            var validator = new DenAgentRuntimeValidator(den, "personal", new(new(new Capabilities())), providers, new ModelRouter(providers));
            await body(new(den, policy, record, new(store.Manifest.DenId, "personal", record.Id, record.Revision), local, remote, validator));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (store is not null) try { await store.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { cleanup.Add(error); }
        }
        if (cleanup.Count != 0) throw new AggregateException(primary is null ? cleanup : new[] { primary! }.Concat(cleanup));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
    private sealed record Fixture(DulcheDen Den, Policy Policy, AgentDefinitionRecord Record,
        DenAgentReference Reference, Provider Local, Provider Remote, DenAgentRuntimeValidator Validator);
    private sealed class Policy : IDenAccessPolicy
    {
        public bool Read { get; set; } = true;
        public ValueTask<bool> IsAllowedAsync(string principal, string ns, string id, DenPermission permission, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(principal == "owner" && ns == "personal" &&
            (permission == DenPermission.Write || permission == DenPermission.Read && Read)); }
    }
    private sealed class Capabilities : ICapabilityRepository
    {
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<CapabilityDefinition>>([]);
        public Task UpsertCapabilityAsync(CapabilityDefinition definition, CancellationToken ct) => throw new NotSupportedException();
        public Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Provider(string id, bool local) : IModelProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => local;
        public bool CanManageModels => false;
        public int Reads { get; private set; }
        public HashSet<ToolCapability> ModelCapabilities { get; } = [ToolCapability.Text];
        public Func<int, Task>? AfterRead { get; set; }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); ++Reads;
            if (AfterRead is { } callback) await callback(Reads);
            return [new(id, local, new("exact-model", 1, "synthetic", "1", "synthetic", ModelCapabilities, DateTimeOffset.UnixEpoch))];
        }
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException("Validation cannot execute a model.");
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException("Validation cannot execute a model.");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct) => throw new NotSupportedException("Validation cannot execute tools.");
    }
}
