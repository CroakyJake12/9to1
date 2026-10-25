using System.Xml;
using System.Xml.Linq;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Dev;

public enum DeveloperCuiTextChildEdit { MoveUp, MoveDown, Remove }

public static partial class DeveloperCuiPropertyEditor
{
    private sealed record TextChildContext(CuiComponent Parent, int Index, XElement[] Elements);
    private sealed record OriginalTextElementSpan(int Start, int Length);

    private static bool SimpleLiteralText(CuiComponent component, XElement element) =>
        component.Type == "Text" && element.IsEmpty && element.Name.Namespace == XNamespace.None &&
        !string.IsNullOrWhiteSpace(component.AuthoredId) && component.Children.Count == 0 && component.ElseChildren.Count == 0 &&
        component.Actions.Count == 0 && component.Condition is null && !component.IsDefinition && !component.IsThemeScope &&
        component.List is null && component.Repeat is null && component.PropertyRegion is null &&
        component.Properties.Values.All(value => value is CuiLiteralValue) && component.TryGetLiteralAttribute("text", out _) &&
        element.Attributes().All(attribute => attribute.Name.Namespace == XNamespace.None && (IdentityAttribute(attribute) ||
            CuiControlRegistry.Default.TryGetProperty(attribute.Name.LocalName, out var property) && SupportedLiteral(property))) &&
        element.Attributes().Count(IdentityAttribute) == 1 && element.Attributes().Any(attribute => IdentityAttribute(attribute) && attribute.Value == component.AuthoredId) &&
        element.Document!.Root!.DescendantsAndSelf().Count(node => node.Attributes().Any(attribute => IdentityAttribute(attribute) && attribute.Value == component.AuthoredId)) == 1;

    private static bool IdentityAttribute(XAttribute attribute) => attribute.Name.Namespace == XNamespace.None &&
        (attribute.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase) || attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));

    private static bool PlainTextHierarchyContext(CuiDocument document, CuiComponent selectedParent)
    {
        var pending = new Stack<CuiComponent>(document.Components);
        while (pending.TryPop(out var node))
        {
            if (!SupportsTextChild(node) || node.Condition is not null || node.ElseChildren.Count != 0) continue;
            if (ReferenceEquals(node, selectedParent)) return true;
            foreach (var child in node.Children) pending.Push(child);
        }
        return false;
    }

    private static TextChildContext? TextChildEditingContext(CuiDocument document, CuiComponent selected, XElement element)
    {
        if (!SimpleLiteralText(selected, element) || element.Parent is not { } xmlParent) return null;
        var parents = Descendants(document.Components).Where(parent => parent.Children.Any(child => ReferenceEquals(child, selected))).ToArray();
        if (parents.Length != 1) return null;
        var parent = parents[0];
        if (!SupportsTextChild(parent) || !PlainTextHierarchyContext(document, parent) || string.IsNullOrWhiteSpace(parent.AuthoredId) ||
            xmlParent.Name.Namespace != XNamespace.None || !xmlParent.Name.LocalName.Equals(parent.Type, StringComparison.OrdinalIgnoreCase) ||
            xmlParent.Attributes().Count(IdentityAttribute) != 1 || !xmlParent.Attributes().Any(attribute => IdentityAttribute(attribute) && attribute.Value == parent.AuthoredId) ||
            xmlParent.Document!.Root!.DescendantsAndSelf().Count(node => node.Attributes().Any(attribute => IdentityAttribute(attribute) && attribute.Value == parent.AuthoredId)) != 1)
            return null;
        var elements = xmlParent.Elements().ToArray();
        if (elements.Length != parent.Children.Count) return null;
        for (var index = 0; index < elements.Length; index++)
        {
            var component = parent.Children[index]; var xml = elements[index];
            if (xml.Name.Namespace != XNamespace.None || !xml.Name.LocalName.Equals(component.Type, StringComparison.OrdinalIgnoreCase) ||
                component.AuthoredId is { } id && !xml.Attributes().Any(attribute => IdentityAttribute(attribute) && attribute.Value == id)) return null;
        }
        var selectedIndex = Array.FindIndex(elements, node => ReferenceEquals(node, element));
        return selectedIndex >= 0 ? new(parent, selectedIndex, elements) : null;
    }

    /// <summary>Moves/removes only self-closing literal Text element lexemes. Original surrounding
    /// comments, whitespace, entities and other elements stay byte-for-byte in their source slots.
    /// The maintained compiler must verify the complete proposed source before it becomes a draft.</summary>
    public static DeveloperCuiPropertyChange EditTextChild(DeveloperCuiPropertyInspection inspection, string currentText,
        DeveloperCuiTextChildEdit edit, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(inspection); ArgumentNullException.ThrowIfNull(currentText);
        if (!Enum.IsDefined(edit)) throw new ArgumentOutOfRangeException(nameof(edit));
        token.ThrowIfCancellationRequested();
        if (!DeveloperCuiSourceAnalysis.Supports(inspection.RelativePath, currentText) || Hash(currentText) != inspection.ContentSha256)
            return new(currentText, "The source changed or exceeds the designer limit. Check CUI and select the current Text again.");
        Dictionary<string, OriginalTextElementSpan?> spans;
        try { spans = CaptureOriginalTextElements(currentText, token); }
        catch (Exception error) when (error is XmlException or NotSupportedException)
        { return new(currentText, error.Message); }
        var fresh = Capture(inspection.RelativePath, currentText, inspection.AuthoredId, token);
        var context = fresh.Element is { } element && fresh.Document is { } document && fresh.Component is { } component
            ? TextChildEditingContext(document, component, element) : null;
        if (fresh.Inspection.UnavailableReason is not null || context is null ||
            !spans.TryGetValue(inspection.AuthoredId, out var selected) || selected is null)
            return new(currentText, fresh.Inspection.UnavailableReason ?? "Select a uniquely named self-closing literal Text in a named Page or basic layout Container. Bindings, actions, shorthand and managed scopes stay in source.");
        var expected = context.Parent.Children.Select(child => (child.Type, child.AuthoredId)).ToList();
        string revised;
        if (edit == DeveloperCuiTextChildEdit.Remove)
        {
            revised = currentText.Remove(selected.Start, selected.Length); expected.RemoveAt(context.Index);
        }
        else
        {
            var otherIndex = context.Index + (edit == DeveloperCuiTextChildEdit.MoveUp ? -1 : 1);
            if (otherIndex < 0 || otherIndex >= context.Parent.Children.Count ||
                !SimpleLiteralText(context.Parent.Children[otherIndex], context.Elements[otherIndex]) ||
                !spans.TryGetValue(context.Parent.Children[otherIndex].AuthoredId!, out var other) || other is null)
                return new(currentText, "Move supports an adjacent self-closing literal Text sibling. Other controls and managed constructs stay in source.");
            var left = selected.Start < other.Start ? selected : other; var right = ReferenceEquals(left, selected) ? other : selected;
            if ((long)left.Start + left.Length > right.Start)
                return new(currentText, "The original Text boundaries overlap. The draft is unchanged.");
            revised = string.Concat(currentText.AsSpan(0, left.Start), currentText.AsSpan(right.Start, right.Length),
                currentText.AsSpan(left.Start + left.Length, right.Start - left.Start - left.Length)) +
                currentText.Substring(left.Start, left.Length) + currentText[(right.Start + right.Length)..];
            (expected[context.Index], expected[otherIndex]) = (expected[otherIndex], expected[context.Index]);
        }
        token.ThrowIfCancellationRequested();
        var options = new CuiCompilerOptions(new(CuiRuntimeCompatibility.LanguageVersion,
            CuiRuntimeCompatibility.RuntimeAbiVersion, CuiControlRegistry.ComponentRegistryVersion));
        var compilation = new CuiCompiler().Compile(revised, inspection.RelativePath, options);
        token.ThrowIfCancellationRequested();
        if (!compilation.Succeeded || compilation.Output is null)
            return new(currentText, "The change did not compile. " + string.Join(" ", compilation.Diagnostics.Take(5)));
        var compiled = Descendants(compilation.Output.Semantics.Components)
            .Concat(compilation.Output.Semantics.Templates.Values.SelectMany(template => Descendants(template.Content))).ToArray();
        var parentAfter = compiled.Where(node => node.AuthoredId == context.Parent.AuthoredId).ToArray();
        if (parentAfter.Length != 1 || !parentAfter[0].Children.Select(child => (child.Type, child.AuthoredId)).SequenceEqual(expected) ||
            edit == DeveloperCuiTextChildEdit.Remove && compiled.Any(node => node.AuthoredId == inspection.AuthoredId))
            return new(currentText, "The compiler could not verify the requested Text hierarchy. The draft is unchanged.");
        return new(revised);
    }

    private static Dictionary<string, OriginalTextElementSpan?> CaptureOriginalTextElements(string source, CancellationToken token)
    {
        using var input = new StringReader(source);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = DeveloperCuiSourceAnalysis.MaximumAnalysisCharacters });
        var result = new Dictionary<string, OriginalTextElementSpan?>(StringComparer.Ordinal); var count = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.Depth > DeveloperCuiDraftPreviewCompiler.MaximumDepth)
                throw new NotSupportedException("Text hierarchy editing supports at most 32 nested levels.");
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (++count > DeveloperCuiDraftPreviewCompiler.MaximumElements)
                throw new NotSupportedException("Text hierarchy editing supports at most 500 elements.");
            OriginalTextElementSpan? span = null;
            if (reader.NamespaceURI.Length == 0 && reader.LocalName.Equals("Text", StringComparison.OrdinalIgnoreCase) && reader.IsEmptyElement)
            {
                var info = (IXmlLineInfo)reader;
                var start = VerifyTagAnchor(source, info.LineNumber, info.LinePosition, "<" + reader.Name, token);
                var end = FindOriginalTagEnd(source, start, token);
                if (end < start + 1 || source[end - 1] != '/') throw new NotSupportedException("The original Text tag boundary could not be verified.");
                span = new(start, end + 1 - start);
            }
            if (!reader.MoveToFirstAttribute()) continue;
            do
            {
                if (reader.NamespaceURI.Length != 0 || !(reader.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase) || reader.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))) continue;
                if (!result.TryAdd(reader.Value, span)) result[reader.Value] = null;
            } while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        return result;
    }
}
