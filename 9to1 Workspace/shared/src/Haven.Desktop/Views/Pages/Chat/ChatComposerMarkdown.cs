using System;
using System.Collections.Generic;
using System.Text;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Bidirectional projection between the canonical rich-text document and its portable
/// Markdown-compatible draft form. The draft is exactly what the plain composer input shows and
/// what the existing send/storage pipeline carries, so the projection is lossless: a document
/// serializes to a draft that parses back to an equal document, and arbitrary literal delimiter
/// text is preserved through backslash escaping.
///
/// Delimiter rules (deterministic, so parser and serializer agree):
/// <list type="bullet">
/// <item>Bold <c>**text**</c>, Italic <c>_text_</c>, Code <c>`text`</c>, Strikethrough <c>~~text~~</c>.</item>
/// <item>Link <c>[text](url)</c>.</item>
/// <item>Combined marks nest outer-to-inner as: Link, Code, Bold, Italic, Strikethrough.</item>
/// <item>Block markers <c>&gt; </c> and <c>- </c> are ordinary content characters; the parser
/// derives block kind from them and does not strip them.</item>
/// <item>Literal <c>*</c>, <c>_</c>, <c>~</c>, <c>`</c> and <c>[</c> inside content are escaped
/// with a backslash so arbitrary text survives a round trip.</item>
/// </list>
/// </summary>
public static class ChatComposerMarkdown
{
    /// <summary>Characters that open a delimiter and must be escaped when literal.</summary>
    private static bool IsDelimiterChar(char c) => c is '*' or '_' or '~' or '`' or '[';

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
            sb.Append(RenderRun(content, marks, link));
            i = j;
        }
        return sb.ToString();
    }

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
            var run = RenderRun(content, marks, link, i, sourceToDraft);
            sb.Append(run);
            i = j;
        }
        return sb.ToString();
    }

    /// <summary>Builds the rendered Markdown for one content run, writing per-source offsets into
    /// <paramref name="destMap"/> (absolute positions) when provided.</summary>
    private static string RenderRun(string content, ChatComposerInlineMark marks, string? link, int contentStart = 0, int[]? destMap = null)
    {
        // 1. Escape literal delimiter characters in the raw content.
        var inner = new StringBuilder();
        for (int k = 0; k < content.Length; k++)
        {
            char c = content[k];
            int outBase = inner.Length;
            if (IsDelimiterChar(c)) inner.Append('\\');
            inner.Append(c);
            if (destMap is not null) destMap[contentStart + k] = outBase;
        }

        // 2. Compute outer delimiters from the applied marks (link outermost).
        string left = string.Empty, right = string.Empty;
        if ((marks & ChatComposerInlineMark.Strikethrough) != 0) { left += "~~"; right = "~~" + right; }
        if ((marks & ChatComposerInlineMark.Italic) != 0) { left += "_"; right = "_" + right; }
        if ((marks & ChatComposerInlineMark.Bold) != 0) { left += "**"; right = "**" + right; }
        if ((marks & ChatComposerInlineMark.Code) != 0) { left += "`"; right = "`" + right; }
        if ((marks & ChatComposerInlineMark.Link) != 0)
        {
            left += "[";
            right = "](" + (link ?? ChatComposerRichTextDocument.DefaultLinkPlaceholder) + ")" + right;
        }

        // 3. Shift recorded offsets by the left delimiter prefix.
        if (destMap is not null && left.Length > 0)
        {
            for (int k = contentStart; k < contentStart + content.Length; k++)
                destMap[k] += left.Length;
        }

        return left + inner + right;
    }

    /// <summary>
    /// Parses a draft back into a canonical document. Returns <c>false</c> only for a null input;
    /// otherwise a document is always produced (unbalanced delimiters are treated literally).
    /// Also fills <paramref name="draftToSource"/>, a per-draft-character map to the source index
    /// (content) or -1 (delimiter / escape marker).
    /// </summary>
    public static bool TryParseDraft(string? draft, out ChatComposerRichTextDocument document, out int[] draftToSource)
    {
        draft ??= string.Empty;
        var text = new StringBuilder();
        var marks = new List<ChatComposerInlineMark>();
        var links = new List<string?>();
        draftToSource = new int[draft.Length];

        var stack = new List<(ChatComposerInlineMark Mark, bool IsLink, string? PendingUrl)>();
        int linkOpenSource = -1;

        int i = 0;
        while (i < draft.Length)
        {
            char c = draft[i];
            char next = i + 1 < draft.Length ? draft[i + 1] : '\0';

            // Literal escape: a backslash before a delimiter char emits that char literally.
            if (c == '\\' && IsDelimiterChar(next))
            {
                draftToSource[i] = -1;
                AppendContent(next, i + 1);
                draftToSource[i + 1] = text.Length - 1;
                i += 2;
                continue;
            }

            // Link close sequence "](" ... ")".
            if (c == ']' && stack.Count > 0 && stack[^1].IsLink)
            {
                if (next == '(')
                {
                    int close = draft.IndexOf(')', i + 2);
                    if (close >= 0)
                    {
                        var url = draft.Substring(i + 2, close - (i + 2));
                        for (int src = linkOpenSource; src >= 0 && src < links.Count; src++)
                        {
                            if ((marks[src] & ChatComposerInlineMark.Link) != 0) links[src] = url;
                        }
                        draftToSource[i] = -1;
                        draftToSource[i + 1] = -1;
                        for (int di = i + 2; di <= close; di++) draftToSource[di] = -1;
                        stack.RemoveAt(stack.Count - 1);
                        linkOpenSource = -1;
                        i = close + 1;
                        continue;
                    }
                }
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

        document = new ChatComposerRichTextDocument();
        var t = text.ToString();
        var m = new ChatComposerInlineMark[t.Length];
        var l = new string?[t.Length];
        for (int k = 0; k < t.Length && k < marks.Count; k++) { m[k] = marks[k]; l[k] = links[k]; }
        document.ApplyParsed(t, m, l);
        return true;

        void AppendContent(char ch, int draftIndex)
        {
            var effective = ChatComposerInlineMark.None;
            foreach (var frame in stack)
            {
                if (frame.IsLink) effective |= ChatComposerInlineMark.Link;
                else effective |= frame.Mark;
            }
            draftToSource[draftIndex] = text.Length;
            text.Append(ch);
            marks.Add(effective);
            links.Add(null);
        }
    }

    /// <summary>Maps a draft selection [start, end) to the source range it covers. Offsets that
    /// land on delimiters are clamped to the nearest content boundary.</summary>
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

    /// <summary>Serializes the canonical plain text (block markers retained, no inline delimiters,
    /// no escape markers). This is the accessible/plain authoring view of the document.</summary>
    public static string SerializePlainText(ChatComposerRichTextDocument document) => document?.Text ?? string.Empty;
}
