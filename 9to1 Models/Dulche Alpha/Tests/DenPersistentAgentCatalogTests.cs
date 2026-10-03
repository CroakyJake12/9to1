using System.Text.Json;
using Dulche.Runtime.Agents;
using NineToOne.Dulche.Den;

namespace Dulche.Runtime.Agents.Tests;

public sealed class DenPersistentAgentCatalogTests
{
    [Fact]
    public async Task Current_Den_revision_changes_both_runtime_projections_without_a_catalog_copy()
    {
        await WithDenAsync(async (den, policy) =>
        {
            var id = Guid.NewGuid().ToString("D");
            var original = await den.SaveAsync(Definition(id, "personal", "First", "original"), 0, Operation());
            var catalog = new DenPersistentAgentCatalog(den, "personal");
            var first = Require((await catalog.GetCurrentProjectionAsync(id)).Value);
            Assert.Equal(den.Store.Manifest.DenId, first.Reference.DenId);
            Assert.Equal(id, first.Execution.AgentId);
            Assert.Equal(Guid.Parse(id), first.SharedDefinition.Id);
            Assert.Equal(original.Revision, first.Reference.DefinitionRevision);
            Assert.Equal("original", first.SharedDefinition.Instructions);
            Assert.Equal("Canonical purpose", first.SharedDefinition.Description);
            var updated = await den.SaveAsync(original with { DisplayName = "Changed", Instructions = "current",
                AllowedPermissions = ["calendar-read"] }, original.Revision, Operation());
            var second = Require((await catalog.GetCurrentProjectionAsync(id)).Value);
            Assert.Equal(updated.Revision, second.Reference.DefinitionRevision);
            Assert.Equal("Changed", second.Execution.Name);
            Assert.Equal("current", second.SharedDefinition.Instructions);
            Assert.Equal(new[] { "calendar-read" }, second.Execution.CapabilityPolicy.AllowedCapabilities.Order(StringComparer.Ordinal));
            Assert.Equal(AgentFailureCode.DefinitionRevisionUnavailable,
                (await catalog.GetRevisionAsync(id, original.Revision)).Error?.Code);
            Assert.Equal(first.SharedDefinition.Id, second.SharedDefinition.Id);
            Assert.Equal(id, second.Execution.AgentId);
            Assert.True(policy.Execute);
        });
    }

    [Fact]
    public async Task Same_ID_in_another_namespace_or_Den_cannot_select_a_foreign_definition()
    {
        await WithDenAsync(async (den, _) =>
        {
            var id = Guid.NewGuid().ToString("D");
            var personal = await den.SaveAsync(Definition(id, "personal", "Personal", "personal instructions"), 0, Operation());
            var fixtureAdmin = new DulcheDen(den.Store, den.AccessPolicy, "fixture-admin");
            await fixtureAdmin.SaveAsync(Definition(id, "organisation", "Organisation", "private organisation instructions"), 0, Operation());
            var catalog = new DenPersistentAgentCatalog(den, "personal");
            var actual = Require((await catalog.GetCurrentProjectionAsync(id)).Value);
            Assert.Equal("Personal", actual.SharedDefinition.Name);
            Assert.Equal(AgentFailureCode.AgentUnavailableInScope,
                (await catalog.GetProjectionAsync(actual.Reference with { NamespaceId = "organisation" })).Error?.Code);
            Assert.Equal(AgentFailureCode.AgentUnavailableInScope,
                (await catalog.GetProjectionAsync(actual.Reference with { DenId = Guid.NewGuid().ToString("D") })).Error?.Code);
            Assert.Equal(personal.Revision, Require((await catalog.GetRevisionAsync(id, personal.Revision)).Value).DefinitionRevision);
            Assert.Equal(AgentFailureCode.PermissionDenied,
                (await catalog.ResolveForAsync(new("another-principal", "Connect"))).Error?.Code);
            Assert.Equal(AgentFailureCode.PermissionDenied,
                (await new DenPersistentAgentCatalog(den, "organisation").GetCurrentProjectionAsync(id)).Error?.Code);
        });
    }

    [Fact]
    public async Task Revoked_execution_permission_is_rechecked_after_original_Den_reads()
    {
        await WithDenAsync(async (den, policy) =>
        {
            var id = Guid.NewGuid().ToString("D");
            await den.SaveAsync(Definition(id, "personal", "Agent", "instructions"), 0, Operation());
            var catalog = new DenPersistentAgentCatalog(den, "personal");
            Assert.Null((await catalog.GetCurrentProjectionAsync(id)).Error);
            policy.Execute = false;
            Assert.Equal(AgentFailureCode.PermissionDenied, (await catalog.GetCurrentProjectionAsync(id)).Error?.Code);
            policy.Execute = true;
            policy.RevokeOnSecondExecuteCheck = true;
            policy.ExecutionChecks = 0;
            Assert.Equal(AgentFailureCode.PermissionDenied, (await catalog.GetCurrentProjectionAsync(id)).Error?.Code);
            Assert.Equal(2, policy.ExecutionChecks);
        });
    }

    [Fact]
    public async Task Unsupported_string_ID_and_malformed_policy_fail_without_minting_an_identity()
    {
        await WithDenAsync(async (den, _) =>
        {
            await den.SaveAsync(Definition("named-agent", "personal", "Named", "instructions"), 0, Operation());
            var catalog = new DenPersistentAgentCatalog(den, "personal");
            var unsupported = await catalog.GetCurrentProjectionAsync("named-agent");
            Assert.Null(unsupported.Value);
            Assert.Equal(AgentFailureCode.CapabilityUnavailable, unsupported.Error?.Code);
            var id = Guid.NewGuid().ToString("D");
            await den.SaveAsync(Definition(id, "personal", "Invalid", "instructions") with { ModelPolicyJson = "{" }, 0, Operation());
            Assert.Equal(AgentFailureCode.InvalidInvocationContext, (await catalog.GetCurrentProjectionAsync(id)).Error?.Code);
            Assert.Equal(2, (await den.ListAsync<AgentDefinitionRecord>("personal")).Count);
            Assert.NotNull(await den.GetAsync<AgentDefinitionRecord>("personal", "named-agent"));
        });
    }

    [Fact]
    public async Task Saved_capability_policy_narrows_original_permissions_and_retains_denials_and_approval()
    {
        await WithDenAsync(async (den, _) =>
        {
            var id = Guid.NewGuid().ToString("D");
            var saved = await den.SaveAsync(Definition(id, "personal", "Agent", "instructions") with
            {
                AllowedPermissions = ["web-search", "calendar-read"],
                CapabilityPolicyJson = JsonSerializer.Serialize(new AgentCapabilityPolicy(
                    new HashSet<string> { "web-search", "CALENDAR-READ", "not-originally-allowed" },
                    new HashSet<string> { "web-search" }, false), DenJson.Options)
            }, 0, Operation());
            var projected = Require((await new DenPersistentAgentCatalog(den, "personal").GetCurrentProjectionAsync(id)).Value);
            Assert.Equal(saved.Id, projected.Execution.AgentId); Assert.Equal(saved.Revision, projected.Execution.DefinitionRevision);
            Assert.Equal(new[] { "calendar-read" }, projected.Execution.CapabilityPolicy.AllowedCapabilities);
            Assert.Contains("web-search", projected.Execution.CapabilityPolicy.DeniedCapabilities);
            Assert.True(projected.Execution.CapabilityPolicy.RequireApprovalForConsequentialActions);
            Assert.NotNull(projected.AuthoringDefinition); Assert.Equal(saved.Revision, projected.AuthoringDefinition.Revision);
        });
    }

    [Fact]
    public async Task Saved_delegation_is_validated_detached_and_never_inferred_from_absence()
    {
        await WithDenAsync(async (den, _) =>
        {
            var id = Guid.NewGuid().ToString("D"); var child = Guid.NewGuid().ToString("D");
            var saved = await den.SaveAsync(Definition(id, "personal", "Agent", "instructions") with
            {
                DelegationPolicyJson = JsonSerializer.Serialize(new AgentDelegationPolicy(new HashSet<string> { child },
                    new HashSet<string> { "bounded-class" }, true, 2, 1, new(MaxToolCalls: 2)), DenJson.Options)
            }, 0, Operation());
            var catalog = new DenPersistentAgentCatalog(den, "personal");
            var policy = Require((await catalog.GetCurrentProjectionAsync(id)).Value).Execution.DelegationPolicy;
            Assert.True(policy.AllowSubagents); Assert.Equal(2, policy.MaximumConcurrency); Assert.Equal(1, policy.MaximumDepth);
            Assert.Contains(child, policy.AllowedAgentIds); Assert.Equal(2, policy.Budget.MaxToolCalls);
            saved = await den.SaveAsync(saved with { DelegationPolicyJson = null }, saved.Revision, Operation());
            var absent = Require((await catalog.GetCurrentProjectionAsync(id)).Value).Execution.DelegationPolicy;
            Assert.False(absent.AllowSubagents); Assert.Empty(absent.AllowedAgentIds); Assert.Empty(absent.AllowedAgentClasses);
        });
    }

    [Theory]
    [InlineData("capability-null")]
    [InlineData("capability-unknown")]
    [InlineData("capability-case-collision")]
    [InlineData("delegation-depth")]
    [InlineData("delegation-null-budget")]
    public async Task Invalid_saved_typed_policy_is_refused_from_the_actual_current_Den_record(string kind)
    {
        await WithDenAsync(async (den, _) =>
        {
            var record = Definition(Guid.NewGuid().ToString("D"), "personal", "Agent", "instructions");
            record = kind switch
            {
                "capability-null" => record with { CapabilityPolicyJson = "{\"allowedCapabilities\":null,\"deniedCapabilities\":[]}" },
                "capability-unknown" => record with { CapabilityPolicyJson = "{\"allowedCapabilities\":[],\"deniedCapabilities\":[],\"unexpectedGrant\":true}" },
                "capability-case-collision" => record with { CapabilityPolicyJson = JsonSerializer.Serialize(new AgentCapabilityPolicy(
                    new HashSet<string> { "web-search", "WEB-SEARCH" }, new HashSet<string>()), DenJson.Options) },
                "delegation-depth" => record with { DelegationPolicyJson = JsonSerializer.Serialize(new AgentDelegationPolicy(
                    new HashSet<string>(), new HashSet<string>(), true, 1, 0, new()), DenJson.Options) },
                _ => record with { DelegationPolicyJson = "{\"allowedAgentIds\":[],\"allowedAgentClasses\":[],\"allowSubagents\":false,\"maximumConcurrency\":1,\"maximumDepth\":0,\"budget\":null}" }
            };
            await den.SaveAsync(record, 0, Operation());
            var result = await new DenPersistentAgentCatalog(den, "personal").GetCurrentProjectionAsync(record.Id);
            Assert.Null(result.Value); Assert.Equal(AgentFailureCode.InvalidInvocationContext, result.Error?.Code);
        });
    }

    [Fact]
    public async Task Availability_narrows_original_scope_and_preserves_null_versus_empty_without_granting_foreign_access()
    {
        await WithDenAsync(async (den, _) =>
        {
            var saved = await den.SaveAsync(Definition(Guid.NewGuid().ToString("D"), "personal", "Agent", "instructions"), 0, Operation());
            var catalog = new DenPersistentAgentCatalog(den, "personal");
            var context = new AgentInvocationContext("actual-principal", "Connect", "space-original", "project-original");
            var projection = Require((await catalog.GetCurrentProjectionAsync(saved.Id)).Value);
            Assert.Null((await catalog.GetInvocationProjectionAsync(projection.Reference, context)).Error);
            saved = await den.SaveAsync(saved with { AvailabilityBindings = [] }, saved.Revision, Operation());
            projection = Require((await catalog.GetCurrentProjectionAsync(saved.Id)).Value);
            Assert.Equal(AgentFailureCode.AgentUnavailableInScope, (await catalog.GetInvocationProjectionAsync(projection.Reference, context)).Error?.Code);
            Assert.Empty((await catalog.ResolveForAsync(context)).Value!);
            saved = await den.SaveAsync(saved with { AvailabilityBindings = [new(Guid.NewGuid(), AgentAvailabilityScope.Space, "space-original")] }, saved.Revision, Operation());
            projection = Require((await catalog.GetCurrentProjectionAsync(saved.Id)).Value);
            Assert.Null((await catalog.GetInvocationProjectionAsync(projection.Reference, context)).Error);
            Assert.Single((await catalog.ResolveForAsync(context)).Value!);
            Assert.Equal(AgentFailureCode.AgentUnavailableInScope, (await catalog.GetInvocationProjectionAsync(projection.Reference,
                context with { SpaceId = "foreign-space" })).Error?.Code);
            Assert.Equal(AgentFailureCode.PermissionDenied, (await catalog.GetInvocationProjectionAsync(projection.Reference,
                context with { CallerId = "foreign-caller" })).Error?.Code);
            Assert.Equal(saved.Revision, (await den.GetAsync<AgentDefinitionRecord>("personal", saved.Id))!.Revision);
        });
    }

    private static AgentDefinitionRecord Definition(string id, string space, string name, string instructions) => new()
    {
        Id = id, NamespaceId = space, DisplayName = name, Version = "display-version", Instructions = instructions,
        Enabled = true, Description = "Canonical purpose", AllowedPermissions = ["web-search"], ToolIds = ["canonical-tool-id"],
        ModelPolicyJson = JsonSerializer.Serialize(new AgentModelPolicy(Inherit: true, RequiredCapabilities: new HashSet<string>()), DenJson.Options),
        BudgetJson = JsonSerializer.Serialize(new AgentBudgetLimits(MaxToolCalls: 3), DenJson.Options)
    };

    private static T Require<T>(T? value) where T : class => value ?? throw new InvalidOperationException("Expected the actual canonical result.");
    private static string Operation() => Guid.NewGuid().ToString("N");

    private static async Task WithDenAsync(Func<DulcheDen, MutablePolicy, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-agent-projection-" + Guid.NewGuid().ToString("N"));
        var policy = new MutablePolicy();
        DenStore? store = null;
        Exception? primary = null;
        try
        {
            store = await DenStore.CreateAsync(root, [new("personal", "personal"), new("organisation", "organisation")]);
            await action(new(store, policy, "actual-principal"), policy);
        }
        catch (Exception error) { primary = error; }
        var cleanupErrors = new List<Exception>();
        try { if (store is not null) await store.DisposeAsync(); }
        catch (Exception cleanup) { cleanupErrors.Add(cleanup); }
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (Exception cleanup) { if (!cleanupErrors.Any(error => ReferenceEquals(error, cleanup))) cleanupErrors.Add(cleanup); }
        if (cleanupErrors.Count != 0)
        {
            if (primary is not null && !cleanupErrors.Any(error => ReferenceEquals(error, primary))) cleanupErrors.Insert(0, primary);
            if (cleanupErrors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupErrors[0]).Throw();
            throw new AggregateException(cleanupErrors);
        }
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private sealed class MutablePolicy : IDenAccessPolicy
    {
        public bool Execute { get; set; } = true;
        public bool RevokeOnSecondExecuteCheck { get; set; }
        public int ExecutionChecks { get; set; }
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (principalId == "fixture-admin") return ValueTask.FromResult(true);
            if (principalId != "actual-principal" || namespaceId != "personal") return ValueTask.FromResult(false);
            if (permission != DenPermission.Execute) return ValueTask.FromResult(true);
            ExecutionChecks++;
            return ValueTask.FromResult(Execute && (!RevokeOnSecondExecuteCheck || ExecutionChecks < 2));
        }
    }
}
