using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Catalog;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Actual_Agent_service_permission_pause_envelope_is_readable_without_promoting_completion_or_retry()
    {
        // Reuse the genuine source-owner path and its existing controlled typed
        // collaborators. This is not installed account/provider/permission proof.
        await using var rig = new Rig();
        var rows = new AgentRows();
        var (service, definition) = CreateAgentCaller(rig, rows);
        var actual = await service.RunAsync(definition.Id, "Actual envelope display", TestContext.Current.CancellationToken);
        var envelope = Assert.IsType<AgentActivityObservation>(JsonSerializer.Deserialize<AgentActivityObservation>(actual.ActivityJson));
        Assert.Equal(AgentActivityObservation.CurrentSchemaVersion, envelope.SchemaVersion);
        Assert.Equal(AgentActivityObservation.OwningProducer, envelope.Producer);
        Assert.Equal(actual.Id, envelope.AgentRunId);
        Assert.Empty(envelope.Activities);
        Assert.Equal("No tool events were recorded for this run.", AgentsHavenScene.FormatActivityLog(actual));
        Assert.Equal(AgentRunStatus.Suspended, actual.Status);
        Assert.Null(actual.CompletedAt);
        Assert.False(envelope.ObservationComplete);
        Assert.False(service.HasOriginalUnstartedRetrySource(actual.Id));
        Assert.Null(await service.GetRecordedInvocationEvidenceAsync(actual.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, rig.Client.Dispatches);
    }
}

public sealed class AgentActivityDisplayCompatibilityTests
{
    [Fact]
    public void Known_schema1_projects_same_display_fields_and_redacts_private_payload_even_with_completion_metadata()
    {
        var run = DisplayRun();
        var activity = new ToolActivity(Guid.NewGuid(), "Public source title", "private tool response", true,
            TimeSpan.FromMilliseconds(24), DateTimeOffset.UnixEpoch);
        var envelope = new AgentActivityObservation(AgentActivityObservation.CurrentSchemaVersion,
            AgentActivityObservation.OwningProducer, run.Id, true, false, [activity], []);
        var formatted = AgentsHavenScene.FormatActivityLog(run with { ActivityJson = JsonSerializer.Serialize(envelope) });
        Assert.Contains("Public source title", formatted, StringComparison.Ordinal);
        Assert.Contains("Succeeded", formatted, StringComparison.Ordinal);
        Assert.Contains("24 ms", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("private tool response", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservationComplete", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Id.ToString(), formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Known_repository_legacy_wrapper_preserves_entire_legacy_array_display_and_null_behavior()
    {
        var run = DisplayRun();
        var activity = new ToolActivity(Guid.NewGuid(), "Legacy public title", "private legacy details", false,
            TimeSpan.FromMilliseconds(12), DateTimeOffset.UnixEpoch);
        var array = JsonSerializer.Serialize(new[] { activity });
        var wrapper = JsonSerializer.Serialize(new
        { LegacyActivitySchema = "array", Activities = new[] { activity }, CanonicalBindingVersion = 1,
          CanonicalTask = new AgentRunCanonicalBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7, TaskExecutionLifecycle.Suspended) });
        var expected = AgentsHavenScene.FormatActivityLog(run with { ActivityJson = array });
        var actual = AgentsHavenScene.FormatActivityLog(run with { ActivityJson = wrapper });
        Assert.Equal(expected, actual);
        Assert.Contains("Legacy public title", actual, StringComparison.Ordinal);
        Assert.Contains("Needs attention", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("private legacy details", actual, StringComparison.Ordinal);
        Assert.Equal("No tool events were recorded for this run.", AgentsHavenScene.FormatActivityLog(run with { ActivityJson = "null" }));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("unknown-producer")]
    [InlineData("foreign-run")]
    [InlineData("bad-complete")]
    [InlineData("bad-invocations")]
    [InlineData("bad-activities")]
    [InlineData("ambiguous-legacy")]
    public void Unknown_future_malformed_or_foreign_envelope_is_honestly_unreadable(string fault)
    {
        var run = DisplayRun();
        var json = JsonSerializer.SerializeToNode(new AgentActivityObservation(1,
            AgentActivityObservation.OwningProducer, run.Id, false, false, [], []))!.AsObject();
        switch (fault)
        {
            case "future": json["SchemaVersion"] = 2; break;
            case "unknown-producer": json["Producer"] = "copied.public.metadata"; break;
            case "foreign-run": json["AgentRunId"] = Guid.NewGuid().ToString(); break;
            case "bad-complete": json["ObservationComplete"] = "true"; break;
            case "bad-invocations": json["Invocations"] = new JsonObject(); break;
            case "bad-activities": json["Activities"] = new JsonObject(); break;
            case "ambiguous-legacy": json["LegacyActivitySchema"] = "array"; break;
        }
        Assert.Equal("Saved activity log could not be read.", AgentsHavenScene.FormatActivityLog(run with { ActivityJson = json.ToJsonString() }));
    }

    [Theory]
    [InlineData("{\"Activities\":[]}")]
    [InlineData("{\"LegacyActivitySchema\":\"unknown\",\"Activities\":[]}")]
    [InlineData("{\"LegacyActivitySchema\":\"array\",\"Activities\":[],\"FutureActivitySchema\":2}")]
    [InlineData("{\"LegacyActivitySchema\":\"array\",\"Activities\":[],\"Activities\":[]}")]
    public void Unknown_or_duplicate_legacy_wrapper_is_not_relabelled_as_known_activity(string json)
    {
        Assert.Equal("Saved activity log could not be read.", AgentsHavenScene.FormatActivityLog(DisplayRun() with { ActivityJson = json }));
    }

    [Fact]
    public void Known_envelope_keeps_original_size_event_validity_and_visible_count_bounds()
    {
        var run = DisplayRun();
        AgentActivityObservation Envelope(IReadOnlyList<ToolActivity> activities) => new(1,
            AgentActivityObservation.OwningProducer, run.Id, false, false, activities, []);
        var invalid = new ToolActivity(Guid.NewGuid(), "Title", "private", true, TimeSpan.FromMilliseconds(-1), DateTimeOffset.UnixEpoch);
        Assert.Equal("Saved activity log contains invalid events.", AgentsHavenScene.FormatActivityLog(run with { ActivityJson = JsonSerializer.Serialize(Envelope([invalid])) }));
        var values = Enumerable.Range(0, 10).Select(index => new ToolActivity(Guid.NewGuid(), $"Event {index}", "private", true, TimeSpan.FromMilliseconds(1), DateTimeOffset.UnixEpoch)).ToArray();
        var formatted = AgentsHavenScene.FormatActivityLog(run with { ActivityJson = JsonSerializer.Serialize(Envelope(values)) });
        Assert.Contains("2 earlier events omitted.", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("Event 0", formatted, StringComparison.Ordinal);
        Assert.Contains("Event 9", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("private", formatted, StringComparison.Ordinal);
        var tooLarge = new ToolActivity(Guid.NewGuid(), new string('x', 256 * 1024), "private", true, TimeSpan.Zero, DateTimeOffset.UnixEpoch);
        Assert.Equal("Saved activity log is too large to display.", AgentsHavenScene.FormatActivityLog(run with { ActivityJson = JsonSerializer.Serialize(Envelope([tooLarge])) }));
    }

    private static AgentRun DisplayRun() => new(Guid.NewGuid(), Guid.NewGuid(), "Saved", "Original task",
        AgentRunStatus.Suspended, "model", string.Empty, string.Empty, "[]", "[]",
        DateTimeOffset.UnixEpoch, null, null);
}
