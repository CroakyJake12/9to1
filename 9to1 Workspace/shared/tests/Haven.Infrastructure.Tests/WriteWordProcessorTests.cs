using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class WriteWordProcessorTests
{
    [Fact]
    public void Editor_supports_word_processor_formatting_structure_layout_review_and_history()
    {
        var document = NotesDocument.Create("Coursework"); var block = document.Sections[0].Pages[0].Blocks[0]; block.PlainText = "Alpha beta"; block.Runs = [new NotesTextRun { Text = "Alpha ", Bold = true }, new NotesTextRun { Text = "beta", Italic = true }]; var editor = new WriteDocumentEditor(document); editor.SelectBlock(block.Id, 8); editor.ToggleCharacter(WriteCharacterFormat.Underline); editor.SetFontFamily("Aptos"); editor.SetFontSize(12); editor.SetAlignment(NotesTextAlignment.Justify); editor.SetLineSpacing(1.5); editor.SetLeftIndent(24); editor.SetFirstLineIndent(18);
        Assert.True(block.Runs[1].Underline); Assert.Equal("Aptos", block.Runs[1].FontFamily); Assert.Equal(12, block.Runs[1].FontSize); Assert.Equal(NotesTextAlignment.Justify, block.Paragraph.Alignment); Assert.Equal(1.5, block.Paragraph.LineSpacing); Assert.Equal(24, block.Paragraph.IndentLeft); Assert.Equal(18, block.Paragraph.FirstLineIndent);
        editor.SelectBlock(block.Id, 8); Assert.True(editor.SplitRunAtCaret()); Assert.Equal(3, block.Runs.Count); Assert.True(editor.MergeRunWithPrevious()); Assert.Equal(2, block.Runs.Count); var list = editor.InsertBlock(NotesBlockKind.List, NotesListKind.Checklist); Assert.Equal(NotesListKind.Checklist, list.List!.Kind); editor.AddListItem(); Assert.Equal(2, list.List.Items.Count); var table = editor.InsertBlock(NotesBlockKind.Table); editor.SelectBlock(table.Id); editor.AddTableRow(); editor.AddTableColumn(); Assert.Equal(4, table.Table!.Rows.Count); Assert.All(table.Table.Rows, row => Assert.Equal(4, row.Cells.Count));
        editor.SetPagePreset("Letter"); editor.SetOrientation("Landscape"); editor.SetMargins(54); editor.SetPageNumbers(false); Assert.Equal("Landscape", document.PageSetup.Orientation); Assert.True(document.PageSetup.WidthPoints > document.PageSetup.HeightPoints); Assert.Equal(54, document.PageSetup.MarginLeftPoints); Assert.False(document.PageSetup.ShowPageNumbers); editor.SelectBlock(block.Id); editor.AddComment("Check this wording"); editor.AddCitation("Source", "Author", "https://example.test"); Assert.Single(document.Comments); Assert.Single(document.Citations);
        Assert.Single(editor.Find("beta")); Assert.Equal(1, editor.ReplaceAll("beta", "gamma")); Assert.Contains("gamma", block.PlainText); Assert.True(editor.CanUndo); Assert.True(editor.Undo()); Assert.Contains("beta", document.Sections[0].Pages[0].Blocks[0].PlainText); Assert.True(editor.Redo()); Assert.Contains("gamma", document.Sections[0].Pages[0].Blocks[0].PlainText);
    }

    [Fact]
    public void Text_editing_preserves_existing_run_formatting()
    {
        var document = NotesDocument.Create();
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.Runs = [new NotesTextRun { Text = "Bold ", Bold = true }, new NotesTextRun { Text = "italic", Italic = true }];
        block.PlainText = "Bold italic";
        var editor = new WriteDocumentEditor(document);
        WriteDocumentPosition? caretAtChange = null;
        editor.Changed += (_, _) => caretAtChange = editor.DocumentCaret;
        editor.SelectBlock(block.Id, 5);
        editor.ReplaceSelectedText("Bold stronger italic", 12);

        Assert.Equal("Bold stronger ", block.Runs[0].Text);
        Assert.True(block.Runs[0].Bold);
        Assert.Equal("italic", block.Runs[1].Text);
        Assert.True(block.Runs[1].Italic);
        Assert.Equal("Bold stronger italic", block.PlainText);
        Assert.Equal(new WriteDocumentPosition(block.Id, 12), caretAtChange.GetValueOrDefault());
        Assert.Equal(new WriteDocumentPosition(block.Id, 12), editor.DocumentCaret);
        Assert.False(editor.HasSelection);
        Assert.True(editor.Undo());
        Assert.Equal(new WriteDocumentPosition(block.Id, 5), editor.DocumentCaret);
    }

    [Fact]
    public void Document_range_selection_replaces_text_across_paragraphs_as_one_edit()
    {
        var document = NotesDocument.Create("Range editing");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.PlainText = "Hello";
        first.Runs = [new NotesTextRun { Text = "Hello" }];
        var second = NotesBlock.CreateParagraph();
        second.PlainText = "World";
        second.Runs = [new NotesTextRun { Text = "World" }];
        page.Blocks.Add(second);
        var editor = new WriteDocumentEditor(document);

        editor.SetDocumentCaret(first.Id, 0);
        editor.SetDocumentCaret(second.Id, 3, extendSelection: true);

        Assert.True(editor.HasDocumentSelection);
        Assert.Equal("llo\nWor", editor.SelectedDocumentText);
        Assert.True(editor.InsertDocumentText("X"));
        Assert.False(editor.HasDocumentSelection);
        Assert.Equal("HeXld", Assert.Single(editor.TextBlocks()).PlainText);
        Assert.Equal(3, editor.DocumentCaret.Offset);

        Assert.True(editor.Undo());
        Assert.Equal(2, editor.TextBlocks().Count);
        Assert.Equal("Hello", editor.TextBlocks()[0].PlainText);
        Assert.Equal("World", editor.TextBlocks()[1].PlainText);
        Assert.True(editor.HasDocumentSelection);
        Assert.Equal("llo\nWor", editor.SelectedDocumentText);
        Assert.Equal(new WriteDocumentPosition(second.Id, 3), editor.DocumentCaret);

        Assert.True(editor.Redo());
        Assert.False(editor.HasDocumentSelection);
        Assert.Equal(new WriteDocumentPosition(first.Id, 3), editor.DocumentCaret);
    }

    [Fact]
    public void Undo_and_redo_restore_the_editing_selection_and_caret_with_document_state()
    {
        var document = NotesDocument.Create("Selection history");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha beta";
        block.Runs = [new NotesTextRun { Text = "Alpha beta" }];
        var editor = new WriteDocumentEditor(document);

        editor.SelectBlock(block.Id, caretIndex: 10, selectionStart: 6, selectionEnd: 10);
        editor.ReplaceSelectedText("Alpha gamma", caretIndex: 11);

        Assert.True(editor.Undo());
        Assert.Equal("Alpha beta", editor.SelectedText);
        Assert.True(editor.HasSelection);
        Assert.Equal(10, editor.CaretIndex);
        Assert.Equal(6, editor.SelectionStart);
        Assert.Equal(10, editor.SelectionEnd);

        Assert.True(editor.Redo());
        Assert.Equal("Alpha gamma", editor.SelectedText);
        Assert.False(editor.HasSelection);
        Assert.Equal(11, editor.CaretIndex);
    }

    [Fact]
    public void A_new_edit_after_undo_discards_the_old_redo_branch()
    {
        var document = NotesDocument.Create("Redo branch");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "First";
        block.Runs = [new NotesTextRun { Text = "First" }];
        var editor = new WriteDocumentEditor(document);

        editor.ReplaceSelectedText("Second", 6);
        Assert.True(editor.Undo());
        Assert.True(editor.CanRedo);
        editor.ReplaceSelectedText("Third", 5);

        Assert.False(editor.CanRedo);
        Assert.False(editor.Redo());
        Assert.Equal("Third", editor.SelectedBlock?.PlainText);
    }

    [Fact]
    public void Replace_all_maps_caret_and_selection_through_replacements_before_and_inside_the_range()
    {
        var document = NotesDocument.Create("Replace all selection state");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha alpha";
        block.Runs = [new NotesTextRun { Text = block.PlainText }];
        var editor = new WriteDocumentEditor(document);
        editor.SelectBlock(block.Id, caretIndex: 11, selectionStart: 6, selectionEnd: 11);
        WriteDocumentPosition? caretAtChange = null;
        editor.Changed += (_, _) => caretAtChange = editor.DocumentCaret;

        Assert.Equal(2, editor.ReplaceAll("alpha", "x"));

        Assert.Equal("x x", block.PlainText);
        Assert.Equal(new WriteDocumentPosition(block.Id, 3), caretAtChange.GetValueOrDefault());
        Assert.Equal(new WriteDocumentPosition(block.Id, 3), editor.DocumentCaret);
        Assert.Equal(2, editor.SelectionStart);
        Assert.Equal(3, editor.SelectionEnd);
        Assert.Equal("x", editor.SelectedText);
        Assert.True(editor.Undo());
        Assert.Equal("Alpha alpha", editor.SelectedBlock?.PlainText);
        Assert.Equal(11, editor.CaretIndex);
        Assert.Equal(6, editor.SelectionStart);
        Assert.Equal(11, editor.SelectionEnd);
        Assert.True(editor.Redo());
        Assert.Equal("x", editor.SelectedText);
        Assert.Equal(new WriteDocumentPosition(block.Id, 3), editor.DocumentCaret);
    }

    [Fact]
    public void Replace_all_maps_cross_paragraph_anchor_and_caret_by_their_own_block_lengths()
    {
        var document = NotesDocument.Create("Cross-paragraph replace state");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.PlainText = "Alpha";
        first.Runs = [new NotesTextRun { Text = "Alpha" }];
        var second = NotesBlock.CreateParagraph("Alpha");
        second.Order = 1;
        page.Blocks.Add(second);
        var editor = new WriteDocumentEditor(document);
        editor.SetDocumentCaret(first.Id, 0);
        editor.SetDocumentCaret(second.Id, 5, extendSelection: true);

        Assert.Equal(2, editor.ReplaceAll("Alpha", "A"));

        Assert.Equal("A", editor.TextBlocks()[0].PlainText);
        Assert.Equal("A", editor.TextBlocks()[1].PlainText);
        Assert.Equal(new WriteDocumentPosition(first.Id, 0), editor.DocumentAnchor.GetValueOrDefault());
        Assert.Equal(new WriteDocumentPosition(second.Id, 1), editor.DocumentCaret);
        Assert.True(editor.HasDocumentSelection);
        Assert.Equal("A\nA", editor.SelectedDocumentText);
        Assert.True(editor.Undo());
        Assert.Equal(new WriteDocumentPosition(first.Id, 0), editor.DocumentAnchor.GetValueOrDefault());
        Assert.Equal(new WriteDocumentPosition(second.Id, 5), editor.DocumentCaret);
        Assert.Equal("Alpha\nAlpha", editor.SelectedDocumentText);
    }

    [Fact]
    public void Replace_all_does_not_report_read_only_media_identity_as_replaced()
    {
        var document = NotesDocument.Create("Media replace scope");
        var media = new NotesMediaData { OriginalName = "alpha.png" };
        var image = new NotesBlock { Kind = NotesBlockKind.Image, Media = media };
        document.Sections[0].Pages[0].Blocks.Add(image);
        var equation = NotesBlock.EquationBlock();
        equation.Order = 1;
        var equationData = equation.Equation!;
        equationData.Source = "alpha+1";
        equationData.RenderedText = "alpha+1";
        equationData.AccessibleAlternative = "alpha description";
        document.Sections[0].Pages[0].Blocks.Add(equation);
        var editor = new WriteDocumentEditor(document);

        Assert.Equal(3, editor.Find("alpha").Count);
        Assert.Equal(2, editor.ReplaceAll("alpha", "beta"));
        Assert.True(editor.CanUndo);
        Assert.Equal("alpha.png", media.OriginalName);
        Assert.Equal("beta+1", equationData.Source);
        Assert.Equal("beta+1", equationData.RenderedText);
        Assert.Equal("beta description", equationData.AccessibleAlternative);

        media.AltText = "alpha";
        Assert.Equal(1, editor.ReplaceAll("alpha", "beta"));
        Assert.Equal("beta", media.AltText);

        var identityOnly = NotesDocument.Create("Filename only");
        var filename = new NotesMediaData { OriginalName = "alpha.png" };
        identityOnly.Sections[0].Pages[0].Blocks.Add(new NotesBlock { Kind = NotesBlockKind.Image, Media = filename });
        var identityEditor = new WriteDocumentEditor(identityOnly);
        Assert.Single(identityEditor.Find("alpha"));
        Assert.Equal(0, identityEditor.ReplaceAll("alpha", "beta"));
        Assert.False(identityEditor.CanUndo);
        Assert.Equal("alpha.png", filename.OriginalName);
    }

    [Fact]
    public void Run_boundary_history_restores_the_active_run_on_redo()
    {
        var document = NotesDocument.Create("Run-boundary history");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "AlphaBeta";
        block.Runs = [new NotesTextRun { Text = "AlphaBeta", Italic = true }];
        var editor = new WriteDocumentEditor(document);
        editor.SelectBlock(block.Id, 5);

        Assert.True(editor.SplitRunAtCaret());
        Assert.Equal(1, editor.ActiveRunIndex);
        Assert.True(editor.Undo());
        Assert.True(editor.Redo());

        Assert.Equal(1, editor.ActiveRunIndex);
        Assert.Equal("Beta", editor.ActiveRun?.Text);
        Assert.True(editor.ActiveRun?.Italic == true);
    }

    [Fact]
    public void Document_enter_backspace_and_selection_formatting_follow_word_processor_boundaries()
    {
        var document = NotesDocument.Create("Keyboard editing");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "AlphaBeta";
        block.Runs = [new NotesTextRun { Text = "AlphaBeta" }];
        var editor = new WriteDocumentEditor(document);

        editor.SetDocumentCaret(block.Id, 5);
        Assert.True(editor.InsertDocumentText("\n"));
        var split = editor.TextBlocks();
        Assert.Equal(2, split.Count);
        Assert.Equal("Alpha", split[0].PlainText);
        Assert.Equal("Beta", split[1].PlainText);

        editor.SetDocumentCaret(split[1].Id, 0);
        Assert.True(editor.BackspaceDocument());
        var merged = Assert.Single(editor.TextBlocks());
        Assert.Equal("AlphaBeta", merged.PlainText);

        editor.SetDocumentCaret(merged.Id, 5);
        editor.SetDocumentCaret(merged.Id, 9, extendSelection: true);
        editor.ToggleCharacter(WriteCharacterFormat.Bold);
        var runs = editor.TextBlocks()[0].Runs;
        Assert.Contains(runs, run => run.Text == "Alpha" && !run.Bold);
        Assert.Contains(runs, run => run.Text == "Beta" && run.Bold);
    }

    [Fact]
    public void Enter_inside_a_formatted_run_preserves_prefix_and_suffix_run_properties()
    {
        var document = NotesDocument.Create("Formatted paragraph split");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "AlphaBeta";
        block.Runs =
        [
            new NotesTextRun { Text = "Alpha", FontFamily = "Aptos" },
            new NotesTextRun { Text = "Beta", FontFamily = "Georgia", Bold = true, Italic = true }
        ];
        var editor = new WriteDocumentEditor(document);
        editor.SetDocumentCaret(block.Id, 7);

        Assert.True(editor.InsertDocumentText("\n"));

        var blocks = editor.TextBlocks();
        Assert.Equal("AlphaBe", blocks[0].PlainText);
        Assert.Equal("ta", blocks[1].PlainText);
        Assert.Contains(blocks[0].Runs, run => run.Text == "Alpha" && run.FontFamily == "Aptos");
        Assert.Contains(blocks[0].Runs, run => run.Text == "Be" && run.FontFamily == "Georgia" && run.Bold && run.Italic);
        Assert.Contains(blocks[1].Runs, run => run.Text == "ta" && run.FontFamily == "Georgia" && run.Bold && run.Italic);
        var splitRuns = blocks.SelectMany(value => value.Runs).ToArray();
        Assert.Equal(splitRuns.Length, splitRuns.Select(run => run.Id).Distinct().Count());

        Assert.True(editor.Undo());
        var restored = Assert.Single(editor.TextBlocks());
        Assert.Equal("AlphaBeta", restored.PlainText);
        Assert.Contains(restored.Runs, run => run.Text == "Beta" && run.FontFamily == "Georgia" && run.Bold && run.Italic);
        Assert.True(editor.Redo());
        Assert.Equal("ta", editor.TextBlocks()[1].PlainText);
    }

    [Fact]
    public void Enter_split_preserves_paragraph_style_and_format_without_duplicating_page_break()
    {
        var document = NotesDocument.Create("Paragraph split format");
        var page = document.Sections[0].Pages[0];
        var quote = new NotesBlock { Kind = NotesBlockKind.Quote, StyleId = "quote", PlainText = "AlphaBeta", Runs = [new NotesTextRun { Text = "AlphaBeta", Italic = true }] };
        quote.Paragraph.Alignment = NotesTextAlignment.Right;
        quote.Paragraph.LineSpacing = 1.75;
        quote.Paragraph.SpaceBefore = 12;
        quote.Paragraph.SpaceAfter = 16;
        quote.Paragraph.IndentLeft = 24;
        quote.Paragraph.IndentRight = 18;
        quote.Paragraph.FirstLineIndent = 4;
        quote.Paragraph.KeepWithNext = true;
        quote.Paragraph.PageBreakBefore = true;
        page.Blocks[0] = quote;
        var editor = new WriteDocumentEditor(document);
        editor.SetDocumentCaret(quote.Id, 5);

        Assert.True(editor.InsertDocumentText("\n"));

        var continuation = editor.TextBlocks()[1];
        Assert.Equal(NotesBlockKind.Quote, continuation.Kind);
        Assert.Equal("quote", continuation.StyleId);
        Assert.Equal(NotesTextAlignment.Right, continuation.Paragraph.Alignment);
        Assert.Equal(1.75, continuation.Paragraph.LineSpacing);
        Assert.Equal(12, continuation.Paragraph.SpaceBefore);
        Assert.Equal(16, continuation.Paragraph.SpaceAfter);
        Assert.Equal(24, continuation.Paragraph.IndentLeft);
        Assert.Equal(18, continuation.Paragraph.IndentRight);
        Assert.Equal(4, continuation.Paragraph.FirstLineIndent);
        Assert.True(continuation.Paragraph.KeepWithNext);
        Assert.False(continuation.Paragraph.PageBreakBefore);
        Assert.True(page.Blocks[0].Paragraph.PageBreakBefore);
        Assert.True(editor.Undo());
        Assert.Single(editor.TextBlocks());
        Assert.True(editor.Redo());
        Assert.Equal("Beta", editor.TextBlocks()[1].PlainText);
    }

    [Fact]
    public void Cross_paragraph_selection_delete_preserves_formatting_from_both_source_runs()
    {
        var document = NotesDocument.Create("Formatted paragraph join");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.PlainText = "Alpha";
        first.Runs = [new NotesTextRun { Text = "A", FontFamily = "Aptos" }, new NotesTextRun { Text = "lpha", Italic = true }];
        var second = NotesBlock.CreateParagraph();
        second.PlainText = "Bravo";
        second.Runs = [new NotesTextRun { Text = "Br", Bold = true }, new NotesTextRun { Text = "avo", Underline = true }];
        page.Blocks.Add(second);
        var editor = new WriteDocumentEditor(document);

        editor.SetDocumentCaret(first.Id, 2);
        editor.SetDocumentCaret(second.Id, 3, extendSelection: true);
        Assert.True(editor.DeleteDocumentSelection());

        var joined = Assert.Single(editor.TextBlocks());
        Assert.Equal("Alvo", joined.PlainText);
        Assert.Contains(joined.Runs, run => run.Text == "A" && run.FontFamily == "Aptos");
        Assert.Contains(joined.Runs, run => run.Text == "l" && run.Italic);
        Assert.Contains(joined.Runs, run => run.Text == "vo" && run.Underline);
        Assert.Equal(joined.Runs.Count, joined.Runs.Select(run => run.Id).Distinct().Count());
        Assert.True(editor.Undo());
        Assert.Equal("Alpha", editor.TextBlocks()[0].PlainText);
        Assert.Equal("Bravo", editor.TextBlocks()[1].PlainText);
        Assert.True(editor.Redo());
        joined = Assert.Single(editor.TextBlocks());
        Assert.Contains(joined.Runs, run => run.Text == "vo" && run.Underline);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Paragraph_backspace_and_forward_delete_preserve_joined_run_formatting(bool backspace)
    {
        var document = NotesDocument.Create("Formatted paragraph merge");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.PlainText = "Alpha";
        first.Runs = [new NotesTextRun { Text = "Alpha", Bold = false }];
        var second = NotesBlock.CreateParagraph();
        second.PlainText = "Bravo";
        second.Runs = [new NotesTextRun { Text = "Bravo", Bold = true }];
        page.Blocks.Add(second);
        var editor = new WriteDocumentEditor(document);
        if (backspace)
        {
            editor.SetDocumentCaret(second.Id, 0);
            Assert.True(editor.BackspaceDocument());
        }
        else
        {
            editor.SetDocumentCaret(first.Id, 5);
            Assert.True(editor.DeleteForwardDocument());
        }

        var joined = Assert.Single(editor.TextBlocks());
        Assert.Equal("AlphaBravo", joined.PlainText);
        Assert.Contains(joined.Runs, run => run.Text == "Alpha" && !run.Bold);
        Assert.Contains(joined.Runs, run => run.Text == "Bravo" && run.Bold);
    }

    [Fact]
    public void Deleting_all_text_across_paragraphs_keeps_an_editable_empty_run()
    {
        var document = NotesDocument.Create("Empty formatted paragraph");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.PlainText = "Alpha";
        first.Runs = [new NotesTextRun { Text = "Alpha", FontFamily = "Aptos" }];
        var second = NotesBlock.CreateParagraph();
        second.PlainText = "Bravo";
        second.Runs = [new NotesTextRun { Text = "Bravo", Bold = true }];
        page.Blocks.Add(second);
        var editor = new WriteDocumentEditor(document);

        editor.SetDocumentCaret(first.Id, 0);
        editor.SetDocumentCaret(second.Id, 5, extendSelection: true);
        Assert.True(editor.DeleteDocumentSelection());

        var empty = Assert.Single(editor.TextBlocks());
        var emptyRun = Assert.Single(empty.Runs);
        Assert.Equal(string.Empty, emptyRun.Text);
        Assert.Equal("Aptos", emptyRun.FontFamily);
        Assert.True(editor.InsertDocumentText("Draft"));
        Assert.Equal("Draft", empty.PlainText);
        Assert.Equal("Aptos", Assert.Single(empty.Runs).FontFamily);
    }

    [Fact]
    public void Paragraph_formatting_applies_once_to_each_paragraph_touched_by_document_selection()
    {
        var document = NotesDocument.Create("Paragraph selection formatting");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.PlainText = "Alpha";
        first.Runs = [new NotesTextRun { Text = "Alpha" }];
        first.Paragraph.Alignment = NotesTextAlignment.Left;
        var second = NotesBlock.CreateParagraph();
        second.PlainText = "Bravo";
        second.Runs = [new NotesTextRun { Text = "Bravo" }];
        second.Paragraph.Alignment = NotesTextAlignment.Center;
        var third = NotesBlock.CreateParagraph();
        third.PlainText = "Charlie";
        third.Runs = [new NotesTextRun { Text = "Charlie" }];
        third.Paragraph.Alignment = NotesTextAlignment.Right;
        page.Blocks.AddRange([second, third]);
        var editor = new WriteDocumentEditor(document);
        var untouchedAlignment = third.Paragraph.Alignment;
        var changed = 0;
        editor.Changed += (_, _) => changed++;

        editor.SetDocumentCaret(first.Id, 2);
        editor.SetDocumentCaret(second.Id, 2, extendSelection: true);
        Assert.True(editor.IsAlignmentMixed);
        editor.SetAlignment(NotesTextAlignment.Justify);

        Assert.Equal(NotesTextAlignment.Justify, first.Paragraph.Alignment);
        Assert.Equal(NotesTextAlignment.Justify, second.Paragraph.Alignment);
        Assert.Equal(untouchedAlignment, third.Paragraph.Alignment);
        Assert.Equal(1, changed);
        Assert.False(editor.IsAlignmentMixed);
        Assert.True(editor.Undo());
        var restored = editor.TextBlocks();
        Assert.NotEqual(NotesTextAlignment.Justify, restored.Single(block => block.Id == first.Id).Paragraph.Alignment);
        Assert.NotEqual(NotesTextAlignment.Justify, restored.Single(block => block.Id == second.Id).Paragraph.Alignment);
        Assert.Equal(untouchedAlignment, restored.Single(block => block.Id == third.Id).Paragraph.Alignment);
        Assert.True(editor.IsAlignmentMixed);
        Assert.True(editor.HasDocumentSelection);
    }

    [Fact]
    public void Applying_a_paragraph_style_to_document_selection_updates_each_selected_block_atomically()
    {
        var document = NotesDocument.Create("Selected paragraph style");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.StyleId = "normal";
        first.PlainText = "Alpha";
        first.Runs = [new NotesTextRun { Text = "Alpha" }];
        var second = NotesBlock.CreateParagraph("Bravo");
        second.Order = 1;
        second.StyleId = "quote";
        var third = NotesBlock.CreateParagraph("Charlie");
        third.Order = 2;
        page.Blocks.AddRange([second, third]);
        var editor = new WriteDocumentEditor(document);
        editor.SetDocumentCaret(first.Id, 1);
        editor.SetDocumentCaret(second.Id, 3, extendSelection: true);

        editor.ApplyStyle("heading-1");

        Assert.Equal("heading-1", first.StyleId);
        Assert.Equal("heading-1", second.StyleId);
        Assert.Equal("normal", third.StyleId);
        Assert.True(editor.Undo());
        var restored = editor.TextBlocks();
        Assert.Equal("normal", restored.Single(block => block.Id == first.Id).StyleId);
        Assert.Equal("quote", restored.Single(block => block.Id == second.Id).StyleId);
        Assert.Equal("normal", restored.Single(block => block.Id == third.Id).StyleId);
        Assert.True(editor.HasDocumentSelection);
    }

    [Fact]
    public void Inserting_a_paragraph_moves_the_typed_editing_caret_to_the_new_block()
    {
        var document = NotesDocument.Create("Insert paragraph state");
        var original = document.Sections[0].Pages[0].Blocks[0];
        original.PlainText = "Original";
        original.Runs = [new NotesTextRun { Text = "Original" }];
        var editor = new WriteDocumentEditor(document);
        editor.SelectBlock(original.Id, caretIndex: 4, selectionStart: 2, selectionEnd: 4);

        var inserted = editor.InsertBlock(NotesBlockKind.Paragraph);

        Assert.Equal(new WriteDocumentPosition(inserted.Id, 0), editor.DocumentCaret);
        Assert.Equal(inserted.Id, editor.SelectedBlockId);
        Assert.False(editor.HasSelection);
        Assert.True(editor.InsertDocumentText("Draft"));
        Assert.Equal("Original", original.PlainText);
        Assert.Equal("Draft", inserted.PlainText);

        editor.SetDocumentCaret(original.Id, 1);
        editor.SetDocumentCaret(inserted.Id, 2, extendSelection: true);
        var table = editor.InsertTable(1, 1);
        Assert.Equal(table.Id, editor.SelectedBlockId);
        Assert.Equal(table.Table!.Rows[0].Cells[0].Id, editor.SelectedTableCellId);
        var firstCell = editor.SelectedTableCellId;
        editor.SelectBlock(table.Id);
        Assert.Equal(firstCell, editor.SelectedTableCellId);
        Assert.False(editor.HasDocumentSelection);
        Assert.False(editor.HasSelection);
        editor.SetDocumentCaret(original.Id, 1);
        Assert.Equal(original.Id, editor.SelectedBlockId);
        Assert.Null(editor.SelectedTableCellId);
    }

    [Fact]
    public void Line_spacing_selection_state_and_edit_cover_each_selected_paragraph_once()
    {
        var document = NotesDocument.Create("Mixed paragraph spacing");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.PlainText = "Alpha";
        first.Runs = [new NotesTextRun { Text = "Alpha" }];
        first.Paragraph.LineSpacing = 1;
        var second = NotesBlock.CreateParagraph("Bravo");
        second.Order = 1;
        second.Paragraph.LineSpacing = 1.5;
        var third = NotesBlock.CreateParagraph("Charlie");
        third.Order = 2;
        third.Paragraph.LineSpacing = 1;
        page.Blocks.AddRange([second, third]);
        var editor = new WriteDocumentEditor(document);
        editor.SetDocumentCaret(first.Id, 1);
        editor.SetDocumentCaret(second.Id, 2, extendSelection: true);

        Assert.True(editor.IsLineSpacingMixed);
        editor.SetLineSpacing(2);
        Assert.Equal(2, first.Paragraph.LineSpacing);
        Assert.Equal(2, second.Paragraph.LineSpacing);
        Assert.Equal(1, third.Paragraph.LineSpacing);
        Assert.False(editor.IsLineSpacingMixed);
        Assert.True(editor.Undo());
        Assert.True(editor.IsLineSpacingMixed);
        Assert.Equal(1.5, editor.TextBlocks().Single(block => block.Id == second.Id).Paragraph.LineSpacing);
        Assert.True(editor.HasDocumentSelection);
    }

    [Fact]
    public void Document_caret_movement_clears_a_stale_single_block_selection()
    {
        var document = NotesDocument.Create("Caret state transition");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "Alpha beta";
        block.Runs = [new NotesTextRun { Text = block.PlainText }];
        var editor = new WriteDocumentEditor(document);
        editor.SelectBlock(block.Id, caretIndex: 10, selectionStart: 6, selectionEnd: 10);
        Assert.True(editor.HasSelection);

        editor.SetDocumentCaret(block.Id, 2);

        Assert.False(editor.HasSelection);
        Assert.Equal(2, editor.CaretIndex);
        Assert.Equal(2, editor.SelectionStart);
        Assert.Equal(2, editor.SelectionEnd);
        Assert.Equal("Alpha beta", editor.SelectedText);
    }

    [Fact]
    public void Deleting_the_selected_text_block_moves_caret_to_surviving_replacement_and_history_restores_it()
    {
        var document = NotesDocument.Create("Delete selected block state");
        var page = document.Sections[0].Pages[0];
        var first = page.Blocks[0];
        first.PlainText = "Before";
        first.Runs = [new NotesTextRun { Text = "Before" }];
        var selected = NotesBlock.CreateParagraph("Remove me");
        selected.Order = 1;
        page.Blocks.Add(selected);
        var editor = new WriteDocumentEditor(document);
        editor.SelectBlock(selected.Id, 4);

        Assert.True(editor.DeleteSelected());
        Assert.Equal(first.Id, editor.SelectedBlockId);
        Assert.Equal(new WriteDocumentPosition(first.Id, "Before".Length), editor.DocumentCaret);
        Assert.False(editor.HasSelection);
        Assert.True(editor.InsertDocumentText("!"));
        Assert.Equal("Before!", first.PlainText);

        Assert.True(editor.Undo());
        Assert.Equal("Before", editor.TextBlocks().Single(block => block.Id == first.Id).PlainText);
        Assert.Equal(first.Id, editor.SelectedBlockId);
        Assert.True(editor.Undo());
        Assert.Equal(selected.Id, editor.SelectedBlockId);
        Assert.Equal(new WriteDocumentPosition(selected.Id, 4), editor.DocumentCaret);
        Assert.True(editor.Redo());
        Assert.Equal(first.Id, editor.SelectedBlockId);
        Assert.Equal(new WriteDocumentPosition(first.Id, "Before".Length), editor.DocumentCaret);
        Assert.True(editor.Redo());
        Assert.Equal("Before!", editor.TextBlocks().Single(block => block.Id == first.Id).PlainText);
    }
}
