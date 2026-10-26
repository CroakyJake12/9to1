using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Views.Pages.Spaces;

namespace Haven.Desktop.Tests;

public sealed class SpaceGraphContextPickerTests
{
    [AvaloniaFact]
    public async Task Space_graph_context_picker_authored_choice_edits_the_selected_draft_and_round_trips_without_publishing_or_reading_content()
    {
        var token = TestContext.Current.CancellationToken;
        using var paths = new PickerPaths();
        var registry = new SpaceRegistry(new VersionedAtomicSettingsStore(paths));
        var reference = Reference("configured-text");
        var current = await registry.CreateAsync("Context authoring", cancellationToken: token);
        var layout = Layout();
        current = await registry.SaveDraftAsync(current.Id, SpaceConfigurationDocument.From(current) with
        { ContextReferences = [reference], LayoutDocument = layout }, current.Revision,
            current.DraftConfigurationRevision, current.ActiveConfigurationRevision, token);
        var activeBefore = JsonSerializer.Serialize(SpaceConfigurationDocument.From(current));
        var page = new SpaceBuilderPage(registry, current.Id, new PickerPresentationReadiness());
        var window = new Window { Content = page, Width = 1200, Height = 1000 };
        try
        {
            window.Show(); await page.ActivateAsync(token);
            await OpenGraphAsync(page, layout.Nodes[1].Id);
            await ClickAsync(page, "space-builder-graph-context-refresh");
            var choice = Assert.Single(page.GraphContextChoices);
            Assert.Contains("configured-text", choice.Label, StringComparison.Ordinal);
            Assert.Contains("does not read content or grant permission", Find<TextBlock>(page, "space-builder-graph-context-status").Text, StringComparison.Ordinal);
            await ClickAsync(page, "space-builder-graph-context-assign");
            var draft = page.State.Draft.LayoutDocument!;
            Assert.Equal(layout.GraphId, draft.GraphId);
            Assert.Equal(layout.Nodes[1].Id, draft.Nodes[1].Id);
            Assert.Equal(reference.ContextId.ToString("D"), draft.Nodes[1].Metadata["contextId"]);
            Assert.True(page.State.HasChanges);
            var editor = page.OriginalGraphEditor!;
            Assert.True(editor.Editor.Document.Revision > layout.Revision);
            Assert.Equal(reference.ContextId.ToString("D"), Assert.Single(editor.NodeConfigurationInputs).Value.Text);
            Assert.Equal(activeBefore, JsonSerializer.Serialize(SpaceConfigurationDocument.From((await registry.ReadExistingAsync(current.Id, token))!)));
            Assert.True(await page.SaveAsync(token));
            var reopened = await new SpaceRegistry(new VersionedAtomicSettingsStore(paths)).ReadExistingAsync(current.Id, token);
            Assert.Equal(current.Revision, reopened!.Revision);
            Assert.Equal(current.ActiveConfigurationRevision, reopened.ActiveConfigurationRevision);
            Assert.Equal(reference, Assert.Single(reopened.DraftConfiguration!.ContextReferences!));
            Assert.Equal(reference.ContextId.ToString("D"), reopened.DraftConfiguration.LayoutDocument!.Nodes[1].Metadata["contextId"]);
            var resolved = await new SpacePolicyResolver(registry).ResolveDraftAsync(reopened,
                reopened.DraftConfiguration, action => action(), _ => { }, token);
            SpaceUserMessageGraph.Compile(reopened.Id, reopened.Revision + 1, reopened.DraftConfiguration.LayoutDocument)
                .ValidateContextDependencies(resolved.Policy);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Space_graph_context_picker_uses_effective_inherited_sources_and_the_runtime_item_allocation()
    {
        var token = TestContext.Current.CancellationToken;
        using var paths = new PickerPaths(); var registry = new SpaceRegistry(new VersionedAtomicSettingsStore(paths));
        var first = Reference("first") with { AddedAt = DateTimeOffset.UnixEpoch };
        var excluded = Reference("excluded") with { IncludedAutomatically = false, AddedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) };
        var denied = Reference("denied") with { Permission = SpaceContextPermission.Denied };
        var unknown = Reference("unknown") with { Permission = SpaceContextPermission.Unknown };
        var second = Reference("second") with { Permission = SpaceContextPermission.ReadWrite, AddedAt = DateTimeOffset.UnixEpoch.AddSeconds(2) };
        var overflow = Reference("beyond-limit") with { AddedAt = DateTimeOffset.UnixEpoch.AddSeconds(3) };
        var parent = await registry.CreateAsync("Source parent", cancellationToken: token);
        parent = await registry.UpdateAsync(parent with { ContextReferences = [first, excluded, denied, unknown, second, overflow] }, parent.Revision, token);
        var child = await registry.CreateAsync("Source child", description: null, parentSpaceId: parent.Id, cancellationToken: token);
        var layout = Layout();
        child = await registry.SaveDraftAsync(child.Id, SpaceConfigurationDocument.From(child) with
        { Inheritance = new SpaceInheritancePolicy(Sources: true), ContextPolicy = new(MaximumRetrievedItems: 2), LayoutDocument = layout },
            child.Revision, child.DraftConfigurationRevision, child.ActiveConfigurationRevision, token);
        var page = new SpaceBuilderPage(registry, child.Id, new PickerPresentationReadiness());
        var window = new Window { Content = page, Width = 1200, Height = 1000 };
        try
        {
            window.Show(); await page.ActivateAsync(token); await OpenGraphAsync(page, layout.Nodes[1].Id);
            await ClickAsync(page, "space-builder-graph-context-refresh");
            Assert.Equal(new[] { "files / first", "files / second" }, page.GraphContextChoices.Select(row => row.Label));
            await page.AssignGraphContextChoiceAsync(page.GraphContextChoices[1].Input, token);
            Assert.Equal(second.ContextId.ToString("D"), page.State.Draft.LayoutDocument!.Nodes[1].Metadata["contextId"]);
            Assert.Empty(page.State.Draft.ContextReferences); // Inherited references are not cloned into the child.
            Assert.Equal(parent.Revision, (await registry.ReadExistingAsync(parent.Id, token))!.Revision);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("other-node")]
    [InlineData("draft-edit")]
    [InlineData("ancestor-change")]
    [InlineData("foreign-input")]
    public async Task Space_graph_context_picker_refuses_stale_or_foreign_choices_without_editing_another_node(string change)
    {
        var token = TestContext.Current.CancellationToken;
        using var paths = new PickerPaths(); var registry = new SpaceRegistry(new VersionedAtomicSettingsStore(paths));
        var reference = Reference("reviewed");
        var parent = await registry.CreateAsync("Parent", cancellationToken: token);
        parent = await registry.UpdateAsync(parent with { ContextReferences = [reference] }, parent.Revision, token);
        var current = await registry.CreateAsync("Guarded picker", description: null, parentSpaceId: parent.Id, cancellationToken: token);
        var layout = Layout(twoAppendNodes: true);
        current = await registry.SaveDraftAsync(current.Id, SpaceConfigurationDocument.From(current) with
        { Inheritance = new SpaceInheritancePolicy(Sources: true), LayoutDocument = layout }, current.Revision,
            current.DraftConfigurationRevision, current.ActiveConfigurationRevision, token);
        var page = new SpaceBuilderPage(registry, current.Id, new PickerPresentationReadiness());
        var window = new Window { Content = page, Width = 1200, Height = 1000 };
        try
        {
            window.Show(); await page.ActivateAsync(token); await OpenGraphAsync(page, layout.Nodes[1].Id);
            await ClickAsync(page, "space-builder-graph-context-refresh");
            var input = Assert.Single(page.GraphContextChoices).Input;
            if (change == "other-node") page.OriginalGraphEditor!.Editor.SelectNode(layout.Nodes[2].Id);
            if (change == "draft-edit") Assert.True(page.TryEdit(HavenOS.Apps.Spaces.Builder.SpaceBuilderField.Description, "newer draft"));
            if (change == "ancestor-change")
                _ = await registry.UpdateAsync(parent with { ContextReferences = [reference with { IncludedAutomatically = false }] }, parent.Revision, token);
            await page.AssignGraphContextChoiceAsync(change == "foreign-input" ? new object() : input, token);
            Assert.All(page.State.Draft.LayoutDocument!.Nodes.Where(node => node.TypeId == SpaceUserMessageGraph.AppendCapturedContextType),
                node => Assert.Equal("", node.Metadata["contextId"]));
            Assert.Equal(layout.GraphId, page.State.Draft.LayoutDocument.GraphId);
            Assert.Contains("changed", Find<TextBlock>(page, "space-builder-graph-context-status").Text, StringComparison.Ordinal);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Space_graph_context_picker_joins_its_held_metadata_read_and_does_not_publish_choices_for_a_newer_node()
    {
        var token = TestContext.Current.CancellationToken;
        using var paths = new PickerPaths();
        var settings = new VersionedAtomicSettingsStore(paths); var registry = new SpaceRegistry(settings);
        var current = await registry.CreateAsync("Held picker", cancellationToken: token);
        var layout = Layout(twoAppendNodes: true);
        current = await registry.SaveDraftAsync(current.Id, SpaceConfigurationDocument.From(current) with
        { ContextReferences = [Reference("reviewed")], LayoutDocument = layout }, current.Revision,
            current.DraftConfigurationRevision, current.ActiveConfigurationRevision, token);
        var held = new HeldMetadataSettings(settings);
        var page = new SpaceBuilderPage(new SpaceRegistry(held), current.Id, new PickerPresentationReadiness());
        var window = new Window { Content = page, Width = 1200, Height = 1000 };
        Task? refresh = null;
        try
        {
            window.Show(); await page.ActivateAsync(token); await OpenGraphAsync(page, layout.Nodes[1].Id);
            held.Hold = true;
            refresh = page.RefreshGraphContextChoicesAsync(token);
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(refresh.IsCompleted);
            await page.RefreshGraphContextChoicesAsync(token); // Single admitted driver, including before UI dispatch.
            Assert.Equal(1, held.ReadsWhileHeld);
            page.OriginalGraphEditor!.Editor.SelectNode(layout.Nodes[2].Id);
            held.Release.TrySetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Empty(page.GraphContextChoices);
            Assert.All(page.State.Draft.LayoutDocument!.Nodes.Where(node => node.TypeId == SpaceUserMessageGraph.AppendCapturedContextType),
                node => Assert.Equal("", node.Metadata["contextId"]));
        }
        finally
        {
            held.Release.TrySetResult();
            if (refresh is not null) await refresh;
            await page.CloseAndDrainAsync(); window.Content = null; window.Close();
        }
    }

    private sealed class HeldMetadataSettings(IVersionedSettingsStore original) : IVersionedSettingsStore
    {
        internal bool Hold;
        internal int ReadsWhileHeld;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        {
            if (Hold && !Release.Task.IsCompleted)
            { Interlocked.Increment(ref ReadsWhileHeld); Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return await original.GetAsync<T>(key, token);
        }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => original.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => original.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => original.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => original.ImportAsync(manifest, token);
    }

    private static async Task OpenGraphAsync(SpaceBuilderPage page, Guid appendNode)
    {
        await ClickAsync(page, "space-builder-view-expert");
        await ClickAsync(page, "space-builder-expert-user-message");
        page.OriginalGraphEditor!.Editor.SelectNode(appendNode); Dispatcher.UIThread.RunJobs();
    }
    private static T Find<T>(SpaceBuilderPage page, string id) where T : Control =>
        ((Control)page.Host.Content!).GetVisualDescendants().Prepend((Control)page.Host.Content!).OfType<T>()
            .Single(control => control.Name == id || AutomationProperties.GetAutomationId(control) == id);
    private static async Task ClickAsync(SpaceBuilderPage page, string id)
    {
        if (TopLevel.GetTopLevel(page) is Window window) window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Find<Button>(page, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await page.Host.CaptureOriginalActionSettlement(page.Show!).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
    }
    private static SpaceContextReference Reference(string entity) => new(Guid.NewGuid(), SpaceContextReferenceKind.File,
        "files", entity, "metadata-version-1", SpaceContextPermission.Read, SpaceContextIndexState.NotRequired, true, DateTimeOffset.UnixEpoch);
    private static SpaceLayoutDocument Layout(bool twoAppendNodes = false)
    {
        var source = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.SourceType);
        var append = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.AppendCapturedContextType);
        var second = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.AppendCapturedContextType);
        var output = SpaceUserMessageGraph.CreateNode(SpaceUserMessageGraph.OutputType);
        SpaceLayoutNode[] nodes = twoAppendNodes ? [source, append, second, output] : [source, append, output];
        return new(nodes, nodes.Zip(nodes.Skip(1), (from, to) => new SpaceLayoutEdge(Guid.NewGuid(), from.Id, "out", to.Id, "in")).ToArray())
        { GraphId = Guid.NewGuid(), Revision = 7, ConnectionProfileId = SpaceUserMessageGraph.ProfileId };
    }
    // Controlled presentation-only unit fixture. No installed/Home/model/body readiness is asserted.
    private sealed class PickerPresentationReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "PICKER_UNIT_PRESENTATION", "Controlled CUI authoring fixture; no live source access.")); }
    }
    private sealed class PickerPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "space-context-picker-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
