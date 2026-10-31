using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Apps.Boards.Contract;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
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
                    new HavenRichTextRun { Text = "aa", FontFamily = "Inter", FontSize = 11, Foreground = "#FFFF0000", Background = "#FFFFFF00" },
                    new HavenRichTextRun { Text = "bb", FontFamily = "Georgia", FontSize = 19, Foreground = "#FF0000FF", Background = "#FF00FF00" }
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
                    foreach (var (caret, family, size, color) in new[]
                    {
                        (0, "Inter", "11", "#FFFF0000"), (2, "Inter", "11", "#FFFF0000"),
                        (3, "Georgia", "19", "#FF0000FF"), (4, "Georgia", "19", "#FF0000FF")
                    })
                    {
                        input.SelectionStart = caret; input.SelectionEnd = caret; input.CaretIndex = caret;
                        Assert.True(projection.TryGetValue("FontDraft", out var font)); Assert.Equal(family, font);
                        Assert.True(projection.TryGetValue("SizeDraft", out var fontSize)); Assert.Equal(size, fontSize);
                        Assert.True(projection.TryGetValue("TextColor", out foreground));
                        Assert.Equal(Color.Parse(color), Assert.IsAssignableFrom<ISolidColorBrush>(foreground).Color);
                        Assert.True(projection.TryGetValue("TextColorMixed", out mixed)); Assert.Equal(false, mixed);
                    }
                    // A stale nonempty selection cannot be presented as a caret just
                    // because the current range ticket cannot be captured.
                    var projectedBlock = vm.FindBlock(vm.SelectedBlockId)!;
                    input.SelectionStart = 0; input.SelectionEnd = 2;
                    projectedBlock.Runs[0].Text = "zz";
                    Assert.True(projection.TryGetValue("FontDraft", out var staleFont)); Assert.Equal("Mixed", staleFont);
                    Assert.True(projection.TryGetValue("TextColorMixed", out mixed)); Assert.Equal(true, mixed);
                    projectedBlock.Runs[0].Text = "aa";
                    input.SelectionStart = 3; input.SelectionEnd = 3; input.CaretIndex = 3;
                    projectedBlock.Text = "zzzz";
                    Assert.True(projection.TryGetValue("FontDraft", out staleFont)); Assert.Equal("Mixed", staleFont);
                    Assert.True(projection.TryGetValue("TextColorMixed", out mixed)); Assert.Equal(true, mixed);
                    projectedBlock.Text = "aabb";
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

    [Fact]
    public async Task Mounted_list_row_keeps_supported_formatting_and_refuses_unstored_properties()
    {
        var directory = Path.Combine(Path.GetTempPath(), "boards-list-toolbar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var documents = new BoardsLocalDocuments("list-toolbar", directory);
            var id = await documents.CreateAsync();
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                var block = page.Blocks[0]; block.Text = "Durable list row"; seed.MarkDirty();
                Assert.True(await seed.ConvertBlockKindAsync(page.Id, block.Id, HavenRichBlockKind.BulletList));
                await seed.SaveAsync();
            }
            var opening = TestUiThread.Run(() => BoardsWritablePresentation.OpenLocalDocumentAsync(documents, id, (_, _) => Task.CompletedTask));
            BoardsTabberInsertionTests.OnUi(() => opening);
            var presentation = await opening;
            Window? window = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    var vm = presentation.ViewModel;
                    window = new Window { Content = presentation.Content, Width = 1500, Height = 700 };
                    window.Show(); window.UpdateLayout();
                    var row = vm.PageBlocks.Single(block => block.Kind == "bulleted");
                    vm.FocusBlock(row.Id); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    foreach (var controlId in new[] { "AdaptiveStyle", "AdaptiveFont", "AdaptiveSize", "AdaptiveHighlight" })
                    {
                        var control = BoardsCuiControlFinder.FindByAutomationId<Control>(presentation.Content, controlId)!;
                        Assert.NotNull(control); Assert.True(control.IsEffectivelyVisible);
                        Assert.False(control.IsEffectivelyEnabled);
                        Assert.Contains("individual list", ToolTip.GetTip(control)?.ToString());
                    }
                    var fontGroup = BoardsCuiControlFinder.FindByAutomationId<Control>(presentation.Content, "AdaptiveFontSection")!;
                    Assert.True(fontGroup.IsEffectivelyEnabled);
                    Assert.Contains("Font, style and size unavailable", ToolTip.GetTip(fontGroup)?.ToString());
                    var caseMenu = presentation.Content.GetLogicalDescendants().OfType<MenuItem>().First(item =>
                        Avalonia.Automation.AutomationProperties.GetName(item) == "Text case");
                    Assert.True(caseMenu.IsEffectivelyEnabled);
                    Assert.Equal("Change text case", ToolTip.GetTip(caseMenu));
                    Assert.True(BoardsCuiControlFinder.FindByAutomationId<Button>(presentation.Content, "RangeBold")!.IsEffectivelyEnabled);
                    Assert.True(BoardsCuiControlFinder.FindByAutomationId<MenuItem>(presentation.Content, "AdaptiveTextColor")!.IsEffectivelyEnabled);
                    using var context = new AdaptiveToolbar.Context(vm);
                    var before = vm.Session.Snapshot;
                    Assert.False(context.TrySetValue("FontDraft", "Georgia"));
                    Assert.False(context.TrySetValue("SizeDraft", "24"));
                    Assert.False(context.TrySetValue("StyleIndex", 0));
                    foreach (var command in new[] { "FontGeorgia", "Size24", "Highlight#FFFFFF00", "Spacing2", "AlignRight", "ListNumbered" })
                    {
                        var refused = context.DispatchAsync(command, null).AsTask(); BoardsTabberInsertionTests.Await(refused);
                        Assert.Equal(before, vm.Session.Snapshot);
                        Assert.Contains("individual list", vm.Get("StatusText")?.ToString());
                    }
                    foreach (var command in new[] { "ToggleBold", "TextColor#FFD32F2F", "ToggleSub", "CaseUpper", "IndentInc" })
                    {
                        var applied = context.DispatchAsync(command, null).AsTask(); BoardsTabberInsertionTests.Await(applied);
                    }
                    var updated = vm.PageBlocks.Single(block => block.Kind == "bulleted");
                    Assert.True(updated.Bold); Assert.Equal("#FFD32F2F", updated.Foreground);
                    Assert.Equal("subscript", updated.Baseline); Assert.Equal("DURABLE LIST ROW", updated.Text);
                    Assert.Equal(1, updated.Level);
                });
                BoardsTabberInsertionTests.OnUi(() => presentation.SaveAsync().AsTask());
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
            await using var reopened = await documents.OpenAsync(id);
            var saved = reopened.Document.Sections[0].Pages[0].Blocks.Single(block => block.Kind == "bulleted");
            Assert.True(saved.Bold); Assert.Equal("#FFD32F2F", saved.Foreground);
            Assert.Equal("subscript", saved.Baseline); Assert.Equal("DURABLE LIST ROW", saved.Text);
            Assert.Equal(1, saved.Level);
        }
        finally { Directory.Delete(directory, true); }
    }

}
