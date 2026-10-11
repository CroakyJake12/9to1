using System.Text;
using CakeOS.Cui.Language;
using LanguageSpan = CakeOS.Cui.Language.CuiSourceSpan;

namespace Haven.CUI.DevTools;

public sealed record CuiFormatterOptions
{
    public CuiFormatterOptions(string indentUnit = "    ", string newLine = "\n")
    {
        if (string.IsNullOrEmpty(indentUnit))
            throw new ArgumentException("Formatter indent unit cannot be empty.", nameof(indentUnit));
        if (string.IsNullOrEmpty(newLine))
            throw new ArgumentException("Formatter newline cannot be empty.", nameof(newLine));
        if (newLine != "\n" && newLine != "\r\n")
            throw new ArgumentException("Formatter newline must be LF or CRLF.", nameof(newLine));

        IndentUnit = indentUnit;
        NewLine = newLine;
    }

    public string IndentUnit { get; }

    public string NewLine { get; }

    public static CuiFormatterOptions Default { get; } = new();
}

public sealed record CuiFormatResult(
    string SourceName,
    string Output,
    bool Changed,
    IReadOnlyList<CuiDiagnostic> Diagnostics);

/// <summary>
/// Deterministic CUI formatter. Formatting whitespace outside text-bearing content is
/// normalized. Comments, tag content, quoted values, shorthand forms and text-bearing
/// content are emitted verbatim, so formatting cannot change language semantics.
/// </summary>
public sealed class CuiFormatter
{
    private enum TokenKind
    {
        Comment,
        Tag,
        Reference,
        Text
    }

    private sealed record Token(TokenKind Kind, string Value, int Offset);

    public CuiFormatResult Format(string source, string sourceName, CuiFormatterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var settings = options ?? CuiFormatterOptions.Default;
        var diagnostics = new CuiDiagnosticBag();

        if (!CuiProjectItems.IsCuiSourceFamily(sourceName))
        {
            diagnostics.Error("CUIF001",
                "CUI formatting accepts only .cui, .cui-theme and .cui-prefab sources.",
                LanguageSpan.At(sourceName, 1, 1));
            return new CuiFormatResult(sourceName, string.Empty, false, diagnostics.Diagnostics);
        }

        var tokens = Tokenize(source, sourceName, diagnostics);
        if (diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            return new CuiFormatResult(sourceName, string.Empty, false, diagnostics.Diagnostics);

        var output = new StringBuilder(source.Length);
        var textBearing = new List<bool>();
        var depth = 0;
        var pendingNewLine = false;

        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Tag && token.Value.Length > 2 && token.Value[1] == '/')
            {
                if (depth > 0) depth--;
                if (textBearing.Count > 0) textBearing.RemoveAt(textBearing.Count - 1);
            }

            if (token.Kind == TokenKind.Text)
            {
                if (IsAllWhitespace(token.Value))
                {
                    if (IsInsideTextBearingScope(textBearing))
                    {
                        output.Append(token.Value);
                        continue;
                    }

                    pendingNewLine = true;
                    continue;
                }

                StartLine(output, settings, depth, pendingNewLine);
                output.Append(token.Value);
                pendingNewLine = false;
                continue;
            }

            StartLine(output, settings, depth, pendingNewLine);
            output.Append(token.Value);

            if (token.Kind == TokenKind.Comment)
            {
                pendingNewLine = true;
                continue;
            }

            if (token.Kind == TokenKind.Reference)
            {
                pendingNewLine = false;
                continue;
            }

            var content = token.Value[1..^1];
            if (content.StartsWith('/'))
            {
                pendingNewLine = !IsInsideTextBearingScope(textBearing);
                continue;
            }

            var selfClosing = content.TrimEnd().EndsWith('/');
            textBearing.Add(IsTextBearingHeader(content));
            pendingNewLine = !textBearing[^1];
            if (!selfClosing) depth++;
        }

        if (output.Length > 0 && !output.ToString().EndsWith(settings.NewLine, StringComparison.Ordinal))
            output.Append(settings.NewLine);

        var formatted = output.ToString();
        return new CuiFormatResult(sourceName, formatted, formatted != source, diagnostics.Diagnostics);
    }

    private static void StartLine(StringBuilder output, CuiFormatterOptions settings, int depth, bool pendingNewLine)
    {
        if (!pendingNewLine || output.Length == 0) return;

        output.Append(settings.NewLine);
        output.Append(string.Concat(Enumerable.Repeat(settings.IndentUnit, depth)));
    }

    private static List<Token> Tokenize(string source, string sourceName, CuiDiagnosticBag diagnostics)
    {
        var tokens = new List<Token>();
        var index = 0;

        while (index < source.Length)
        {
            if (source[index] != '<')
            {
                var start = index;
                while (index < source.Length && source[index] != '<') index++;
                tokens.Add(new Token(TokenKind.Text, source[start..index], start));
                continue;
            }

            if (source.AsSpan(index).StartsWith("<#", StringComparison.Ordinal))
            {
                var commentEnd = source.IndexOf("#>", index + 2, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    diagnostics.Error("CUIF002", "CUI comment is missing its closing '#>' delimiter.",
                        PositionAt(source, sourceName, index));
                    return tokens;
                }

                tokens.Add(new Token(TokenKind.Comment, source[index..(commentEnd + 2)], index));
                index = commentEnd + 2;
                continue;
            }

            if (!TryReadTagEnd(source, index, out var tagEnd))
            {
                diagnostics.Error("CUIF003", "CUI tag is missing its closing '>' delimiter.",
                    PositionAt(source, sourceName, index));
                return tokens;
            }

            var value = source[index..(tagEnd + 1)];
            tokens.Add(new Token(IsReferenceToken(value) ? TokenKind.Reference : TokenKind.Tag, value, index));
            index = tagEnd + 1;
        }

        return tokens;
    }

    private static bool IsReferenceToken(string value)
    {
        if (value.Length < 2) return false;
        var parts = value[1..^1].Split(':');
        return parts.Length == 3 && parts[1].Length > 0 && parts.All(part => !part.Any(char.IsWhiteSpace));
    }

    private static bool IsTextBearingHeader(string content)
    {
        var tokens = SplitHeader(content);
        if (tokens.Count == 0) return false;

        if (string.Equals(StripIdentityMarkers(tokens[0]), "Text", StringComparison.OrdinalIgnoreCase))
            return true;

        return tokens.Skip(1).Any(IsPreserveWhitespaceAttribute);
    }

    private static bool IsPreserveWhitespaceAttribute(string token)
    {
        var separator = token.IndexOfAny([':', '=']);
        if (separator <= 0 || separator >= token.Length - 1) return false;

        var name = token[..separator];
        if (!string.Equals(name, "Whitespace", StringComparison.OrdinalIgnoreCase)) return false;

        var value = token[(separator + 1)..].Trim('"', '\'');
        return string.Equals(value, "Preserve", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripIdentityMarkers(string token)
    {
        if (token.Length == 0) return token;
        if (token[0] is '!' or '&' or '$' or '@') token = token[1..];

        var marker = token.IndexOf("::", StringComparison.Ordinal);
        return marker < 0 ? token : token[..marker];
    }

    private static List<string> SplitHeader(string header)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        var quote = '\0';

        for (var index = 0; index < header.Length; index++)
        {
            var character = header[index];
            if (quote != '\0')
            {
                token.Append(character);
                if (character == quote && (index == 0 || header[index - 1] != '\\')) quote = '\0';
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                token.Append(character);
            }
            else if (char.IsWhiteSpace(character))
            {
                if (token.Length > 0)
                {
                    result.Add(token.ToString());
                    token.Clear();
                }
            }
            else
            {
                token.Append(character);
            }
        }

        if (token.Length > 0) result.Add(token.ToString());
        return result;
    }

    private static bool TryReadTagEnd(string source, int start, out int end)
    {
        var quote = '\0';
        for (var index = start + 1; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == quote && source[index - 1] != '\\') quote = '\0';
                continue;
            }

            if (character is '"' or '\'') quote = character;
            else if (character == '>')
            {
                end = index;
                return true;
            }
        }

        end = source.Length;
        return false;
    }

    private static bool IsAllWhitespace(string value) =>
        value.Length > 0 && value.All(char.IsWhiteSpace);

    private static bool IsInsideTextBearingScope(List<bool> textBearing) =>
        textBearing.Count > 0 && textBearing[^1];

    private static LanguageSpan PositionAt(string source, string sourceName, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < Math.Min(offset, source.Length); index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return LanguageSpan.At(sourceName, line, column);
    }
}