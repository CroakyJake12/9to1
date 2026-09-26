using System.Security;
using System.Text;
using CakeOS.Cui.Language;

namespace CakeOS.Cui;

internal sealed record CuiSyntaxNormalizationResult(
    string XmlSource,
    IReadOnlyDictionary<int, string> TextReferences);

/// <summary>
/// Normalizes CUI's compact lexical forms into the XML-shaped input consumed by
/// the semantic parser. It preserves text references as opaque markers so they
/// remain typed values in the resulting semantic tree.
/// </summary>
internal sealed class CuiSyntaxNormalizer(CuiDiagnosticBag diagnostics)
{
    private const char TextReferenceStart = '\uE000';
    private const char TextReferenceEnd = '\uE001';

    public CuiSyntaxNormalizationResult Normalize(string source, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = new StringBuilder(source.Length);
        var textReferences = new Dictionary<int, string>();
        var elementStack = new Stack<string>();
        var nextReferenceId = 0;

        for (var index = 0; index < source.Length;)
        {
            if (source.AsSpan(index).StartsWith("<#", StringComparison.Ordinal))
            {
                var commentEnd = source.IndexOf("#>", index + 2, StringComparison.Ordinal);
                var end = commentEnd < 0 ? source.Length : commentEnd + 2;
                if (commentEnd < 0)
                {
                    diagnostics.Error("CUI040", "CUI comment is missing its closing '#>' delimiter.",
                        SpanAt(source, sourceName, index));
                }
                AppendBlanked(output, source.AsSpan(index, end - index));
                index = end;
                continue;
            }

            if (TryReadReference(source, index, out var referenceEnd, out var reference))
            {
                textReferences.Add(nextReferenceId, reference);
                output.Append(TextReferenceStart).Append(nextReferenceId++).Append(TextReferenceEnd);
                index = referenceEnd;
                continue;
            }

            if (source[index] == '<' && TryReadTagEnd(source, index, out var tagEnd))
            {
                var tagContent = source[(index + 1)..tagEnd];
                output.Append(RewriteTag(tagContent, source, sourceName, index, elementStack));
                index = tagEnd + 1;
                continue;
            }

            output.Append(source[index++]);
        }

        return new CuiSyntaxNormalizationResult(output.ToString(), textReferences);
    }

    private string RewriteTag(
        string content,
        string source,
        string sourceName,
        int sourceOffset,
        Stack<string> elementStack)
    {
        content = ReplaceTagReferences(content);
        if (content.StartsWith("!--", StringComparison.Ordinal)
            || content.StartsWith("!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("?", StringComparison.Ordinal))
        {
            return $"<{content}>";
        }

        if (content.StartsWith("/", StringComparison.Ordinal))
        {
            var closingName = content[1..].Trim();
            if (closingName == "$") closingName = "Property";
            else if (closingName == "&") closingName = "ActionEvent";

            if (elementStack.Count > 0)
            {
                var expected = elementStack.Peek();
                if (IsBuiltIn(expected)
                    && string.Equals(expected, closingName, StringComparison.OrdinalIgnoreCase))
                {
                    closingName = expected;
                }
                if (string.Equals(expected, closingName, StringComparison.Ordinal))
                    elementStack.Pop();
            }

            return $"</{closingName}>";
        }

        var selfClosing = content.TrimEnd().EndsWith("/", StringComparison.Ordinal);
        var header = selfClosing ? content.TrimEnd()[..^1].TrimEnd() : content.Trim();
        var tokens = SplitHeader(header);
        if (tokens.Count == 0) return "<>";

        var attributes = new List<string>();
        var tagToken = tokens[0];
        string type;

        if (tagToken.StartsWith('&'))
        {
            type = "ActionEvent";
            var eventInfo = ParseEventToken(tagToken[1..]);
            attributes.Add($"Type=\"{EscapeAttribute(eventInfo.Name)}\"");
            if (eventInfo.TargetId is not null)
                attributes.Add($"TargetID=\"{EscapeAttribute(eventInfo.TargetId)}\"");
            if (eventInfo.TargetGroup is not null)
                attributes.Add($"TargetGroup=\"{EscapeAttribute(eventInfo.TargetGroup)}\"");
        }
        else if (tagToken.StartsWith('@'))
        {
            type = "Keyframe";
            attributes.Add($"Name=\"{EscapeAttribute(tagToken[1..])}\"");
        }
        else if (tagToken.StartsWith('$'))
        {
            type = "Property";
            var identityFree = ExtractIdentity(tagToken, attributes);
            var property = ParsePropertyToken(identityFree[1..]);
            if (property.Name.Length == 0 || property.Value.Length == 0)
            {
                diagnostics.Error("CUI041", "A '$' property region requires a property name and value.",
                    SpanAt(source, sourceName, sourceOffset));
            }
            else
            {
                attributes.Add($"Property=\"{EscapeAttribute(property.Name)}\"");
                attributes.Add($"{property.Name}=\"{EscapeAttribute(property.Value.Replace('_', ' '))}\"");
            }
            tagToken = identityFree;
        }
        else
        {
            var isIndependent = tagToken.StartsWith('!');
            if (isIndependent) tagToken = tagToken[1..];

            var identityFree = ExtractIdentity(tagToken, attributes);
            if (identityFree.StartsWith("Property=", StringComparison.OrdinalIgnoreCase))
            {
                type = "Property";
                var propertyName = identityFree["Property=".Length..];
                if (propertyName.Length == 0)
                {
                    diagnostics.Error("CUI041", "A Property region requires a property name and value.",
                        SpanAt(source, sourceName, sourceOffset));
                }
                else
                {
                    attributes.Add($"Property=\"{EscapeAttribute(propertyName)}\"");
                }
            }
            else
            {
                type = CanonicalBuiltInName(identityFree);
            }

            if (isIndependent)
                attributes.Add("ActiveIndependently=\"True\"");

            tagToken = identityFree;
        }

        for (var tokenIndex = 1; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            if (string.Equals(token, "(Definition)", StringComparison.OrdinalIgnoreCase))
            {
                attributes.Add("Definition=\"True\"");
                continue;
            }

            if (TryNormalizeAttribute(token, out var normalizedAttribute))
                attributes.Add(normalizedAttribute);
            else if (token.Length > 0)
                diagnostics.Error("CUI042", $"Invalid CUI attribute syntax '{token}'.",
                    SpanAt(source, sourceName, sourceOffset));
        }

        var attributeText = attributes.Count == 0 ? string.Empty : " " + string.Join(' ', attributes);
        var rewritten = $"<{type}{attributeText}{(selfClosing ? "/" : string.Empty)}>";
        if (!selfClosing) elementStack.Push(type);
        return rewritten;
    }

    private static string ReplaceTagReferences(string content)
    {
        var output = new StringBuilder(content.Length);
        var quote = '\0';
        for (var index = 0; index < content.Length;)
        {
            if (TryReadReference(content, index, out var referenceEnd, out var reference))
            {
                var binding = ToBindingExpression(reference);
                if (quote == '\0') output.Append('"');
                output.Append(binding);
                if (quote == '\0') output.Append('"');
                index = referenceEnd;
                continue;
            }

            var character = content[index++];
            if (quote != '\0')
            {
                if (character == quote && (index < 2 || content[index - 2] != '\\')) quote = '\0';
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            output.Append(character);
        }
        return output.ToString();
    }

    private static string ExtractIdentity(string token, List<string> attributes)
    {
        var groupMarker = token.IndexOf(":::", StringComparison.Ordinal);
        var identityPrefix = groupMarker < 0 ? token : token[..groupMarker];
        if (groupMarker >= 0)
        {
            var groups = token[(groupMarker + 3)..];
            if (groups.Length > 0)
                attributes.Add($"Group=\"{EscapeAttribute(groups)}\"");
        }

        var idMarker = identityPrefix.IndexOf("::", StringComparison.Ordinal);
        if (idMarker < 0) return identityPrefix;

        var id = identityPrefix[(idMarker + 2)..];
        if (id.Length > 0)
            attributes.Add($"ID=\"{EscapeAttribute(id)}\"");
        return identityPrefix[..idMarker];
    }

    private static (string Name, string Value) ParsePropertyToken(string token)
    {
        var separator = token.IndexOfAny([':', '=']);
        if (separator <= 0 || separator >= token.Length - 1)
            return (string.Empty, string.Empty);
        return (token[..separator], token[(separator + 1)..]);
    }

    private static (string Name, string? TargetId, string? TargetGroup) ParseEventToken(string token)
    {
        var targetStart = token.IndexOf('{');
        if (targetStart < 0 || !token.EndsWith('}')) return (token, null, null);

        var name = token[..targetStart];
        var target = token[(targetStart + 1)..^1];
        if (target.StartsWith(":::", StringComparison.Ordinal))
            return (name, null, target[3..]);
        if (target.StartsWith("::", StringComparison.Ordinal))
            return (name, target[2..], null);
        return (token, null, null);
    }

    private static bool TryNormalizeAttribute(string token, out string normalized)
    {
        normalized = string.Empty;
        var equals = token.IndexOf('=');
        if (equals > 0)
        {
            normalized = token;
            return true;
        }

        var colon = token.IndexOf(':');
        if (colon <= 0 || colon >= token.Length - 1 || token.Contains("::", StringComparison.Ordinal))
            return false;

        var name = token[..colon];
        var value = token[(colon + 1)..].Replace('_', ' ');
        normalized = $"{name}=\"{EscapeAttribute(value)}\"";
        return true;
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
            if (TryReadReference(source, index, out var referenceEnd, out _))
            {
                index = referenceEnd - 1;
                continue;
            }

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

    private static bool TryReadReference(
        string source,
        int start,
        out int end,
        out string reference)
    {
        end = start;
        reference = string.Empty;
        if (start < 0 || start + 3 >= source.Length || source[start] != '<') return false;

        var closing = source.IndexOf('>', start + 1);
        if (closing < 0) return false;
        var content = source[(start + 1)..closing];
        var parts = content.Split(':');
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[1])) return false;

        var isUntyped = parts[0].Length == 0;
        var isTyped = parts[0].Length > 0 && parts[0].All(character => char.IsLetterOrDigit(character) || character is '_' or '.');
        if (!isUntyped && !isTyped) return false;
        if (parts.Any(part => part.Any(char.IsWhiteSpace))) return false;

        reference = source[start..(closing + 1)];
        end = closing + 1;
        return true;
    }

    private static string ToBindingExpression(string reference)
    {
        var parts = reference[1..^1].Split(':');
        var type = parts[0];
        var path = parts[1];
        var mode = parts[2].Equals("static", StringComparison.OrdinalIgnoreCase) ? "OneTime" : "OneWay";
        var options = new List<string> { $"mode={mode}" };
        if (type.Length > 0) options.Add($"type={type}");
        return $"{{Binding {path}, {string.Join(", ", options)}}}";
    }

    private static bool IsBuiltIn(string name) =>
        CanonicalBuiltInName(name) is "Cui" or "Page" or "Container" or "Text" or "Image"
            or "Audio" or "Video" or "Object" or "Button" or "Input" or "Layer" or "Anchor"
            or "Theme" or "Style" or "Property" or "Variable" or "Prefab" or "If" or "Else"
            or "Repeat" or "Import" or "Keyframe" or "ActionEvent" or "Resources" or "Styles"
            or "Actions" or "Templates" or "DefaultTheme";

    private static string CanonicalBuiltInName(string name)
    {
        string[] builtIns =
        [
            "Cui", "Page", "Container", "Text", "Image", "Audio", "Video", "Object",
            "Button", "Input", "Layer", "Anchor", "Theme", "Style", "Property", "Variable",
            "Prefab", "If", "Else", "Repeat", "Import", "Keyframe", "ActionEvent",
            "Resources", "Styles", "Actions", "Templates", "DefaultTheme",
        ];
        return builtIns.FirstOrDefault(candidate =>
            string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) ?? name;
    }

    private static string EscapeAttribute(string value) =>
        SecurityElement.Escape(value) ?? string.Empty;

    private static CuiSourceSpan SpanAt(string source, string sourceName, int offset)
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
        return CuiSourceSpan.At(sourceName, line, column);
    }

    private static void AppendBlanked(StringBuilder output, ReadOnlySpan<char> source)
    {
        foreach (var character in source)
            output.Append(character is '\r' or '\n' ? character : ' ');
    }
}
