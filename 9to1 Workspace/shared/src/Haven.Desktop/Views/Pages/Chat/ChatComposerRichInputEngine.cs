using System;
using System.Globalization;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// The shared REAL rich-input engine. It is the single document/transaction owner for a bound
/// composer: native typing, composition replacement, paste, formatting commands, block toggles
/// and undo/redo all flow through this one engine and are recorded in ONE history (see
/// <see cref="ChatComposerEditor"/>). This is the causal fix over the previous model-only port
/// that re-parsed the plain draft per action and kept a disconnected history.
///
/// The engine is presentation-independent: it exposes semantic operations and a <see cref="Changed"/>
/// event. The generic CUI renderer (delivered as a root-applicable patch) drives these operations
/// from real native/headless text, selection and keyboard events and repaints styled runs from
/// <see cref="ChatComposerRichTextDocument"/>.
/// </summary>
public sealed class ChatComposerRichInputEngine : IDisposable
{
    private readonly ChatComposerEditor _editor;
    private bool _disposed;
    private long _generation;

    public ChatComposerRichInputEngine(ChatComposerRichTextDocument? document = null)
    {
        _editor = new ChatComposerEditor(document);
    }

    /// <summary>The retained editor (one history shared by typing, paste and formatting).</summary>
    public ChatComposerEditor Editor => _editor;

    /// <summary>The authoritative document.</summary>
    public ChatComposerRichTextDocument Document => _editor.Document;

    /// <summary>Monotonic revision; also the transaction identity base.</summary>
    public long Revision => _editor.Revision;

    /// <summary>Generation fence: increments on every committed transaction/undo/redo. Hosts may use
    /// this to coarsen repaints or to detect edits made while a deferred operation was in flight.</summary>
    public long Generation => _generation;

    /// <summary>Raised after any committed change (native or command) with the new generation.</summary>
    public event Action<long>? Changed;

    public event EventHandler? Disposed;

    public bool IsDisposed => _disposed;

    /// <summary>The marks newly typed text at a collapsed caret should inherit.</summary>
    public ChatComposerInlineMark TypingStyle
    {
        get => _editor.TypingStyle;
        set => _editor.TypingStyle = value;
    }

    // ---------- Native editing operations (all recorded in the single history) ----------

    /// <summary>Types text at a caret. If the insertion contains a newline while the caret sits at
    /// the end of a quoted/bulleted line, the block marker is continued onto the new line.</summary>
    public ChatComposerEditTransaction TypeText(int caret, string text)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(text)) return default;
        caret = Clamp(caret, Document.Length);

        var tx = _editor.TypeText(caret, text);
        // Block continuation: if we inserted a newline and the preceding line was a quote/bullet,
        // append the matching marker to the freshly started line.
        if (text.IndexOf('\n') >= 0 && tx.Id != 0)
        {
            ContinueBlockAfterNewline(caret, text);
        }
        Publish();
        return tx;
    }

    /// <summary>Replaces the selected [anchor, caret) with text (or inserts at a collapsed caret).</summary>
    public ChatComposerEditTransaction ReplaceSelection(int anchor, int caret, string text)
    {
        ThrowIfDisposed();
        var (start, end) = Normalize(anchor, caret);
        if (string.IsNullOrEmpty(text))
        {
            if (end <= start) return default;
            var dtx = _editor.DeleteRange(start, end);
            Publish();
            return dtx;
        }
        var tx = _editor.ReplaceRange(start, end, text);
        if (text.IndexOf('\n') >= 0 && tx.Id != 0)
        {
            ContinueBlockAfterNewline(start, text);
        }
        Publish();
        return tx;
    }

    /// <summary>Backspace at the selection/caret, deleting one grapheme cluster when collapsed.</summary>
    public ChatComposerEditTransaction Backspace(int anchor, int caret)
    {
        ThrowIfDisposed();
        var (start, end) = Normalize(anchor, caret);
        if (end > start)
        {
            var tx = _editor.DeleteRange(start, end);
            Publish();
            return tx;
        }
        if (start <= 0) return default;
        int prev = PreviousGraphemeBoundary(Document.Text, start);
        var dtx = _editor.DeleteRange(prev, start);
        Publish();
        return dtx;
    }

    /// <summary>Delete at the selection/caret, deleting one grapheme cluster when collapsed.</summary>
    public ChatComposerEditTransaction Delete(int anchor, int caret)
    {
        ThrowIfDisposed();
        var (start, end) = Normalize(anchor, caret);
        if (end > start)
        {
            var tx = _editor.DeleteRange(start, end);
            Publish();
            return tx;
        }
        if (start >= Document.Length) return default;
        int next = NextGraphemeBoundary(Document.Text, start);
        var dtx = _editor.DeleteRange(start, next);
        Publish();
        return dtx;
    }

    // ---------- Formatting commands (same shared document/history) ----------

    /// <summary>Applies a named format command to the selection [anchor, caret). A collapsed caret
    /// toggles the typing style so the NEXT real insertion inherits it.</summary>
    public ChatComposerEditTransaction ApplyCommand(string formatCommand, int anchor, int caret, string? linkUrl = null)
    {
        ThrowIfDisposed();
        var op = ParseCommand(formatCommand);
        var (start, end) = Normalize(anchor, caret);
        bool collapsed = end <= start;

        var blockKind = BlockKindFor(op);
        if (blockKind != ChatComposerBlockKind.Paragraph)
        {
            if (collapsed)
            {
                var (ls, le) = Document.LineBounds(start);
                start = ls;
                end = le;
                collapsed = false;
            }
            // Empty line: insert the marker directly so a block can be started from an empty caret.
            if (end <= start)
            {
                string marker = ChatComposerRichTextDocument.MarkerFor(blockKind);
                var tx = _editor.RecordMutation("block", d => d.Insert(start, marker, ChatComposerInlineMark.None, null));
                Publish();
                return tx;
            }
            var btx = _editor.ToggleBlockKind(start, end, blockKind);
            Publish();
            return btx;
        }

        var mark = ToInlineMark(op);
        if (collapsed)
        {
            if ((TypingStyle & mark) != 0) TypingStyle &= ~mark;
            else TypingStyle |= mark;
            // No content change; still bump generation so renderers can reflect the new style.
            _generation++;
            Changed?.Invoke(_generation);
            return default;
        }

        var itx = _editor.ToggleInlineMark(start, end, mark, linkUrl);
        Publish();
        return itx;
    }

    // ---------- Undo / redo (single shared history) ----------

    public long Undo()
    {
        ThrowIfDisposed();
        var rev = _editor.Undo();
        Publish();
        return rev;
    }

    public long Redo()
    {
        ThrowIfDisposed();
        var rev = _editor.Redo();
        Publish();
        return rev;
    }

    // ---------- Document snapshot / interchange ----------

    /// <summary>Replaces the whole document (used by hosts when a draft is imported or restored) and
    /// keeps history intact (restoring is not itself a recorded transaction).</summary>
    public void SetContent(ChatComposerRichTextDocument document)
    {
        ThrowIfDisposed();
        _editor.SetContent(document);
        _generation++;
        Changed?.Invoke(_generation);
    }

    public string SerializeDraft() => ChatComposerMarkdown.SerializeDraft(Document);
    public string SerializePlainText() => ChatComposerMarkdown.SerializePlainText(Document);

    // ---------- Helpers ----------

    private void ContinueBlockAfterNewline(int insertionStart, string text)
    {
        // insertionStart is the boot where text was inserted; find the last newline in text.
        int lastNl = text.LastIndexOf('\n');
        if (lastNl < 0) return;
        // The new line starts right after that newline within the inserted text.
        int anchor = insertionStart + lastNl + 1;
        var (ls, le) = Document.LineBounds(anchor);
        // LineBounds(anchor) returns [start,end) of the line containing anchor; if anchor == start
        // this is the line that begins at the content pos. Determine the previous line's kind.
        if (ls > 0)
        {
            var (pls, ple) = Document.LineBounds(ls - 1);
            var kind = Document.LineKind(pls, ple);
            if (kind == ChatComposerBlockKind.Quote || kind == ChatComposerBlockKind.BulletItem)
            {
                string marker = kind == ChatComposerBlockKind.Quote ? "> " : "- ";
                // Only continue if the new line doesn't already carry a marker.
                if (le - ls < marker.Length || Document.Text.Substring(ls, marker.Length) != marker)
                    _editor.RecordMutation("block-continue", d => d.Insert(ls, marker, ChatComposerInlineMark.None, null));
            }
        }
    }

    private void Publish()
    {
        _generation++;
        Changed?.Invoke(_generation);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChatComposerRichInputEngine));
    }

    private static (int Start, int End) Normalize(int anchor, int caret)
    {
        if (caret < anchor) (anchor, caret) = (caret, anchor);
        return (anchor, caret);
    }

    private static int Clamp(int value, int length) => Math.Clamp(value, 0, length);

    /// <summary>Returns the grapheme-cluster boundary before <paramref name="index"/> (the start of
    /// the cluster containing the char just before index), using the same <c>StringInfo</c> rules
    /// the platform input control uses.</summary>
    internal static int PreviousGraphemeBoundary(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index <= 0) return 0;
        int prev = 0;
        foreach (var b in StringInfo.ParseCombiningCharacters(text))
        {
            if (b >= index) break;
            prev = b;
        }
        return prev;
    }

    internal static int NextGraphemeBoundary(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        foreach (var b in StringInfo.ParseCombiningCharacters(text))
            if (b > index) return b;
        return text.Length;
    }

    internal static ChatComposerFormatOp ParseCommand(string command)
    {
        return command?.Trim() switch
        {
            "Bold" => ChatComposerFormatOp.Bold,
            "Italic" => ChatComposerFormatOp.Italic,
            "Code" => ChatComposerFormatOp.Code,
            "Link" => ChatComposerFormatOp.Link,
            "Quote" => ChatComposerFormatOp.Quote,
            "Bullet" => ChatComposerFormatOp.Bullet,
            "Strikethrough" => ChatComposerFormatOp.Strike,
            "Underline" => ChatComposerFormatOp.Underline,
            "Superscript" => ChatComposerFormatOp.Superscript,
            "Subscript" => ChatComposerFormatOp.Subscript,
            "Latex" => ChatComposerFormatOp.Latex,
            "Heading1" => ChatComposerFormatOp.Heading1,
            "Heading2" => ChatComposerFormatOp.Heading2,
            "Heading3" => ChatComposerFormatOp.Heading3,
            "Heading4" => ChatComposerFormatOp.Heading4,
            "Heading5" => ChatComposerFormatOp.Heading5,
            "Heading6" => ChatComposerFormatOp.Heading6,
            "CodeBlock" => ChatComposerFormatOp.CodeBlock,
            "NumberedList" or "OrderedList" => ChatComposerFormatOp.OrderedList,
            "LetteredList" => ChatComposerFormatOp.LetteredList,
            _ => throw new InvalidOperationException($"Unknown composer format command '{command}'."),
        };
    }

    internal static ChatComposerInlineMark ToInlineMark(ChatComposerFormatOp op) => op switch
    {
        ChatComposerFormatOp.Bold => ChatComposerInlineMark.Bold,
        ChatComposerFormatOp.Italic => ChatComposerInlineMark.Italic,
        ChatComposerFormatOp.Code => ChatComposerInlineMark.Code,
        ChatComposerFormatOp.Link => ChatComposerInlineMark.Link,
        ChatComposerFormatOp.Strike => ChatComposerInlineMark.Strikethrough,
        ChatComposerFormatOp.Underline => ChatComposerInlineMark.Underline,
        ChatComposerFormatOp.Superscript => ChatComposerInlineMark.Superscript,
        ChatComposerFormatOp.Subscript => ChatComposerInlineMark.Subscript,
        ChatComposerFormatOp.Latex => ChatComposerInlineMark.Latex,
        _ => ChatComposerInlineMark.None,
    };

    internal static ChatComposerBlockKind BlockKindFor(ChatComposerFormatOp op) => op switch
    {
        ChatComposerFormatOp.Quote => ChatComposerBlockKind.Quote,
        ChatComposerFormatOp.Bullet => ChatComposerBlockKind.BulletItem,
        ChatComposerFormatOp.Heading1 => ChatComposerBlockKind.Heading1,
        ChatComposerFormatOp.Heading2 => ChatComposerBlockKind.Heading2,
        ChatComposerFormatOp.Heading3 => ChatComposerBlockKind.Heading3,
        ChatComposerFormatOp.Heading4 => ChatComposerBlockKind.Heading4,
        ChatComposerFormatOp.Heading5 => ChatComposerBlockKind.Heading5,
        ChatComposerFormatOp.Heading6 => ChatComposerBlockKind.Heading6,
        ChatComposerFormatOp.CodeBlock => ChatComposerBlockKind.CodeBlock,
        ChatComposerFormatOp.OrderedList => ChatComposerBlockKind.OrderedList,
        ChatComposerFormatOp.LetteredList => ChatComposerBlockKind.LetteredList,
        _ => ChatComposerBlockKind.Paragraph,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
