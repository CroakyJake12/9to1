using System;
using System.Threading;
using System.Threading.Tasks;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Internal command enum parsed from the composer's named format strings
/// (<c>Bold</c>, <c>Italic</c>, <c>Code</c>, <c>Link</c>, <c>Quote</c>,
/// <c>Bullet</c>, <c>Strikethrough</c>).
/// </summary>
internal enum ChatComposerFormatOp
{
    Bold,
    Italic,
    Code,
    Link,
    Strike,
    Quote,
    Bullet,
}

/// <summary>
/// The typed adapter that connects the shared non-visual rich-text engine
/// (<see cref="ChatComposerRichTextDocument"/> + <see cref="ChatComposerEditor"/>) to the real
/// composer input surfaces. Both the standalone ChatSpace source and the Haven native scene use
/// this same adapter, so formatting behavior is identical across hosts.
///
/// The adapter is a transport and command router only: it owns one shared document/editor per
/// mounted composer, synchronizes it from the current plain draft, applies the requested semantic
/// operation, re-projects the draft, and writes it back through the live input port. For the
/// unmounted/whole-draft fallback it routes through the supplied draft setter and returns its
/// actual admitted ValueTask instead of swallowing it.
/// </summary>
public sealed class ChatComposerFormattingPort
{
    private readonly Func<IChatComposerInputPort?>? _liveInputProvider;
    private readonly Func<string>? _fallbackDraft;
    private readonly Func<string, CancellationToken, ValueTask>? _fallbackSetDraft;
    private readonly ChatComposerEditor _editor = new();

    public ChatComposerFormattingPort(
        Func<IChatComposerInputPort?>? liveInputProvider = null,
        Func<string>? fallbackDraft = null,
        Func<string, CancellationToken, ValueTask>? fallbackSetDraft = null)
    {
        _liveInputProvider = liveInputProvider;
        _fallbackDraft = fallbackDraft;
        _fallbackSetDraft = fallbackSetDraft;
    }

    /// <summary>The shared editor backing this port (exposed for host undo/redo wiring).</summary>
    public ChatComposerEditor Editor => _editor;

    /// <summary>
    /// Applies a named format command. Uses the live input port when mounted; otherwise falls back
    /// to the whole-draft setter and returns its admitted task. Inline mark commands on a
    /// non-empty selection toggle the mark over the selection; with a collapsed caret they toggle
    /// the no-selection typing style. Block commands (quote/bullet) toggle the affected lines.
    /// </summary>
    public async ValueTask ApplyAsync(string formatCommand, CancellationToken cancellationToken = default)
    {
        var op = ParseCommand(formatCommand);
        var live = _liveInputProvider?.Invoke();

        if (live is not null)
        {
            ApplyToLive(live, op);
            return;
        }

        if (_fallbackDraft is not null && _fallbackSetDraft is not null)
        {
            var draft = _fallbackDraft() ?? string.Empty;
            var result = ApplyToText(draft, 0, draft.Length, op);
            if (result.Changed)
                await _fallbackSetDraft(result.Draft!, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("Chat composer formatting port has no live input or draft fallback to edit.");
    }

    /// <summary>Inserts an emoji at the live caret (replacing any selection), or appends it to the
    /// fallback draft.</summary>
    public async ValueTask InsertEmojiAsync(string emoji, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(emoji)) return;
        var live = _liveInputProvider?.Invoke();
        if (live is not null)
        {
            var text = live.Text ?? string.Empty;
            var start = Math.Clamp(live.SelectionStart, 0, text.Length);
            var length = Math.Clamp(live.SelectionLength, 0, text.Length - start);
            live.Text = text.Remove(start, length).Insert(start, emoji);
            live.SetSelection(start + emoji.Length, start + emoji.Length);
            live.Focus();
            return;
        }
        if (_fallbackDraft is not null && _fallbackSetDraft is not null)
        {
            var draft = _fallbackDraft() ?? string.Empty;
            await _fallbackSetDraft(draft + emoji, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reverses the last recorded engine transaction and re-projects the draft to the
    /// live input.</summary>
    public void Undo()
    {
        var live = _liveInputProvider?.Invoke();
        if (live is null || !_editor.CanUndo) return;
        _editor.Undo();
        RewriteLive(live, _editor.Document);
    }

    /// <summary>Re-applies the most recently undone engine transaction.</summary>
    public void Redo()
    {
        var live = _liveInputProvider?.Invoke();
        if (live is null || !_editor.CanRedo) return;
        _editor.Redo();
        RewriteLive(live, _editor.Document);
    }

    private static ChatComposerFormatOp ParseCommand(string command)
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
            _ => throw new InvalidOperationException($"Unknown composer format command '{command}'."),
        };
    }

    private void ApplyToLive(IChatComposerInputPort live, ChatComposerFormatOp op)
    {
        var text = live.Text ?? string.Empty;
        int start = Math.Clamp(live.SelectionStart, 0, text.Length);
        int length = Math.Clamp(live.SelectionLength, 0, text.Length - start);

        var result = ApplyToText(text, start, length, op);
        if (!result.Changed) return;

        RewriteLive(live, result.Draft!, result.SelectionStart, result.SelectionEnd);
    }

    private (bool Changed, string? Draft, int? SelectionStart, int? SelectionEnd) ApplyToText(
        string draft, int selectionStart, int selectionLength, ChatComposerFormatOp op)
    {
        bool parsed = ChatComposerMarkdown.TryParseDraft(draft, out var parsedDoc, out var draftToSource);
        if (!parsed) return (false, null, null, null);

        var sourceLength = parsedDoc.Length;
        int srcStart, srcEnd;
        bool collapsed = selectionLength <= 0;
        int caret = 0;
        if (collapsed)
        {
            caret = ChatComposerMarkdown.MapDraftCaret(draftToSource, draft.Length, selectionStart, sourceLength);
            srcStart = srcEnd = caret;
        }
        else
        {
            ChatComposerMarkdown.MapDraftSelection(draftToSource, draft.Length, selectionStart, selectionStart + selectionLength, sourceLength, out srcStart, out srcEnd);
        }

        // Block commands with a collapsed caret act on the whole line containing the caret.
        if (collapsed && op is ChatComposerFormatOp.Quote or ChatComposerFormatOp.Bullet)
        {
            var (ls, le) = parsedDoc.LineBounds(caret);
            srcStart = ls;
            srcEnd = le;
        }

        // Synchronize the shared document with the parsed draft, then apply the semantic op.
        _editor.SetContent(parsedDoc);

        if (op is ChatComposerFormatOp.Quote or ChatComposerFormatOp.Bullet)
        {
            var kind = op == ChatComposerFormatOp.Quote ? ChatComposerBlockKind.Quote : ChatComposerBlockKind.BulletItem;
            _editor.ToggleBlockKind(srcStart, srcEnd, kind);
        }
        else
        {
            var mark = ToInlineMark(op);
            _editor.ToggleInlineMark(srcStart, srcEnd, mark);
        }

        // Re-project and compute the post-edit selection.
        var newDoc = _editor.Document;
        var newDraft = ChatComposerMarkdown.SerializeDraftWithMap(newDoc, out var sourceToDraft);
        bool changed = newDraft != draft;

        int postStart, postEnd;
        if (op is ChatComposerFormatOp.Quote or ChatComposerFormatOp.Bullet)
        {
            var overlaps = newDoc.OverlappingLines(srcStart, srcEnd);
            if (overlaps.Count > 0)
            {
                postStart = overlaps[0].Start;
                postEnd = overlaps[^1].End;
            }
            else
            {
                postStart = srcStart;
                postEnd = srcEnd;
            }
        }
        else if (collapsed)
        {
            postStart = postEnd = srcStart;
        }
        else
        {
            postStart = srcStart;
            postEnd = srcEnd;
        }

        MapSourceToDraft(sourceToDraft, newDraft.Length, postStart, postEnd, out int nextStart, out int nextEnd);
        return (changed, newDraft, nextStart, nextEnd);
    }

    private void RewriteLive(IChatComposerInputPort live, ChatComposerRichTextDocument document)
    {
        var draft = ChatComposerMarkdown.SerializeDraft(document);
        RewriteLive(live, draft, null, null);
    }

    private void RewriteLive(IChatComposerInputPort live, string draft, int? selectionStart, int? selectionEnd)
    {
        live.Text = draft;
        if (selectionStart.HasValue && selectionEnd.HasValue)
        {
            live.SetSelection(selectionStart.Value, selectionEnd.Value);
        }
        else
        {
            live.PlaceCaretAtEnd();
        }
    }

    private static ChatComposerInlineMark ToInlineMark(ChatComposerFormatOp op) => op switch
    {
        ChatComposerFormatOp.Bold => ChatComposerInlineMark.Bold,
        ChatComposerFormatOp.Italic => ChatComposerInlineMark.Italic,
        ChatComposerFormatOp.Code => ChatComposerInlineMark.Code,
        ChatComposerFormatOp.Link => ChatComposerInlineMark.Link,
        ChatComposerFormatOp.Strike => ChatComposerInlineMark.Strikethrough,
        _ => ChatComposerInlineMark.None,
    };

    private static void MapSourceToDraft(int[] sourceToDraft, int draftLength, int srcStart, int srcEnd, out int draftStart, out int draftEnd)
    {
        if (srcStart >= srcEnd || sourceToDraft.Length == 0)
        {
            draftStart = draftEnd = draftLength;
            return;
        }
        srcStart = Math.Clamp(srcStart, 0, sourceToDraft.Length);
        srcEnd = Math.Clamp(srcEnd, 0, sourceToDraft.Length);
        if (srcStart < sourceToDraft.Length) draftStart = sourceToDraft[srcStart];
        else draftStart = draftLength;
        if (srcEnd - 1 >= 0 && srcEnd - 1 < sourceToDraft.Length) draftEnd = sourceToDraft[srcEnd - 1] + 1;
        else draftEnd = draftLength;
        // Clamp to valid draft range.
        draftStart = Math.Clamp(draftStart, 0, draftLength);
        draftEnd = Math.Clamp(draftEnd, 0, draftLength);
    }
}
