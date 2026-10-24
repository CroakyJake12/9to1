using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class AutomationGraphTestRunnerTests
{
    [Fact]
    public async Task Device_node_without_preview_fails_test_and_requires_explicit_real_run()
    {
        var target = new DeviceTargetDescriptor("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, "test.device");
        var node = AutomationGraphNodeDefinition.FromDevice(new DeviceAutomationNodeDefinition(Guid.NewGuid(), target, "ui.snapshot", new Dictionary<string, string>()));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [node], []);
        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Equal(AutomationGraphRunMode.Test, result.Mode);
        var trace = Assert.Single(result.Trace); Assert.Equal(AutomationGraphTraceStatus.Failed, trace.Status);
        Assert.Contains("no device preview", trace.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit real Run", trace.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_graph_forwards_an_action_output_to_the_next_emit_node()
    {
        var source = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "ready" });
        var forward = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "{{input}}" });
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [source, forward], [new(source.Id, forward.Id)]);

        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AutomationGraphRunMode.Test, result.Mode);
        var forwardTrace = Assert.Single(result.Trace, item => item.NodeId == forward.Id);
        Assert.Equal("ready", forwardTrace.Output);
        Assert.Equal("ready", forwardTrace.Inputs![source.Id]);
        Assert.Contains("forward", forwardTrace.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_runner_accepts_selected_typed_trigger_sample_data()
    {
        var trigger = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Trigger", null, null,
            new Dictionary<string, string>())
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, "string", true)]
        };
        var action = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "{{input}}" })
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, "string", true), new("out", "Out", AutomationGraphPortDirection.Output, "string", true)]
        };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue> { ["payload"] = AutomationGraphValue.FromString("sample input") }
        };

        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None, samples);

        Assert.True(result.Succeeded);
        var input = Assert.Single(Assert.Single(result.Trace, item => item.NodeId == action.Id).TypedInputs!["payload"]);
        Assert.True(input.TryGetString(out var value));
        Assert.Equal("sample input", value);
        var output = Assert.Single(result.Trace.Single(item => item.NodeId == action.Id).TypedOutputs!);
        Assert.Equal("out", output.Key);
        Assert.True(output.Value.TryGetString(out var forwarded));
        Assert.Equal("sample input", forwarded);
    }

    [Fact]
    public async Task Test_runner_does_not_coerce_typed_emit_input_to_a_different_output_type()
    {
        using var sampleJson = System.Text.Json.JsonDocument.Parse("42");
        var trigger = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Trigger", null, null,
            new Dictionary<string, string>())
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, "integer", true)]
        };
        var action = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "{{input}}" })
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, "integer", true), new("out", "Out", AutomationGraphPortDirection.Output, "string", true)]
        };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue>
            {
                ["payload"] = AutomationGraphValue.FromJson("integer", sampleJson.RootElement)
            }
        };

        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None, samples);

        Assert.False(result.Succeeded);
        Assert.Contains("without a registered conversion", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Trace, item => item.NodeId == action.Id && item.Status == AutomationGraphTraceStatus.Failed);
    }

    [Theory]
    [InlineData(AutomationGraphValueState.Null)]
    [InlineData(AutomationGraphValueState.Unavailable)]
    [InlineData(AutomationGraphValueState.Redacted)]
    public async Task Test_runner_emit_forwards_typed_absence_states_without_collapsing_them(AutomationGraphValueState state)
    {
        var trigger = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Trigger", null, null,
            new Dictionary<string, string>())
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, "string", true)]
        };
        var action = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "{{input}}" })
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, "string", true), new("out", "Out", AutomationGraphPortDirection.Output, "string", true)]
        };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        var sample = state switch
        {
            AutomationGraphValueState.Null => AutomationGraphValue.Null("string"),
            AutomationGraphValueState.Unavailable => AutomationGraphValue.Unavailable("string"),
            AutomationGraphValueState.Redacted => AutomationGraphValue.Redacted("string"),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue> { ["payload"] = sample }
        };

        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None, samples);

        Assert.True(result.Succeeded);
        var forwarded = Assert.Single(result.Trace.Single(item => item.NodeId == action.Id).TypedOutputs!).Value;
        Assert.Equal(state, forwarded.State);
        Assert.Null(forwarded.JsonValue);
    }

    [Fact]
    public void Draft_preparation_reopens_next_validated_revision_without_changing_active_identity_or_cas_base()
    {
        using var schemaDocument = System.Text.Json.JsonDocument.Parse("{\"type\":\"object\",\"additionalProperties\":false}");
        using var configuration = System.Text.Json.JsonDocument.Parse("{}");
        var profile = "automations";
        var type = new GraphNodeType("automations.manual-trigger", 1, "automations", "automations",
            schemaDocument.RootElement.Clone(), []);
        var schemas = new NodeGraphSchemaRegistry([type], [new GraphCapabilityProfile(profile,
            new HashSet<string>([type.TypeId], StringComparer.Ordinal),
            new HashSet<string>([type.CapabilityId], StringComparer.Ordinal))]);
        var automationId = Guid.NewGuid(); var graphId = Guid.NewGuid();
        const long savedDraftRevision = 4; const long activeRevision = 2;
        var saved = new GraphDocument(GraphDocument.CurrentSchemaVersion, graphId, savedDraftRevision, profile,
            GraphRevisionState.Draft,
            [new GraphNode(Guid.NewGuid(), type.TypeId, type.Version, type.AppId, type.CapabilityId, null,
                configuration.RootElement.Clone(), [])], []);
        var binding = new AutomationDefinitionGraphBinding(graphId, savedDraftRevision, activeRevision);
        var reusable = new ReusableTaskDefinition(automationId, "Automation", "", "", null, false,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) { GraphBinding = binding };
        var linked = new AutomationDefinition(automationId, "Automation", HavenMode.Chat, "",
            AutomationScheduleKind.Unscheduled, "{}", null, null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            { GraphBinding = binding };
        var current = new GraphPublicationSnapshot(Guid.NewGuid(), reusable, linked, saved,
            savedDraftRevision, activeRevision);
        var edited = saved with { Revision = savedDraftRevision + 1 };

        var preparation = AutomationGraphPublicationPreparation.PrepareNextDraft(current, edited, schemas);
        var reopened = preparation.Read();

        Assert.Equal(automationId, reopened.Reusable.Id);
        Assert.Equal(automationId, reopened.LinkedSchedule!.Id);
        Assert.Equal(graphId, reopened.Draft.GraphId);
        Assert.Equal(savedDraftRevision, reopened.ExpectedGraphRevision);
        Assert.Equal(savedDraftRevision + 1, reopened.Draft.Revision);
        Assert.Equal(savedDraftRevision + 1, reopened.Reusable.GraphBinding!.DraftRevision);
        Assert.Equal(activeRevision, reopened.OriginalActiveRevision);
        Assert.Equal(activeRevision, reopened.LinkedSchedule.GraphBinding!.ActiveRevision);
        Assert.Equal(savedDraftRevision, current.Reusable.GraphBinding!.DraftRevision);
        Assert.Equal(activeRevision, current.LinkedSchedule.GraphBinding!.ActiveRevision);
        Assert.Equal(savedDraftRevision, current.Draft.Revision);

        // A detached proposal is not permission to reinterpret an unknown saved
        // schema or pass an Active graph off as the saved Draft CAS basis.
        Assert.Throws<InvalidOperationException>(() => AutomationGraphPublicationPreparation.PrepareNextDraft(
            current with { Draft = saved with { SchemaVersion = GraphDocument.CurrentSchemaVersion + 1 } }, edited, schemas));
        Assert.Throws<InvalidOperationException>(() => AutomationGraphPublicationPreparation.PrepareNextDraft(
            current with { Draft = saved with { State = GraphRevisionState.Active } }, edited, schemas));
        Assert.Equal(GraphRevisionState.Draft, current.Draft.State);
        Assert.Equal(GraphDocument.CurrentSchemaVersion, current.Draft.SchemaVersion);
    }
}
