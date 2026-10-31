using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsToolbarCapabilitiesTests
{
    [Theory]
    [InlineData(HavenRichBlockKind.BulletList, "bulleted")]
    [InlineData(HavenRichBlockKind.NumberedList, "numbered")]
    [InlineData(HavenRichBlockKind.Checklist, "checklist")]
    public async Task Mounted_list_typography_and_overflow_edit_the_entire_row_and_reopen(
        HavenRichBlockKind kind, string projectedKind)
    {
        var directory = Path.Combine(Path.GetTempPath(), "boards-toolbar-capabilities-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var documents = new BoardsLocalDocuments("toolbar-capabilities", directory);
            var id = await documents.CreateAsync();
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                var block = page.Blocks[0];
                block.Text = "The entire list row";
                seed.MarkDirty();
                Assert.True(await seed.ConvertBlockKindAsync(page.Id, block.Id, kind));
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
                    var row = vm.PageBlocks.Single(block => block.Kind == projectedKind);
                    vm.FocusBlock(row.Id); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    T Find<T>(string controlId) where T : Control =>
                        Assert.IsAssignableFrom<T>(BoardsCuiControlFinder.FindByAutomationId<T>(presentation.Content, controlId));
                    foreach (var controlId in new[] { "AdaptiveFont", "AdaptiveSize", "AdaptiveHighlight", "OverflowAdaptiveFont", "OverflowAdaptiveSize" })
                        Assert.True(Find<Control>(controlId).IsEffectivelyEnabled);
                    Assert.False(Find<Control>("AdaptiveStyle").IsEffectivelyEnabled);
                    Assert.False(Find<Control>("OverflowAdaptiveStyle").IsEffectivelyEnabled);
                    Assert.Contains("entire list item", ToolTip.GetTip(Find<Control>("AdaptiveFont"))?.ToString());

                    // A selection inside a list editor must not turn row attributes
                    // into a misleading claim that list substring runs exist.
                    var editor = presentation.Content.GetVisualDescendants().OfType<TextBox>()
                        .Single(input => input.Text == "The entire list row");
                    editor.Focus(); editor.SelectionStart = 4; editor.SelectionEnd = 10;
                    void Invoke(string controlId)
                    {
                        var menu = Find<MenuItem>(controlId);
                        Assert.True(menu.IsEffectivelyEnabled);
                        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    }
                    Invoke("AdaptiveFontSection_Menu_Choose_font_family_FontGeorgia");
                    Assert.Equal("Georgia", vm.FindBlock(row.Id)!.FontFamily);
                    // The overflow command is bound to the same operation as the
                    // visible colour well and persists the same whole-row value.
                    window.Width = 640; window.UpdateLayout();
                    Invoke("OverflowAdaptiveOverflow_Colours_Highlight_FFFFFF00");
                    Invoke("OverflowAdaptiveOverflow_Text_Spacing2");
                    var increase = Find<Button>("OverflowAdaptiveOverflow_Font_and_style_Vertical_Stack_FontBigger");
                    Assert.True(increase.IsEffectivelyEnabled);
                    increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    var changed = vm.FindBlock(row.Id)!;
                    Assert.Equal("The entire list row", changed.Text);
                    Assert.Equal("Georgia", changed.FontFamily);
                    Assert.Equal(16, changed.FontSize);
                    Assert.Equal("#FFFFFF00", changed.Background);
                    Assert.Equal(2, changed.LineSpacing);
                    Assert.Equal("16", Find<TextBox>("AdaptiveSize").Text);
                    Assert.Equal("16", Find<TextBox>("OverflowAdaptiveSize").Text);
                });
                BoardsTabberInsertionTests.OnUi(() => presentation.SaveAsync().AsTask());
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
            await using var reopened = await documents.OpenAsync(id);
            var saved = reopened.Document.Sections[0].Pages[0].Blocks.Single(block => block.Kind == projectedKind);
            Assert.Equal("Georgia", saved.FontFamily);
            Assert.Equal(16, saved.FontSize);
            Assert.Equal("#FFFFFF00", saved.Background);
            Assert.Equal(2, saved.LineSpacing);
            Assert.Equal("The entire list row", saved.Text);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task List_highlight_none_clears_inherited_surface_and_spacing_case_keep_typing_focus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "boards-list-typing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var documents = new BoardsLocalDocuments("list-typing", directory);
            var id = await documents.CreateAsync();
            await using (var seed = await documents.OpenAsync(id))
            {
                var page = seed.Document.Sections[0].Pages[0];
                var block = page.Blocks[0]; block.Text = "List text"; seed.MarkDirty();
                Assert.True(await seed.ConvertBlockKindAsync(page.Id, block.Id, HavenRichBlockKind.BulletList));
                await seed.UpdateStyleAsync("normal", style => style.Background = "#FFCCFF90");
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
                    TextBox Editor() => presentation.Content.GetVisualDescendants().OfType<TextBox>()
                        .Single(input => input.Name == "chk_" + row.Id.Replace(':', '_'));
                    void Invoke(string controlId, string triggerId)
                    {
                        var trigger = Assert.IsAssignableFrom<MenuItem>(BoardsCuiControlFinder.FindByAutomationId<MenuItem>(presentation.Content, triggerId));
                        Assert.True(trigger.Focus());
                        var menu = Assert.IsAssignableFrom<MenuItem>(BoardsCuiControlFinder.FindByAutomationId<MenuItem>(presentation.Content, controlId));
                        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        BoardsTabberInsertionTests.Await(presentation.DrainRenderingAsync());
                        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    }
                    Editor().Focus(); Editor().CaretIndex = 4;
                    Invoke("AdaptiveHighlight_HighlightNone", "AdaptiveHighlight");
                    Assert.Equal("#00000000", vm.FindBlock(row.Id)!.Background);
                    Assert.True(Editor().IsKeyboardFocusWithin);
                    Invoke("AdaptiveParagraphSection_Menu_3_Line_spacing_Spacing2", "AdaptiveParagraphSection_Menu_3_Line_spacing");
                    Assert.True(Editor().IsKeyboardFocusWithin);
                    Assert.Equal(4, Editor().CaretIndex);
                    window.KeyTextInput("!");
                    Assert.Equal("List! text", vm.FindBlock(row.Id)!.Text);
                    Invoke("AdaptiveFontSection_Menu_2_Text_case_CaseUpper", "AdaptiveFontSection_Menu_2_Text_case");
                    Assert.True(Editor().IsKeyboardFocusWithin);
                    window.KeyTextInput("?");
                    Assert.Equal("LIST!? TEXT", vm.FindBlock(row.Id)!.Text);
                    Assert.Equal("#00000000", vm.AdaptiveColorValues().Background);
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
            Assert.Equal("#00000000", saved.Background);
            Assert.Equal(2, saved.LineSpacing);
            Assert.Equal("LIST!? TEXT", saved.Text);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Mounted_paragraph_spacing_changes_layout_and_persists_explicit_zero()
    {
        var directory = Path.Combine(Path.GetTempPath(), "boards-paragraph-space-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var documents = new BoardsLocalDocuments("paragraph-space", directory);
            var id = await documents.CreateAsync();
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
                    var block = vm.PageBlocks.First(item => item.Kind == "paragraph");
                    vm.FocusBlock(block.Id); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    void Invoke(string controlId)
                    {
                        var item = Assert.IsAssignableFrom<MenuItem>(BoardsCuiControlFinder.FindByAutomationId<MenuItem>(presentation.Content, controlId));
                        Assert.True(item.IsEffectivelyEnabled);
                        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    }
                    TextBox Editor() => presentation.Content.GetVisualDescendants().OfType<TextBox>()
                        .Single(input => input.Name == "t_" + block.Id);
                    Invoke("AdaptiveSpaceBefore12");
                    Invoke("OverflowAdaptiveSpaceAfter24");
                    Invoke("OverflowAdaptiveOverflow_Text_AlignRight");
                    Assert.Equal(12, vm.FindBlock(block.Id)!.SpaceBefore);
                    Assert.Equal(24, vm.FindBlock(block.Id)!.SpaceAfter);
                    Assert.Equal(12, Editor().Margin.Top);
                    Assert.Equal(24, Editor().Margin.Bottom);
                    Assert.Equal(Avalonia.Media.TextAlignment.Right, Editor().TextAlignment);
                    using var context = new AdaptiveToolbar.Context(vm);
                    Assert.True(context.TryGetValue("SpaceBefore12Selected", out var before)); Assert.Equal(true, before);
                    Assert.True(context.TryGetValue("SpaceAfter24Selected", out var after)); Assert.Equal(true, after);
                    Assert.True(context.TryGetValue("AlignRightSelected", out var alignment)); Assert.Equal(true, alignment);
                    // Zero remains explicit even when the paragraph style has spacing.
                    Invoke("OverflowAdaptiveSpaceAfter0");
                    Assert.Equal(0, vm.FindBlock(block.Id)!.SpaceAfter);
                    Assert.Equal(0, Editor().Margin.Bottom);
                });
                BoardsTabberInsertionTests.OnUi(() => presentation.SaveAsync().AsTask());
            }
            finally
            {
                TestUiThread.Run(() => window?.Close());
                BoardsTabberInsertionTests.OnUi(() => presentation.DisposeAsync().AsTask());
            }
            await using var reopened = await documents.OpenAsync(id);
            var saved = reopened.Document.Sections[0].Pages[0].Blocks.First(block => block.Kind == "paragraph");
            Assert.Equal(12, saved.SpaceBefore); Assert.Equal(0, saved.SpaceAfter);
            Assert.Equal("right", saved.Alignment);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Contextual_capabilities_guard_hidden_text_commands_on_objects()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            var block = vm.PageBlocks.First();
            block.Kind = "image";
            vm.FocusBlock(block.Id);
            using var context = new AdaptiveToolbar.Context(vm);
            Assert.False(vm.AdaptiveCapabilities().Typography);
            Assert.True(vm.AdaptiveCapabilities().Properties);
            Assert.False(context.TrySetValue("FontDraft", "Georgia"));
            Assert.False(context.TrySetValue("StyleIndex", 0));
            context.DispatchAsync("FontGeorgia", null).GetAwaiter().GetResult();
            context.DispatchAsync("Spacing2", null).GetAwaiter().GetResult();
            Assert.Equal(string.Empty, block.FontFamily);
            Assert.Equal(0, block.LineSpacing);
        });
    }
}
