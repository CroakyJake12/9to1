using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class AutomationGraphAiEditorTests
{
    [Fact]
    public async Task ProposeEditAsync_AcceptsValidatedTypedGraph()
    {
        var node = new AutomationGraphNodeDefinition(Guid.NewGuid(), BuiltInAutomationNodeCategory.Action, null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "ready" })
        { Title = "Emit", X = 40, Y = 80 };
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [node], []));
        var provider = new FakeProvider(response);
        var service = new AutomationGraphAiEditor(provider);

        var result = await service.ProposeEditAsync(AutomationGraphDefinition.Empty, "Add an emit action", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Graph);
        Assert.Single(result.Graph!.Nodes);
        Assert.Equal(node.Id, result.Graph.Nodes[0].Id);
        Assert.Equal(1, provider.ModelDiscoveryCount);
        Assert.Equal(1, provider.CompletionCount);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsInvalidCycleWithoutReturningGraph()
    {
        var first = ActionNode();
        var second = ActionNode();
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [first, second],
            [
                new AutomationGraphEdgeDefinition(first.Id, second.Id),
                new AutomationGraphEdgeDefinition(second.Id, first.Id)
            ]));
        var service = new AutomationGraphAiEditor(new FakeProvider(response));

        var result = await service.ProposeEditAsync(AutomationGraphDefinition.Empty, "Make a loop", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("cycle", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProposeEditAsync_ProviderDiscoveryFailureReturnsNoMutation()
    {
        var service = new AutomationGraphAiEditor(new FakeProvider("", new InvalidOperationException("No API key configured.")));
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [ActionNode()], []);

        var result = await service.ProposeEditAsync(current, "Change the graph", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("No API key", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Single(current.Nodes);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsUnsupportedCurrentSchemaBeforeProviderDiscovery()
    {
        var current = new AutomationGraphDefinition(2, [ActionNode()], []);
        var provider = new FakeProvider("{}");
        var service = new AutomationGraphAiEditor(provider);

        var result = await service.ProposeEditAsync(current, "Change the graph", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("current graph", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.ModelDiscoveryCount);
        Assert.Equal(0, provider.CompletionCount);
        Assert.Equal(2, current.Version);
        Assert.Single(current.Nodes);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsCyclicCurrentGraphBeforeProviderDiscovery()
    {
        var first = ActionNode(); var second = ActionNode();
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second],
        [
            new AutomationGraphEdgeDefinition(first.Id, second.Id),
            new AutomationGraphEdgeDefinition(second.Id, first.Id)
        ]);
        var provider = new FakeProvider("{}");
        var service = new AutomationGraphAiEditor(provider);

        var result = await service.ProposeEditAsync(current, "Repair this graph", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("cycle", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.ModelDiscoveryCount);
        Assert.Equal(0, provider.CompletionCount);
        Assert.Equal(2, current.Nodes.Count);
        Assert.Equal(2, current.Edges.Count);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsWrongPortDirectionBeforeProviderDiscovery()
    {
        var first = ActionNode() with { Ports = [new("in", "In", AutomationGraphPortDirection.Input, "flow", true)] };
        var second = ActionNode();
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second],
            [new AutomationGraphEdgeDefinition(first.Id, second.Id) { FromPortId = "in" }]);
        var provider = new FakeProvider("{}");
        var service = new AutomationGraphAiEditor(provider);

        var result = await service.ProposeEditAsync(current, "Repair this graph", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("not an output", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.ModelDiscoveryCount);
        Assert.Equal(0, provider.CompletionCount);
        Assert.Equal("in", Assert.Single(current.Edges).FromPortId);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsInvalidBuiltinActionSchemaBeforeProviderDiscovery()
    {
        var invalidAction = ActionNode() with
        {
            Parameters = new Dictionary<string, string> { ["action"] = "unknown" }
        };
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [invalidAction], []);
        var provider = new FakeProvider("{}");
        var service = new AutomationGraphAiEditor(provider);

        var result = await service.ProposeEditAsync(current, "Repair this graph", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("support emit and delay", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.ModelDiscoveryCount);
        Assert.Equal(0, provider.CompletionCount);
        Assert.Equal("unknown", current.Nodes[0].Parameters["action"]);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsMalformedCurrentNodePayloadBeforeProviderDiscovery()
    {
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion,
            [ActionNode() with { Parameters = null! }], []);
        var provider = new FakeProvider("{}");

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(
            current, "Repair malformed parameters", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("missing node", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.ModelDiscoveryCount);
        Assert.Equal(0, provider.CompletionCount);
        Assert.Null(current.Nodes[0].Parameters);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsDeviceNodeWithoutExistingTargetActionContractBeforeProviderDiscovery()
    {
        var deviceNode = new AutomationGraphNodeDefinition(Guid.NewGuid(), DeviceAutomationNodeCategory.Key,
            null, null, new Dictionary<string, string>());
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [deviceNode], []);
        var provider = new FakeProvider("{}");

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(
            current, "Change device action", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("missing its target or action", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.ModelDiscoveryCount);
        Assert.Equal(0, provider.CompletionCount);
        Assert.Null(current.Nodes[0].DeviceTarget);
        Assert.Null(current.Nodes[0].ActionKey);
    }

    [Fact]
    public async Task ProposeEditAsync_AllowsValidNonemptyCurrentGraphEdit()
    {
        var current = ActionNode();
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [current with { Parameters = new Dictionary<string, string> { ["action"] = "emit", ["value"] = "updated" } }], []));
        var provider = new FakeProvider(response);
        var service = new AutomationGraphAiEditor(provider);

        var result = await service.ProposeEditAsync(
            new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [current], []),
            "Update the emitted value", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(current.Id, Assert.Single(result.Graph!.Nodes).Id);
        Assert.Equal("updated", result.Graph.Nodes[0].Parameters["value"]);
        Assert.Equal(1, provider.ModelDiscoveryCount);
        Assert.Equal(1, provider.CompletionCount);
    }

    [Fact]
    public async Task ProposeEditAsync_PreservesOpaqueCustomNodeAndConnectionDuringBuiltinEdit()
    {
        var custom = OpaqueNode();
        var builtin = ActionNode();
        var customConnection = new AutomationGraphEdgeDefinition(custom.Id, builtin.Id)
        {
            FromPortId = "result",
            ToPortId = "in",
            Label = "Existing custom output",
            Metadata = new Dictionary<string, string> { ["owner"] = "custom" }
        };
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [custom, builtin], [customConnection]);
        var updatedBuiltin = builtin with
        {
            Parameters = new Dictionary<string, string> { ["action"] = "emit", ["value"] = "updated builtin" }
        };
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [custom, updatedBuiltin], [customConnection]));
        var provider = new FakeProvider(response);

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(current,
            "Change only the built-in action", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("custom nodes were preserved as opaque", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("were not validated", result.Status, StringComparison.OrdinalIgnoreCase);
        var preserved = Assert.Single(result.Graph!.Nodes, node => node.Id == custom.Id);
        Assert.Equal(custom.Category, preserved.Category);
        Assert.Equal(custom.ActionKey, preserved.ActionKey);
        Assert.Equal(custom.Parameters["opaque.setting"], preserved.Parameters["opaque.setting"]);
        Assert.Equal(custom.Metadata["opaque.metadata"], preserved.Metadata["opaque.metadata"]);
        Assert.Equal(custom.Ports, preserved.Ports);
        Assert.Equal(custom.X, preserved.X); Assert.Equal(custom.Y, preserved.Y);
        Assert.Equal(custom.Width, preserved.Width); Assert.Equal(custom.Height, preserved.Height);
        Assert.Equal(custom.Title, preserved.Title); Assert.Equal(custom.Subtitle, preserved.Subtitle);
        var preservedConnection = Assert.Single(result.Graph.Edges);
        Assert.Equal(customConnection.Id, preservedConnection.Id);
        Assert.Equal(customConnection.FromNodeId, preservedConnection.FromNodeId);
        Assert.Equal(customConnection.ToNodeId, preservedConnection.ToNodeId);
        Assert.Equal(customConnection.FromPortId, preservedConnection.FromPortId);
        Assert.Equal(customConnection.ToPortId, preservedConnection.ToPortId);
        Assert.Equal(customConnection.Label, preservedConnection.Label);
        Assert.Equal(customConnection.Metadata["owner"], preservedConnection.Metadata["owner"]);
        Assert.Equal("updated builtin", result.Graph.Nodes.Single(node => node.Id == builtin.Id).Parameters["value"]);
        Assert.Equal("original", current.Nodes.Single(node => node.Id == custom.Id).Parameters["opaque.setting"]);
        Assert.Equal(1, provider.ModelDiscoveryCount);
        Assert.Equal(1, provider.CompletionCount);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsNewUnregisteredCustomCategory()
    {
        var current = ActionNode();
        var invented = OpaqueNode();
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [current, invented], []));
        var provider = new FakeProvider(response);

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(
            new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [current], []),
            "Add a custom action", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("New custom node category", result.Status, StringComparison.Ordinal);
        Assert.Equal("ready", current.Parameters["value"]);
        Assert.Equal(1, provider.ModelDiscoveryCount);
        Assert.Equal(1, provider.CompletionCount);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsMutationOfOpaqueCustomNodeConfiguration()
    {
        var custom = OpaqueNode();
        var mutated = custom with { Parameters = new Dictionary<string, string> { ["opaque.setting"] = "changed" } };
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [mutated], []));
        var provider = new FakeProvider(response);
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [custom], []);

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(
            current, "Change the custom action", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("Custom node", result.Status, StringComparison.Ordinal);
        Assert.Contains("was modified", result.Status, StringComparison.Ordinal);
        Assert.Equal("original", current.Nodes[0].Parameters["opaque.setting"]);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("ports")]
    public async Task ProposeEditAsync_RejectsOpaqueCustomMetadataOrPortSchemaMutation(string changedField)
    {
        var custom = OpaqueNode();
        var mutated = changedField switch
        {
            "metadata" => custom with { Metadata = new Dictionary<string, string> { ["opaque.metadata"] = "changed" } },
            "ports" => custom with { Ports = [new("result", "Result", AutomationGraphPortDirection.Output, "string", true)] },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField))
        };
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [mutated], []));
        var provider = new FakeProvider(response);
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [custom], []);

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(
            current, "Change custom metadata or ports", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("was modified", result.Status, StringComparison.Ordinal);
        Assert.Equal("preserve", current.Nodes[0].Metadata["opaque.metadata"]);
        Assert.Equal("flow", current.Nodes[0].Ports.Single().DataType);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsMutationOfOpaqueCustomConnection()
    {
        var custom = OpaqueNode(); var builtin = ActionNode();
        var connection = new AutomationGraphEdgeDefinition(custom.Id, builtin.Id)
        {
            FromPortId = "result", ToPortId = "in", Label = "Keep this route"
        };
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [custom, builtin], [connection]);
        var mutatedConnection = connection with { Label = "Redirected route" };
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [custom, builtin], [mutatedConnection]));
        var provider = new FakeProvider(response);

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(
            current, "Redirect the custom connection", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("connection touching an opaque custom node", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Keep this route", Assert.Single(current.Edges).Label);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsRemovalOfOpaqueCustomNodeAndAttachedConnection()
    {
        var custom = OpaqueNode(); var builtin = ActionNode();
        var connection = new AutomationGraphEdgeDefinition(custom.Id, builtin.Id) { FromPortId = "result" };
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [custom, builtin], [connection]);
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [builtin], []));
        var provider = new FakeProvider(response);

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(
            current, "Remove the custom action", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("was removed or omitted", result.Status, StringComparison.Ordinal);
        Assert.Single(current.Nodes, node => node.Id == custom.Id);
        Assert.Equal(connection.Id, Assert.Single(current.Edges).Id);
    }

    [Fact]
    public async Task ProposeEditAsync_DoesNotClaimSuccessWhenCustomNodeRequestReturnsUnchangedGraph()
    {
        var custom = OpaqueNode();
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [custom], []);
        var provider = new FakeProvider(AutomationGraphCodec.Serialize(current));

        var result = await new AutomationGraphAiEditor(provider).ProposeEditAsync(
            current, "Change the custom node configuration", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("No supported edit", result.Status, StringComparison.Ordinal);
        Assert.Equal("original", current.Nodes[0].Parameters["opaque.setting"]);
        Assert.Equal(1, provider.ModelDiscoveryCount);
        Assert.Equal(1, provider.CompletionCount);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsModelGraphWithMissingConnectionIdentityWithoutRepairingIt()
    {
        const string response = "{\"version\":1,\"nodes\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"category\":\"Trigger\",\"parameters\":{}},{\"id\":\"22222222-2222-2222-2222-222222222222\",\"category\":\"Action\",\"parameters\":{}}],\"edges\":[{\"fromNodeId\":\"11111111-1111-1111-1111-111111111111\",\"toNodeId\":\"22222222-2222-2222-2222-222222222222\"}]}";
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [ActionNode()], []);
        var service = new AutomationGraphAiEditor(new FakeProvider(response));

        var result = await service.ProposeEditAsync(current, "Connect a trigger to an action", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Equal(AutomationGraphDefinition.CurrentVersion, current.Version);
        Assert.Single(current.Nodes);
        Assert.Empty(current.Edges);
    }

    private static AutomationGraphNodeDefinition ActionNode() =>
        new(Guid.NewGuid(), BuiltInAutomationNodeCategory.Action, null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "ready" })
        { Title = "Emit" };

    private static AutomationGraphNodeDefinition OpaqueNode() =>
        new(Guid.NewGuid(), "custom.calendar.create", null, "calendar.create",
            new Dictionary<string, string> { ["opaque.setting"] = "original" })
        {
            X = 40, Y = 70, Width = 240, Height = 120, Title = "Calendar create", Subtitle = "Custom connector action",
            Ports = [new("result", "Result", AutomationGraphPortDirection.Output, "flow", true)],
            Metadata = new Dictionary<string, string> { ["opaque.metadata"] = "preserve" }
        };

    private sealed class FakeProvider(string response, Exception? discoveryFailure = null) : IProviderModelClient
    {
        public int ModelDiscoveryCount { get; private set; }
        public int CompletionCount { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        {
            ModelDiscoveryCount++;
            if (discoveryFailure is not null) return Task.FromException<IReadOnlyList<ModelDescriptor>>(discoveryFailure);
            IReadOnlySet<ToolCapability> capabilities = new HashSet<ToolCapability> { ToolCapability.Text };
            return Task.FromResult<IReadOnlyList<ModelDescriptor>>(
                [new ModelDescriptor("fake-text", 0, "test", "", "", capabilities, DateTimeOffset.UtcNow)]);
        }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return await CompleteAsync(request, cancellationToken);
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        { CompletionCount++; return Task.FromResult(response); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }
}
