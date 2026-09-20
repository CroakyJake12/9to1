using System.Xml;
using System.Xml.Linq;

namespace CakeOS.Cui.Markup;

public sealed record CuiDocument(string SourceName, CuiElement Root);

public sealed record CuiElement(
    string Name,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<CuiElement> Children,
    string Text);

public sealed class CuiMarkupException(string sourceName, int line, int column, string message, Exception? inner = null)
    : FormatException($"{sourceName}:{line}:{column}: {message}", inner)
{
    public string SourceName { get; } = sourceName;
    public int Line { get; } = line;
    public int Column { get; } = column;
}

/// <summary>Parses the deliberately renderer-neutral CUI document tree.</summary>
public sealed class CuiMarkupParser
{
    public CuiDocument Parse(string source, string sourceName = "markup.cui")
    {
        ArgumentNullException.ThrowIfNull(source);
        CuiMarkupFormat.RequireCuiInput(sourceName);

        try
        {
            var document = XDocument.Parse(source, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            var root = document.Root ?? throw new CuiMarkupException(sourceName, 1, 1, "CUI markup has no root element.");
            var lineInfo = (IXmlLineInfo)root;
            if (root.Name.Namespace != XNamespace.None)
                throw new CuiMarkupException(sourceName, lineInfo.LineNumber, lineInfo.LinePosition, "CUI markup does not accept XML namespaces.");
            if (!root.Name.LocalName.Equals("Cui", StringComparison.Ordinal))
                throw new CuiMarkupException(sourceName, lineInfo.LineNumber, lineInfo.LinePosition, "CUI markup must have a <Cui> root element.");

            return new CuiDocument(sourceName, ParseElement(root, sourceName));
        }
        catch (CuiMarkupException)
        {
            throw;
        }
        catch (XmlException exception)
        {
            throw new CuiMarkupException(sourceName, exception.LineNumber, exception.LinePosition, exception.Message, exception);
        }
    }

    private static CuiElement ParseElement(XElement element, string sourceName)
    {
        var lineInfo = (IXmlLineInfo)element;
        if (element.Name.Namespace != XNamespace.None)
            throw new CuiMarkupException(sourceName, lineInfo.LineNumber, lineInfo.LinePosition, "CUI markup does not accept XML namespaces.");

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes())
        {
            var attributeInfo = (IXmlLineInfo)attribute;
            if (attribute.IsNamespaceDeclaration || attribute.Name.Namespace != XNamespace.None)
                throw new CuiMarkupException(sourceName, attributeInfo.LineNumber, attributeInfo.LinePosition, "CUI markup does not accept XML namespaces.");
            attributes.Add(attribute.Name.LocalName, attribute.Value);
        }

        var children = element.Elements().Select(child => ParseElement(child, sourceName)).ToArray();
        var text = string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)).Trim();
        return new CuiElement(element.Name.LocalName, attributes, children, text);
    }
}

/// <summary>Loads native CUI markup from disk after enforcing the CUI-only input boundary.</summary>
public sealed class CuiMarkupLoader
{
    private readonly CuiMarkupParser _parser = new();

    public CuiDocument Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        CuiMarkupFormat.RequireCuiInput(fullPath);
        return _parser.Parse(File.ReadAllText(fullPath), fullPath);
    }
}

public static class CuiMarkupFormat
{
    public const string Extension = ".cui";

    public static bool IsCuiInput(string sourceName) =>
        string.Equals(Path.GetExtension(sourceName), Extension, StringComparison.OrdinalIgnoreCase);

    public static void RequireCuiInput(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (!IsCuiInput(sourceName))
            throw new NotSupportedException(
                $"CUI accepts only {Extension} inputs. Legacy .axaml and .hui inputs are rejected: '{sourceName}'.");
    }
}
