using System.Text.Json;
using Dulche.Runtime;
using Xunit;

public sealed class ToolRegistryTests
{
    private static readonly CapabilityRequestContext Context = new("user", "linux", new HashSet<string> { "write" },
        new HashSet<string> { "automations", "image", "present", "plugin", "computer" }, new HashSet<string>(), new HashSet<string>(), true);
    private static RegistryCapability Tool(string id, string owner, string intent, string[]? inputs = null, string[]? outputs = null,
        CapabilityReadiness state = CapabilityReadiness.Executable, CapabilitySourceType source = CapabilitySourceType.App) =>
        new(id, source, owner, "1", id, "Generate", new HashSet<string> { intent }, new HashSet<string>(inputs ?? []),
            new HashSet<string>(outputs ?? []), "{\"type\":\"object\"}", new HashSet<string> { "write" }, new HashSet<string> { "linux" }, [], state, true, true, false,
            intent == "time.trigger" ? ["remind me", "set a reminder"] : intent == "image.generate" ? ["create an image", "generate an image"] : ["create a presentation"], owner);
    private static ResolutionIntent Intent(string tag) => new(tag, JsonSerializer.SerializeToElement(new { }));

    [Fact] public async Task SemanticOutcomeResolutionAndTypedCompositionDoNotNeedActionName()
    {
        var registry = new DulcheToolRegistry(new Authority());
        await registry.RegisterAsync("home", Tool("time", "automations", "time.trigger"));
        await registry.RegisterAsync("image", Tool("render", "image", "image.generate", outputs: ["image.reference"]));
        await registry.RegisterAsync("present", Tool("insert", "present", "slide.insert", inputs: ["image.reference"], outputs: ["slide.reference"]));
        Assert.Single(registry.Search("remind me tomorrow at 4 PM", Context));
        var result = registry.Resolve([Intent("create an image"), Intent("slide.insert")], Context);
        Assert.Null(result.Failure);
        var plan = Assert.Single(result.Plans);
        Assert.Equal("step-1", Assert.Single(plan.Steps[1].Dependencies));
        Assert.Equal("Generate", plan.Steps[0].ActionId);
    }
    [Fact] public async Task ExplicitInvocationPrioritisesAndPermissionAndConfigurationRemainDistinct()
    {
        var registry = new DulcheToolRegistry(new Authority());
        await registry.RegisterAsync("image", Tool("a", "image", "image.generate"));
        await registry.RegisterAsync("plugin", Tool("b", "plugin", "image.generate"));
        var explicitContext = Context with { ExplicitOwnerInvocations = new HashSet<string> { "plugin" } };
        Assert.Equal("b", Assert.Single(registry.Resolve([Intent("image.generate")], explicitContext).Plans).Steps[0].CapabilityId);
        Assert.Equal(ResolutionFailureKind.PermissionBlocked, registry.Resolve([Intent("image.generate")], Context with { PermissionScopes = new HashSet<string>() }).Failure);
        await registry.RegisterAsync("image", Tool("a", "image", "image.generate", state: CapabilityReadiness.AccountConnectionRequired));
        await registry.RemoveAsync("plugin", "b");
        Assert.Equal(ResolutionFailureKind.MissingPrerequisite, registry.Resolve([Intent("image.generate")], Context).Failure);
    }
    [Fact] public async Task RegistryChangesInvalidatePriorResolutionAndUntrustedRegistrationFails()
    {
        var registry = new DulcheToolRegistry(new Authority());
        Assert.False((await registry.RegisterAsync("model-output", Tool("a", "image", "image.generate"))).Succeeded);
        await registry.RegisterAsync("image", Tool("a", "image", "image.generate"));
        var resolved = registry.Resolve([Intent("image.generate")], Context);
        Assert.NotNull(registry.ExplainResolution(resolved.ResolutionId, Context));
        await registry.RemoveAsync("image", "a");
        Assert.Null(registry.ExplainResolution(resolved.ResolutionId, Context));
        Assert.Equal(ResolutionFailureKind.NoMatchingCapability, registry.Resolve([Intent("image.generate")], Context).Failure);
    }
    [Fact] public async Task ComputerUseIsAbsentWithoutExplicitInvocation()
    {
        var registry = new DulcheToolRegistry(new Authority());
        await registry.RegisterAsync("computer", Tool("computer", "computer", "ui.operate", source: CapabilitySourceType.ComputerUse));
        Assert.Empty(registry.List(Context).Entries);
        Assert.Single(registry.List(Context with { ComputerUseInvocationId = "explicit-1" }).Entries);
    }
    private sealed class Authority : IToolRegistryAuthority
    {
        public ValueTask<bool> MayRegisterAsync(string registrar, string owner, CancellationToken cancellationToken) =>
            ValueTask.FromResult(registrar == "home" || registrar == owner);
    }
}
