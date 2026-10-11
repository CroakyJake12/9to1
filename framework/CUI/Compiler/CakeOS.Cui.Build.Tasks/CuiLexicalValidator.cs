using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CakeOS.Cui.Build.Tasks;

// Plain immutable type rather than a record: netstandard2.0 has no IsExternalInit.
public sealed class CuiLexicalValidationResult
{
    public CuiLexicalValidationResult(bool isValid, string? rootLocalName, string? error, string normalized)
    {
        IsValid = isValid;
        RootLocalName = rootLocalName;
        Error = error;
        Normalized = normalized;
    }

    public bool IsValid { get; }

    public string? RootLocalName { get; }

    public string? Error { get; }

    public string Normalized { get; }
}

/// <summary>
/// Validation-only adapter for CUI's canonical lexical form. Core's net10 normalizer
/// (framework/CUI/Core/CuiSyntaxNormalizer.cs) cannot be referenced from this
/// netstandard2.0 build task, so this reproduces only the lexical mapping needed to
/// tell a canonical CUI document from malformed syntax: the (Definition) modifier,
/// ':' attribute shorthand, the '$'/'&'/'@'/'!' regions, '::' identity, '#>' comments
/// and text references. It builds no document tree and never rewrites the original
/// resource bytes; the normalized form is the validation view.
/// </summary>
public sealed class CuiLexicalValidator
{
    private const string ReferenceMarker = "ref";

    private static readonly string[] BuiltInNames =
    [
        "Cui", "Page", "Container", "Text", "Image", "Audio", "Video", "Object",
        "Button", "Input", "Layer", "Anchor", "Theme", "Style", "Property", "Variable",
        "Prefab", "If", "Else", "Repeat", "Import", "Keyframe", "ActionEvent",
        "Resources", "Styles", "Actions", "Templates", "DefaultTheme",
    ];

    public CuiLexicalValidationResult Validate(string source)
    {
        if (source is null)
            return Invalid("The resource has no content.", string.Empty);

        var normalized = new StringBuilder();
        var elementStack = new Stack<string>();
        int index = source.StartsWith("\uFEFF", StringComparison.Ordinal) ? 1 : 0;

        while (index < source.Length)
        {
            if (source[index] == '<' && index + 1 < source.Length && source[index + 1] == '#')
            {
                int commentEnd = source.IndexOf("#>", index + 2, StringComparison.Ordinal);
                if (commentEnd < 0)
                    return Invalid("CUI040: CUI comment is missing its closing '#>' delimiter.", normalized.ToString());
                index = commentEnd + 2;
                continue;
            }

            if (TryReadReference(source, index, out int referenceEnd))
            {
                normalized.Append(ReferenceMarker);
                index = referenceEnd;
                continue;
            }

            if (source[index] == '<')
            {
                if (!TryReadTagEnd(source, index, out int tagEnd))
                    return Invalid("CUI043: CUI tag is missing its closing '>' delimiter.", normalized.ToString());

                string content = source.Substring(index + 1, tagEnd - index - 1);
                string? error = RewriteTag(content, normalized, elementStack);
                if (error is not null)
                    return Invalid(error, normalized.ToString());

                index = tagEnd + 1;
                continue;
            }

            normalized.Append(source[index]);
            index++;
        }

        if (elementStack.Count > 0)
            return Invalid($"CUI044: element '{elementStack.Peek()}' is not closed.", normalized.ToString());

        try
        {
            var document = XDocument.Parse(normalized.ToString());
            return new CuiLexicalValidationResult(true, document.Root?.Name.LocalName, null, normalized.ToString());
        }
        catch (XmlException ex)
        {
            return Invalid(ex.Message, normalized.ToString());
        }
    }

    private string? RewriteTag(string content, StringBuilder normalized, Stack<string> elementStack)
    {
        content = ReplaceTagReferences(content);

        if (content.StartsWith("!--", StringComparison.Ordinal)
            || content.StartsWith("!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("?", StringComparison.Ordinal))
        {
            normalized.Append('<').Append(content).Append('>');
            return null;
        }

        if (content.StartsWith("/", StringComparison.Ordinal))
        {
            string closingName = content.Substring(1).Trim();
            if (closingName == "$") closingName = "Property";
            else if (closingName == "&") closingName = "ActionEvent";

            if (elementStack.Count > 0)
            {
                string expected = elementStack.Peek();
                if (IsBuiltIn(expected) && string.Equals(expected, closingName, StringComparison.OrdinalIgnoreCase))
                    closingName = expected;
                if (string.Equals(expected, closingName, StringComparison.Ordinal))
                    elementStack.Pop();
            }

            normalized.Append("</").Append(closingName).Append('>');
            return null;
        }

        string trimmed = content.TrimEnd();
        bool selfClosing = trimmed.EndsWith("/", StringComparison.Ordinal);
        string header = selfClosing ? trimmed.Substring(0, trimmed.Length - 1).TrimEnd() : content.Trim();
        var tokens = SplitHeader(header);
        if (tokens.Count == 0)
            return "CUI043: CUI tag is missing its element name.";

        var attributes = new List<string>();
        string tagToken = tokens[0];
        string type;

        if (tagToken.StartsWith("&", StringComparison.Ordinal))
        {
            type = "ActionEvent";
            var eventInfo = ParseEventToken(tagToken.Substring(1));
            attributes.Add($"Type=\"{EscapeAttribute(eventInfo.Name)}\"");
            if (eventInfo.TargetId is not null)
                attributes.Add($"TargetID=\"{EscapeAttribute(eventInfo.TargetId)}\"");
            if (eventInfo.TargetGroup is not null)
                attributes.Add($"TargetGroup=\"{EscapeAttribute(eventInfo.TargetGroup)}\"");
        }
        else if (tagToken.StartsWith("@", StringComparison.Ordinal))
        {
            type = "Keyframe";
            attributes.Add($"Name=\"{EscapeAttribute(tagToken.Substring(1))}\"");
        }
        else if (tagToken.StartsWith("$", StringComparison.Ordinal))
        {
            type = "Property";
            string identityFree = ExtractIdentity(tagToken, attributes);
            var property = ParsePropertyToken(identityFree.Substring(1));
            if (property.Name.Length == 0 || property.Value.Length == 0)
                return "CUI041: a '$' property region requires a property name and value.";

            attributes.Add($"Property=\"{EscapeAttribute(property.Name)}\"");
            attributes.Add($"{property.Name}=\"{EscapeAttribute(property.Value.Replace('_', ' '))}\"");
        }
        else
        {
            bool isIndependent = tagToken.StartsWith("!", StringComparison.Ordinal);
            if (isIndependent) tagToken = tagToken.Substring(1);

            string identityFree = ExtractIdentity(tagToken, attributes);
            if (identityFree.StartsWith("Property=", StringComparison.OrdinalIgnoreCase))
            {
                type = "Property";
                string propertyName = identityFree.Substring("Property=".Length);
                if (propertyName.Length == 0)
                    return "CUI041: a Property region requires a property name and value.";

                attributes.Add($"Property=\"{EscapeAttribute(propertyName)}\"");
            }
            else
            {
                type = CanonicalBuiltInName(identityFree);
            }

            if (isIndependent)
                attributes.Add("ActiveIndependently=\"True\"");
        }

        for (int tokenIndex = 1; tokenIndex < tokens.Count; tokenIndex++)
        {
            string token = tokens[tokenIndex];

            // A quoted literal is an attribute value, not the canonical modifier.
            if (string.Equals(token, "(Definition)", StringComparison.OrdinalIgnoreCase))
            {
                attributes.Add("Definition=\"True\"");
                continue;
            }

            if (TryNormalizeAttribute(token, out string normalizedAttribute))
                attributes.Add(normalizedAttribute);
            else
                return $"CUI042: invalid CUI attribute syntax '{token}'.";
        }

        string attributeText = attributes.Count == 0 ? string.Empty : " " + string.Join(" ", attributes);
        normalized.Append('<').Append(type).Append(attributeText);

        if (selfClosing)
            normalized.Append("/>");
        else
        {
            normalized.Append('>');
            elementStack.Push(type);
        }

        return null;
    }

    private static string ReplaceTagReferences(string content)
    {
        var output = new StringBuilder(content.Length);
        char quote = '\0';
        int index = 0;

        while (index < content.Length)
        {
            if (TryReadReference(content, index, out int referenceEnd))
            {
                if (quote == '\0') output.Append('"');
                output.Append(ReferenceMarker);
                if (quote == '\0') output.Append('"');
                index = referenceEnd;
                continue;
            }

            char character = content[index++];
            if (quote != '\0')
            {
                if (character == quote && (index < 2 || content[index - 2] != '\\')) quote = '\0';
            }
            else if (character == '"' || character == '\'')
            {
                quote = character;
            }

            output.Append(character);
        }

        return output.ToString();
    }

    private static List<string> SplitHeader(string header)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';

        for (int index = 0; index < header.Length; index++)
        {
            char character = header[index];

            if (quote != '\0')
            {
                token.Append(character);
                if (character == quote && (index == 0 || header[index - 1] != '\\')) quote = '\0';
                continue;
            }

            if (character == '"' || character == '\'')
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
        char quote = '\0';

        for (int index = start + 1; index < source.Length; index++)
        {
            if (TryReadReference(source, index, out int referenceEnd))
            {
                index = referenceEnd - 1;
                continue;
            }

            char character = source[index];
            if (quote != '\0')
            {
                if (character == quote && source[index - 1] != '\\') quote = '\0';
                continue;
            }

            if (character == '"' || character == '\'') quote = character;
            else if (character == '>')
            {
                end = index;
                return true;
            }
        }

        end = source.Length;
        return false;
    }

    private static bool TryReadReference(string source, int start, out int end)
    {
        end = start;
        if (start < 0 || start + 3 >= source.Length || source[start] != '<') return false;

        int closing = source.IndexOf('>', start + 1);
        if (closing < 0) return false;

        var parts = source.Substring(start + 1, closing - start - 1).Split(':');
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[1])) return false;

        bool isUntyped = parts[0].Length == 0;
        bool isTyped = parts[0].Length > 0 && parts[0].All(character => char.IsLetterOrDigit(character) || character == '_' || character == '.');
        if (!isUntyped && !isTyped) return false;
        if (parts.Any(part => part.Any(char.IsWhiteSpace))) return false;

        end = closing + 1;
        return true;
    }

    private static string ExtractIdentity(string token, List<string> attributes)
    {
        int groupMarker = token.IndexOf(":::", StringComparison.Ordinal);
        string identityPrefix = groupMarker < 0 ? token : token.Substring(0, groupMarker);

        if (groupMarker >= 0)
        {
            string groups = token.Substring(groupMarker + 3);
            if (groups.Length > 0)
                attributes.Add($"Group=\"{EscapeAttribute(groups)}\"");
        }

        int idMarker = identityPrefix.IndexOf("::", StringComparison.Ordinal);
        if (idMarker < 0) return identityPrefix;

        string id = identityPrefix.Substring(idMarker + 2);
        if (id.Length > 0)
            attributes.Add($"ID=\"{EscapeAttribute(id)}\"");

        return identityPrefix.Substring(0, idMarker);
    }

    private static (string Name, string Value) ParsePropertyToken(string token)
    {
        int separator = token.IndexOfAny(new[] { ':', '=' });
        if (separator <= 0 || separator >= token.Length - 1)
            return (string.Empty, string.Empty);

        return (token.Substring(0, separator), token.Substring(separator + 1));
    }

    private static (string Name, string? TargetId, string? TargetGroup) ParseEventToken(string token)
    {
        int targetStart = token.IndexOf('{');
        if (targetStart < 0 || !token.EndsWith("}", StringComparison.Ordinal)) return (token, null, null);

        string name = token.Substring(0, targetStart);
        string target = token.Substring(targetStart + 1, token.Length - targetStart - 2);

        if (target.StartsWith(":::", StringComparison.Ordinal))
            return (name, null, target.Substring(3));
        if (target.StartsWith("::", StringComparison.Ordinal))
            return (name, target.Substring(2), null);

        return (token, null, null);
    }

    private static bool TryNormalizeAttribute(string token, out string normalized)
    {
        normalized = string.Empty;

        if (token.IndexOf('=') > 0)
        {
            normalized = token;
            return true;
        }

        int colon = token.IndexOf(':');
        if (colon <= 0 || colon >= token.Length - 1 || token.IndexOf("::", StringComparison.Ordinal) >= 0)
            return false;

        string name = token.Substring(0, colon);
        string value = token.Substring(colon + 1).Replace('_', ' ');
        normalized = $"{name}=\"{EscapeAttribute(value)}\"";
        return true;
    }

    private static bool IsBuiltIn(string name) => BuiltInNames.Contains(CanonicalBuiltInName(name));

    private static string CanonicalBuiltInName(string name) =>
        BuiltInNames.FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) ?? name;

    private static string EscapeAttribute(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static CuiLexicalValidationResult Invalid(string error, string normalized) =>
        new(false, null, error, normalized);
}
