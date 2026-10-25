using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class SpaceGraphTextNormalizationTests
{
    [AvaloniaFact]
    public async Task Space_graph_text_normalization_generic_palette_and_enum_controls_preview_and_activate_the_same_draft()
    {
        var token = TestContext.Current.CancellationToken;
        using var paths = new NormalizationPaths();
        var registry = new SpaceRegistry(new VersionedAtomicSettingsStore(paths));
        var space = await registry.CreateAsync("Normalization authoring", cancellationToken: token);
        SpaceLayoutDocument? latest = null;
        using var page = new SpaceLayoutEditorPage(space, draft => latest = draft);
        Add(page, SpaceUserMessageGraph.SourceType);
        Add(page, SpaceUserMessageGraph.NormalizeTextType);
        Add(page, SpaceUserMessageGraph.OutputType);
        var nodes = page.Editor.Document.Nodes.ToDictionary(node => node.TypeId, StringComparer.Ordinal);
        var normalize = nodes[SpaceUserMessageGraph.NormalizeTextType];
        Assert.True(page.Editor.Connect(nodes[SpaceUserMessageGraph.SourceType].Id, "out", normalize.Id, "in"));
        Assert.True(page.Editor.Connect(normalize.Id, "out", nodes[SpaceUserMessageGraph.OutputType].Id, "in"));
        page.Editor.SelectNode(normalize.Id);
        Assert.Empty(page.NodeConfigurationInputs);
        Assert.Equal(new[] { "preserve", "NFC", "NFKC" }, page.NodeConfigurationOptions.Where(option => option.Key == "unicodeForm").Select(option => option.Value));
        Assert.Equal(new[] { "preserve", "LF" }, page.NodeConfigurationOptions.Where(option => option.Key == "lineEndings").Select(option => option.Value));
        Assert.Equal(new[] { "preserve", "trim-ascii-space-tab-cr-lf" }, page.NodeConfigurationOptions.Where(option => option.Key == "edgeWhitespace").Select(option => option.Value));
        Assert.True(Option(page, "unicodeForm", "NFC").Accessibility.Selected);
        Assert.True(Option(page, "lineEndings", "LF").Accessibility.Selected);
        Assert.True(Option(page, "edgeWhitespace", "preserve").Accessibility.Selected);
        Invoke(Option(page, "unicodeForm", "NFKC"));
        Invoke(Option(page, "edgeWhitespace", "trim-ascii-space-tab-cr-lf"));
        Assert.Equal("NFKC", latest!.Nodes.Single(node => node.Id == normalize.Id).Metadata["unicodeForm"]);
        Assert.Equal("trim-ascii-space-tab-cr-lf", latest.Nodes.Single(node => node.Id == normalize.Id).Metadata["edgeWhitespace"]);
        Assert.True(page.Editor.Undo());
        Assert.True(Option(page, "edgeWhitespace", "preserve").Accessibility.Selected);
        Assert.True(page.Editor.Redo());
        Assert.True(Option(page, "edgeWhitespace", "trim-ascii-space-tab-cr-lf").Accessibility.Selected);
        var graphId = latest.GraphId; var graphRevision = latest.Revision;
        var sample = " \tＡ\u00a0e\u0301\r\n ";
        page.PreviewSampleInput.Text = sample;
        await page.PreviewSampleAsync();
        Assert.Equal(sample, page.PreviewSampleInput.Text);
        Assert.Contains("A é", page.PreviewOutput.Content, StringComparison.Ordinal);
        Assert.Contains("Unicode=NFKC; line endings=LF; edge whitespace=trim-ascii-space-tab-cr-lf; changed=True", page.PreviewTrace.Content, StringComparison.Ordinal);
        Assert.Equal(graphRevision, latest.Revision); // Preview performs no draft edit or live request.
        var saved = await registry.SaveDraftAsync(space.Id, SpaceConfigurationDocument.From(space) with { LayoutDocument = latest },
            space.Revision, space.DraftConfigurationRevision, space.ActiveConfigurationRevision, token);
        Assert.Null(saved.LayoutDocument);
        var active = await registry.ActivateDraftAsync(saved.Id, saved.Revision,
            saved.DraftConfigurationRevision, saved.ActiveConfigurationRevision, token);
        Assert.Equal(space.Id, active.Id);
        Assert.Equal(space.Revision + 1, active.Revision);
        Assert.Equal(graphId, active.LayoutDocument!.GraphId);
        Assert.Equal("NFKC", active.LayoutDocument.Nodes.Single(node => node.Id == normalize.Id).Metadata["unicodeForm"]);
        var graph = SpaceUserMessageGraph.Compile(active.Id, active.Revision, active.LayoutDocument);
        var original = new Haven.Core.ChatMessage(Guid.NewGuid(), Guid.NewGuid(), Haven.Core.MessageRole.User,
            sample, null, null, null, DateTimeOffset.UtcNow);
        var result = await graph.ExecuteAsync(original, token);
        Assert.Equal("A é", result.DerivedText);
        Assert.Equal(sample, original.Content);
    }

    private static void Add(SpaceLayoutEditorPage page, string typeId) =>
        Invoke(Assert.Single(page.UserMessagePalette, entry => entry.TypeId == typeId).Button);
    private static Button Option(SpaceLayoutEditorPage page, string key, string value) =>
        Assert.Single(page.NodeConfigurationOptions, option => option.Key == key && option.Value == value).Button;
    private static void Invoke(Button button)
    {
        var key = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(button.KeyDown(key)); Assert.True(button.KeyUp(key));
    }
    private sealed class NormalizationPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "space-normalization-authoring-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
