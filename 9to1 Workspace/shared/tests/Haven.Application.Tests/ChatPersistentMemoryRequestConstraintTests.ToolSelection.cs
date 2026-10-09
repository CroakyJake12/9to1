using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    // These controls run SAME Chat/planner/model request production with controlled
    // transport and no tool call. They qualify negative request selection, never
    // a canonical Task, Home/resource permission, workspace effect or installed app.
    [Fact]
    public Task Exact_tool_selection_removes_other_fresh_read_definitions_before_actual_provider_request()
        => WithToolSelectionWorkspace(async (rig, root) =>
        {
            var options = ToolOptions(new(["read_file"]));
            var restored = JsonSerializer.Deserialize<GenerationOptions>(JsonSerializer.Serialize(options))!;
            await rig.SendToolSelectionAsync(root, restored);
            var actual = Assert.Single(rig.Provider.ToolRequests);
            Assert.Equal(["read_file"], actual.Tools.Select(tool => tool.Name));
            Assert.Same(restored, actual.Options);
            Assert.Empty(rig.Provider.Requests);
        });

    [Fact]
    public Task Empty_tool_selection_offers_no_tool_definitions_to_actual_provider()
        => WithToolSelectionWorkspace(async (rig, root) =>
        {
            await rig.SendToolSelectionAsync(root, ToolOptions(new([])));
            Assert.Empty(rig.Provider.ToolRequests);
            var actual = Assert.Single(rig.Provider.Requests);
            Assert.Empty(actual.Options!.RequestedToolSelectionConstraints!.AllowedToolNames);
        });

    [Fact]
    public Task Requested_write_name_cannot_add_a_definition_missing_under_actual_Ask_policy()
        => WithToolSelectionWorkspace(async (rig, root) =>
        {
            await rig.SendToolSelectionAsync(root, ToolOptions(new(["write_file"])));
            Assert.Empty(rig.Provider.ToolRequests);
            Assert.Single(rig.Provider.Requests);
        });

    [Fact]
    public Task Null_tool_selection_preserves_the_current_unconstrained_Ask_read_plan()
        => WithToolSelectionWorkspace(async (rig, root) =>
        {
            await rig.SendToolSelectionAsync(root, ToolOptions(null));
            var names = Assert.Single(rig.Provider.ToolRequests).Tools.Select(tool => tool.Name).ToArray();
            Assert.Contains("read_file", names); Assert.Contains("list_files", names);
            Assert.Contains("search_files", names); Assert.DoesNotContain("write_file", names);
        });

    [Fact]
    public Task Fresh_but_unselected_provider_call_is_denied_before_actual_tool_admission_or_runtime()
        => WithToolSelectionWorkspace(async (rig, root) =>
        {
            rig.Provider.NextControlledToolCall = new("list_files", new Dictionary<string, JsonElement>());
            await rig.SendToolSelectionAsync(root, ToolOptions(new(["read_file"])) with { ActionLimit = 1 });
            var request = Assert.Single(rig.Provider.ToolRequests);
            Assert.Equal(["read_file"], request.Tools.Select(tool => tool.Name));
            var activity = Assert.Single(rig.ToolActivities);
            Assert.False(activity.Succeeded);
            var evidence = Assert.Single(activity.InvocationEvidence);
            Assert.Equal(ToolInvocationObservationStatus.DeniedBeforeDispatch, evidence.Status);
            Assert.Equal("REQUEST_TOOL_NOT_SELECTED", evidence.ReportedFailureCode);
            Assert.Equal(0, rig.OriginalSafety.OriginalToolAdmissions);
        });

    [Fact]
    public void Durable_tool_restriction_copies_caller_names_and_preserves_old_options_payload()
    {
        string[] names = ["read_file"];
        var original = new ModelRequestToolSelectionConstraints(names); names[0] = "write_file";
        Assert.True(original.Allows("read_file")); Assert.False(original.Allows("write_file"));
        Assert.False(original.Allows("READ_FILE"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)original.AllowedToolNames).Add("write_file"));
        var options = ToolOptions(original);
        var restored = JsonSerializer.Deserialize<GenerationOptions>(JsonSerializer.Serialize(options))!;
        Assert.Equal(options, restored);
        Assert.Equal("{\"Temperature\":0.7,\"ContextLimit\":32768,\"ActionLimit\":24}",
            JsonSerializer.Serialize(new GenerationOptions()));
    }

    private static GenerationOptions ToolOptions(ModelRequestToolSelectionConstraints? restriction) => new()
    {
        RequestedContextConstraints = new(false), RequestedRoutingConstraints = new(false, false),
        RequestedToolSelectionConstraints = restriction
    };

    private static async Task WithToolSelectionWorkspace(Func<Rig, string, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-tool-request-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var healthy = false;
        try
        {
            var rig = new Rig(new MemorySource(), actualWorkspaceRoot: root);
            await body(rig, root); healthy = true;
        }
        finally
        {
            // Only this newly created exclusive fixture after the actual Send task
            // and iterator finally work joined. Failures remain available for inspection.
            if (healthy) Directory.Delete(root);
        }
    }
}
