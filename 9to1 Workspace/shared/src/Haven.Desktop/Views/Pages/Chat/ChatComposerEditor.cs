using System;
using System.Collections.Generic;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Stable identity of one recorded edit transaction. <see cref="Id"/> is a monotonic
/// revision shared with <see cref="ChatComposerEditor.Revision"/>; the same <c>Id</c> never
/// denotes two different content changes.</summary>
public readonly record struct ChatComposerEditTransaction(long Id, string? Kind);

/// <summary>
/// The editor layer over <see cref="ChatComposerRichTextDocument"/>. It makes every mutation a
/// reversible transaction, records an undo/redo history and carries the no-selection typing style
/// (the marks that newly typed text at the caret inherits). The document state is immutable once a
/// step returns; each mutation builds the next state from the previous one, so the undo stack is a
/// simple chain of prior states.
/// </summary>
public sealed class ChatComposerEditor
{
    private readonly Stack<ChatComposerRichTextDocument> _undo = new();
    private readonly Stack<ChatComposerRichTextDocument> _redo = new();
    private long _revision;

    public ChatComposerEditor(ChatComposerRichTextDocument? document = null)
    {
        Document = document?.Clone() ?? new ChatComposerRichTextDocument();
    }

    /// <summary>The current document (the authoritative rich-text state).</summary>
    public ChatComposerRichTextDocument Document { get; private set; }

    /// <summary>Monotonic revision; increments on every recorded (content-changing) transaction,
    /// undo and redo. Used as the transaction identity.</summary>
    public long Revision => _revision;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The marks applied to text typed at a collapsed caret (no selection). Toggling an
    /// inline style at a collapsed caret updates this so subsequent typing carries the style until
    /// it is toggled off again.</summary>
    public ChatComposerInlineMark TypingStyle { get; set; }

    /// <summary>Installs a document as the current state without recording a transaction. Used to
    /// synchronize after parsing an externally edited draft; the undo/redo history is retained so
    /// earlier format transactions remain reversible.</summary>
    internal void SetContent(ChatComposerRichTextDocument document)
    {
        Document = document?.Clone() ?? ChatComposerRichTextDocument.Empty;
    }

    /// <summary>Applies a mutation; records a transaction only when content actually changed.
    /// Returns the transaction identity (or default when nothing changed).</summary>
    private ChatComposerEditTransaction Mutate(string kind, Action<ChatComposerRichTextDocument> change)
    {
        var before = Document.Clone();
        change(Document);
        if (ChatComposerRichTextDocument.DeepEquals(before, Document))
        {
            // No observable change; do not record an empty transaction.
            return default;
        }
        _undo.Push(before);
        _redo.Clear();
        _revision++;
        return new ChatComposerEditTransaction(_revision, kind);
    }

    /// <summary>Toggles an inline mark over [start, end). A collapsed range toggles
    /// <see cref="TypingStyle"/> instead of editing content.</summary>
    public ChatComposerEditTransaction ToggleInlineMark(int start, int end, ChatComposerInlineMark mark, string? linkUrl = null)
    {
        if (end <= start)
        {
            // Collapsed caret: typing-style toggle.
            if ((TypingStyle & mark) != 0) TypingStyle &= ~mark;
            else TypingStyle |= mark;
            return default;
        }
        return Mutate("inline", d => d.ToggleInlineMark(start, end, mark, linkUrl));
    }

    /// <summary>Toggles a block kind over every line overlapping [start, end).</summary>
    public ChatComposerEditTransaction ToggleBlockKind(int start, int end, ChatComposerBlockKind kind)
    {
        return Mutate("block", d => d.ToggleBlockKind(start, end, kind));
    }

    /// <summary>Types text at a position using the current <see cref="TypingStyle"/>.</summary>
    public ChatComposerEditTransaction TypeText(int position, string text)
    {
        if (string.IsNullOrEmpty(text)) return default;
        return Mutate("type", d => d.Insert(position, text, TypingStyle, null));
    }

    /// <summary>Replaces [start, end) with typed text using the current typing style.</summary>
    public ChatComposerEditTransaction ReplaceSelection(int start, int end, string text)
    {
        if (string.IsNullOrEmpty(text)) return default;
        return Mutate("replace", d => d.ReplaceSelection(start, end, text, TypingStyle));
    }

    /// <summary>Reverses the most recent transaction. Returns the new revision.</summary>
    public long Undo()
    {
        if (_undo.Count == 0) return _revision;
        _redo.Push(Document);
        Document = _undo.Pop();
        _revision++;
        return _revision;
    }

    /// <summary>Re-applies the most recently undone transaction. Returns the new revision.</summary>
    public long Redo()
    {
        if (_redo.Count == 0) return _revision;
        _undo.Push(Document);
        Document = _redo.Pop();
        _revision++;
        return _revision;
    }
}
