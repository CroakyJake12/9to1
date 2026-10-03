using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Actual shared evaluator over a configured fixture policy. This checks the owning
/// rename restriction; no tool dispatch, Home grant or production persisted-store claim is inferred.</summary>
public sealed class FilesOriginalModelPermissionTests
{
    [Fact]
    public async Task Configured_EditFiles_denial_applies_to_original_rename_and_current_policy_refresh_preserves_defaults()
    {
        var model = new ProviderModelDescriptor("ollama", true, new ModelDescriptor("fixture-model", 1,
            "fixture", "1B", "fixture", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools },
            DateTimeOffset.UnixEpoch));
        var denied = ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel, model.Key,
            ModelPermissionScope.ThisDevice, RestrictedModelCapability.EditFiles);
        var store = new ConfiguredPolicyStore(new ModelPermissionPolicy([denied]));
        var evaluator = new ModelPermissionEvaluator(store);
        var rename = ModelToolPermissionMap.Map("files_rename");
        Assert.Equal(RestrictedModelCapability.EditFiles, rename);
        var originalDecision = await evaluator.EvaluateAsync(model, rename!.Value, cancellationToken: CancellationToken.None);
        Assert.False(originalDecision.Allowed);
        Assert.Equal(denied, originalDecision.DenyingRule);
        Assert.False((await evaluator.EvaluateAsync(model, ModelToolPermissionMap.Map("write_file")!.Value,
            cancellationToken: CancellationToken.None)).Allowed);
        var other = model with { Model = model.Model with { Name = "another-model" } };
        Assert.True((await evaluator.EvaluateAsync(other, rename.Value, cancellationToken: CancellationToken.None)).Allowed);
        await store.SavePolicyAsync(ModelPermissionPolicy.Empty, CancellationToken.None);
        Assert.True((await evaluator.EvaluateAsync(model, rename.Value, cancellationToken: CancellationToken.None)).Allowed);
        Assert.Equal(4, store.Reads);
        Assert.Null(ModelToolPermissionMap.Map("list_files"));
        Assert.Null(ModelToolPermissionMap.Map("unknown_tool"));
    }

    private sealed class ConfiguredPolicyStore(ModelPermissionPolicy initial) : IModelPermissionStore
    {
        private ModelPermissionPolicy _policy = initial;
        internal int Reads;
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Reads++; return Task.FromResult(_policy); }
        public Task SavePolicyAsync(ModelPermissionPolicy policy, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); _policy = policy; return Task.CompletedTask; }
    }
}
