using System.Xml;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Dev;

public static partial class DeveloperCuiPropertyEditor
{
    private sealed record ChildAnchor(string ElementName, int Offset, int ReplacedLength, bool Empty, int ElementCount);

    /// <summary>Adds one literal Text child to an exact original XML-shaped parent. XML reader
    /// locations are verified against original tag bytes; CUI parsing/compilation remains owned
    /// by the maintained language pipeline. Returns an unsaved draft only, with no IO or grant.</summary>
    public static DeveloperCuiPropertyChange AddTextChild(DeveloperCuiPropertyInspection inspection, string currentText,
        string childId, string value, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(inspection); ArgumentNullException.ThrowIfNull(currentText);
        ArgumentNullException.ThrowIfNull(childId); ArgumentNullException.ThrowIfNull(value); token.ThrowIfCancellationRequested();
        if (!DeveloperCuiSourceAnalysis.Supports(inspection.RelativePath, currentText))
            return new(currentText, "Add Text supports .cui source up to 256,000 characters.");
        if (Hash(currentText) != inspection.ContentSha256)
            return new(currentText, "The source changed. Check CUI and select the current parent again.");
        if (childId.Length is < 1 or > 128 || value.Length > MaximumPropertyCharacters)
            return new(currentText, "Enter a component ID up to 128 characters and text up to 16,000 characters.");
        try { XmlConvert.VerifyNCName(childId); XmlConvert.VerifyXmlChars(value); }
        catch (XmlException) { return new(currentText, "Use a valid XML component ID and text characters."); }
        if (CuiRichParser.ParseValue(value, CakeOS.Cui.Language.CuiSourceSpan.At(inspection.RelativePath, 1, 1)) is not CuiLiteralValue)
            return new(currentText, "Add Text accepts fixed text. Enter bindings and resources in source.");
        ChildAnchor anchor;
        try { anchor = FindChildAnchor(currentText, inspection.AuthoredId, childId, token); }
        catch (XmlException error) { return new(currentText, "Add Text needs XML-shaped CUI. Use source for shorthand. " + error.Message); }
        catch (NotSupportedException error) { return new(currentText, error.Message); }
        if (anchor.ElementCount >= DeveloperCuiDraftPreviewCompiler.MaximumElements)
            return new(currentText, "Adding this child would exceed the 500-element designer limit.");
        var fresh = Capture(inspection.RelativePath, currentText, inspection.AuthoredId, token);
        var parent = fresh.Component;
        if (fresh.Element is null || fresh.Inspection.UnavailableReason is not null || parent is null || fresh.Document is null ||
            !SupportsTextChild(parent) || !IsBasicAuthoringContext(fresh.Document, parent))
            return new(currentText, fresh.Inspection.UnavailableReason ?? "Select a Page or basic layout Container. Definitions, templates, repeats, theme scopes and custom layouts stay in source.");
        static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal).Replace("\r", "&#xD;", StringComparison.Ordinal)
            .Replace("\n", "&#xA;", StringComparison.Ordinal).Replace("\t", "&#x9;", StringComparison.Ordinal);
        var child = "<Text id=\"" + Escape(childId) + "\" text=\"" + Escape(value) + "\" />";
        var insertion = anchor.Empty ? ">" + child + "</" + anchor.ElementName + ">" : child;
        if ((long)currentText.Length - anchor.ReplacedLength + insertion.Length > DeveloperCuiSourceAnalysis.MaximumAnalysisCharacters)
            return new(currentText, "The changed source would exceed the 256,000-character designer limit.");
        var revised = string.Concat(currentText.AsSpan(0, anchor.Offset), insertion.AsSpan(), currentText.AsSpan(anchor.Offset + anchor.ReplacedLength));
        token.ThrowIfCancellationRequested();
        var options = new CuiCompilerOptions(new(CuiRuntimeCompatibility.LanguageVersion,
            CuiRuntimeCompatibility.RuntimeAbiVersion, CuiControlRegistry.ComponentRegistryVersion));
        // Compiler-only CPU work, with no import resolver, runtime scene or execution adapter.
        var compilation = new CuiCompiler().Compile(revised, inspection.RelativePath, options);
        token.ThrowIfCancellationRequested();
        if (!compilation.Succeeded || compilation.Output is null)
            return new(currentText, "The change did not compile. " + string.Join(" ", compilation.Diagnostics.Take(5)));
        var parents = Descendants(compilation.Output.Semantics.Components)
            .Concat(compilation.Output.Semantics.Templates.Values.SelectMany(template => Descendants(template.Content)))
            .Where(component => component.AuthoredId == inspection.AuthoredId).ToArray();
        var children = parents.Length == 1 ? parents[0].Children.Where(component => component.AuthoredId == childId).ToArray() : Array.Empty<CuiComponent>();
        if (children.Length != 1 || children[0].Type != "Text" || !children[0].TryGetLiteralAttribute("text", out var actual) || actual != value)
            return new(currentText, "The compiler could not verify the new text in the selected parent. The draft is unchanged.");
        return new(revised);
    }

    private static bool SupportsTextChild(CuiComponent parent)
    {
        if (parent.IsDefinition || parent.IsThemeScope || parent.List is not null || parent.Repeat is not null || parent.PropertyRegion is not null) return false;
        var type = parent.Properties.FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value;
        if (parent.Type.Equals("Page", StringComparison.OrdinalIgnoreCase)) return type is null;
        return parent.Type.Equals("Container", StringComparison.OrdinalIgnoreCase) && type is CuiLiteralValue literal &&
            new[] { "Vertical Stack", "Horizontal Stack", "Wrap", "Grid", "Absolute", "Overlay", "Overlay Float", "Floating" }.Contains(literal.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBasicAuthoringContext(CuiDocument document, CuiComponent selected)
    {
        // Use the authoritative semantic scopes, not guessed XML tag spellings. Template
        // bodies are deliberately absent here; repeated/definition/region scopes stop traversal.
        var pending = new Stack<CuiComponent>(document.Components);
        while (pending.TryPop(out var node))
        {
            if (!SupportsTextChild(node)) continue;
            if (ReferenceEquals(node, selected)) return true;
            foreach (var child in node.Children) pending.Push(child);
            foreach (var child in node.ElseChildren) pending.Push(child);
        }
        return false;
    }

    private static ChildAnchor FindChildAnchor(string source, string parentId, string childId, CancellationToken token)
    {
        using var input = new StringReader(source);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = DeveloperCuiSourceAnalysis.MaximumAnalysisCharacters });
        ChildAnchor? anchor = null; var parents = 0; var count = 0; var parentDepth = -1; string? parentName = null;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.Depth > DeveloperCuiDraftPreviewCompiler.MaximumDepth)
                throw new NotSupportedException("Add Text supports at most 32 nested levels.");
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (++count > DeveloperCuiDraftPreviewCompiler.MaximumElements)
                    throw new NotSupportedException("Add Text supports at most 500 elements.");
                var name = reader.Name; var depth = reader.Depth; var empty = reader.IsEmptyElement;
                var line = ((IXmlLineInfo)reader).LineNumber; var column = ((IXmlLineInfo)reader).LinePosition; var selected = false;
                if (reader.MoveToFirstAttribute())
                {
                    do
                    {
                        if (reader.NamespaceURI.Length != 0 || !(reader.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase) || reader.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))) continue;
                        if (reader.Value == childId) throw new NotSupportedException("This component ID already exists. Choose a different ID.");
                        if (reader.Value == parentId) selected = true;
                    } while (reader.MoveToNextAttribute());
                    reader.MoveToElement();
                }
                if (!selected) continue;
                if (++parents != 1) throw new NotSupportedException("The parent ID is duplicated. Give the parent a unique ID in source.");
                parentDepth = depth; parentName = name;
                if (empty)
                {
                    var start = VerifyTagAnchor(source, line, column, "<" + name, token);
                    var end = FindOriginalTagEnd(source, start, token);
                    if (end < 1 || source[end - 1] != '/') throw new NotSupportedException("The empty parent tag could not be verified.");
                    anchor = new(name, end - 1, 2, true, 0);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == parentDepth && reader.Name == parentName && anchor is null)
            {
                var info = (IXmlLineInfo)reader;
                anchor = new(reader.Name, VerifyTagAnchor(source, info.LineNumber, info.LinePosition, "</" + reader.Name, token), 0, false, 0);
            }
        }
        return parents == 1 && anchor is not null ? anchor with { ElementCount = count }
            : throw new NotSupportedException("Select one uniquely named XML-shaped parent. Shorthand stays in source.");
    }

    private static int VerifyTagAnchor(string source, int line, int column, string prefix, CancellationToken token)
    {
        if (DeveloperTextNavigation.TryResolve(source, FormattableString.Invariant($"{line}:{column}"), out var reported, token))
            for (var start = Math.Max(0, reported - 2); start <= reported; start++)
            {
                if (!source.AsSpan(start).StartsWith(prefix, StringComparison.Ordinal)) continue;
                var end = start + prefix.Length;
                if (end < source.Length && (char.IsWhiteSpace(source[end]) || source[end] is '/' or '>')) return start;
            }
        throw new NotSupportedException("The original parent tag position could not be verified. Edit this component in source.");
    }

    private static int FindOriginalTagEnd(string source, int start, CancellationToken token)
    {
        var quote = '\0';
        for (var index = start; index < source.Length; index++)
        {
            if ((index & 1023) == 0) token.ThrowIfCancellationRequested();
            var next = source[index];
            if (quote != '\0') { if (next == quote) quote = '\0'; }
            else if (next is '\'' or '"') quote = next;
            else if (next == '>') return index;
        }
        throw new NotSupportedException("The original parent tag is incomplete.");
    }
}
