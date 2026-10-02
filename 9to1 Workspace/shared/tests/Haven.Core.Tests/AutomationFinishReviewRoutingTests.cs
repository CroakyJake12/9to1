using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Registered descriptor routing only; no model inference, owner approval or effect proof.</summary>
public sealed class AutomationFinishReviewRoutingTests
{
    [Theory]
    [InlineData(HavenMode.Tasks, true, true, true, true)]
    [InlineData(HavenMode.Studio, true, true, true, true)]
    [InlineData(HavenMode.Chat, true, true, true, false)]
    [InlineData(HavenMode.Tasks, false, true, true, false)]
    [InlineData(HavenMode.Tasks, true, false, true, false)]
    [InlineData(HavenMode.Tasks, true, true, false, false)]
    public void Only_registered_finish_descriptor_with_actual_mode_capability_and_host_routes_to_Automation(
        HavenMode mode, bool registered, bool host, bool capability, bool expected)
    {
        var definition = new OllamaToolDefinition("automation_finish_review", "Controlled exact registered descriptor",
            new Dictionary<string, object>(), []);
        var active = ActiveCapability.FromDefinition(Assert.Single(CapabilityRegistryCatalog.BuiltIns,
            value => value.Key == "create-automation"));
        var context = new ToolAvailabilityContext(mode, null, capability ? [active] : [],
            PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask, true, false, false, host);
        var sources = new ToolDefinitionSources([], [], [], [], registered ? [definition] : [], []);
        var plan = new ToolAvailabilityPlanner().Create(context, sources);
        Assert.Equal(expected, plan.TryGetRuntime(definition.Name, out var runtime));
        if (expected)
        {
            Assert.Equal(ToolRuntimeKind.Automation, runtime);
            Assert.Same(definition, Assert.Single(plan.Definitions, value => value.Name == definition.Name));
        }
        else Assert.DoesNotContain(plan.Definitions, value => value.Name == definition.Name);
        Assert.False(plan.TryGetRuntime("automation_finish_review_extra", out _));
    }
}
