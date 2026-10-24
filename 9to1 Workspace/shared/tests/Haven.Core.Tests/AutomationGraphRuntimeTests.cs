using System.Text.Json;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;

namespace Haven.Core.Tests;

public sealed class AutomationGraphRuntimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Unsupported_graph_version_is_rejected_by_direct_runtime_before_execution(int version)
    {
        var action = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(version, [action], []);

        Assert.Contains(AutomationGraphRunner.ValidateStructure(graph), issue => issue.Code == "graph.version.unsupported");
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "graph.version.unsupported");
        Assert.Equal(0, executor.CanExecuteCount);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
        Assert.Equal(version, graph.Version);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_graph_collections_are_rejected_without_executor_discovery(bool missingNodes)
    {
        var action = Node("Action"); var executor = new RecordingExecutor("Action");
        IReadOnlyList<AutomationGraphNodeDefinition> nodes = missingNodes ? null! : new[] { action };
        IReadOnlyList<AutomationGraphEdgeDefinition> edges = missingNodes ? Array.Empty<AutomationGraphEdgeDefinition>() : null!;
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, nodes, edges);

        Assert.Contains(AutomationGraphRunner.ValidateStructure(graph), issue => issue.Code == "graph.shape");
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "graph.shape");
        Assert.Equal(0, executor.CanExecuteCount);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Empty_graph_and_valid_graph_with_no_edges_keep_their_distinct_behavior()
    {
        var emptyIssues = AutomationGraphRunner.ValidateStructure(AutomationGraphDefinition.Empty);
        Assert.Contains(emptyIssues, issue => issue.Code == "graph.empty");
        Assert.DoesNotContain(emptyIssues, issue => issue.Code == "graph.shape");

        var executor = new RecordingExecutor("Action");
        var oneNode = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [Node("Action")], []);
        var result = await new AutomationGraphRunner([executor]).RunAsync(oneNode, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(1, executor.ExecuteCount);
    }

    [Fact]
    public async Task Missing_node_category_with_connections_returns_structured_validation_before_executor_discovery()
    {
        var malformed = Node("Trigger") with { Category = null! };
        var target = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [malformed, target],
            [new AutomationGraphEdgeDefinition(malformed.Id, target.Id)]);

        var issues = AutomationGraphRunner.ValidateStructure(graph);
        Assert.Contains(issues, issue => issue.Code == "node.category.missing");
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "node.category.missing");
        Assert.Equal(0, executor.CanExecuteCount);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_port_schema_is_rejected_before_executor_discovery(bool invalidDataType)
    {
        var port = new AutomationGraphPortDefinition("out", "Out",
            invalidDataType ? AutomationGraphPortDirection.Output : (AutomationGraphPortDirection)99,
            invalidDataType ? " " : "string", true);
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion,
            [Node("Action") with { Ports = [port] }], []);
        var executor = new RecordingExecutor("Action");

        var issues = AutomationGraphRunner.ValidateStructure(graph);
        Assert.Contains(issues, issue => issue.Code == (invalidDataType ? "port.type.missing" : "port.direction.invalid"));
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == (invalidDataType ? "port.type.missing" : "port.direction.invalid"));
        Assert.Equal(0, executor.CanExecuteCount);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Theory]
    [InlineData("node-parameters")]
    [InlineData("node-metadata")]
    [InlineData("edge-metadata")]
    public async Task Missing_graph_payload_collections_are_rejected_before_executor_discovery(string missingField)
    {
        var first = Node("Action"); var second = Node("Action");
        var graph = missingField switch
        {
            "node-parameters" => new AutomationGraphDefinition(AutomationGraphDefinition.CurrentSchemaVersion,
                [first with { Parameters = null! }], []),
            "node-metadata" => new AutomationGraphDefinition(AutomationGraphDefinition.CurrentSchemaVersion,
                [first with { Metadata = null! }], []),
            "edge-metadata" => new AutomationGraphDefinition(AutomationGraphDefinition.CurrentSchemaVersion,
                [first, second], [new AutomationGraphEdgeDefinition(first.Id, second.Id) { Metadata = null! }]),
            _ => throw new ArgumentOutOfRangeException(nameof(missingField))
        };
        var executor = new RecordingExecutor("Action");

        var issues = AutomationGraphRunner.ValidateStructure(graph);
        Assert.Contains(issues, issue => issue.Code == "graph.shape");
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "graph.shape");
        Assert.Equal(0, executor.CanExecuteCount);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Invalid_cycle_is_rejected_before_any_executor_side_effect()
    {
        var first = Node("Action"); var second = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new(first.Id, second.Id), new(second.Id, first.Id)]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Contains(result.ValidationIssues, issue => issue.Code == "graph.cycle"); Assert.Equal(0, executor.ExecuteCount); Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Empty_connection_id_is_rejected_before_any_executor_side_effect()
    {
        var first = Node("Action"); var second = Node("Action"); var executor = new RecordingExecutor("Action");
        var edge = new AutomationGraphEdgeDefinition(first.Id, second.Id) { Id = Guid.Empty };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [edge]);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "edge.id.empty");
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Duplicate_connection_ids_are_rejected_before_branch_execution()
    {
        var first = Node("Action"); var second = Node("Action"); var third = Node("Action");
        var duplicateId = Guid.NewGuid(); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second, third],
        [
            new AutomationGraphEdgeDefinition(first.Id, second.Id) { Id = duplicateId },
            new AutomationGraphEdgeDefinition(first.Id, third.Id) { Id = duplicateId }
        ]);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "edge.id.duplicate");
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Single_connection_output_accepts_exactly_one_valid_edge()
    {
        var source = Node("Action") with
        {
            Ports = [new("out", "Out", AutomationGraphPortDirection.Output, "flow", false)]
        };
        var target = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [source, target],
            [new AutomationGraphEdgeDefinition(source.Id, target.Id)]);

        Assert.DoesNotContain(AutomationGraphRunner.ValidateStructure(graph), issue => issue.Code == "port.multiple.output");
        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, executor.ExecuteCount);
        Assert.DoesNotContain(result.ValidationIssues, issue => issue.Code == "port.multiple.output");
    }

    [Fact]
    public async Task Single_connection_output_port_rejects_fanout_before_executor_discovery()
    {
        var source = Node("Action") with
        {
            Ports = [new("out", "Out", AutomationGraphPortDirection.Output, "flow", false)]
        };
        var first = Node("Action") with
        {
            Ports = [new("in", "In", AutomationGraphPortDirection.Input, "flow", true)]
        };
        var second = Node("Action") with
        {
            Ports = [new("in", "In", AutomationGraphPortDirection.Input, "flow", true)]
        };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [source, first, second],
        [
            new AutomationGraphEdgeDefinition(source.Id, first.Id),
            new AutomationGraphEdgeDefinition(source.Id, second.Id)
        ]);
        var executor = new RecordingExecutor("Action");

        var issues = AutomationGraphRunner.ValidateStructure(graph);
        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.Contains(issues, issue => issue.Code == "port.multiple.output" && issue.NodeId == source.Id);
        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "port.multiple.output" && issue.NodeId == source.Id);
        Assert.Equal(0, executor.CanExecuteCount);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Condition_selects_only_matching_branch_and_marks_other_branch_skipped()
    {
        var trigger = Node("Trigger");
        var condition = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Condition", null, null, new Dictionary<string, string> { ["expression"] = "true" });
        var yes = Node("Action"); var no = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, condition, yes, no], [new AutomationGraphEdgeDefinition(trigger.Id, condition.Id), new AutomationGraphEdgeDefinition(condition.Id, yes.Id) { Branch = "true" }, new AutomationGraphEdgeDefinition(condition.Id, no.Id) { Branch = "false" }]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(1, executor.ExecuteCount); Assert.Equal(yes.Id, executor.ExecutedNodes.Single()); Assert.Contains(result.Trace, item => item.NodeId == condition.Id && item.Branch == "true"); Assert.Contains(result.Trace, item => item.NodeId == no.Id && item.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task Condition_output_ports_select_branch_even_without_legacy_branch_metadata(string expression, bool expectedTrue)
    {
        var trigger = Node("Trigger");
        var condition = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Condition", null, null,
            new Dictionary<string, string> { ["expression"] = expression })
        {
            Ports =
            [
                new("in", "In", AutomationGraphPortDirection.Input, "flow", true),
                new("true", "True", AutomationGraphPortDirection.Output, "flow", true),
                new("false", "False", AutomationGraphPortDirection.Output, "flow", true)
            ]
        };
        var yes = Node("Action"); var no = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, condition, yes, no],
        [
            new(trigger.Id, condition.Id),
            new AutomationGraphEdgeDefinition(condition.Id, yes.Id) { FromPortId = "true" },
            new AutomationGraphEdgeDefinition(condition.Id, no.Id) { FromPortId = "false" }
        ]);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.True(result.Succeeded);
        var selected = expectedTrue ? yes : no;
        var skipped = expectedTrue ? no : yes;
        Assert.Equal(new[] { selected.Id }, executor.ExecutedNodes);
        Assert.Contains(result.Trace, item => item.NodeId == skipped.Id && item.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Conflicting_condition_port_and_legacy_branch_metadata_are_rejected()
    {
        var condition = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Condition", null, null,
            new Dictionary<string, string> { ["expression"] = "true" })
        {
            Ports =
            [
                new("in", "In", AutomationGraphPortDirection.Input, "flow", true),
                new("true", "True", AutomationGraphPortDirection.Output, "flow", true),
                new("false", "False", AutomationGraphPortDirection.Output, "flow", true)
            ]
        };
        var target = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [condition, target],
            [new AutomationGraphEdgeDefinition(condition.Id, target.Id) { FromPortId = "true", Branch = "false" }]);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "edge.branch.mismatch");
        Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task Typed_condition_uses_connected_numeric_values_to_select_branch()
    {
        using var leftDocument = JsonDocument.Parse("5");
        using var rightDocument = JsonDocument.Parse("3");
        var (graph, trigger, condition, whenTrue, whenFalse) = TypedConditionGraph("greaterThan");
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger] = new Dictionary<string, AutomationGraphValue>
            {
                ["left"] = AutomationGraphValue.FromJson("number", leftDocument.RootElement),
                ["right"] = AutomationGraphValue.FromJson("number", rightDocument.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, samples);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { whenTrue }, executor.ExecutedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == condition && trace.Branch == "true");
        Assert.Contains(result.Trace, trace => trace.NodeId == whenFalse && trace.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Typed_integer_condition_compares_values_without_double_precision_loss()
    {
        using var leftDocument = JsonDocument.Parse("9007199254740993");
        using var rightDocument = JsonDocument.Parse("9007199254740992");
        var (graph, trigger, _, whenTrue, whenFalse) = TypedConditionGraph("greaterThan", "integer");
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger] = new Dictionary<string, AutomationGraphValue>
            {
                ["left"] = AutomationGraphValue.FromJson("integer", leftDocument.RootElement),
                ["right"] = AutomationGraphValue.FromJson("integer", rightDocument.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, samples);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { whenTrue }, executor.ExecutedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenFalse && trace.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Theory]
    [InlineData("greaterThan", "9007199254740993", "9007199254740992", true)]
    [InlineData("equals", "9007199254740992", "9007199254740993", false)]
    [InlineData("greaterThan", "1.25", "1.2", true)]
    [InlineData("equals", "1.2300", "123e-2", true)]
    [InlineData("greaterThan", "1e-400", "0", true)]
    [InlineData("equals", "1e-400", "0", false)]
    public async Task Typed_number_condition_compares_decimal_tokens_exactly(
        string operation, string leftJson, string rightJson, bool expectedTrue)
    {
        var (result, whenTrue, whenFalse, executedNodes) =
            await RunTypedNumberConditionAsync(operation, leftJson, rightJson);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { expectedTrue ? whenTrue : whenFalse }, executedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == (expectedTrue ? whenTrue : whenFalse) &&
            trace.Status == AutomationGraphTraceStatus.Succeeded);
    }

    [Theory]
    [InlineData("greaterThan")]
    [InlineData("equals")]
    public async Task Typed_number_condition_refuses_exponents_outside_exact_comparison_bounds(string operation)
    {
        var (result, whenTrue, whenFalse, executedNodes) =
            await RunTypedNumberConditionAsync(operation, "1e1000001", "0");

        Assert.False(result.Succeeded);
        Assert.Contains("exact token or exponent bounds", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(executedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenTrue && trace.Status == AutomationGraphTraceStatus.Skipped);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenFalse && trace.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Typed_number_condition_refuses_numeric_tokens_over_the_exact_length_bound()
    {
        var oversizedToken = "1" + new string('0', 256);
        var (result, whenTrue, whenFalse, executedNodes) =
            await RunTypedNumberConditionAsync("greaterThan", oversizedToken, "0");

        Assert.False(result.Succeeded);
        Assert.Contains("exact token or exponent bounds", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(executedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenTrue && trace.Status == AutomationGraphTraceStatus.Skipped);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenFalse && trace.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Typed_number_condition_refuses_numeric_tokens_outside_its_bounded_precision()
    {
        var overBoundToken = "1" + new string('0', 256);
        var (result, whenTrue, whenFalse, executedNodes) = await RunTypedNumberConditionAsync("greaterThan", overBoundToken, "0");

        Assert.False(result.Succeeded);
        Assert.Contains("exact token or exponent bounds", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(executedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenTrue && trace.Status == AutomationGraphTraceStatus.Skipped);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenFalse && trace.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Typed_condition_does_not_select_a_branch_from_unavailable_data()
    {
        using var rightDocument = JsonDocument.Parse("3");
        var (graph, trigger, _, whenTrue, whenFalse) = TypedConditionGraph("greaterThan");
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger] = new Dictionary<string, AutomationGraphValue>
            {
                ["left"] = AutomationGraphValue.Unavailable("number"),
                ["right"] = AutomationGraphValue.FromJson("number", rightDocument.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, samples);

        Assert.False(result.Succeeded);
        Assert.Contains("unavailable or redacted", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(executor.ExecutedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenTrue && trace.Status == AutomationGraphTraceStatus.Skipped);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenFalse && trace.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Typed_condition_does_not_guess_explicit_null_comparison_semantics()
    {
        using var rightDocument = JsonDocument.Parse("3");
        var (graph, trigger, _, whenTrue, whenFalse) = TypedConditionGraph("equals");
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger] = new Dictionary<string, AutomationGraphValue>
            {
                ["left"] = AutomationGraphValue.Null("number"),
                ["right"] = AutomationGraphValue.FromJson("number", rightDocument.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, samples);

        Assert.False(result.Succeeded);
        Assert.Contains("explicit Null", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(executor.ExecutedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenTrue && trace.Status == AutomationGraphTraceStatus.Skipped);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenFalse && trace.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Typed_condition_does_not_infer_string_semantics_for_an_unregistered_type()
    {
        using var leftDocument = JsonDocument.Parse("\"alphabet\"");
        using var rightDocument = JsonDocument.Parse("\"alpha\"");
        const string customType = "contoso.label";
        var (graph, trigger, _, whenTrue, whenFalse) = TypedConditionGraph("contains", customType);
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger] = new Dictionary<string, AutomationGraphValue>
            {
                ["left"] = AutomationGraphValue.FromJson(customType, leftDocument.RootElement),
                ["right"] = AutomationGraphValue.FromJson(customType, rightDocument.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, samples);

        Assert.False(result.Succeeded);
        Assert.Contains("registered string values", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(executor.ExecutedNodes);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenTrue && trace.Status == AutomationGraphTraceStatus.Skipped);
        Assert.Contains(result.Trace, trace => trace.NodeId == whenFalse && trace.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public void Canonical_comparison_nodes_are_registered_and_project_to_the_shared_condition_runtime()
    {
        var schemas = CanonicalAutomationCoreNodes.CreateRegistry();
        Assert.True(CanonicalAutomationCoreNodes.IsRegistered(schemas));
        var manual = CanonicalAutomationCoreNodes.CreateNode(schemas, CanonicalAutomationCoreNodes.ManualType);
        var comparison = CanonicalAutomationCoreNodes.CreateNode(schemas, "automations.condition.number.greater-than");
        var graph = new GraphDocument(GraphDocument.CurrentSchemaVersion, Guid.NewGuid(), 1,
            CanonicalAutomationCoreNodes.ProfileId, GraphRevisionState.Draft, [manual, comparison], []);

        Assert.Empty(schemas.Validate(graph));
        var projection = new NodeGraphAutomationAdapter(schemas, CanonicalAutomationCoreNodes.Bindings()).Compile(graph);
        var condition = Assert.Single(projection.Definition.Nodes, node => node.Id == comparison.NodeId);

        Assert.Equal("Condition", condition.Category);
        Assert.Equal("greaterThan", condition.Parameters["operator"]);
        Assert.Contains(condition.EffectivePorts, port => port.Id == "left" && port.DataType == "number" &&
            port.Direction == AutomationGraphPortDirection.Input);
        Assert.Contains(condition.EffectivePorts, port => port.Id == "right" && port.DataType == "number" &&
            port.Direction == AutomationGraphPortDirection.Input);
        Assert.Contains(condition.EffectivePorts, port => port.Id == "true" && port.DataType == "flow" &&
            port.Direction == AutomationGraphPortDirection.Output);
    }

    [Fact]
    public async Task Test_mode_is_forwarded_and_preserved_in_trace_result()
    {
        var action = Node("Action"); var executor = new RecordingExecutor("Action"); var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [action], []);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(AutomationGraphRunMode.Test, result.Mode); Assert.Equal(AutomationGraphRunMode.Test, executor.LastMode); Assert.Equal(AutomationGraphTraceStatus.Succeeded, Assert.Single(result.Trace).Status);
    }

    [Fact]
    public async Task Unsupported_run_mode_is_rejected_before_any_executor_side_effect()
    {
        var action = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [action], []);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, (AutomationGraphRunMode)99, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "run.mode.invalid");
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Port_direction_errors_fail_validation_before_executor_runs()
    {
        var first = Node("Action") with { Ports = [new("in", "In", AutomationGraphPortDirection.Input, "flow", true)] }; var second = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new AutomationGraphEdgeDefinition(first.Id, second.Id) { FromPortId = "in" }]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Contains(result.ValidationIssues, issue => issue.Code == "edge.from-port.direction"); Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task Trace_captures_resolved_upstream_inputs_for_each_executed_node()
    {
        var first = Node("Action");
        var second = Node("Action");
        var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new(first.Id, second.Id)]);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        var firstTrace = result.Trace.Single(trace => trace.NodeId == first.Id);
        var secondTrace = result.Trace.Single(trace => trace.NodeId == second.Id);
        Assert.Empty(firstTrace.Inputs ?? []);
        Assert.NotNull(secondTrace.Inputs);
        Assert.Equal(first.Id.ToString(), secondTrace.Inputs[first.Id]);
    }

    [Fact]
    public async Task Selected_typed_trigger_sample_flows_through_declared_output_port()
    {
        var trigger = Node("Trigger") with
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, "string", true)]
        };
        var action = Node("Action") with
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, "string", true)]
        };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        var triggerOutputs = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue>
            {
                ["payload"] = AutomationGraphValue.FromString("selected sample")
            }
        };

        var result = await new AutomationGraphRunner([new RecordingExecutor("Action")]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, triggerOutputs);

        Assert.True(result.Succeeded);
        var typedInput = Assert.Single(Assert.Single(result.Trace, item => item.NodeId == action.Id).TypedInputs!["payload"]);
        Assert.True(typedInput.TryGetString(out var actual));
        Assert.Equal("selected sample", actual);
    }

    [Fact]
    public async Task Mismatched_trigger_sample_type_is_rejected_before_executor_runs()
    {
        var trigger = Node("Trigger") with
        {
            Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, "integer", true)]
        };
        var action = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload" }]);
        var triggerOutputs = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue>
            {
                ["payload"] = AutomationGraphValue.FromString("wrong type")
            }
        };

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Real, CancellationToken.None, triggerOutputs);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "run.input.type.mismatch");
        Assert.Equal(0, executor.ExecuteCount);
    }

    [Theory]
    [InlineData("number", "true")]
    [InlineData("boolean", "1")]
    [InlineData("integer", "1.5")]
    public async Task Trigger_sample_json_shape_must_match_declared_builtin_type(string dataType, string json)
    {
        var trigger = Node("Trigger") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, dataType, true)] };
        var action = Node("Action") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, dataType, true)] };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        using var document = JsonDocument.Parse(json);
        var triggerOutputs = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue>
            {
                ["payload"] = AutomationGraphValue.FromJson(dataType, document.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, triggerOutputs);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "run.input.value.shape");
        Assert.Equal(0, executor.CanExecuteCount);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Empty(result.Trace);
    }

    [Theory]
    [InlineData("number", "1.25", JsonValueKind.Number)]
    [InlineData("boolean", "false", JsonValueKind.False)]
    [InlineData("integer", "42", JsonValueKind.Number)]
    [InlineData("string", "\"sample\"", JsonValueKind.String)]
    [InlineData("contoso.vector", "{\"x\":1}", JsonValueKind.Object)]
    public async Task Trigger_sample_json_shape_accepts_valid_builtin_type_controls(string dataType, string json, JsonValueKind expectedKind)
    {
        var trigger = Node("Trigger") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, dataType, true)] };
        var action = Node("Action") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, dataType, true)] };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        using var document = JsonDocument.Parse(json);
        var triggerOutputs = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue>
            {
                ["payload"] = AutomationGraphValue.FromJson(dataType, document.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, triggerOutputs);

        Assert.True(result.Succeeded);
        Assert.Equal(1, executor.ExecuteCount);
        var value = Assert.Single(Assert.Single(result.Trace, item => item.NodeId == action.Id).TypedInputs!["payload"]);
        Assert.Equal(expectedKind, value.JsonValue!.Value.ValueKind);
    }

    [Theory]
    [InlineData(AutomationGraphValueState.Null)]
    [InlineData(AutomationGraphValueState.Unavailable)]
    [InlineData(AutomationGraphValueState.Redacted)]
    public async Task Trigger_samples_preserve_null_unavailable_and_redacted_as_distinct_states(AutomationGraphValueState state)
    {
        var trigger = Node("Trigger") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, "string", true)] };
        var action = Node("Action") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, "string", true)] };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        var sample = state switch
        {
            AutomationGraphValueState.Null => AutomationGraphValue.Null("string"),
            AutomationGraphValueState.Unavailable => AutomationGraphValue.Unavailable("string"),
            AutomationGraphValueState.Redacted => AutomationGraphValue.Redacted("string"),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        var triggerOutputs = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue> { ["payload"] = sample }
        };

        var result = await new AutomationGraphRunner([new RecordingExecutor("Action")]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, triggerOutputs);

        Assert.True(result.Succeeded);
        var input = Assert.Single(Assert.Single(result.Trace, item => item.NodeId == action.Id).TypedInputs!["payload"]);
        Assert.Equal(state, input.State);
    }

    [Fact]
    public async Task Trigger_error_sample_fails_the_dependent_node_without_collapsing_to_unavailable()
    {
        var trigger = Node("Trigger") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, "string", true)] };
        var action = Node("Action") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, "string", true)] };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        var triggerOutputs = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue>
            {
                ["payload"] = AutomationGraphValue.Failure("string", "sample.failed", "Sample production failed.")
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, triggerOutputs);

        Assert.False(result.Succeeded);
        Assert.Equal(0, executor.ExecuteCount);
        var failed = Assert.Single(result.Trace, item => item.NodeId == action.Id);
        Assert.Equal(AutomationGraphTraceStatus.Failed, failed.Status);
        Assert.Equal(AutomationGraphValueState.Error, Assert.Single(failed.TypedInputs!["payload"]).State);
    }

    [Fact]
    public async Task Redacted_trigger_sample_cannot_carry_hidden_json_payload()
    {
        var trigger = Node("Trigger") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Output, "string", true)] };
        var action = Node("Action") with { Ports = [new("payload", "Payload", AutomationGraphPortDirection.Input, "string", true)] };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        using var document = JsonDocument.Parse("\"sensitive value\"");
        var triggerOutputs = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue>
            {
                ["payload"] = new AutomationGraphValue("string", AutomationGraphValueState.Redacted, document.RootElement.Clone())
            }
        };
        var executor = new RecordingExecutor("Action");

        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, triggerOutputs);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "run.input.value.shape");
        Assert.Equal(0, executor.CanExecuteCount);
        Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task Runtime_executes_150_node_chain_with_complete_ordered_trace()
    {
        const int count = 150;
        var nodes = Enumerable.Range(0, count).Select(_ => Node("StressAction")).ToArray();
        var edges = Enumerable.Range(0, count - 1).Select(index => new AutomationGraphEdgeDefinition(nodes[index].Id, nodes[index + 1].Id)).ToArray();
        var executor = new RecordingExecutor("StressAction");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, nodes, edges);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(count, executor.ExecuteCount);
        Assert.Equal(count, result.Trace.Count);
        Assert.Equal(nodes.Select(node => node.Id), result.Trace.Select(trace => trace.NodeId));
        Assert.All(result.Trace, trace => Assert.Equal(AutomationGraphTraceStatus.Succeeded, trace.Status));
    }

    private static (AutomationGraphDefinition Graph, Guid Trigger, Guid Condition, Guid TrueAction, Guid FalseAction)
        TypedConditionGraph(string operation, string dataType = "number")
    {
        var trigger = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Trigger", null, null,
            new Dictionary<string, string>())
        {
            Ports =
            [
                new("out", "Out", AutomationGraphPortDirection.Output, "flow", true),
                new("left", "Left", AutomationGraphPortDirection.Output, dataType, true),
                new("right", "Right", AutomationGraphPortDirection.Output, dataType, true)
            ]
        };
        var condition = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Condition", null, null,
            new Dictionary<string, string> { ["operator"] = operation })
        {
            Ports =
            [
                new("in", "In", AutomationGraphPortDirection.Input, "flow"),
                new("left", "Left", AutomationGraphPortDirection.Input, dataType),
                new("right", "Right", AutomationGraphPortDirection.Input, dataType),
                new("true", "True", AutomationGraphPortDirection.Output, "flow", true),
                new("false", "False", AutomationGraphPortDirection.Output, "flow", true)
            ]
        };
        var whenTrue = Node("Action"); var whenFalse = Node("Action");
        return (new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion,
            [trigger, condition, whenTrue, whenFalse],
            [
                new AutomationGraphEdgeDefinition(trigger.Id, condition.Id),
                new AutomationGraphEdgeDefinition(trigger.Id, condition.Id) { FromPortId = "left", ToPortId = "left" },
                new AutomationGraphEdgeDefinition(trigger.Id, condition.Id) { FromPortId = "right", ToPortId = "right" },
                new AutomationGraphEdgeDefinition(condition.Id, whenTrue.Id) { FromPortId = "true" },
                new AutomationGraphEdgeDefinition(condition.Id, whenFalse.Id) { FromPortId = "false" }
            ]), trigger.Id, condition.Id, whenTrue.Id, whenFalse.Id);
    }

    private static async Task<(AutomationGraphRunResult Result, Guid WhenTrue, Guid WhenFalse, Guid[] ExecutedNodes)>
        RunTypedNumberConditionAsync(string operation, string leftJson, string rightJson)
    {
        using var leftDocument = JsonDocument.Parse(leftJson);
        using var rightDocument = JsonDocument.Parse(rightJson);
        var (graph, trigger, _, whenTrue, whenFalse) = TypedConditionGraph(operation);
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger] = new Dictionary<string, AutomationGraphValue>
            {
                ["left"] = AutomationGraphValue.FromJson("number", leftDocument.RootElement),
                ["right"] = AutomationGraphValue.FromJson("number", rightDocument.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");
        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, samples);
        return (result, whenTrue, whenFalse, executor.ExecutedNodes.ToArray());
    }

    private static async Task<(AutomationGraphRunResult Result, Guid TrueAction, Guid FalseAction, Guid[] ExecutedNodes)>
        RunTypedNumberConditionAsync(string operation, string leftJson, string rightJson)
    {
        using var leftDocument = JsonDocument.Parse(leftJson);
        using var rightDocument = JsonDocument.Parse(rightJson);
        var (graph, trigger, _, whenTrue, whenFalse) = TypedConditionGraph(operation);
        var samples = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger] = new Dictionary<string, AutomationGraphValue>
            {
                ["left"] = AutomationGraphValue.FromJson("number", leftDocument.RootElement),
                ["right"] = AutomationGraphValue.FromJson("number", rightDocument.RootElement)
            }
        };
        var executor = new RecordingExecutor("Action");
        var result = await new AutomationGraphRunner([executor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, samples);
        return (result, whenTrue, whenFalse, executor.ExecutedNodes.ToArray());
    }

    private static AutomationGraphNodeDefinition Node(string category) => new(Guid.NewGuid(), category, null, null, new Dictionary<string, string>());
    private sealed class RecordingExecutor(string category) : IAutomationGraphNodeExecutor
    {
        public int CanExecuteCount { get; private set; } public int ExecuteCount { get; private set; } public List<Guid> ExecutedNodes { get; } = []; public AutomationGraphRunMode? LastMode { get; private set; }
        public bool CanExecute(AutomationGraphNodeDefinition node) { CanExecuteCount++; return string.Equals(node.Category, category, StringComparison.OrdinalIgnoreCase); }
        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); ExecuteCount++; ExecutedNodes.Add(context.Node.Id); LastMode = context.Mode; return Task.FromResult(new AutomationGraphNodeExecutionResult(true, "Executed.", context.Node.Id.ToString())); }
    }
}
