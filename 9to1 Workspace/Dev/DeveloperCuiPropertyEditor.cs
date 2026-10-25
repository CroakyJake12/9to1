using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Dev;

public sealed record DeveloperCuiInspectableProperty(string Name, string Value, string LanguageType, bool CanEdit, string? UnavailableReason);
public sealed record DeveloperCuiPropertyInspection(Guid Id, string RelativePath, string ContentSha256, string AuthoredId,
    string ComponentType, IReadOnlyList<DeveloperCuiInspectableProperty> Properties, string? UnavailableReason)
{
    public bool CanAddTextChild { get; init; }
    public bool CanRemoveText { get; init; }
    public bool CanMoveTextUp { get; init; }
    public bool CanMoveTextDown { get; init; }
}
public sealed record DeveloperCuiPropertyChange(string Text, string? Error = null);

/// <summary>Bounded authoring edits of existing literal attributes in XML-shaped CUI. The
/// authoritative semantic parser verifies identity/value meaning; System.Xml supplies positions
/// in ORIGINAL text, since CUI semantic spans describe normalized shorthand. Never serializes a
/// document or rewrites unsupported syntax. Returns a draft only, with no IO or write authority.</summary>
public static partial class DeveloperCuiPropertyEditor
{
    public const int MaximumPropertyCharacters = 16_000;
    private sealed record Captured(DeveloperCuiPropertyInspection Inspection, XElement? Element)
    {
        public CuiComponent? Component { get; init; }
        public CuiDocument? Document { get; init; }
    }
    public static DeveloperCuiPropertyInspection Inspect(string path, string text, string authoredId, CancellationToken token = default) =>
        Capture(path, text, authoredId, token).Inspection;

    public static DeveloperCuiPropertyChange Change(DeveloperCuiPropertyInspection inspection, string currentText,
        string propertyName, string value, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(inspection); ArgumentNullException.ThrowIfNull(currentText);
        ArgumentNullException.ThrowIfNull(propertyName); ArgumentNullException.ThrowIfNull(value);
        token.ThrowIfCancellationRequested();
        if (Hash(currentText) != inspection.ContentSha256)
            return new(currentText, "The source changed after inspection. Check and select the current component again.");
        if (inspection.UnavailableReason is { } unavailable) return new(currentText, unavailable);
        var fresh = Capture(inspection.RelativePath, currentText, inspection.AuthoredId, token);
        var property = fresh.Inspection.Properties.SingleOrDefault(item => item.Name == propertyName);
        if (fresh.Element is null || property is not { CanEdit: true })
            return new(currentText, property?.UnavailableReason ?? fresh.Inspection.UnavailableReason ?? "Select an editable existing literal attribute.");
        if (value.Length > MaximumPropertyCharacters) return new(currentText, "This literal exceeds the bounded property editor limit.");
        var registry = CuiControlRegistry.Default;
        if (!registry.TryGetProperty(propertyName, out var descriptor) ||
            CuiRichParser.ParseValue(value, CuiSourceSpan.At(inspection.RelativePath, 1, 1)) is not CuiLiteralValue || !ValidLiteral(descriptor, value))
            return new(currentText, "Enter a valid literal for this registered property. Bindings, resources and unsupported value forms stay code-defined.");
        try { XmlConvert.VerifyXmlChars(value); }
        catch (XmlException) { return new(currentText, "The literal contains characters XML cannot represent."); }
        if (value == property.Value) return new(currentText);
        var attribute = fresh.Element.Attributes().Single(item => item.Name.LocalName == propertyName);
        var info = (IXmlLineInfo)attribute;
        if (!DeveloperTextNavigation.TryResolve(currentText, FormattableString.Invariant($"{info.LineNumber}:{info.LinePosition}"), out var start, token) ||
            !currentText.AsSpan(start).StartsWith(propertyName, StringComparison.Ordinal))
            return new(currentText, "The original attribute position could not be verified; edit this property in source.");
        var cursor = start + propertyName.Length;
        while (cursor < currentText.Length && char.IsWhiteSpace(currentText[cursor])) cursor++;
        if (cursor >= currentText.Length || currentText[cursor++] != '=') return new(currentText, "The original attribute boundary changed.");
        while (cursor < currentText.Length && char.IsWhiteSpace(currentText[cursor])) cursor++;
        if (cursor >= currentText.Length || currentText[cursor] is not ('\'' or '"')) return new(currentText, "The original quoted attribute boundary could not be verified.");
        var quote = currentText[cursor++]; var end = currentText.IndexOf(quote, cursor);
        if (end < cursor) return new(currentText, "The original quoted attribute is incomplete.");
        var encoded = value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(quote.ToString(), quote == '\'' ? "&apos;" : "&quot;", StringComparison.Ordinal)
            .Replace("\r", "&#xD;", StringComparison.Ordinal).Replace("\n", "&#xA;", StringComparison.Ordinal).Replace("\t", "&#x9;", StringComparison.Ordinal);
        if ((long)currentText.Length - (end - cursor) + encoded.Length > DeveloperCuiSourceAnalysis.MaximumAnalysisCharacters)
            return new(currentText, "The complete changed source exceeds the bounded inspector limit; the original draft was preserved.");
        var revised = string.Concat(currentText.AsSpan(0, cursor), encoded.AsSpan(), currentText.AsSpan(end));
        var verified = Capture(inspection.RelativePath, revised, inspection.AuthoredId, token).Inspection;
        var changed = verified.Properties.SingleOrDefault(item => item.Name == propertyName);
        if (verified.UnavailableReason is not null || changed is not { CanEdit: true } || changed.Value != value)
            return new(currentText, "The CUI parser could not verify the exact new literal. The original source was preserved.");
        token.ThrowIfCancellationRequested();
        return new(revised);
    }

    private static Captured Capture(string path, string text, string authoredId, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredId); token.ThrowIfCancellationRequested();
        var hash = Hash(text);
        Captured Refuse(string reason) => new(new(Guid.NewGuid(), path, hash, authoredId, "", [], reason), null);
        if (!DeveloperCuiSourceAnalysis.Supports(path, text)) return Refuse("The property inspector requires CUI source of at most 256,000 characters.");
        XDocument xml;
        try
        {
            using var input = new StringReader(text);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = DeveloperCuiSourceAnalysis.MaximumAnalysisCharacters });
            xml = XDocument.Load(reader, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (XmlException) { return Refuse("This source uses shorthand or syntax outside the bounded XML-shaped inspector. Keep editing it in source; it will not be normalized or rewritten."); }
        var targets = xml.Root?.DescendantsAndSelf().Where(element => element.Attributes().Any(attribute =>
            attribute.Name.Namespace == XNamespace.None && (attribute.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase) ||
            attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase)) && attribute.Value == authoredId)).ToArray() ?? [];
        if (targets.Length != 1) return Refuse("A unique authored id is required for property editing. Duplicate or ambiguous identities remain code-defined.");
        var parser = new CuiRichParser(); var document = parser.Parse(text, path);
        token.ThrowIfCancellationRequested();
        if (parser.Diagnostics.Diagnostics.Any(problem => problem.Severity == CuiDiagnosticSeverity.Error))
            return Refuse("Resolve the reported CUI syntax problems before using the property inspector.");
        var components = Descendants(document.Components).Concat(document.Templates.Values.SelectMany(template => Descendants(template.Content)))
            .Where(component => component.AuthoredId == authoredId).ToArray();
        if (components.Length != 1) return Refuse("The authoritative CUI parser could not uniquely identify this component.");
        var selectedComponent = components[0]; var target = targets[0]; var registry = CuiControlRegistry.Default;
        if (target.Name.Namespace != XNamespace.None || !target.Name.LocalName.Equals(selectedComponent.Type, StringComparison.OrdinalIgnoreCase) ||
            !registry.TryResolveElement(selectedComponent.Type, out var elementDescriptor))
            return Refuse("This component requires a custom source adapter; only registered built-in components are editable here.");
        var type = selectedComponent.Properties.FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value as CuiLiteralValue;
        var properties = new List<DeveloperCuiInspectableProperty>();
        foreach (var attribute in target.Attributes())
        {
            token.ThrowIfCancellationRequested();
            if (attribute.Name.Namespace != XNamespace.None || !registry.TryGetProperty(attribute.Name.LocalName, out var descriptor)) continue;
            var values = selectedComponent.Properties.Where(pair => registry.TryGetProperty(pair.Key, out var mapped) && mapped.Name == descriptor.Name).ToArray();
            if (values.Length == 0) continue; // structural identity/actions are not visual property values
            var duplicates = target.Attributes().Count(other => registry.TryGetProperty(other.Name.LocalName, out var mapped) && mapped.Name == descriptor.Name);
            var literal = values.Length == 1 ? values[0].Value as CuiLiteralValue : null;
            var reason = duplicates != 1 ? "Aliased or repeated properties remain code-defined."
                : literal is null ? "Bindings, resources and expressions remain code-defined."
                : literal.Value != attribute.Value ? "The original attribute and CUI value could not be matched exactly."
                : !descriptor.IsWritable || !registry.SupportsProperty(elementDescriptor, type?.Value, descriptor) ? "This property is not a writable registered visual property."
                : !SupportedLiteral(descriptor) ? "This property's structured value requires its own editor; use source."
                : attribute.Value.Length > MaximumPropertyCharacters ? "This literal exceeds the bounded property editor limit." : null;
            properties.Add(new(attribute.Name.LocalName, attribute.Value, descriptor.LanguageType, reason is null, reason));
        }
        var textContext = TextChildEditingContext(document, selectedComponent, target);
        var canMoveUp = textContext is { Index: > 0 } up && SimpleLiteralText(up.Parent.Children[up.Index - 1], up.Elements[up.Index - 1]);
        var canMoveDown = textContext is { } down && down.Index + 1 < down.Parent.Children.Count &&
            SimpleLiteralText(down.Parent.Children[down.Index + 1], down.Elements[down.Index + 1]);
        return new(new(Guid.NewGuid(), path, hash, authoredId, selectedComponent.Type, Array.AsReadOnly(properties.ToArray()), null)
            { CanAddTextChild = SupportsTextChild(selectedComponent) && IsBasicAuthoringContext(document, selectedComponent),
                CanRemoveText = textContext is not null, CanMoveTextUp = canMoveUp, CanMoveTextDown = canMoveDown }, target)
            { Component = selectedComponent, Document = document };
    }

    private static bool SupportedLiteral(CuiRuntimePropertyDescriptor property) => property.LanguageType is "text" or "content" or "boolean" or "integer" or "number" or "length" || property.AllowedValues.Count > 0;
    private static bool ValidLiteral(CuiRuntimePropertyDescriptor property, string value) => property.AllowedValues.Count > 0 ? property.AllowedValues.Contains(value) : property.LanguageType switch
    {
        "text" or "content" => true,
        "boolean" => bool.TryParse(value, out _),
        "integer" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        "number" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
        "length" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) && double.IsFinite(length) && length >= 0,
        _ => false
    };
    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> components)
    {
        foreach (var component in components)
        {
            yield return component;
            foreach (var child in Descendants(component.Children)) yield return child;
            foreach (var child in Descendants(component.ElseChildren)) yield return child;
        }
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
