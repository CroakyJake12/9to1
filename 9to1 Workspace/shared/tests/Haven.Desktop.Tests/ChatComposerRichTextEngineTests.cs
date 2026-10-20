using System;
using System.Threading.Tasks;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

/// <summary>
/// Tests for the shared non-visual composer rich-text engine: the canonical document, the
/// editor's reversible transactions, the Markdown projection round-trip, and the typed formatting
/// port that drives the real composer input surface.
/// </summary>
public sealed class ChatComposerRichTextEngineTests
{
    // ---------- Markdown projection round-trip ----------

    [Fact]
    public void Empty_document_serializes_to_empty_draft_and_parses_back()
    {
        var doc = ChatComposerRichTextDocument.Empty;
        Assert.Equal(string.Empty, ChatComposerMarkdown.SerializeDraft(doc));

        Assert.True(ChatComposerMarkdown.TryParseDraft(string.Empty, out var parsed, out _));
        Assert.Equal(string.Empty, parsed.Text);
        Assert.Equal(0, parsed.Length);
    }

    [Fact]
    public void Builder_toggle_produces_draft_delimiters_and_round_trips()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("hello world"));
        editor.ToggleInlineMark(0, 5, ChatComposerInlineMark.Bold);

        var draft = ChatComposerMarkdown.SerializeDraft(editor.Document);
        Assert.Equal("**hello** world", draft);

        // Round-trip: parse the draft back and compare to the source document.
        Assert.True(ChatComposerMarkdown.TryParseDraft(draft, out var reparsed, out _));
        Assert.True(ChatComposerRichTextDocument.DeepEquals(editor.Document, reparsed));
    }

    [Fact]
    public void Generated_drafts_round_trip_preserving_marks_blocks_and_links()
    {
        var cases = new[]
        {
            "plain",
            "**bold** and _italic_",
            "`code` with ~~strike~~",
            "[hello](url)",
            "> quote line",
            "- bullet line",
            "**bold _and italic_**",
            "mixed `code` **bold** _italic_",
        };
        foreach (var draft in cases)
        {
            Assert.True(ChatComposerMarkdown.TryParseDraft(draft, out var doc, out _), $"parse failed: {draft}");
            var redrafted = ChatComposerMarkdown.SerializeDraft(doc);
            Assert.True(ChatComposerMarkdown.TryParseDraft(redrafted, out var reparsed, out _), $"reparse failed: {draft}");
            Assert.True(
                ChatComposerRichTextDocument.DeepEquals(doc, reparsed),
                $"round-trip mismatch for [{draft}] -> [{redrafted}]");
        }
    }

    [Fact]
    public void Plain_projection_is_lossless_authoring_text()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("**bold** and _italic_"));
        // Parse strips delimiters, so authority text has none.
        Assert.Equal("bold and italic", editor.Document.Text);
        Assert.Equal(editor.Document.Text, ChatComposerMarkdown.SerializePlainText(editor.Document));
    }

    // ---------- Inline styles ----------

    [Fact]
    public void Toggle_bold_off_removes_mark_and_delimiters()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("**hello**"));
        // "hello" is bold (delimiters stripped by parse), so the text is "hello" with bold marks.
        Assert.Equal("hello", editor.Document.Text);
        Assert.Equal(ChatComposerInlineMark.Bold, editor.Document.Marks[0]);

        editor.ToggleInlineMark(0, 5, ChatComposerInlineMark.Bold);
        Assert.Equal(ChatComposerInlineMark.None, editor.Document.Marks[0]);
        Assert.Equal("hello", ChatComposerMarkdown.SerializeDraft(editor.Document));
    }

    [Fact]
    public void Successive_bold_then_italic_with_caret_movement()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("word"));
        // Bold "word".
        editor.ToggleInlineMark(0, 4, ChatComposerInlineMark.Bold);
        Assert.Equal("**word**", ChatComposerMarkdown.SerializeDraft(editor.Document));

        // Move the caret into the middle and italicize subsequent typing via typing style.
        editor.ToggleInlineMark(2, 2, ChatComposerInlineMark.Italic); // collapsed -> typing style
        Assert.Equal(ChatComposerInlineMark.Italic, editor.TypingStyle & ChatComposerInlineMark.Italic);

        // Type at the middle with the italic typing style.
        editor.TypeText(2, "X");
        Assert.Equal("woXrd", editor.Document.Text);
        Assert.Equal(ChatComposerInlineMark.Italic, editor.Document.Marks[2]);
    }

    [Fact]
    public void Backward_selection_is_normalized_like_forward()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("hello world"));
        // Reversed range (end before start) is normalized identically to forward.
        editor.ToggleInlineMark(11, 6, ChatComposerInlineMark.Bold);
        Assert.Equal("hello **world**", ChatComposerMarkdown.SerializeDraft(editor.Document));
    }

    [Fact]
    public void Link_toggle_wraps_with_placeholder_url_and_toggles_off()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("site"));
        editor.ToggleInlineMark(0, 4, ChatComposerInlineMark.Link);
        Assert.Equal("[site](url)", ChatComposerMarkdown.SerializeDraft(editor.Document));
        Assert.Equal("url", editor.Document.Links[0]);

        editor.ToggleInlineMark(0, 4, ChatComposerInlineMark.Link);
        Assert.Equal("site", ChatComposerMarkdown.SerializeDraft(editor.Document));
        Assert.Null(editor.Document.Links[0]);
    }

    [Fact]
    public void Strikethrough_path_is_supported()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("gone"));
        editor.ToggleInlineMark(0, 4, ChatComposerInlineMark.Strikethrough);
        Assert.Equal("~~gone~~", ChatComposerMarkdown.SerializeDraft(editor.Document));
    }

    // ---------- Block styles ----------

    [Fact]
    public void Quote_toggle_over_multiline_selection_prefixes_each_line()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("line one\nline two\nline three"));
        // Select lines 1-2 entirely (through end of document).
        int start = editor.Document.Text.IndexOf('\n') + 1;
        int end = editor.Document.Text.Length;
        editor.ToggleBlockKind(start, end, ChatComposerBlockKind.Quote);
        Assert.Equal("line one\n> line two\n> line three", editor.Document.Text);
    }

    [Fact]
    public void Bullet_toggle_converts_and_toggles_back()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("item"));
        editor.ToggleBlockKind(0, 4, ChatComposerBlockKind.BulletItem);
        Assert.Equal("- item", editor.Document.Text);
        Assert.Equal(ChatComposerBlockKind.BulletItem, editor.Document.LineKindAt(2));

        editor.ToggleBlockKind(0, 4, ChatComposerBlockKind.BulletItem);
        Assert.Equal("item", editor.Document.Text);
        Assert.Equal(ChatComposerBlockKind.Paragraph, editor.Document.LineKindAt(0));
    }

    [Fact]
    public void Bullet_toggle_converts_quote_to_bullet()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("> quote"));
        editor.ToggleBlockKind(0, editor.Document.Length, ChatComposerBlockKind.BulletItem);
        Assert.Equal("- quote", editor.Document.Text);
    }

    [Fact]
    public void Applying_same_block_kind_again_is_a_noop()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("- item"));
        long before = editor.Revision;
        editor.ToggleBlockKind(0, editor.Document.Length, ChatComposerBlockKind.BulletItem);
        Assert.Equal(before, editor.Revision);
        Assert.Equal("- item", editor.Document.Text);
    }

    [Fact]
    public void Collapsed_caret_block_command_acts_on_the_whole_line()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("alpha\nbeta\n"));
        // Collapsed caret in the second line -> quote toggles the line.
        editor.ToggleBlockKind(6, 6, ChatComposerBlockKind.Quote);
        Assert.Equal("alpha\n> beta", editor.Document.Text.TrimEnd());
    }

    // ---------- Replace selection / typing ----------

    [Fact]
    public void Replace_selection_replaces_selected_range_with_typing_style()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("hello world"));
        editor.TypingStyle = ChatComposerInlineMark.Bold;
        editor.ReplaceSelection(6, 11, "there");
        Assert.Equal("hello there", editor.Document.Text);
        Assert.Equal(ChatComposerInlineMark.Bold, editor.Document.Marks[6]);
    }

    // ---------- Undo / redo transaction identity ----------

    [Fact]
    public void Undo_redo_preserve_transaction_identity_and_states()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("abc"));

        long before = editor.Revision;
        var t1 = editor.ToggleInlineMark(0, 1, ChatComposerInlineMark.Bold);
        Assert.True(t1.Id > before);
        Assert.Equal("**a**bc", ChatComposerMarkdown.SerializeDraft(editor.Document));

        var t2 = editor.ToggleInlineMark(1, 2, ChatComposerInlineMark.Italic);
        Assert.True(t2.Id > t1.Id);

        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);

        long revAfterTwo = editor.Revision;
        editor.Undo();
        Assert.Equal(revAfterTwo + 1, editor.Revision);
        Assert.Equal("**a**bc", ChatComposerMarkdown.SerializeDraft(editor.Document));
        Assert.True(editor.CanRedo);

        editor.Redo();
        Assert.Equal(revAfterTwo + 2, editor.Revision);
        Assert.Equal("**a**_b_c", ChatComposerMarkdown.SerializeDraft(editor.Document));
    }

    [Fact]
    public void Noop_toggle_does_not_record_a_transaction()
    {
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse("abc"));
        long before = editor.Revision;
        editor.ToggleInlineMark(0, 0, ChatComposerInlineMark.Bold); // collapsed -> style only
        Assert.Equal(before, editor.Revision);
        Assert.False(editor.CanUndo);
    }

    // ---------- Emoji / combining text boundaries ----------

    [Fact]
    public void Surrogate_pair_and_combining_sequences_survive_formatting()
    {
        string emoji = "\U0001F600"; // 😀 (surrogate pair)
        var editor = new ChatComposerEditor();
        editor.SetContent(Parse(emoji + " hi"));

        // Select the whole string and bold it; surrogate pair must not be split/truncated.
        editor.ToggleInlineMark(0, editor.Document.Length, ChatComposerInlineMark.Bold);
        Assert.Contains(emoji, editor.Document.Text);
        Assert.Equal(emoji.Length + 3, editor.Document.Length); // 2 + space + 2 hi chars

        // Combining sequence: 'e' + U+0301 combining acute.
        var editor2 = new ChatComposerEditor();
        editor2.SetContent(Parse("e\u0301"));
        editor2.ToggleInlineMark(0, editor2.Document.Length, ChatComposerInlineMark.Bold);
        Assert.Equal("e\u0301", editor2.Document.Text);
    }

    // ---------- Port (typed adapter) driven through a fake input port ----------

    [Fact]
    public void Port_applies_bold_to_live_selection_and_selects_the_formatted_region()
    {
        var input = new FakeChatComposerInputPort { Text = "hello world" };
        input.SetSelection(6, 11);
        var port = new ChatComposerFormattingPort(() => input);

        port.ApplyAsync("Bold").AsTask().GetAwaiter().GetResult();

        Assert.Equal("hello **world**", input.Text);
        // The formatted region (the bolded "world") is re-selected in the new draft.
        Assert.Equal(8, input.SelectionStart);
        Assert.Equal(5, input.SelectionLength);
    }

    [Fact]
    public void Port_strikethrough_and_code_via_live_input()
    {
        var input = new FakeChatComposerInputPort { Text = "todo" };
        input.SetSelection(0, 4);
        var port = new ChatComposerFormattingPort(() => input);

        port.ApplyAsync("Strikethrough").AsTask().GetAwaiter().GetResult();
        Assert.Equal("~~todo~~", input.Text);

        input.Text = "x = 1";
        input.SetSelection(0, 6);
        port.ApplyAsync("Code").AsTask().GetAwaiter().GetResult();
        Assert.Equal("`x = 1`", input.Text);
    }

    [Fact]
    public void Port_collapsed_caret_bold_sets_typing_style_without_rewrite()
    {
        var input = new FakeChatComposerInputPort { Text = "abc" };
        input.SetSelection(1, 1); // collapsed caret
        var port = new ChatComposerFormattingPort(() => input);

        port.ApplyAsync("Bold").AsTask().GetAwaiter().GetResult();

        // No visible text change; typing style is set for subsequent typing.
        Assert.Equal("abc", input.Text);
        Assert.Equal(ChatComposerInlineMark.Bold, port.Editor.TypingStyle & ChatComposerInlineMark.Bold);
    }

    [Fact]
    public void Port_quote_toggles_line_from_collapsed_caret()
    {
        var input = new FakeChatComposerInputPort { Text = "alpha beta" };
        input.SetSelection(2, 2);
        var port = new ChatComposerFormattingPort(() => input);

        port.ApplyAsync("Quote").AsTask().GetAwaiter().GetResult();
        Assert.Equal("> alpha beta", input.Text);
    }

    [Fact]
    public void Port_emoji_insert_replaces_selection_and_keeps_focus()
    {
        var input = new FakeChatComposerInputPort { Text = "hello" };
        input.SetSelection(0, 5);
        var port = new ChatComposerFormattingPort(() => input);

        port.InsertEmojiAsync("\U0001F600").AsTask().GetAwaiter().GetResult();

        Assert.Equal("\U0001F600", input.Text);
        Assert.Equal(2, input.SelectionStart);
        Assert.Equal(0, input.SelectionLength);
        Assert.True(input.IsFocused);
    }

    [Fact]
    public void Port_fallback_draft_uses_admitted_setter_task()
    {
        string? setDraft = null;
        var admitted = new ValueTask();
        var port = new ChatComposerFormattingPort(
            liveInputProvider: () => null,
            fallbackDraft: () => "hello",
            fallbackSetDraft: (value, ct) => { setDraft = value; return admitted; });

        port.ApplyAsync("Bold").AsTask().GetAwaiter().GetResult();

        Assert.Equal("**hello**", setDraft);
    }

    [Fact]
    public void Port_unknown_command_fails_honestly()
    {
        var input = new FakeChatComposerInputPort { Text = "abc" };
        var port = new ChatComposerFormattingPort(() => input);
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            port.ApplyAsync("Underline").AsTask().GetAwaiter().GetResult());
        Assert.Contains("Underline", ex.Message);
    }

    private static ChatComposerRichTextDocument Parse(string draft)
    {
        Assert.True(ChatComposerMarkdown.TryParseDraft(draft, out var doc, out _));
        return doc;
    }

    private sealed class FakeChatComposerInputPort : IChatComposerInputPort
    {
        public string Text { get; set; } = string.Empty;
        public int CaretIndex => SelectionStart;
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        public string SelectedText =>
            SelectionLength > 0 ? Text.Substring(SelectionStart, SelectionLength) : string.Empty;
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
