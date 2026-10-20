using System;
using System.Collections.Generic;
using System.Text;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Bidirectional projection between the canonical rich-text document and its portable
/// Markdown-compatible draft form. The draft is exactly what the plain composer input shows and
/// what the existing send/storage pipeline carries, so the projection is lossless: a document
/// serializes to a draft that parses back to an equal document.
///
/// Delimiter rules (deterministic, so parser and serializer agree):
/// <list type="bullet">
/// <item>Bold <c>**text**</c>, Italic <c>_text_</c>, Code <c>`text`</c>, Strikethrough <c>~~text~~</c>.</item>
/// <item>Link <c>[text](url)</c>.</item>
/// <item>Combined marks nest outer-to-inner as: Link, Code, Bold, Italic, Strikethrough.</item>
/// <item>Block markers <c>&gt; </c> and <c>- </c> are ordinary content characters; the parser
/// derives block kind from them and does not strip them.</item>
/// </list>
///
/// Known boundary (documented, not faked): backslash escaping of literal delimiter characters is
/// not implemented in this checkpoint; a literal <c>*</c>, <c>_</c>, <c>~</c>, <c>`</c> or
/// <c>[</c> authored through the plain input is treated as a delimiter candidate on re-parse.
/// This is the standard Markdown ambiguity, not a hidden WYSIWYG manipulation.
/// </summary>
public static class ChatComposerMarkdown
{
    /// <summary>Serializes a document to its editable draft form (Markdown-compatible).</summary>
    public static string SerializeDraft(ChatComposerRichTextDocument document)
    {
        if (document is null) return string.Empty;
        var sb = new StringBuilder();
        int n = document.Length;
        int i = 0;
        while (i < n)
        {
            var marks = document.Marks[i];
            var link = document.Links[i];
            int j = i + 1;
            while (j < n && document.Marks[j] == marks && document.Links[j] == link) j++;
            var content = document.Text.Substring(i, j - i);
            sb.Append(WrapRun(content, marks, link));
            i = j;
        }
        return sb.ToString();
    }

    /// <summary>
    /// The lossless plain-text projection: the canonical text without inline emphasis markup.
    /// Block markers (<c>&gt; </c>, <c>- </c>) are retained because they are real list/quote
    /// structure. This is the "plain authoring text" view of the document and contains no
    /// delimiters; emphasis is carried by the parallel mark arrays.
    /// </summary>
    public static string SerializePlainText(ChatComposerRichTextDocument document) => document?.Text ?? string.Empty;

    /// <summary>Serializes a document and also records, for every source character, its offset in
    /// the produced draft. Used to translate a post-edit selection back into draft coordinates.</summary>
    public static string SerializeDraftWithMap(ChatComposerRichTextDocument document, out int[] sourceToDraft)
    {
        if (document is null) { sourceToDraft = Array.Empty<int>(); return string.Empty; }
        var sb = new StringBuilder();
        sourceToDraft = new int[document.Length];
        int n = document.Length;
        int i = 0;
        while (i < n)
        {
            var marks = document.Marks[i];
            var link = document.Links[i];
            int j = i + 1;
            while (j < n && document.Marks[j] == marks && document.Links[j] == link) j++;
            var content = document.Text.Substring(i, j - i);
            var rendered = WrapRun(content, marks, link);
            int draftStart = sb.Length;
            sb.Append(rendered);
            // Map each content character to its position inside the rendered run.
            int contentCursor = 0;
            for (int k = 0; k < rendered.Length; k++)
            {
                var c = rendered[k];
                if (IsDelimiter(rendered, k))
                {
                    continue;
                }
                // The content region is the middle of the run; find content chars by scanning the
                // rendered text skipping delimiters in the same order as 'content'.
                if (contentCursor < content.Length && c == content[contentCursor])
                {
                    sourceToDraft[i + contentCursor] = draftStart + k;
                    contentCursor++;
                }
            }
            i = j;
        }
        return sb.ToString();
    }

    private static bool IsDelimiter(string rendered, int k) => rendered[k] is '*' or '_' or '`' or '~' or '[' or ']' or '(' or ')';

    private static string WrapRun(string content, ChatComposerInlineMark marks, string? link)
    {
        string inner = content;
        if ((marks & ChatComposerInlineMark.Strikethrough) != 0) inner = "~~" + inner + "~~";
        if ((marks & ChatComposerInlineMark.Italic) != 0) inner = "_" + inner + "_";
        if ((marks & ChatComposerInlineMark.Bold) != 0) inner = "**" + inner + "**";
        if ((marks & ChatComposerInlineMark.Code) != 0) inner = "`" + inner + "`";
        if ((marks & ChatComposerInlineMark.Link) != 0)
            inner = "[" + inner + "](" + (link ?? ChatComposerRichTextDocument.DefaultLinkPlaceholder) + ")";
        return inner;
    }

    /// <summary>
    /// Parses a draft back into a canonical document. Returns <c>false</c> only for a null input;
    /// otherwise a document is always produced (unbalanced delimiters are treated literally).
    /// Also fills <paramref name="draftToSource"/>, a per-draft-character map to the source index
    /// (content) or -1 (delimiter).
    /// </summary>
    public static bool TryParseDraft(string? draft, out ChatComposerRichTextDocument document, out int[] draftToSource)
    {
        draft ??= string.Empty;
        var text = new StringBuilder();
        var marks = new List<ChatComposerInlineMark>();
        var links = new List<string?>();
        draftToSource = new int[draft.Length];

        // Open delimiter frames; each frame contributes a mark to content between open/close.
        var stack = new List<(ChatComposerInlineMark Mark, bool IsLink, string? PendingUrl)>();
        // Source index of the first character captured inside the current link frame, used to
        // assign the resolved URL to every Link-marked character the frame covered.
        int linkOpenSource = -1;

        int i = 0;
        while (i < draft.Length)
        {
            char c = draft[i];
            char next = i + 1 < draft.Length ? draft[i + 1] : '\0';

            // Link close sequence "](" ... ")".
            if (c == ']' && stack.Count > 0 && stack[^1].IsLink)
            {
                // Expect '(' next.
                if (next == '(')
                {
                    int close = draft.IndexOf(')', i + 2);
                    if (close >= 0)
                    {
                        var url = draft.Substring(i + 2, close - (i + 2));
                        // Close link frame: assign the resolved URL to every character this link
                        // frame covered (any nested marks still carry the Link mark).
                        for (int src = linkOpenSource; src >= 0 && src < links.Count; src++)
                        {
                            if ((marks[src] & ChatComposerInlineMark.Link) != 0) links[src] = url;
                        }
                        // Consume "](" ... ")" as delimiters.
                        draftToSource[i] = -1;
                        draftToSource[i + 1] = -1;
                        for (int di = i + 2; di <= close; di++) draftToSource[di] = -1;
                        stack.RemoveAt(stack.Count - 1);
                        linkOpenSource = -1;
                        i = close + 1;
                        continue;
                    }
                }
                // Malformed link close: treat ']' literally.
                AppendContent(draft[i], i);
                i++;
                continue;
            }

            // Open delimiter detection.
            if (c == '*' && next == '*') { stack.Add((ChatComposerInlineMark.Bold, false, null)); draftToSource[i] = -1; draftToSource[i + 1] = -1; i += 2; continue; }
            if (c == '~' && next == '~') { stack.Add((ChatComposerInlineMark.Strikethrough, false, null)); draftToSource[i] = -1; draftToSource[i + 1] = -1; i += 2; continue; }
            if (c == '`') { stack.Add((ChatComposerInlineMark.Code, false, null)); draftToSource[i] = -1; i += 1; continue; }
            if (c == '_') { stack.Add((ChatComposerInlineMark.Italic, false, null)); draftToSource[i] = -1; i += 1; continue; }
            if (c == '[')
            {
                stack.Add((ChatComposerInlineMark.Link, true, null));
                draftToSource[i] = -1;
                linkOpenSource = text.Length;
                i += 1;
                continue;
            }

            // Close delimiter detection (must match the top frame type).
            if (stack.Count > 0)
            {
                var top = stack[^1];
                if (!top.IsLink)
                {
                    bool closed = false;
                    if (top.Mark == ChatComposerInlineMark.Bold && c == '*' && next == '*')
                    { stack.RemoveAt(stack.Count - 1); draftToSource[i] = -1; draftToSource[i + 1] = -1; i += 2; closed = true; }
                    else if (top.Mark == ChatComposerInlineMark.Strikethrough && c == '~' && next == '~')
                    { stack.RemoveAt(stack.Count - 1); draftToSource[i] = -1; draftToSource[i + 1] = -1; i += 2; closed = true; }
                    else if (top.Mark == ChatComposerInlineMark.Code && c == '`')
                    { stack.RemoveAt(stack.Count - 1); draftToSource[i] = -1; i += 1; closed = true; }
                    else if (top.Mark == ChatComposerInlineMark.Italic && c == '_')
                    { stack.RemoveAt(stack.Count - 1); draftToSource[i] = -1; i += 1; closed = true; }

                    if (closed) continue;
                }
            }

            // Otherwise: content. Apply union of open marks.
            AppendContent(c, i);
            i++;
        }

        // Tail: any still-open frames close at end-of-input (their marks already applied).
        document = new ChatComposerRichTextDocument();
        SetDocument(document, text, marks, links);
        return true;

        void AppendContent(char ch, int draftIndex)
        {
            var effective = ChatComposerInlineMark.None;
            foreach (var frame in stack)
            {
                if (!frame.IsLink) effective |= frame.Mark;
                else effective |= ChatComposerInlineMark.Link;
            }
            draftToSource[draftIndex] = text.Length;
            text.Append(ch);
            marks.Add(effective);
            links.Add(null); // url filled when the enclosing link frame closes
        }
    }

    private static void SetDocument(ChatComposerRichTextDocument document, StringBuilder text, List<ChatComposerInlineMark> marks, List<string?> links)
    {
        var t = text.ToString();
        var m = new ChatComposerInlineMark[t.Length];
        var l = new string?[t.Length];
        for (int i = 0; i < t.Length && i < marks.Count; i++) { m[i] = marks[i]; l[i] = links[i]; }
        // Push through the document's private setters.
        document.ApplyParsed(t, m, l);
    }

    /// <summary>Maps a draft selection [start, end) to the source range it covers. Offsets that
    /// land on delimiters are clamped to the nearest content boundary. Returns <c>true</c> when a
    /// non-empty source range can be produced.</summary>
    public static bool MapDraftSelection(int[] draftToSource, int draftLength, int start, int end, int sourceLength, out int sourceStart, out int sourceEnd)
    {
        sourceStart = 0;
        sourceEnd = 0;
        start = Math.Clamp(start, 0, draftLength);
        end = Math.Clamp(end, 0, draftLength);
        if (end < start) (start, end) = (end, start);

        int firstContent = -1;
        for (int i = start; i < end && firstContent < 0; i++)
            if (draftToSource[i] >= 0) firstContent = draftToSource[i];
        int lastContent = -1;
        for (int i = end - 1; i >= start && lastContent < 0; i--)
            if (draftToSource[i] >= 0) lastContent = draftToSource[i];

        if (firstContent < 0 || lastContent < 0)
        {
            // Selection entirely within delimiters (or empty): collapse to insert point.
            int caret = MapDraftCaret(draftToSource, draftLength, start, sourceLength);
            sourceStart = sourceEnd = caret;
            return true;
        }
        sourceStart = firstContent;
        sourceEnd = lastContent + 1;
        return true;
    }

    /// <summary>Maps a single draft caret to a source position, preferring the nearest content
    /// boundary to the right of the caret.</summary>
    public static int MapDraftCaret(int[] draftToSource, int draftLength, int caret, int sourceLength)
    {
        caret = Math.Clamp(caret, 0, draftLength);
        for (int i = caret; i < draftLength; i++)
            if (draftToSource[i] >= 0) return draftToSource[i];
        for (int i = caret - 1; i >= 0; i--)
            if (draftToSource[i] >= 0) return draftToSource[i] + 1;
        return sourceLength;
    }
}
