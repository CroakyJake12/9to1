using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Apps.Boards.Contract;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsAdaptiveToolbarTests
{
    [Fact]
    public async Task Mounted_strip_collapses_sections_but_keeps_fixed_actions_and_restores_them()
    {
        InMemoryRichBoardSession.ClearStore();
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://adaptive-toolbar");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            var root = AdaptiveToolbar.Build(vm);
            var window = new Window { Width = 1500, Height = 200, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var toolbar = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(root, "AdaptivePill")!;
                var layout = Assert.IsType<CuiToolbarPanel>(toolbar.Layout);
                foreach (var width in new[] { 1500d, 640d, 1500d })
                {
                    window.Width = width;
                    window.UpdateLayout();
                    if (width < 700) Assert.True(layout.HiddenItemCount > 0);
                    else Assert.Equal(0, layout.HiddenItemCount);
                    foreach (var id in new[] { "AdaptiveAdd", "AdaptiveDrawing", "AdaptiveAI" })
                    {
                        var action = BoardsCuiControlFinder.FindByAutomationId<Button>(root, id)!;
                        Assert.True(action.IsEffectivelyVisible);
                        Assert.True(action.Bounds.Width > 20);
                        var position = action.TranslatePoint(default, root);
                        Assert.NotNull(position);
                        Assert.True(position!.Value.X + action.Bounds.Width <= root.Bounds.Width + 1);
                    }
                }
                vm.FocusBlock(vm.PageBlocks.First(block => block.Kind == "paragraph").Id);
                vm.SetSelectedFontSize(20); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                void ClickSize(string id)
                {
                    var control = BoardsCuiControlFinder.FindByAutomationId<Button>(root, id)!;
                    Assert.True(control.IsEffectivelyVisible); Assert.True(control.IsEffectivelyEnabled);
                    Assert.True(control.Bounds.Width >= 24); Assert.True(control.Bounds.Height >= 30);
                    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                }
                ClickSize("AdaptiveFontSection_FontBigger");
                BoardsTabberLifetimeTests.Settle(window, () =>
                    BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "AdaptiveSize")!.Text == "22");
                Assert.Equal("22", vm.AdaptiveFontValues().Size);
                Assert.NotEmpty(vm.FindBlock(vm.SelectedBlockId)!.Runs);
                Assert.All(vm.FindBlock(vm.SelectedBlockId)!.Runs, run => Assert.Equal(22, run.FontSize));
                ClickSize("AdaptiveFontSection_FontSmaller");
                BoardsTabberLifetimeTests.Settle(window, () =>
                    BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "AdaptiveSize")!.Text == "20");
                Assert.Equal("20", vm.AdaptiveFontValues().Size);
                Assert.All(vm.FindBlock(vm.SelectedBlockId)!.Runs, run => Assert.Equal(20, run.FontSize));
                Assert.Equal(Avalonia.Layout.VerticalAlignment.Center,
                    BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "AdaptiveFont")!.VerticalContentAlignment);
                Assert.False(BoardsCuiControlFinder.FindByAutomationId<Button>(root, "AdaptiveAI")!.IsEffectivelyEnabled);
                Assert.NotNull(BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "AdaptiveFont"));
                Assert.NotNull(BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "AdaptiveSize"));
            }
            finally { window.Close(); AdaptiveToolbar.Dispose(root); }
        });
    }

    [Fact]
    public async Task Live_projection_and_actions_use_real_values_and_validated_typed_size()
    {
        InMemoryRichBoardSession.ClearStore();
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://adaptive-toolbar-values");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            vm.FocusBlock(vm.PageBlocks.First().Id);
            using var context = new AdaptiveToolbar.Context(vm);
            vm.SetSelectedFontFamily("Georgia");
            vm.SetSelectedFontSize(19);
            Assert.True(context.TryGetValue("FontDraft", out var font)); Assert.Equal("Georgia", font);
            Assert.True(context.TryGetValue("SizeDraft", out var size)); Assert.Equal("19", size);
            Assert.True(context.TrySetValue("SizeDraft", "22"));
            context.DispatchAsync("ApplySize", null).GetAwaiter().GetResult();
            Assert.Equal(22, vm.FindBlock(vm.SelectedBlockId)!.FontSize);
            Assert.True(context.TrySetValue("SizeDraft", "999"));
            context.DispatchAsync("ApplySize", null).GetAwaiter().GetResult();
            Assert.Equal(22, vm.FindBlock(vm.SelectedBlockId)!.FontSize);
            context.DispatchAsync("Spacing1.5", null).GetAwaiter().GetResult();
            Assert.Equal(1.5, vm.FindBlock(vm.SelectedBlockId)!.LineSpacing);
            context.DispatchAsync("Properties", null).GetAwaiter().GetResult();
            Assert.Equal(true, vm.Get("AdaptivePropertiesVisible"));
        });
    }
    [Fact]
    public async Task Colour_projection_follows_the_actual_selected_runs_and_reports_mixed_values()
    {
        var directory = Path.Combine(Path.GetTempPath(), "boards-colour-projection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var documents = new BoardsLocalDocuments("colour-projection", directory);
            var id = await documents.CreateAsync();
            await using (var seed = await documents.OpenAsync(id))
            {
                var block = seed.Document.Sections[0].Pages[0].Blocks[0];
                block.Text = "aabb";
                block.Runs = [
                    new HavenRichTextRun { Text = "aa", Foreground = "#FFFF0000", Background = "#FFFFFF00" },
                    new HavenRichTextRun { Text = "bb", Foreground = "#FF0000FF", Background = "#FF00FF00" }
                ];
                seed.MarkDirty(); await seed.SaveAsync();
            }
            await using var adapter = await documents.OpenAsync(id);
            Assert.Equal(new[] { "#FFFF0000", "#FF0000FF" }, adapter.Document.Sections[0].Pages[0].Blocks[0].Runs.Select(run => run.Foreground));
            Assert.Equal(new[] { "#FFFFFF00", "#FF00FF00" }, adapter.Document.Sections[0].Pages[0].Blocks[0].Runs.Select(run => run.Background));
            TestUiThread.Run(() =>
            {
                using var vm = new BoardsViewModel(adapter);
                var editorHost = new StackPanel();
                var host = new StackPanel();
                vm.Attach(host);
                host.Children.Add(AdaptiveToolbar.Build(vm)); host.Children.Add(editorHost);
                BlockRenderer.Rebuild(editorHost, vm, []);
                var window = new Window { Content = host, Width = 1500, Height = 500 }; window.Show(); window.UpdateLayout();
                try
                {
                    var input = editorHost.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == "aabb");
                    input.Focus(); input.SelectionStart = 0; input.SelectionEnd = 2;
                    using var projection = new AdaptiveToolbar.Context(vm);
                    Assert.True(projection.TryGetValue("TextColor", out var foreground));
                    Assert.Equal(Color.Parse("#FFFF0000"), Assert.IsAssignableFrom<ISolidColorBrush>(foreground).Color);
                    Assert.True(projection.TryGetValue("HighlightColor", out var background));
                    Assert.Equal(Color.Parse("#FFFFFF00"), Assert.IsAssignableFrom<ISolidColorBrush>(background).Color);
                    Assert.True(projection.TryGetValue("TextColorMixed", out var mixed)); Assert.Equal(false, mixed);
                    input.SelectionEnd = 4;
                    Assert.True(projection.TryGetValue("TextColorMixed", out mixed)); Assert.Equal(true, mixed);
                    Assert.True(projection.TryGetValue("HighlightColorMixed", out mixed)); Assert.Equal(true, mixed);
                    Assert.True(projection.TryGetValue("TextColorLabel", out var label)); Assert.Equal("Mixed text colours", label);
                    input.SelectionStart = 2;
                    Assert.True(projection.TryGetValue("TextColor", out foreground));
                    Assert.Equal(Color.Parse("#FF0000FF"), Assert.IsAssignableFrom<ISolidColorBrush>(foreground).Color);
                    Assert.True(projection.TryGetValue("TextColorMixed", out mixed)); Assert.Equal(false, mixed);
                }
                finally { window.Close(); }
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Many_property_notifications_publish_one_final_toolbar_snapshot()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            using var context = new AdaptiveToolbar.Context(vm);
            var publications = 0;
            context.PropertyChanged += (_, _) => publications++;
            for (var index = 0; index < 40; index++) vm.Set("RangeBoldSelected", index % 2 == 1);
            Assert.Equal(0, publications);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, publications);
            Assert.True(context.TryGetValue("RangeBoldSelected", out var current));
            Assert.Equal(true, current);
        });
    }

}
