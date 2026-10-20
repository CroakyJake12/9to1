using System;
using System.Threading.Tasks;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

/// <summary>
/// Causal tests for the shared real rich-input engine and its typed port. These exercise the
/// single document/transaction owner directly (the path a bound renderer drives), plus the port's
/// engine routing. Real native/headless event integration is covered by the patch-registered tests
/// that mount the actual authored CUI composer once the renderer patch is integrated by root.
/// </summary>
public sealed class ChatComposerRichInputEngineTests
{
    // ---------- Native typing and single-history undo ----------

    [Fact]
    public void Native_typing_and_format_share_one_undo_history()
    {
        var engine = new ChatComposerRichInputEngine();
        long baseRev = engine.Revision;

        // Type "ab".
        engine.TypeText(0, "ab");
        Assert.Equal("ab", engine.Document.Text);

        // Bold the first 'a'.
        engine.ApplyCommand("Bold", 0, 1);
        Assert.Equal("**a**b", engine.SerializeDraft());
        Assert.Equal(ChatComposerInlineMark.Bold, engine.Document.Marks[0]);

        // Type "c" at the end (a real insertion after formatting).
        engine.TypeText(2, "c");
        Assert.Equal("abc", engine.Document.Text);

        // Undo must unwind the typing first, then the format, then the original typing, and each
        // undo returns the exact document/format state of that point in the shared history.
        Assert.True(engine.Editor.CanUndo);
        engine.Undo(); // -> state after bold formatting
        Assert.Equal("ab", engine.Document.Text);
        Assert.Equal("**a**b", engine.SerializeDraft());
        engine.Undo(); // -> state after first typing
        Assert.Equal("ab", engine.Document.Text);
        Assert.Equal("ab", engine.SerializeDraft());
        engine.Undo(); // -> empty
        Assert.Equal(string.Empty, engine.Document.Text);
        Assert.Equal(baseRev + 3 + 3, engine.Revision);
        Assert.False(engine.Editor.CanUndo);
    }

    [Fact]
    public void TypeText_records_atomic_transaction_with_identity()
    {
        var engine = new ChatComposerRichInputEngine();
        var tx = engine.TypeText(0, "hi");
        Assert.NotEqual(0L, tx.Id);
        engine.TypeText(2, "!");
        engine.Undo();
        Assert.Equal("hi", engine.Document.Text);
    }

    [Fact]
    public void Collapsed_bold_sets_typing_style_so_next_real_insert_inherits_it()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.TypeText(0, "ab");

        // Collapsed caret between a and b -> set Bold typing style.
        engine.ApplyCommand("Bold", 1, 1);
        Assert.Equal(ChatComposerInlineMark.Bold, engine.TypingStyle & ChatComposerInlineMark.Bold);
        // Document is unchanged (no content rewrite).
        Assert.Equal("ab", engine.Document.Text);
        Assert.Equal("ab", engine.SerializeDraft());

        // The next REAL insertion inherits the mark.
        engine.TypeText(1, "X");
        Assert.Equal("aXb", engine.Document.Text);
        Assert.Equal(ChatComposerInlineMark.Bold, engine.Document.Marks[1]);

        // Then italic at the new caret: style composes.
        engine.ApplyCommand("Italic", 2, 2);
        engine.TypeText(2, "Y");
        Assert.Equal(ChatComposerInlineMark.Bold, engine.Document.Marks[2] & ChatComposerInlineMark.Bold);
        Assert.Equal(ChatComposerInlineMark.Italic, engine.Document.Marks[2] & ChatComposerInlineMark.Italic);
    }

    [Fact]
    public void Consecutive_styles_compose_without_losing_caret_identity()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.TypeText(0, "word");
        engine.ApplyCommand("Bold", 0, 4);
        Assert.Equal("**word**", engine.SerializeDraft());
        // Move to mid-word (collapsed) and italicize typing.
        engine.ApplyCommand("Italic", 2, 2);
        engine.TypeText(2, "|");
        Assert.Equal("wo|rd", engine.Document.Text);
        Assert.Equal(ChatComposerInlineMark.Bold, engine.Document.Marks[2] & ChatComposerInlineMark.Bold);
        Assert.Equal(ChatComposerInlineMark.Italic, engine.Document.Marks[2] & ChatComposerInlineMark.Italic);
    }

    // ---------- Selection direction ----------

    [Fact]
    public void Backward_selection_is_identical_to_forward()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.TypeText(0, "hello world");
        engine.ApplyCommand("Bold", 11, 6); // reversed
        Assert.Equal("hello **world**", engine.SerializeDraft());
        Assert.Equal(ChatComposerInlineMark.Bold, engine.Document.Marks[6]);
    }

    // ---------- Grapheme / surrogate / combining boundaries ----------

    [Fact]
    public void Backspace_deletes_one_grapheme_cluster_not_utf16_char()
    {
        var engine = new ChatComposerRichInputEngine();
        string emoji = "\U0001F600"; // surrogate pair
        engine.TypeText(0, emoji + "a");
        engine.Backspace(3, 3); // caret at end, before 'a'
        Assert.Equal(emoji, engine.Document.Text);
    }

    [Fact]
    public void Combining_sequence_is_treated_as_one_grapheme()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.TypeText(0, "e\u0301x");
        engine.Backspace(3, 3); // delete 'x'
        Assert.Equal("e\u0301", engine.Document.Text);
    }

    // ---------- Block continuation ----------

    [Fact]
    public void Enter_inside_bullet_continues_the_marker()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.ApplyCommand("Bullet", 0, 0); // empty doc, collapsed -> line bullet
        Assert.Equal("- ", engine.Document.Text);
        engine.TypeText(2, "item");
        engine.TypeText(6, "\n"); // newline at end of bullet line
        // The new line should be continued with "- ".
        Assert.StartsWith("- item", engine.Document.Text);
        Assert.Contains("\n- ", engine.Document.Text);
    }

    [Fact]
    public void Enter_block_continuation_is_one_atomic_undo_transaction()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.ApplyCommand("Bullet", 0, 0); // "- "
        engine.TypeText(2, "item");          // "- item"
        // One native Enter must be ONE transaction: text newline + continued marker. Undo once
        // must restore exactly "- item" (marker NOT left behind as a second transaction).
        var tx = engine.TypeText(6, "\n");
        Assert.NotEqual(0L, tx.Id);
        Assert.Equal("- item\n- ", engine.Document.Text);
        engine.Undo();
        Assert.Equal("- item", engine.Document.Text);
        // One more Undo restores the pre-typing bullet marker state.
        engine.Undo();
        Assert.Equal("- ", engine.Document.Text);
        Assert.False(engine.Editor.CanUndo);
    }

    [Fact]
    public void Enter_inside_quote_continues_the_marker()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.TypeText(0, "line");
        engine.ApplyCommand("Quote", 0, 4);
        Assert.Equal("> line", engine.Document.Text);
        // Enter at the end of the quoted line: the new line inherits the quote marker.
        engine.TypeText(6, "\n");
        Assert.Equal("> line\n> ", engine.Document.Text);
        engine.TypeText(9, "next");
        Assert.Equal("> line\n> next", engine.Document.Text);
    }

    // ---------- Plain / Markdown serialization round trip incl escaping ----------

    [Fact]
    public void Literal_delimiter_text_survives_round_trip_via_escaping()
    {
        var engine = new ChatComposerRichInputEngine();
        // Plain typing of literal markers; must remain literal (not become formatting).
        engine.TypeText(0, "a * b _ c ` d [ e");
        var draft = engine.SerializeDraft();
        Assert.True(draft.Contains('\\'));
        Assert.True(ChatComposerMarkdown.TryParseDraft(draft, out var reparsed, out _));
        Assert.Equal(engine.Document.Text, reparsed.Text);
        Assert.True(ChatComposerRichTextDocument.DeepEquals(engine.Document, reparsed));
    }

    [Fact]
    public void Plain_serialization_round_trip_is_equal()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.TypeText(0, "hello world");
        engine.ApplyCommand("Bold", 0, 5);
        var plain = engine.SerializePlainText();
        Assert.Equal("hello world", plain);
    }

    // ---------- Port engine routing (no fake-input-only proof) ----------

    [Fact]
    public void Port_routes_to_engine_when_bound_and_does_not_reparse()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.TypeText(0, "hello world");
        var input = new SimplePort { Text = engine.SerializeDraft() };
        input.SetSelection(6, 11);
        var port = new ChatComposerFormattingPort(liveInputProvider: () => input, richEngine: engine);

        port.ApplyAsync("Bold").AsTask().GetAwaiter().GetResult();

        Assert.Equal(ChatComposerInlineMark.Bold, engine.Document.Marks[6]);
        Assert.Equal("hello **world**", engine.SerializeDraft());
        // The engine-bound path does NOT rewrite the live plain text; the renderer repaints from
        // the engine's Changed event. The input text staying unchanged proves no fake Markdown
        // rewrite happened.
        Assert.Equal("hello world", input.Text);
        // The engine history is the authority (one history, two transactions: type + bold).
        Assert.Equal(2L, engine.Revision);
    }

    [Fact]
    public void Port_undo_uses_engine_history_when_bound()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.TypeText(0, "ab");
        engine.ApplyCommand("Bold", 0, 1);
        var port = new ChatComposerFormattingPort(richEngine: engine);

        port.Undo();
        Assert.Equal("ab", engine.Document.Text);
        port.Undo();
        Assert.Equal(string.Empty, engine.Document.Text);
        port.Redo();
        Assert.Equal("ab", engine.Document.Text);
    }

    // ---------- Lifecycle / generation fence ----------

    [Fact]
    public void Disposed_engine_refuses_edits()
    {
        var engine = new ChatComposerRichInputEngine();
        engine.Dispose();
        Assert.True(engine.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => engine.TypeText(0, "x"));
    }

    [Fact]
    public void Generation_advances_on_each_commit()
    {
        var engine = new ChatComposerRichInputEngine();
        long g0 = engine.Generation;
        long seen = -1;
        engine.Changed += g => seen = g;
        engine.TypeText(0, "a");
        Assert.Equal(g0 + 1, engine.Generation);
        Assert.Equal(engine.Generation, seen);
    }

    private sealed class SimplePort : IChatComposerInputPort
    {
        public string Text { get; set; } = string.Empty;
        public int CaretIndex => SelectionStart;
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        public string SelectedText => SelectionLength > 0 ? Text.Substring(SelectionStart, SelectionLength) : string.Empty;
        public bool IsFocused { get; set; }
        public event EventHandler? TextChanged;
        public event EventHandler? Invalidated;
        public void PlaceCaretAtEnd(bool extendSelection = false)
        {
            int len = Text.Length;
            SelectionStart = len;
            SelectionLength = extendSelection ? len : 0;
        }
        public void SetSelection(int anchor, int caret)
        {
            int s = Math.Min(anchor, caret);
            int e = Math.Max(anchor, caret);
            SelectionStart = s;
            SelectionLength = e - s;
        }
        public void ClearSelection() => SelectionLength = 0;
        public void Focus() => IsFocused = true;
    }
}
