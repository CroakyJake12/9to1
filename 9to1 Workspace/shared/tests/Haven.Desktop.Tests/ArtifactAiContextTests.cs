using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.Services;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Tests;

public sealed class ArtifactAiContextTests
{
    [AvaloniaFact]
    public async Task Context_contains_offscreen_structure_and_changes_revision_for_unsaved_edits()
    {
        var document = new NotesDocument { Title = "Canonical" };
        document.Sections.Add(new NotesSection { Title = "Off-screen section" });
        var context = new ArtifactAiContext("write", () => (document.Id.ToString("N"), document));
        var first = await context.CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(document.Id.ToString("N"), first.DocumentId);
        Assert.Equal(AppAiDataSensitivity.Private, first.Sensitivity);
        Assert.Contains("Off-screen section", first.SemanticState["artifact"].GetRawText());
        document.Sections.Last().Title = "Changed without saving";
        var second = await context.CaptureAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(first.Revision, second.Revision);
        Assert.Empty(context.Actions);
        var result = await context.ExecuteAsync(new("write", "unregistered", System.Text.Json.JsonSerializer.SerializeToElement(new { }), null, "test", AppAiAccessMode.Write), TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal("unknown-action", result.ErrorCode);
    }
}
