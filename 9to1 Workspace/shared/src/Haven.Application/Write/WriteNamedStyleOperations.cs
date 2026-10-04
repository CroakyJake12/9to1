using Haven.Core;

namespace Haven.Application;

/// <summary>Canonical materialization of a Notes named style; callers own authorization, transaction and undo.</summary>
public static class WriteNamedStyleOperations
{
    public static void Apply(NotesBlock block, NotesNamedStyle style)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(style);
        block.StyleId = style.Id;
        block.Kind = style.Id switch { "heading-1" or "heading-2" => NotesBlockKind.Heading, "quote" => NotesBlockKind.Quote, "code" => NotesBlockKind.Code, _ => NotesBlockKind.Paragraph };
        CopyParagraph(style.Paragraph, block.Paragraph);
        EnsureRuns(block);
        foreach (var run in block.Runs) CopyCharacter(style.Character, run);
    }

    internal static void EnsureRuns(NotesBlock block) { if (block.Runs.Count == 0) block.Runs.Add(new NotesTextRun { Text = block.PlainText, Bold = block.Kind == NotesBlockKind.Heading, Italic = block.Kind == NotesBlockKind.Quote, FontFamily = block.Kind == NotesBlockKind.Code ? "Cascadia Mono" : "Montserrat", FontSize = block.Kind == NotesBlockKind.Heading ? 24 : 14 }); }
    internal static void CopyCharacter(NotesTextRun source, NotesTextRun target) { var text = target.Text; target.FontFamily = source.FontFamily; target.FontSize = source.FontSize; target.Bold = source.Bold; target.Italic = source.Italic; target.Underline = source.Underline; target.StrikeThrough = source.StrikeThrough; target.Foreground = source.Foreground; target.Background = source.Background; target.Link = source.Link; target.Language = source.Language; target.Text = text; }
    internal static void CopyParagraph(NotesParagraphFormat source, NotesParagraphFormat target) { target.Alignment = source.Alignment; target.LineSpacing = source.LineSpacing; target.SpaceBefore = source.SpaceBefore; target.SpaceAfter = source.SpaceAfter; target.IndentLeft = source.IndentLeft; target.IndentRight = source.IndentRight; target.FirstLineIndent = source.FirstLineIndent; target.KeepWithNext = source.KeepWithNext; target.PageBreakBefore = source.PageBreakBefore; }
}
