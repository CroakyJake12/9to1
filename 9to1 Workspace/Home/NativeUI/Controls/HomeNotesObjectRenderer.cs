using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Layout;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Home.NativeUI;

internal static class HomeNotesObjectRenderer
{
    public static Control Render(JsonElement canonical)
    {
        var block = JsonSerializer.Deserialize<NotesBlock>(canonical) ?? throw new InvalidDataException("Missing Notes block.");
        _ = new HomeNotesObjectHandler(HomeNotesSharedObjects.ObjectType(block)).Create(block.Id, canonical);
        if (block.Kind == NotesBlockKind.Table) return Table(block.Table!);
        if (block.Kind == NotesBlockKind.List)
        {
            var panel = new StackPanel { Spacing = 4 };
            var index = block.List!.StartNumber;
            foreach (var item in block.List.Items)
            {
                var label = block.List.Kind switch { NotesListKind.Checklist => item.Checked ? "☑ " : "☐ ", NotesListKind.Numbered => (index++).ToString(CultureInfo.InvariantCulture) + ". ", _ => "• " };
                var text = new TextBlock { Text = label + item.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(item.Level * 16, 0, 0, 0) };
                AutomationProperties.SetAutomationId(text, item.Id.ToString("D"));
                AutomationProperties.SetName(text, block.List.Kind == NotesListKind.Checklist ? (item.Checked ? "Checked: " : "Not checked: ") + item.Text : item.Text);
                panel.Children.Add(text);
            }
            return panel;
        }
        var result = new TextBlock { TextWrapping = block.Kind == NotesBlockKind.Code ? TextWrapping.NoWrap : TextWrapping.Wrap,
            TextAlignment = block.Paragraph.Alignment switch { NotesTextAlignment.Center => TextAlignment.Center, NotesTextAlignment.Right => TextAlignment.Right, NotesTextAlignment.Justify => TextAlignment.Justify, _ => TextAlignment.Left },
            Margin = new Thickness(block.Paragraph.IndentLeft, block.Paragraph.SpaceBefore, block.Paragraph.IndentRight, block.Paragraph.SpaceAfter) };
        if (block.Runs.Count == 0) result.Text = block.PlainText;
        else foreach (var run in block.Runs)
        {
            var inline = new Run(run.Text) { FontFamily = new FontFamily(run.FontFamily), FontSize = run.FontSize,
                FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal, FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal,
                Foreground = Brush.Parse(run.Foreground), Background = Brush.Parse(run.Background) };
            var decorations = new TextDecorationCollection();
            if (run.Underline) decorations.AddRange(TextDecorations.Underline);
            if (run.StrikeThrough) decorations.AddRange(TextDecorations.Strikethrough);
            inline.TextDecorations = decorations;
            result.Inlines!.Add(inline);
        }
        AutomationProperties.SetAutomationId(result, block.Id.ToString("D"));
        return result;
    }
    private static Control Table(NotesTableData table)
    {
        var grid = new Grid(); var occupied = new HashSet<(int Row, int Column)>(); var maxColumn = 0;
        for (var row = 0; row < table.Rows.Count; row++)
        {
            var column = 0;
            foreach (var cell in table.Rows[row].Cells)
            {
                while (occupied.Contains((row, column))) column++;
                if (cell.RowSpan > table.Rows.Count - row || cell.ColumnSpan > 1000 || column + cell.ColumnSpan > 1000)
                    throw new InvalidDataException("Table spans exceed the renderable table bounds.");
                for (var r = row; r < row + cell.RowSpan; r++) for (var c = column; c < column + cell.ColumnSpan; c++)
                    if (!occupied.Add((r, c))) throw new InvalidDataException("Table cell spans overlap.");
                var text = new TextBlock { Text = cell.Text, TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = cell.VerticalAlignment switch { "Center" => VerticalAlignment.Center, "Bottom" => VerticalAlignment.Bottom, _ => VerticalAlignment.Top },
                    FontWeight = table.Rows[row].IsHeader || table.HeaderRow && row == 0 ? FontWeight.Bold : FontWeight.Normal };
                AutomationProperties.SetAutomationId(text, cell.Id.ToString("D"));
                var border = new Border { Child = text, Padding = new Thickness(5), BorderThickness = new Thickness(0.5), BorderBrush = Brushes.Gray, Background = Brush.Parse(cell.Background) };
                Grid.SetRow(border, row); Grid.SetColumn(border, column); Grid.SetRowSpan(border, cell.RowSpan); Grid.SetColumnSpan(border, cell.ColumnSpan);
                grid.Children.Add(border); column += cell.ColumnSpan; maxColumn = Math.Max(maxColumn, column);
            }
        }
        for (var row = 0; row < table.Rows.Count; row++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var column = 0; column < maxColumn; column++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        return grid;
    }
}
