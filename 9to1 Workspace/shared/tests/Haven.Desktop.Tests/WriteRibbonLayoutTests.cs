using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Write;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class WriteRibbonLayoutTests
{
    [AvaloniaFact]
    public void Write_ribbon_uses_named_tool_groups_and_keeps_the_page_canvas_dominant()
    {
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(NotesDocument.Create("Structured document"), 0, 1);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1200, Height = 850, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            var groups = scene.RibbonContent.Children.OfType<Container>().ToArray();
            Assert.True(groups.Length >= 3);
            Assert.All(groups, group => Assert.StartsWith("Write.Ribbon.Group.", group.Name, StringComparison.Ordinal));
            Assert.DoesNotContain(scene.RibbonContent.Children, child => child is HavenButton);
            Assert.Contains(groups, group => group.Name == "Write.Ribbon.Group.Font");
            Assert.Contains(groups, group => group.Name == "Write.Ribbon.Group.Formatting");
            Assert.Contains(groups, group => group.Name == "Write.Ribbon.Group.Paragraph");
            Assert.DoesNotContain(scene.RibbonContent.DescendantsAndSelf().OfType<HavenButton>(), button => button.Variant == ButtonVariant.Tertiary);
            Assert.Equal(new HavenElement[] { scene.Header, scene.QuickBar, scene.Ribbon, scene.Ruler }, scene.Chrome.Children);
            Assert.Equal(new HavenElement[] { scene.Chrome, scene.Scroller, scene.StatusBar }, scene.Root.Children);
            Assert.True(scene.Header.Bounds.Bottom <= scene.QuickBar.Bounds.Y + 0.1);
            Assert.True(scene.QuickBar.Bounds.Bottom <= scene.Ribbon.Bounds.Y + 0.1);
            Assert.True(scene.Ribbon.Bounds.Bottom <= scene.Ruler.Bounds.Y + 0.1);
            Assert.True(scene.Ruler.Bounds.Bottom <= scene.Scroller.Bounds.Y + 0.1);
            Assert.True(scene.DocumentSurface.Bounds.Width > 760);
            Assert.True(scene.DocumentSurface.Bounds.Height > scene.Ribbon.Bounds.Height * 2);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Write_navigation_tabs_are_compact_text_tabs_not_filled_pills()
    {
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(NotesDocument.Create("Tabs"), 0, 1);

        foreach (var tab in new[] { scene.HomeTab, scene.InsertTab, scene.LayoutTab, scene.ReviewTab })
        {
            Assert.Equal(ButtonVariant.Text, tab.Variant);
            Assert.Equal(tab == scene.HomeTab ? "ButtonTextPrimary" : "ButtonTextSecondary", tab.GetValue(HavenProperties.Foreground));
            Assert.Equal(HavenLength.Px(30), tab.GetValue(HavenProperties.MinHeight));
            Assert.Equal(HavenLength.Px(6), tab.GetValue(HavenProperties.Radius).TopLeft);
        }
    }

    [AvaloniaFact]
    public void Write_ribbon_shows_the_exact_custom_font_and_fractional_size_in_document_state()
    {
        var document = NotesDocument.Create("Custom typography");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "A paragraph";
        block.Runs = [new NotesTextRun { Text = block.PlainText, FontFamily = "Example Serif", FontSize = 13.5 }];
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(document, 0, 1);

        var controls = scene.RibbonContent.DescendantsAndSelf().OfType<Select>().ToArray();
        var font = Assert.Single(controls, control => control.Name == "Write.Home.Font");
        var size = Assert.Single(controls, control => control.Name == "Write.Home.Size");

        Assert.Equal("Example Serif", font.SelectedItem);
        Assert.Contains("Example Serif", font.Items);
        Assert.Equal("13.5", size.SelectedItem);
        Assert.Contains("13.5", size.Items);
    }

    [AvaloniaFact]
    public void Write_ribbon_marks_mixed_character_and_typography_state_for_a_text_selection()
    {
        var document = NotesDocument.Create("Mixed typography");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha beta";
        block.Runs =
        [
            new NotesTextRun { Text = "Alpha ", FontFamily = "Aptos", FontSize = 12, Foreground = "#FF246BCE", Background = "#FFFFE66D", Bold = true },
            new NotesTextRun { Text = "beta", FontFamily = "Georgia", FontSize = 13.5, Foreground = "#FFD64545", Background = "#00000000", Bold = false }
        ];
        block.Paragraph.LineSpacing = 1;
        block.Paragraph.Alignment = NotesTextAlignment.Left;
        var second = NotesBlock.CreateParagraph("Gamma");
        second.Order = 1;
        second.StyleId = "heading-1";
        second.Paragraph.LineSpacing = 1.5;
        second.Paragraph.Alignment = NotesTextAlignment.Center;
        document.Sections[0].Pages[0].Blocks.Add(second);
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(document, 0, 1);

        Assert.True(scene.DocumentSurface.KeyDown(HavenKey.A, new HavenInputModifiers(Control: true)));

        var controls = scene.RibbonContent.DescendantsAndSelf().OfType<Select>().ToArray();
        var style = Assert.Single(controls, control => control.Name == "Write.Home.Style");
        Assert.Equal("Mixed", style.SelectedItem);
        Assert.Contains("more than one", style.Accessibility.Description, StringComparison.Ordinal);
        Assert.Equal("Mixed", Assert.Single(controls, control => control.Name == "Write.Home.Font").SelectedItem);
        Assert.Equal("Mixed", Assert.Single(controls, control => control.Name == "Write.Home.Size").SelectedItem);
        var spacing = Assert.Single(controls, control => control.Name == "Write.Home.LineSpacing");
        Assert.Equal("Mixed", spacing.SelectedItem);
        Assert.Contains("more than one", spacing.Accessibility.Description, StringComparison.Ordinal);
        var alignments = scene.RibbonContent.DescendantsAndSelf().OfType<HavenButton>()
            .Where(button => button.Name.StartsWith("Write.Align.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, alignments.Length);
        Assert.All(alignments, button => Assert.Equal(ButtonVariant.Secondary, button.Variant));
        var bold = Assert.Single(scene.RibbonContent.DescendantsAndSelf().OfType<HavenButton>(), button => button.Name == "Write.Home.Bold");
        Assert.Equal(ButtonVariant.Secondary, bold.Variant);
        Assert.Contains("Mixed", bold.Accessibility.Description, StringComparison.Ordinal);
        var colourButtons = scene.RibbonContent.DescendantsAndSelf().OfType<HavenButton>()
            .Where(button => button.Name.StartsWith("Write.Home.TextColour.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, colourButtons.Length);
        Assert.All(colourButtons, button => Assert.Equal(ButtonVariant.Secondary, button.Variant));
        var highlightButtons = scene.RibbonContent.DescendantsAndSelf().OfType<HavenButton>()
            .Where(button => button.Name.StartsWith("Write.Home.Highlight.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, highlightButtons.Length);
        Assert.All(highlightButtons, button => Assert.Equal(ButtonVariant.Secondary, button.Variant));
    }

    [AvaloniaFact]
    public void Write_ribbon_uses_uniform_selected_run_values_instead_of_the_caret_neighbor()
    {
        var document = NotesDocument.Create("Caret boundary formatting");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "OffOn";
        block.Runs =
        [
            new NotesTextRun { Text = "Off", FontFamily = "Aptos", FontSize = 12, Bold = false },
            new NotesTextRun { Text = "On", FontFamily = "Georgia", FontSize = 16, Bold = true }
        ];
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(document, 0, 1);

        for (var index = 0; index < 3; index++) Assert.True(scene.DocumentSurface.KeyDown(HavenKey.Right, new HavenInputModifiers()));
        for (var index = 0; index < 2; index++) Assert.True(scene.DocumentSurface.KeyDown(HavenKey.Right, new HavenInputModifiers(Shift: true)));

        var controls = scene.RibbonContent.DescendantsAndSelf().OfType<Select>().ToArray();
        Assert.Equal("Georgia", Assert.Single(controls, control => control.Name == "Write.Home.Font").SelectedItem);
        Assert.Equal("16", Assert.Single(controls, control => control.Name == "Write.Home.Size").SelectedItem);
        var bold = Assert.Single(scene.RibbonContent.DescendantsAndSelf().OfType<HavenButton>(), button => button.Name == "Write.Home.Bold");
        Assert.Equal(ButtonVariant.Primary, bold.Variant);
    }

    [AvaloniaFact]
    public void Table_insert_command_keeps_the_canonical_first_cell_selected()
    {
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(NotesDocument.Create("Insert table selection"), 0, 1);
        var key = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(scene.InsertTab.KeyDown(key));
        Assert.True(scene.InsertTab.KeyUp(key));
        var insertTable = Assert.Single(scene.RibbonContent.DescendantsAndSelf().OfType<HavenButton>(),
            button => button.Name == "Write.Insert.Table2x2");
        Assert.True(insertTable.KeyDown(key));
        Assert.True(insertTable.KeyUp(key));

        var editor = Assert.IsType<WriteDocumentEditor>(scene.SharedInkEditor);
        var table = Assert.IsType<NotesTableData>(editor.SelectedBlock?.Table);
        Assert.Equal(2, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.Equal(2, row.Cells.Count));
        var firstCell = table.Rows[0].Cells[0].Id;
        Assert.Equal(firstCell, editor.SelectedTableCellId);
        Assert.Equal(firstCell, scene.DocumentSurface.ActiveTableCellId);
    }

}
