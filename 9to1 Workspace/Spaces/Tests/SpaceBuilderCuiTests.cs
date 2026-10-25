using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Spaces.Builder;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpaceBuilderCuiTests
{
    [Fact]
    public async Task View_actions_select_projections_of_the_same_loaded_space()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var registry = files.OpenRegistry();
        var space = await registry.CreateAsync("View selection", cancellationToken: deadline.Token);
        var controller = new SpaceBuilderController(registry, space.Id);
        Assert.True(await controller.LoadAsync(deadline.Token));
        var bindings = new SpaceBuilderCuiBindings(() => controller.State,
            (field, value) => controller.SetField(field, value),
            (category, state) => controller.SetCategoryState(category, state),
            (kind, id) => controller.DetachCapability(kind, id),
            () => [], () => [], (_, _, _) => ValueTask.CompletedTask,
            selectView: controller.SelectEditorView, selectExpertSection: controller.SelectExpertSection,
            previewDraft: controller.PreviewDraft, validateDraft: controller.ValidateDraftAsync);

        Assert.True(bindings.TryGetValue("builder.view.isBasic", out var selected));
        Assert.Equal(true, selected);
        await bindings.DispatchAsync("space-builder.view.advanced", null, deadline.Token);
        Assert.Equal(space.Id, controller.State.SpaceId);
        Assert.Equal(space.Revision, controller.State.DraftRevision);
        Assert.Equal(SpaceBuilderEditorView.Advanced, controller.State.EditorView);
        Assert.True(bindings.TryGetValue("builder.view.isAdvanced", out selected));
        Assert.Equal(true, selected);
        await bindings.DispatchAsync("space-builder.expert.model", null, deadline.Token);
        Assert.Equal(SpaceBuilderEditorView.Expert, controller.State.EditorView);
        Assert.Equal(SpaceBuilderExpertSection.ModelLogic, controller.State.ExpertSection);
        Assert.Equal(space.Revision, controller.State.DraftRevision);
    }

    [Fact]
    public async Task Authored_builder_cui_loads_without_diagnostics()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var (tree, diagnostics) = loader.TryLoad(SpaceBuilderCuiDocument.Load());
            Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Control>(tree);
            loader.WireBindings(root);
            var window = new Window { Content = root, Width = 900, Height = 800 };
            window.Show();
            for (var pass = 0; pass < 3; pass++)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
            }
            T Find<T>(string id) where T : Control => root.GetVisualDescendants().OfType<T>()
                .Single(control => control.Name == id || AutomationProperties.GetAutomationId(control) == id);
            var name = Find<TextBox>("space-builder-name");
            Assert.NotNull(name);
            Assert.NotNull(Find<Button>("space-builder-view-basic"));
            Assert.NotNull(Find<Button>("space-builder-view-advanced"));
            Assert.NotNull(Find<Button>("space-builder-view-expert"));
            Assert.NotNull(Find<Button>("space-builder-expert-details"));
            Assert.NotNull(Find<Button>("space-builder-expert-harness"));
            Assert.NotNull(Find<Button>("space-builder-expert-model"));
            Assert.NotNull(Find<Button>("space-builder-expert-user-message"));
            Assert.NotNull(Find<Button>("space-builder-expert-files"));
            var status = Find<TextBlock>("space-builder-status");
            Assert.NotNull(status);
            window.Close();
            loader.Dispose();
            Dispatcher.UIThread.RunJobs();
            return true;
        }, token);
    }
}
