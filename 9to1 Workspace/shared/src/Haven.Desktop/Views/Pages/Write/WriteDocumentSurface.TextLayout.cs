using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WriteDocumentSurface
{
    private HavenParagraphLayout ShapeTextBlock(NotesBlock block, double width)
    {
        var source = block.Runs.Count > 0 ? block.Runs : [new NotesTextRun
        {
            Text = block.PlainText,
            FontFamily = block.Kind == NotesBlockKind.Code ? "Cascadia Mono" : "Montserrat",
            FontSize = block.Kind == NotesBlockKind.Heading ? 24 : 14
        }];
        var runs = source.Select(run => new HavenParagraphRun(run.Text,
            string.IsNullOrWhiteSpace(run.FontFamily) ? "Montserrat" : run.FontFamily,
            Math.Max(8, run.FontSize * _zoom), run.Bold || block.Kind == NotesBlockKind.Heading ? 700 : 400,
            run.Italic, DocumentTextColour(run.Foreground), Colour(run.Background, transparentFallback: true),
            run.Underline, run.StrikeThrough)).ToArray();
        var alignment = block.Paragraph.Alignment switch
        {
            NotesTextAlignment.Center => HavenParagraphAlignment.Center,
            NotesTextAlignment.Right => HavenParagraphAlignment.Right,
            NotesTextAlignment.Justify => HavenParagraphAlignment.Justify,
            _ => HavenParagraphAlignment.Left
        };
        return HavenSceneControl.ShapeParagraph(runs, width, Math.Clamp(block.Paragraph.LineSpacing, .7, 4), alignment);
    }

    private static HavenParagraphLayout ShapeTableCell(TableCellLayout cell) => cell.Paragraph;

    private MeasuredTable MeasureTable(NotesBlock block, double width)
    {
        if (block.Table is not { Rows.Count: > 0 } table) return new(50 * _zoom, []);
        var columns = Math.Max(1, table.Rows.Max(row => row.Cells.Sum(cell => Math.Max(1, cell.ColumnSpan))));
        var unit = width / columns;
        var rowCount = Math.Max(table.Rows.Count, table.Rows.SelectMany((row, index) =>
            row.Cells.Select(cell => index + Math.Max(1, cell.RowSpan))).DefaultIfEmpty(table.Rows.Count).Max());
        var heights = Enumerable.Repeat(42 * _zoom, rowCount).ToArray();
        var prepared = new List<(NotesTableCell Cell, int Row, int Column, int Span, int RowSpan, HavenParagraphLayout Paragraph)>();
        for (var row = 0; row < table.Rows.Count; row++)
        {
            var column = 0;
            foreach (var cell in table.Rows[row].Cells)
            {
                var span = Math.Clamp(cell.ColumnSpan, 1, columns - Math.Min(column, columns - 1));
                var rowSpan = Math.Max(1, cell.RowSpan);
                var weight = row == 0 && table.HeaderRow ? 600 : 400;
                var paragraph = HavenSceneControl.ShapeParagraph([new HavenParagraphRun(cell.Text, "Montserrat", 11 * _zoom,
                    weight, false, new HavenSolidBrush(255, 35, 42, 52))], Math.Max(1, unit * span - 12));
                prepared.Add((cell, row, column, span, rowSpan, paragraph));
                // Grow the same row/span allocation used by paint, hit testing and pagination.
                // Later growth is monotonic, so it cannot invalidate an earlier span's height.
                var allocated = heights.Skip(row).Take(rowSpan).Sum();
                var required = paragraph.Size.Height + 8;
                if (required > allocated) heights[row + rowSpan - 1] += required - allocated;
                column += span;
            }
        }
        var tops = new double[rowCount + 1];
        for (var row = 0; row < rowCount; row++) tops[row + 1] = tops[row] + heights[row];
        var cells = prepared.Select(cell => new TableCellLayout(block, cell.Cell, cell.Row, cell.Column,
            new HavenRect(cell.Column * unit, tops[cell.Row], cell.Span * unit,
                tops[cell.Row + cell.RowSpan] - tops[cell.Row]), cell.Paragraph)).ToArray();
        return new(Math.Max(50 * _zoom, tops[^1]), cells);
    }

    private sealed record MeasuredTable(double Height, IReadOnlyList<TableCellLayout> Cells);

    private static HavenTextLayout ParagraphDescriptor(HavenParagraphLayout paragraph, HavenPoint origin, double width)
    {
        var run = paragraph.Runs[0];
        return new HavenTextLayout(paragraph.Text, run.FontFamily, run.FontSize, run.FontWeight, width, false, run.Italic)
        {
            Paragraph = paragraph, ParagraphOrigin = origin, ParagraphStart = 0, ParagraphLength = paragraph.Text.Length
        };
    }
}
