using Haven.Application.Automations;
using Haven.Core;
using System.Text.Json;
using Xunit;

namespace Haven.Application.Tests;

public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Fact]
    public async Task Space_user_message_graph_uses_shared_registration_and_preserves_unicode_input_identity()
    {
        var id = Guid.NewGuid();
        var layout = UserMessageLayout(2);
        var graph = SpaceUserMessageGraph.Compile(id, 7, layout);
        var input = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User,
            "A👩‍💻e\u0301Z", null, null, null, DateTimeOffset.UtcNow);

        var result = await graph.ExecuteAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal("A👩‍💻", result.DerivedText);
        Assert.Equal("A👩‍💻e\u0301Z", input.Content);
        Assert.Equal(input.Id, result.UserMessageId);
        Assert.Equal(id, result.SpaceId);
        Assert.Equal(7, result.SpaceRevision);
        Assert.Equal(layout.GraphId, result.GraphId);
        Assert.Equal(2, result.OmittedTextElements);
        Assert.Equal(3, result.Trace.Count);
        Assert.All(result.Trace, item => Assert.Equal("Succeeded", item.State));
        Assert.NotEqual(result.InputSha256, result.OutputSha256);
        Assert.Equal(14, CanonicalAutomationCoreNodes.CreateRegistry().ListNodeTypes(SpaceUserMessageGraph.ProfileId).Count);
        Assert.Equal(2, CanonicalAutomationCoreNodes.CreateRegistry().ListNodeTypes(CanonicalAutomationCoreNodes.ProfileId).Count);
    }

    [Fact]
    public async Task Space_graph_compilation_snapshots_editable_configuration_and_repeated_execution_keeps_original_input()
    {
        var layout = UserMessageLayout(3);
        var graph = SpaceUserMessageGraph.Compile(Guid.NewGuid(), 2, layout);
        ((Dictionary<string, string>)layout.Nodes[1].Metadata)["maximumTextElements"] = "1";
        var first = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User,
            "abcdef", null, null, null, DateTimeOffset.UtcNow);
        var second = first with { Id = Guid.NewGuid(), Content = "XYZ123" };

        var a = await graph.ExecuteAsync(first, TestContext.Current.CancellationToken);
        var b = await graph.ExecuteAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal("abc", a.DerivedText);
        Assert.Equal("XYZ", b.DerivedText);
        Assert.Equal(first.Id, a.UserMessageId);
        Assert.Equal(second.Id, b.UserMessageId);
        Assert.NotEqual(a.InputSha256, b.InputSha256);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("port-type")]
    [InlineData("port-direction")]
    [InlineData("port-count")]
    [InlineData("branch")]
    [InlineData("orphan")]
    [InlineData("configuration")]
    [InlineData("cycle")]
    public void Space_graph_rejects_unimplemented_nodes_and_invalid_typed_flow_before_execution(string defect)
    {
        var layout = UserMessageLayout(3);
        var nodes = layout.Nodes.ToArray();
        var edges = layout.Edges.ToArray();
        if (defect == "unsupported") nodes[1] = nodes[1] with { TypeId = "spaces.model.imaginary" };
        if (defect == "port-type") nodes[1] = nodes[1] with { Ports = nodes[1].Ports.Select(port => port with { DataType = "flow" }).ToArray() };
        if (defect == "port-direction") nodes[0] = nodes[0] with { Ports = [nodes[0].Ports[0] with { Direction = (SpaceLayoutPortDirection)99 }] };
        if (defect == "port-count") nodes[1] = nodes[1] with { Ports = [.. nodes[1].Ports, new("extra", "Extra", SpaceLayoutPortDirection.Output, "string", false)] };
        if (defect == "branch") edges[0] = edges[0] with { Branch = "true" };
        if (defect == "orphan") nodes = [.. nodes, nodes[1] with { Id = Guid.NewGuid() }];
        if (defect == "configuration") nodes[1] = nodes[1] with { Metadata = new Dictionary<string, string> { ["maximumTextElements"] = "0" } };
        if (defect == "cycle") edges = [edges[0], edges[1] with { ToNodeId = nodes[0].Id, ToPortId = "out" }];

        var error = Assert.Throws<SpaceRequestPolicyBlockedException>(() =>
            SpaceUserMessageGraph.Compile(Guid.NewGuid(), 1, layout with { Nodes = nodes, Edges = edges }));

        Assert.Equal("SPACE_USER_MESSAGE_GRAPH_INVALID", error.Code);
    }

    [Fact]
    public async Task Space_graph_actual_cancellation_is_preserved_and_not_reported_as_derived_output()
    {
        var graph = SpaceUserMessageGraph.Compile(Guid.NewGuid(), 1, UserMessageLayout(2));
        var input = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User,
            "abc", null, null, null, DateTimeOffset.UtcNow);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => graph.ExecuteAsync(input, cancelled.Token));
        Assert.Equal(cancelled.Token, error.CancellationToken);
    }

    [Fact]
    public async Task Space_graph_real_chat_request_uses_derived_text_while_saved_original_and_trace_keep_identity()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Message derivation", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), UserMessageLayout(3));
        var rig = new GraphRequestRig(source, space.Id);

        await rig.SendAsync("Hello");

        var wire = Assert.Single(rig.Transport.Requests);
        Assert.Equal("Hel", Assert.Single(wire.Messages, message => message.Role == "user").Content);
        var persisted = Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User);
        Assert.Equal("Hello", persisted.Content);
        var derivation = Assert.Single(rig.Events.Values, item => item.ComponentId == "spaces.user-message-graph");
        Assert.Equal(persisted.Id.ToString("D"), derivation.SafeMetadata!["userMessageId"]);
        Assert.Equal(space.Id.ToString("D"), derivation.SafeMetadata["spaceId"]);
        Assert.NotEqual(derivation.SafeMetadata["inputSha256"], derivation.SafeMetadata["outputSha256"]);
        Assert.DoesNotContain("Hello", derivation.SafeMetadata["trace"], StringComparison.Ordinal);
        Assert.True(source.Inner.Validations > 0);
        files.Passed = true;
    }

    [Fact]
    public async Task Space_graph_revision_change_at_actual_model_dispatch_blocks_transport_without_rewriting_input()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Message graph revision", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), UserMessageLayout(3));
        var rig = new GraphRequestRig(source, space.Id);
        rig.Transport.BeforePhysicalDispatch = async () =>
        {
            await files.Registry.UpdateAsync(space with { Instructions = "Changed after derivation" }, space.Revision,
                TestContext.Current.CancellationToken);
        };

        var error = await Record.ExceptionAsync(() => rig.SendAsync("Hello"));

        Assert.NotNull(error);
        Assert.True(ContainsSpaceCode(error, "SPACE_POLICY_CHANGED"));
        Assert.Empty(rig.Transport.Requests);
        Assert.Equal("Hello", Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User).Content);
        files.Passed = true;
    }

    [Theory]
    [InlineData("invariant", "x=-1.5; y=6.02e2; list: 1 2", "-1.5|602|1|2")]
    [InlineData("en-US", "Balance -1,234.50; rate 6.02e2", "-1234.5|602")]
    [InlineData("de-DE", "Kosten 1.234,50; Faktor -2,5", "1234.5|-2.5")]
    [InlineData("fr-FR", "Prix 1\u202F234,50 et 2,5", "1234.5|2.5")]
    public async Task Space_graph_extracts_typed_numbers_with_explicit_locale_and_original_spans(string locale, string text, string expected)
    {
        var layout = StructuredMessageLayout(SpaceUserMessageGraph.ExtractNumbersType, new() { ["locale"] = locale });
        var result = await RunGraph(layout, text);
        var extraction = JsonSerializer.Deserialize<SpaceNumberExtraction>(result.DerivedText, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(locale, extraction.Locale);
        Assert.Equal(expected.Split('|'), extraction.Numbers.Select(number => number.InvariantValue));
        Assert.All(extraction.Numbers, number => Assert.Equal(number.RawText, text.Substring(number.StartUtf16, number.LengthUtf16)));
        Assert.Equal(SpaceUserMessageGraph.NumbersDataType,
            Assert.Single(result.TypedNodeOutputs.Values, value => value.DataType == SpaceUserMessageGraph.NumbersDataType).DataType);
        Assert.Contains(result.Trace, item => item.OutputDataType == SpaceUserMessageGraph.NumbersDataType);
        Assert.False(extraction.MoreResultsOmitted);
        if (locale == "fr-FR") Assert.True(extraction.HasUncertainty); // Space grouping can also separate tokens.
    }

    [Fact]
    public async Task Space_graph_numeric_ambiguity_and_precision_loss_are_exposed_without_guessing_or_rounding()
    {
        const string text = "1,234; 99999999999999999999999999999; 1e-30";
        var result = await RunGraph(StructuredMessageLayout(SpaceUserMessageGraph.ExtractNumbersType, new() { ["locale"] = "en-US" }), text);
        var extraction = JsonSerializer.Deserialize<SpaceNumberExtraction>(result.DerivedText, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(3, extraction.Numbers.Count);
        Assert.Equal("1234", extraction.Numbers[0].InvariantValue);
        Assert.NotNull(extraction.Numbers[0].Uncertainty);
        Assert.Null(extraction.Numbers[1].InvariantValue);
        Assert.Null(extraction.Numbers[2].InvariantValue);
        Assert.All(extraction.Numbers, number => Assert.NotNull(number.Uncertainty));
        Assert.True(extraction.HasUncertainty);
        Assert.Contains(result.Trace, item => item.HasUncertainty);
        Assert.DoesNotContain("99999999999999999999999999999", JsonSerializer.Serialize(result.Trace), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ordinal", "whole-word", "cat", "cat scatter cat_ cat\u0301 cat. CAT", 2)]
    [InlineData("ordinal-ignore-case", "whole-word", "cat", "cat scatter cat_ cat\u0301 cat. CAT", 3)]
    [InlineData("ordinal", "substring", "cat", "cat scatter cat_ cat\u0301 cat. CAT", 5)]
    [InlineData("ordinal-ignore-case", "substring", "cat fish", "cat fish; cat  fish; CAT fish", 2)]
    [InlineData("ordinal", "whole-word", "cat", "𐐀cat cat𐐀 cat", 1)]
    [InlineData("ordinal-ignore-case", "whole-word", "café", "cafe cafe\u0301 café CAFÉ", 2)]
    public async Task Space_graph_phrase_matching_uses_documented_literal_unicode_semantics(string comparison, string boundary, string query, string text, int expected)
    {
        var result = await RunGraph(StructuredMessageLayout(SpaceUserMessageGraph.SearchWordsType,
            new() { ["query"] = query, ["locale"] = "invariant", ["comparison"] = comparison, ["boundary"] = boundary }), text);
        var matches = JsonSerializer.Deserialize<SpacePhraseMatches>(result.DerivedText, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(expected, matches.Matches.Count);
        Assert.Equal(comparison, matches.Comparison);
        Assert.Equal(boundary, matches.Boundary);
        Assert.All(matches.Matches, match => Assert.Equal(match.Text, text.Substring(match.StartUtf16, match.LengthUtf16)));
        Assert.False(matches.MoreResultsOmitted);
        Assert.Contains(result.Trace, item => item.OutputDataType == SpaceUserMessageGraph.MatchesDataType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Space_graph_structured_result_bounds_report_omitted_matches(bool numbers)
    {
        var layout = numbers ? StructuredMessageLayout(SpaceUserMessageGraph.ExtractNumbersType, new() { ["locale"] = "invariant" }) :
            StructuredMessageLayout(SpaceUserMessageGraph.SearchWordsType,
                new() { ["query"] = "cat", ["locale"] = "invariant", ["comparison"] = "ordinal", ["boundary"] = "whole-word" });
        var text = string.Join("; ", Enumerable.Repeat(numbers ? "7" : "cat", 513));
        var result = await RunGraph(layout, text);
        using var rendered = JsonDocument.Parse(result.DerivedText);

        Assert.True(rendered.RootElement.GetProperty("moreResultsOmitted").GetBoolean());
        Assert.Equal(512, rendered.RootElement.GetProperty(numbers ? "numbers" : "matches").GetArrayLength());
        Assert.Contains(result.Trace, item => item.MoreResultsOmitted);
        Assert.Equal(0, result.OmittedTextElements); // This counts only actual truncation, not a guessed JSON-length difference.
    }

    [Fact]
    public void Space_graph_typed_number_output_cannot_connect_directly_to_string_request_input()
    {
        var layout = StructuredMessageLayout(SpaceUserMessageGraph.ExtractNumbersType, new() { ["locale"] = "invariant" });
        var withoutRenderer = layout with
        {
            Nodes = [layout.Nodes[0], layout.Nodes[1], layout.Nodes[3]],
            Edges = [layout.Edges[0], new(Guid.NewGuid(), layout.Nodes[1].Id, "out", layout.Nodes[3].Id, "in")]
        };
        Assert.Throws<SpaceRequestPolicyBlockedException>(() => SpaceUserMessageGraph.Compile(Guid.NewGuid(), 1, withoutRenderer));
    }

    [Fact]
    public async Task Space_graph_real_request_renders_typed_extraction_and_preserves_saved_text_and_private_trace()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Typed numeric request", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry),
            StructuredMessageLayout(SpaceUserMessageGraph.ExtractNumbersType, new() { ["locale"] = "invariant" }));
        var rig = new GraphRequestRig(source, space.Id);

        await rig.SendAsync("secret amount 12 and 3.5");

        var user = Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User);
        Assert.Equal("secret amount 12 and 3.5", user.Content);
        var wire = Assert.Single(rig.Transport.Requests);
        var extraction = JsonSerializer.Deserialize<SpaceNumberExtraction>(Assert.Single(wire.Messages, message => message.Role == "user").Content,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(new[] { "12", "3.5" }, extraction.Numbers.Select(number => number.InvariantValue));
        var trace = Assert.Single(rig.Events.Values, item => item.ComponentId == "spaces.user-message-graph");
        Assert.DoesNotContain("secret amount", JsonSerializer.Serialize(trace.SafeMetadata), StringComparison.Ordinal);
        Assert.Equal(user.Id.ToString("D"), trace.SafeMetadata!["userMessageId"]);
        files.Passed = true;
    }

    [Theory]
    [InlineData("/steps/1/prompt", "compile the existing project")]
    [InlineData("/a~1b/~0key", "escaped names")]
    [InlineData("/", "empty name")]
    [InlineData("/~01", "literal tilde-one")]
    [InlineData("/01", "object field, not array index")]
    public async Task Space_graph_json_selects_exact_ordinal_pointer_values_without_coercion_or_original_message_changes(string pointer, string expected)
    {
        const string text = """{"steps":[{"prompt":"first"},{"prompt":"compile the existing project"}],"a/b":{"~key":"escaped names"},"":"empty name","~1":"literal tilde-one","01":"object field, not array index"}""";
        var message = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User, text, null, null, null, DateTimeOffset.UtcNow);
        var graph = SpaceUserMessageGraph.Compile(Guid.NewGuid(), 3, JsonMessageLayout(pointer, SpaceUserMessageGraph.JsonStringType));
        var result = await graph.ExecuteAsync(message, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.DerivedText);
        Assert.Equal(text, message.Content); Assert.Equal(message.Id, result.UserMessageId);
        var selected = result.TypedNodeOutputs.Single(pair => result.Trace.Single(item => item.NodeId == pair.Key).TypeId == SpaceUserMessageGraph.SelectJsonType).Value;
        Assert.Equal(SpaceUserMessageGraph.JsonDataType, selected.DataType);
        Assert.Equal(expected, selected.JsonValue!.Value.GetProperty("value").GetString());
        Assert.All(result.Trace, item => Assert.Equal("Succeeded", item.State));
    }

    [Theory]
    [InlineData("null", "", "null")]
    [InlineData("{\"value\":null}", "/value", "null")]
    [InlineData("{\"n\":1234567890123456789012345678901234567890}", "/n", "1234567890123456789012345678901234567890")]
    [InlineData("{\"flag\":true}", "/flag", "true")]
    public async Task Space_graph_json_render_preserves_present_null_boolean_and_arbitrary_json_number_text(string text, string pointer, string expected)
    {
        var result = await RunGraph(JsonMessageLayout(pointer, SpaceUserMessageGraph.RenderJsonType), text);
        Assert.Equal(expected, result.DerivedText);
        Assert.Equal(2, result.TypedNodeOutputs.Count(pair => pair.Value.DataType == SpaceUserMessageGraph.JsonDataType));
    }

    [Fact]
    public async Task Space_graph_json_user_fields_cannot_fabricate_graph_uncertainty_or_omission_flags()
    {
        var result = await RunGraph(JsonMessageLayout("", SpaceUserMessageGraph.RenderJsonType),
            """{"hasUncertainty":true,"moreResultsOmitted":true,"nested":{"a":1}}""");
        Assert.All(result.Trace, item => { Assert.False(item.HasUncertainty); Assert.False(item.MoreResultsOmitted); });
        using var value = JsonDocument.Parse(result.DerivedText);
        Assert.True(value.RootElement.GetProperty("hasUncertainty").GetBoolean());
        Assert.True(value.RootElement.GetProperty("moreResultsOmitted").GetBoolean());
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("""{"a":1,"\u0061":2}""")]
    [InlineData("{\"a\":/* comment */1}")]
    [InlineData("{\"a\":1,}")]
    [InlineData("{}{}")]
    [InlineData("\"\\uD800\"")]
    public async Task Space_graph_json_rejects_ambiguous_or_non_strict_input_instead_of_silently_changing_it(string text)
    {
        var error = await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => RunGraph(JsonMessageLayout("", SpaceUserMessageGraph.RenderJsonType), text));
        Assert.Equal("SPACE_USER_MESSAGE_GRAPH_INVALID", error.Code);
        Assert.Contains("Parse JSON", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(text, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/missing")]
    [InlineData("/items/01")]
    [InlineData("/items/-")]
    [InlineData("/items/9")]
    [InlineData("/items/name")]
    public async Task Space_graph_json_missing_fields_or_invalid_array_indices_are_failures_not_empty_strings(string pointer)
    {
        await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => RunGraph(
            JsonMessageLayout(pointer, SpaceUserMessageGraph.RenderJsonType), "{\"items\":[\"only\"]}"));
    }

    [Fact]
    public async Task Space_graph_json_rejects_unsupported_depth_and_requires_explicit_string_conversion()
    {
        await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => RunGraph(
            JsonMessageLayout("", SpaceUserMessageGraph.RenderJsonType), new string('[', 33) + "0" + new string(']', 33)));
        await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => RunGraph(
            JsonMessageLayout("", SpaceUserMessageGraph.JsonStringType), "123"));
        await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => RunGraph(
            JsonMessageLayout("", SpaceUserMessageGraph.JsonStringType), "null"));
        foreach (var pointer in new[] { "#/field", "field", "/trailing~", "/bad~2", "/" + new string('x', 512) })
            Assert.Throws<SpaceRequestPolicyBlockedException>(() => SpaceUserMessageGraph.Compile(Guid.NewGuid(), 1,
                JsonMessageLayout(pointer, SpaceUserMessageGraph.RenderJsonType)));
    }

    [Fact]
    public async Task Space_graph_json_real_request_selects_derived_field_and_keeps_original_saved_message_and_private_trace()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Structured request", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), JsonMessageLayout("/request/text", SpaceUserMessageGraph.JsonStringType));
        var rig = new GraphRequestRig(source, space.Id);
        const string text = """{"privateNote":"keep in original history","request":{"text":"compile the existing project"}}""";
        await rig.SendAsync(text);
        var saved = Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User);
        Assert.Equal(text, saved.Content);
        Assert.Equal("compile the existing project", Assert.Single(Assert.Single(rig.Transport.Requests).Messages, message => message.Role == "user").Content);
        var trace = Assert.Single(rig.Events.Values, item => item.ComponentId == "spaces.user-message-graph");
        Assert.Equal(saved.Id.ToString("D"), trace.SafeMetadata!["userMessageId"]);
        Assert.DoesNotContain("privateNote", JsonSerializer.Serialize(trace.SafeMetadata), StringComparison.Ordinal);
        Assert.DoesNotContain("compile the existing project", JsonSerializer.Serialize(trace.SafeMetadata), StringComparison.Ordinal);
        files.Passed = true;
    }

    [Fact]
    public async Task Space_graph_json_invalid_input_blocks_actual_transport_after_preserving_the_original_message()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Strict structured request", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), JsonMessageLayout("/request", SpaceUserMessageGraph.JsonStringType));
        var rig = new GraphRequestRig(source, space.Id);
        const string text = """{"request":"first","request":"ambiguous"}""";
        var error = await Record.ExceptionAsync(() => rig.SendAsync(text));
        Assert.NotNull(error); Assert.True(ContainsSpaceCode(error, "SPACE_USER_MESSAGE_GRAPH_INVALID"));
        Assert.Empty(rig.Transport.Requests);
        Assert.Equal(text, Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User).Content);
        files.Passed = true;
    }

    [Theory]
    [InlineData("{\"mode\":\"structured\",\"prompt\":\"compile the project\"}", "true", "compile the project")]
    [InlineData("ordinary non-JSON message", "false", "ordinary")]
    public async Task Space_graph_branch_dispatch_skips_inactive_failing_nodes_and_joins_only_the_selected_value(string text, string branch, string expected)
    {
        var layout = ConditionalMessageLayout();
        var message = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User, text, null, null, null, DateTimeOffset.UtcNow);
        var graph = SpaceUserMessageGraph.Compile(Guid.NewGuid(), 4, layout);
        var result = await graph.ExecuteAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.DerivedText);
        Assert.Equal(text, message.Content); Assert.Equal(message.Id, result.UserMessageId);
        var route = Assert.Single(result.Trace, item => item.TypeId == SpaceUserMessageGraph.RouteWordsType);
        Assert.Equal(branch, route.SelectedBranch);
        var routed = Assert.Single(result.TypedNodePortOutputs[route.NodeId]);
        Assert.Equal(branch, routed.Key); Assert.True(routed.Value.TryGetString(out var routedText)); Assert.Equal(text, routedText);
        Assert.DoesNotContain(route.NodeId, result.TypedNodeOutputs.Keys); // No invented legacy "out" on a conditional node.
        var merge = Assert.Single(result.Trace, item => item.TypeId == SpaceUserMessageGraph.MergeType);
        Assert.Equal("Succeeded", merge.State);
        if (branch == "false")
        {
            Assert.All(result.Trace.Where(item => item.TypeId is SpaceUserMessageGraph.ParseJsonType or SpaceUserMessageGraph.SelectJsonType or SpaceUserMessageGraph.JsonStringType), item =>
            { Assert.Equal("Skipped", item.State); Assert.Equal(0, item.InputTextElements); Assert.Equal(0, item.OutputTextElements); Assert.DoesNotContain(item.NodeId, result.TypedNodePortOutputs.Keys); });
            Assert.Equal(text.Length - expected.Length, result.OmittedTextElements);
        }
        else
        {
            Assert.Equal("Skipped", Assert.Single(result.Trace, item => item.TypeId == SpaceUserMessageGraph.TruncateType).State);
            Assert.Equal(0, result.OmittedTextElements); // Inactive truncation never claims omissions.
        }
        Assert.DoesNotContain(text, JsonSerializer.Serialize(result.Trace), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ordinal", "whole-word", "cat", "scatter", "false")]
    [InlineData("ordinal-ignore-case", "whole-word", "cat", "CAT.", "true")]
    [InlineData("ordinal", "whole-word", "cat", "cat\u0301", "false")]
    [InlineData("ordinal", "whole-word", "cat", "𐐀cat", "false")]
    [InlineData("ordinal", "substring", "cat", "scatter", "true")]
    [InlineData("ordinal-ignore-case", "substring", "cat fish", "cat  fish", "false")]
    public async Task Space_graph_branch_matching_keeps_explicit_literal_unicode_semantics(string comparison, string boundary, string query, string text, string branch)
    {
        var layout = DirectConditionalMessageLayout(comparison, boundary, query);
        var result = await RunGraph(layout, text);
        Assert.Equal(text, result.DerivedText);
        Assert.Equal(branch, Assert.Single(result.Trace, item => item.TypeId == SpaceUserMessageGraph.RouteWordsType).SelectedBranch);
        Assert.Equal(branch, Assert.Single(result.TypedNodePortOutputs[layout.Nodes[1].Id]).Key);
    }

    [Theory]
    [InlineData("missing-branch")]
    [InlineData("duplicate-branch")]
    [InlineData("missing-merge")]
    [InlineData("multiple-active-inputs")]
    [InlineData("empty-merge")]
    [InlineData("editable-branch")]
    public void Space_graph_branch_admission_rejects_incomplete_or_ambiguous_flow(string defect)
    {
        var layout = DirectConditionalMessageLayout("ordinal", "whole-word", "cat");
        if (defect == "missing-branch") layout = layout with { Edges = layout.Edges.Where((_, index) => index != 2).ToArray() };
        if (defect == "duplicate-branch") layout = layout with { Edges = layout.Edges.Select((edge, index) => index == 2 ? edge with { FromPortId = "true" } : edge).ToArray() };
        if (defect == "missing-merge") layout = layout with { Nodes = layout.Nodes.Where(node => node.TypeId != SpaceUserMessageGraph.MergeType).ToArray() };
        if (defect == "editable-branch") layout = layout with { Edges = layout.Edges.Select((edge, index) => index == 1 ? edge with { Branch = "true" } : edge).ToArray() };
        if (defect is "multiple-active-inputs" or "empty-merge")
        {
            // A merge cannot become an unconditional two-value join or an independent
            // source just because the generic port permits multiple connections.
            var source = layout.Nodes[0] with { Ports = [layout.Nodes[0].Ports[0] with { AllowsMultipleConnections = true }] };
            layout = layout with { Nodes = [source, layout.Nodes[2], layout.Nodes[3]], Edges = defect == "empty-merge"
                ? [new(Guid.NewGuid(), layout.Nodes[2].Id, "out", layout.Nodes[3].Id, "in")]
                : [new(Guid.NewGuid(), source.Id, "out", layout.Nodes[2].Id, "in"), new(Guid.NewGuid(), source.Id, "out", layout.Nodes[2].Id, "in"),
                    new(Guid.NewGuid(), layout.Nodes[2].Id, "out", layout.Nodes[3].Id, "in")] };
        }
        Assert.Throws<SpaceRequestPolicyBlockedException>(() => SpaceUserMessageGraph.Compile(Guid.NewGuid(), 1, layout));
    }

    [Theory]
    [InlineData("{\"mode\":\"structured\",\"prompt\":\"compile the project\"}", "compile the project", "true")]
    [InlineData("ordinary non-JSON message", "ordinary", "false")]
    public async Task Space_graph_branch_real_request_sends_only_selected_derived_text_and_keeps_saved_original(string text, string expected, string branch)
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Conditional request", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), ConditionalMessageLayout());
        var rig = new GraphRequestRig(source, space.Id);
        await rig.SendAsync(text);
        Assert.Equal(text, Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User).Content);
        Assert.Equal(expected, Assert.Single(Assert.Single(rig.Transport.Requests).Messages, message => message.Role == "user").Content);
        var trace = Assert.Single(rig.Events.Values, item => item.ComponentId == "spaces.user-message-graph").SafeMetadata!["trace"];
        using var document = JsonDocument.Parse(trace);
        Assert.Equal(branch, Assert.Single(document.RootElement.EnumerateArray(), item => item.GetProperty("TypeId").GetString() == SpaceUserMessageGraph.RouteWordsType)
            .GetProperty("SelectedBranch").GetString());
        Assert.DoesNotContain(text, trace, StringComparison.Ordinal);
        files.Passed = true;
    }

    [Fact]
    public async Task Space_graph_branch_selected_failure_blocks_actual_transport_without_falling_back_to_the_other_branch()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Conditional failure", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), ConditionalMessageLayout());
        var rig = new GraphRequestRig(source, space.Id);
        const string text = "structured but not JSON";
        var error = await Record.ExceptionAsync(() => rig.SendAsync(text));
        Assert.NotNull(error); Assert.True(ContainsSpaceCode(error, "SPACE_USER_MESSAGE_GRAPH_INVALID"));
        Assert.Empty(rig.Transport.Requests);
        Assert.Equal(text, Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User).Content);
        files.Passed = true;
    }

    [Theory]
    [InlineData(true, "compile the project")]
    [InlineData(false, "explain the project")]
    public async Task Space_graph_json_boolean_branch_uses_actual_value_and_preserves_the_derived_object(bool chooseRequest, string expected)
    {
        var text = JsonSerializer.Serialize(new { useRequest = chooseRequest, request = "compile the project", fallback = "explain the project" });
        var result = await RunGraph(ConditionalJsonMessageLayout(), text);
        Assert.Equal(expected, result.DerivedText);
        var route = Assert.Single(result.Trace, item => item.TypeId == SpaceUserMessageGraph.RouteJsonBooleanType);
        Assert.Equal(chooseRequest ? "true" : "false", route.SelectedBranch);
        var value = Assert.Single(result.TypedNodePortOutputs[route.NodeId]);
        Assert.Equal(route.SelectedBranch, value.Key);
        Assert.Equal(SpaceUserMessageGraph.JsonDataType, value.Value.DataType);
        var routed = value.Value.JsonValue!.Value.GetProperty("value");
        Assert.Equal(chooseRequest, routed.GetProperty("useRequest").GetBoolean());
        Assert.Equal("compile the project", routed.GetProperty("request").GetString());
        Assert.Single(result.Trace, item => item.TypeId == SpaceUserMessageGraph.SelectJsonType && item.State == "Skipped");
        Assert.Single(result.Trace, item => item.TypeId == SpaceUserMessageGraph.JsonStringType && item.State == "Skipped");
        Assert.Equal(0, result.OmittedTextElements);
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("\"false\"")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("missing")]
    public async Task Space_graph_json_boolean_branch_refuses_coercion_and_absent_values(string condition)
    {
        var text = condition == "missing" ? "{\"request\":\"compile\",\"fallback\":\"explain\"}"
            : "{\"useRequest\":" + condition + ",\"request\":\"compile\",\"fallback\":\"explain\"}";
        var error = await Assert.ThrowsAsync<SpaceRequestPolicyBlockedException>(() => RunGraph(ConditionalJsonMessageLayout(), text));
        Assert.Contains("Route by JSON boolean", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(text, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "compile the project")]
    [InlineData(false, "explain the project")]
    public async Task Space_graph_json_boolean_branch_real_request_keeps_original_history_and_uses_only_the_selected_field(bool chooseRequest, string expected)
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Structured boolean request", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), ConditionalJsonMessageLayout());
        var rig = new GraphRequestRig(source, space.Id);
        var text = JsonSerializer.Serialize(new { useRequest = chooseRequest, request = "compile the project", fallback = "explain the project" });
        await rig.SendAsync(text);
        Assert.Equal(text, Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User).Content);
        Assert.Equal(expected, Assert.Single(Assert.Single(rig.Transport.Requests).Messages, message => message.Role == "user").Content);
        var trace = Assert.Single(rig.Events.Values, item => item.ComponentId == "spaces.user-message-graph").SafeMetadata!["trace"];
        Assert.DoesNotContain(expected, trace, StringComparison.Ordinal);
        files.Passed = true;
    }

    [Fact]
    public async Task Space_graph_json_boolean_branch_does_not_fall_back_when_selected_field_is_missing()
    {
        using var files = new SpacePolicyFiles();
        var space = await files.Registry.CreateAsync("Strict boolean request", cancellationToken: TestContext.Current.CancellationToken);
        var source = new GraphPolicySource(new ControlledSpacePolicySource(files.Registry), ConditionalJsonMessageLayout());
        var rig = new GraphRequestRig(source, space.Id);
        const string text = "{\"useRequest\":true,\"fallback\":\"must not run\"}";
        var error = await Record.ExceptionAsync(() => rig.SendAsync(text));
        Assert.NotNull(error); Assert.True(ContainsSpaceCode(error, "SPACE_USER_MESSAGE_GRAPH_INVALID"));
        Assert.Empty(rig.Transport.Requests);
        Assert.Equal(text, Assert.Single(rig.Repository.Messages, message => message.Role == MessageRole.User).Content);
        files.Passed = true;
    }

    private static SpaceLayoutDocument ConditionalJsonMessageLayout()
    {
        var source = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SourceType);
        var parse = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.ParseJsonType);
        var route = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.RouteJsonBooleanType) with
            { Metadata = new Dictionary<string, string> { ["pointer"] = "/useRequest" } };
        var chosen = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SelectJsonType) with
            { Metadata = new Dictionary<string, string> { ["pointer"] = "/request" } };
        var fallback = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SelectJsonType) with
            { Metadata = new Dictionary<string, string> { ["pointer"] = "/fallback" } };
        var chosenText = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.JsonStringType);
        var fallbackText = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.JsonStringType);
        var merge = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.MergeType);
        var output = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.OutputType);
        return new([source, parse, route, chosen, chosenText, fallback, fallbackText, merge, output],
            [new(Guid.NewGuid(), source.Id, "out", parse.Id, "in"), new(Guid.NewGuid(), parse.Id, "out", route.Id, "in"),
                new(Guid.NewGuid(), route.Id, "true", chosen.Id, "in"), new(Guid.NewGuid(), chosen.Id, "out", chosenText.Id, "in"),
                new(Guid.NewGuid(), chosenText.Id, "out", merge.Id, "in"), new(Guid.NewGuid(), route.Id, "false", fallback.Id, "in"),
                new(Guid.NewGuid(), fallback.Id, "out", fallbackText.Id, "in"), new(Guid.NewGuid(), fallbackText.Id, "out", merge.Id, "in"),
                new(Guid.NewGuid(), merge.Id, "out", output.Id, "in")])
            { GraphId = Guid.NewGuid(), Revision = 1, State = SpaceLayoutRevisionState.Active };
    }

    private static SpaceLayoutDocument DirectConditionalMessageLayout(string comparison, string boundary, string query)
    {
        var source = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SourceType);
        var route = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.RouteWordsType) with { Metadata = new Dictionary<string, string>
            { ["query"] = query, ["locale"] = "invariant", ["comparison"] = comparison, ["boundary"] = boundary } };
        var merge = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.MergeType);
        var output = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.OutputType);
        return new([source, route, merge, output],
            [new(Guid.NewGuid(), source.Id, "out", route.Id, "in"), new(Guid.NewGuid(), route.Id, "true", merge.Id, "in"),
                new(Guid.NewGuid(), route.Id, "false", merge.Id, "in"), new(Guid.NewGuid(), merge.Id, "out", output.Id, "in")])
            { GraphId = Guid.NewGuid(), Revision = 1, State = SpaceLayoutRevisionState.Active };
    }

    private static SpaceLayoutDocument ConditionalMessageLayout()
    {
        var direct = DirectConditionalMessageLayout("ordinal", "whole-word", "structured");
        var source = direct.Nodes[0]; var route = direct.Nodes[1]; var merge = direct.Nodes[2]; var output = direct.Nodes[3];
        var parse = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.ParseJsonType);
        var select = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SelectJsonType) with
            { Metadata = new Dictionary<string, string> { ["pointer"] = "/prompt" } };
        var text = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.JsonStringType);
        var truncate = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.TruncateType) with
            { Metadata = new Dictionary<string, string> { ["maximumTextElements"] = "8" } };
        return direct with { Nodes = [source, route, parse, select, text, truncate, merge, output], Edges =
            [new(Guid.NewGuid(), source.Id, "out", route.Id, "in"), new(Guid.NewGuid(), route.Id, "true", parse.Id, "in"),
                new(Guid.NewGuid(), parse.Id, "out", select.Id, "in"), new(Guid.NewGuid(), select.Id, "out", text.Id, "in"),
                new(Guid.NewGuid(), text.Id, "out", merge.Id, "in"), new(Guid.NewGuid(), route.Id, "false", truncate.Id, "in"),
                new(Guid.NewGuid(), truncate.Id, "out", merge.Id, "in"), new(Guid.NewGuid(), merge.Id, "out", output.Id, "in")] };
    }

    private static SpaceLayoutDocument JsonMessageLayout(string pointer, string renderer)
    {
        var source = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SourceType);
        var parse = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.ParseJsonType);
        var select = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SelectJsonType) with
            { Metadata = new Dictionary<string, string> { ["pointer"] = pointer } };
        var render = SpaceUserMessageGraph.CreateNode(renderer);
        var output = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.OutputType);
        var nodes = new[] { source, parse, select, render, output };
        return new(nodes, nodes.Zip(nodes.Skip(1), (from, to) => new SpaceLayoutEdge(Guid.NewGuid(), from.Id, "out", to.Id, "in")).ToArray())
            { GraphId = Guid.NewGuid(), Revision = 1, State = SpaceLayoutRevisionState.Active };
    }

    private static async Task<SpaceUserMessageGraphResult> RunGraph(SpaceLayoutDocument layout, string text)
    {
        var graph = SpaceUserMessageGraph.Compile(Guid.NewGuid(), 1, layout);
        return await graph.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), MessageRole.User, text, null, null, null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);
    }
    private static SpaceLayoutDocument StructuredMessageLayout(string typeId, Dictionary<string, string> metadata)
    {
        var source = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SourceType);
        var transform = SpaceUserMessageGraph.CreateNode(typeId) with { Metadata = metadata };
        var render = SpaceUserMessageGraph.CreateNode(typeId == SpaceUserMessageGraph.ExtractNumbersType ?
            SpaceUserMessageGraph.NumbersToTextType : SpaceUserMessageGraph.MatchesToTextType);
        var output = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.OutputType);
        var nodes = new[] { source, transform, render, output };
        return new(nodes, nodes.Zip(nodes.Skip(1), (from, to) => new SpaceLayoutEdge(Guid.NewGuid(), from.Id, "out", to.Id, "in")).ToArray())
        { GraphId = Guid.NewGuid(), Revision = 1, State = SpaceLayoutRevisionState.Active };
    }

    private static SpaceLayoutDocument UserMessageLayout(int maximum)
    {
        var source = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SourceType);
        var truncate = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.TruncateType) with
        { Metadata = new Dictionary<string, string> { ["maximumTextElements"] = maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        var output = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.OutputType);
        return new([source, truncate, output],
            [new(Guid.NewGuid(), source.Id, "out", truncate.Id, "in"), new(Guid.NewGuid(), truncate.Id, "out", output.Id, "in")])
        { GraphId = Guid.NewGuid(), Revision = 1, State = SpaceLayoutRevisionState.Active };
    }

    private sealed class GraphPolicySource(ControlledSpacePolicySource inner, SpaceLayoutDocument layout) : IOriginalSpaceRequestPolicySource
    {
        internal ControlledSpacePolicySource Inner => inner;
        public Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, Action<Action> acquire, CancellationToken token) =>
            inner.CaptureAsync(conversation, acquire, token);
        public async Task<OriginalSpacePolicySnapshot> ReadAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token)
        {
            var original = await inner.ReadAsync(input, conversation, acquire, token);
            return new(original.Policy, original.Lineage, original.CapturedContext,
                SpaceUserMessageGraph.Compile(original.Policy.SpaceId, original.Lineage.Ancestors[^1].Revision, layout));
        }
        public Task ValidateAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquire, CancellationToken token) =>
            inner.ValidateAsync(input, conversation, acquire, token);
    }
    private sealed class GraphRequestRig
    {
        private readonly ChatSessionService _chat;
        private readonly Conversation _conversation;
        private readonly ModelDescriptor _model;
        internal readonly SpacePolicyTransport Transport;
        internal readonly GraphConversations Repository = new();
        internal readonly GraphEvents Events = new();
        internal GraphRequestRig(IOriginalSpaceRequestPolicySource source, Guid spaceId)
        {
            var now = DateTimeOffset.UtcNow;
            _conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Graph request", null, null,
                false, false, now, now, SpaceId: spaceId);
            _model = new("space-policy-model", 1, "fixture", "fixture", "fixture", new HashSet<ToolCapability> { ToolCapability.Text }, now);
            Transport = new(_model, new SpacePolicyProvider("fixture-provider"));
            _chat = new(Repository, Transport, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()), executionEvents: Events);
            _chat.BindOriginalSpacePolicySource(source);
        }
        internal async Task SendAsync(string text)
        {
            var original = _chat.CreateOriginalOrdinaryConversation(_conversation, text, _model, EffortLevel.Medium,
                "Fixture", "", new() { RequestedRoutingConstraints = new(AllowCloud: true) },
                TestContext.Current.CancellationToken, body => body());
            var failures = new List<Exception>();
            try { await foreach (var _ in original.ConsumeOriginal()) { } } catch (Exception cause) { failures.Add(cause); }
            try { await original.JoinOriginalSourcesAsync(); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }
    private sealed class GraphEvents : IExecutionEventSink
    {
        internal readonly List<ExecutionEvent> Values = [];
        public bool TryPublish(ExecutionEvent value) { Values.Add(value); return true; }
    }
    private sealed class GraphConversations : IConversationRepository
    {
        internal readonly List<ChatMessage> Messages = [];
        private Conversation? _conversation;
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(_conversation);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.ToArray());
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) { _conversation = conversation; return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) { Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("Unexpected delete.");
    }
}
