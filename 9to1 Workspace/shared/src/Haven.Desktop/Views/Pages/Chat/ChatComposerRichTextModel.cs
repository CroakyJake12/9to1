using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Inline emphasis marks supported by the shared composer rich-text engine. These are the
/// canonical semantic marks; delimiters in the editable draft are a serialization projection,
/// not the authority. Marks are independent flags so a run may carry several at once.
/// </summary>
[Flags]
public enum ChatComposerInlineMark
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Code = 1 << 2,
    Link = 1 << 3,
    Strikethrough = 1 << 4,
}

/// <summary>Block-level kinds supported by the engine. Block kind is derived from the editable
/// draft's line markers (<c>&gt; </c> for quotes, <c>- </c> for bullets), so block state is
/// never dual-maintained with the visible text.</summary>
public enum ChatComposerBlockKind
{
    Paragraph = 0,
    Quote = 1,
    BulletItem = 2,
}

/// <summary>
/// The shared non-visual rich-text document. It is a flat UTF-16 text buffer (lines separated by
/// <c>'\n'</c>) paired with parallel formatting arrays: one mark value and one optional link URL
/// per character. Block kind is derived on demand from leading line markers, so every edit keeps
/// the text and the formatting arrays in lock-step.
///
/// This type owns the canonical document STATE only. All mutating operations are performed through
/// <see cref="ChatComposerEditor"/> so edits are recorded as reversible transactions; this class
/// itself exposes the lower-level, recordable mutation primitives.
/// </summary>
public sealed class ChatComposerRichTextDocument
{
    /// <summary>Default link URL placeholder used when a Link mark is applied without an explicit URL.</summary>
    internal const string DefaultLinkPlaceholder = "url";

    public ChatComposerRichTextDocument()
    {
    }

    private ChatComposerRichTextDocument(string text, ChatComposerInlineMark[] marks, string?[] links)
    {
        Text = text;
        Marks = marks;
        Links = links;
    }

    /// <summary>The flattened editable text (lines joined with <c>'\n'</c>). It contains the
    /// block markers but no inline delimiters; emphasis lives in <see cref="Marks"/>.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>Per-character inline marks, parallel to <see cref="Text"/>.</summary>
    public ChatComposerInlineMark[] Marks { get; private set; } = Array.Empty<ChatComposerInlineMark>();

    /// <summary>Per-character link targets, parallel to <see cref="Text"/>. Non-null only where
    /// the Link mark is set.</summary>
    public string?[] Links { get; private set; } = Array.Empty<string?>();

    public int Length => Text.Length;

    /// <summary>A shared empty document.</summary>
    public static ChatComposerRichTextDocument Empty { get; } = new();

    /// <summary>Creates a deep copy (fresh arrays) safe for undo stack retention.</summary>
    public ChatComposerRichTextDocument Clone() => new(
        Text,
        (ChatComposerInlineMark[])Marks.Clone(),
        (string?[])Links.Clone());

    /// <summary>Structural equality over the text and both formatting arrays.</summary>
    internal static bool DeepEquals(ChatComposerRichTextDocument a, ChatComposerRichTextDocument b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Text != b.Text) return false;
        if (a.Marks.Length != b.Marks.Length || a.Links.Length != b.Links.Length) return false;
        for (int i = 0; i < a.Marks.Length; i++)
            if (a.Marks[i] != b.Marks[i] || a.Links[i] != b.Links[i]) return false;
        return true;
    }

    /// <summary>Verifies the parallel arrays are in step. Called after every mutation; a mismatch
    /// indicates a programming error in an edit primitive.</summary>
    internal void AssertSane()
    {
        Debug.Assert(Text.Length == Marks.Length && Text.Length == Links.Length,
            "Rich-text document text and formatting arrays are out of step.");
    }

    /// <summary>Normalizes a range to valid bounds, clamping rather than throwing for caller
    /// convenience.</summary>
    internal static void NormalizeRange(ref int start, ref int end, int length)
    {
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, 0, length);
        if (end < start) (start, end) = (end, start);
    }

    /// <summary>The [start, end) character offsets of the line containing <paramref name="abs"/>.
    /// The newline character itself belongs to the preceding line, so a line's end excludes its
    /// trailing newline.</summary>
    internal (int Start, int End) LineBounds(int abs)
    {
        if (Text.Length == 0) return (0, 0);
        int s = Math.Clamp(abs, 0, Text.Length);
        while (s > 0 && Text[s - 1] != '\n') s--;
        int e = Math.Clamp(abs, 0, Text.Length);
        while (e < Text.Length && Text[e] != '\n') e++;
        return (s, e);
    }

    /// <summary>The block kind of the line [start, end), derived from its leading marker.</summary>
    internal ChatComposerBlockKind LineKind(int start, int end)
    {
        int len = end - start;
        if (len >= 2)
        {
            if (Text[start] == '>' && Text[start + 1] == ' ') return ChatComposerBlockKind.Quote;
            if (Text[start] == '-' && Text[start + 1] == ' ') return ChatComposerBlockKind.BulletItem;
        }
        return ChatComposerBlockKind.Paragraph;
    }

    /// <summary>The paragraph/quote/bullet kind of the line containing <paramref name="abs"/>.</summary>
    internal ChatComposerBlockKind LineKindAt(int abs)
    {
        var (start, end) = LineBounds(abs);
        return LineKind(start, end);
    }

    /// <summary>Inserts text at an absolute offset, carrying a uniform mark and optional link URL
    /// for every inserted character. The text may contain newlines; each inserted line begins with
    /// the same carrying marks.</summary>
    internal void Insert(int pos, string text, ChatComposerInlineMark mark, string? link)
    {
        if (string.IsNullOrEmpty(text)) return;
        pos = Math.Clamp(pos, 0, Text.Length);

        var newMarks = new ChatComposerInlineMark[Text.Length + text.Length];
        var newLinks = new string?[Text.Length + text.Length];
        for (int i = 0; i < pos; i++) { newMarks[i] = Marks[i]; newLinks[i] = Links[i]; }
        for (int i = 0; i < text.Length; i++) { newMarks[pos + i] = mark; newLinks[pos + i] = link; }
        for (int i = pos; i < Text.Length; i++) { newMarks[i + text.Length] = Marks[i]; newLinks[i + text.Length] = Links[i]; }

        Text = Text.Substring(0, pos) + text + Text.Substring(pos);
        Marks = newMarks;
        Links = newLinks;
        AssertSane();
    }

    /// <summary>Removes the character range [start, end).</summary>
    internal void RemoveRange(int start, int end)
    {
        NormalizeRange(ref start, ref end, Text.Length);
        if (end <= start) return;

        var newText = Text.Remove(start, end - start);
        var newMarks = new ChatComposerInlineMark[newText.Length];
        var newLinks = new string?[newText.Length];
        for (int i = 0; i < start; i++) { newMarks[i] = Marks[i]; newLinks[i] = Links[i]; }
        for (int i = end; i < Text.Length; i++) { newMarks[i - (end - start)] = Marks[i]; newLinks[i - (end - start)] = Links[i]; }

        Text = newText;
        Marks = newMarks;
        Links = newLinks;
        AssertSane();
    }

    /// <summary>Replaces [start, end) with text carrying a uniform mark.</summary>
    internal void ReplaceSelection(int start, int end, string text, ChatComposerInlineMark mark, string? link = null)
    {
        RemoveRange(start, end);
        Insert(start, text, mark, link);
    }

    /// <summary>Installs text parsed from a draft with parallel formatting arrays. Used by the
    /// Markdown projection when synchronizing the document from externally edited plain text.</summary>
    internal void ApplyParsed(string text, ChatComposerInlineMark[] marks, string?[] links)
    {
        Text = text ?? string.Empty;
        Marks = marks ?? Array.Empty<ChatComposerInlineMark>();
        Links = links ?? Array.Empty<string?>();
        if (Marks.Length != Text.Length || Links.Length != Text.Length)
        {
            var m = new ChatComposerInlineMark[Text.Length];
            var l = new string?[Text.Length];
            for (int i = 0; i < Text.Length; i++)
            {
                m[i] = i < Marks.Length ? Marks[i] : ChatComposerInlineMark.None;
                l[i] = i < Links.Length ? Links[i] : null;
            }
            Marks = m;
            Links = l;
        }
        AssertSane();
    }

    /// <summary>Gathers the line ranges that overlap [start, end).</summary>
    internal List<(int Start, int End)> OverlappingLines(int start, int end)
    {
        NormalizeRange(ref start, ref end, Text.Length);
        var ranges = new List<(int, int)>();
        if (end <= start || Text.Length == 0) return ranges;
        int cursor = start;
        while (cursor < end)
        {
            var (ls, le) = LineBounds(cursor);
            ranges.Add((ls, le));
            if (le >= Text.Length) break;
            cursor = le + 1;
        }
        return ranges;
    }

    /// <summary>
    /// Toggles an inline mark over [start, end). If every content character in the range already
    /// carries the mark it is removed everywhere (toggle off); otherwise it is added everywhere.
    /// Newline characters are never modified. Applies to Link sets the per-character target.
    /// </summary>
    public void ToggleInlineMark(int start, int end, ChatComposerInlineMark mark, string? linkUrl = null)
    {
        NormalizeRange(ref start, ref end, Text.Length);
        if (end <= start || mark == ChatComposerInlineMark.None) return;

        bool allSet = true;
        for (int i = start; i < end; i++)
        {
            if (Text[i] != '\n' && (Marks[i] & mark) == 0) { allSet = false; break; }
        }

        for (int i = start; i < end; i++)
        {
            if (Text[i] == '\n') continue;
            if (allSet)
            {
                Marks[i] &= ~mark;
                if ((mark & ChatComposerInlineMark.Link) != 0) Links[i] = null;
            }
            else
            {
                Marks[i] |= mark;
                if ((mark & ChatComposerInlineMark.Link) != 0) Links[i] = linkUrl ?? Links[i] ?? DefaultLinkPlaceholder;
            }
        }
        AssertSane();
    }

    /// <summary>
    /// Toggles a block kind over every line overlapping [start, end). Applying a kind already
    /// present on a line removes it (toggle back to paragraph); applying a different kind converts
    /// the line to the requested kind. Lines are processed bottom-up so earlier offsets stay valid
    /// as prefixes are added/removed.
    /// </summary>
    public void ToggleBlockKind(int start, int end, ChatComposerBlockKind kind)
    {
        var ranges = OverlappingLines(start, end);
        for (int k = ranges.Count - 1; k >= 0; k--)
        {
            var (ls, le) = ranges[k];
            var current = LineKind(ls, le);
            if (current == kind) continue; // already the requested kind -> leave unchanged

            bool hadMarker = current == ChatComposerBlockKind.Quote || current == ChatComposerBlockKind.BulletItem;
            if (hadMarker) RemoveRange(ls, ls + 2);
            Insert(ls, kind == ChatComposerBlockKind.Quote ? "> " : "- ", ChatComposerInlineMark.None, null);
        }
        AssertSane();
    }
}
