using CakeOS.Cui.Markup;

namespace HavenOS.Apps.Data.Cui;

/// <summary>Loads and validates the application-owned external CUI document.</summary>
public sealed class DataCuiSurfaceDefinition
{
    private static readonly IReadOnlyDictionary<string, string> RequiredElements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["data-workspace"] = "Workspace",
        ["formula-bar"] = "FormulaBar",
        ["sheet-tabs"] = "SheetTabs",
        ["spreadsheet-grid"] = "Grid",
        ["sort-panel"] = "SortPanel",
        ["filter-panel"] = "FilterPanel",
        ["query-panel"] = "QueryPanel",
        ["query-editor"] = "CodeEditor",
        ["query-results"] = "Table",
    };

    private static readonly string[] RequiredActions =
    [
        DataSelectCellAction.Id,
        DataEditCellAction.Id,
        DataSetFormulaAction.Id,
        DataSortVisibleRangeAction.Id,
        DataFilterVisibleRangeAction.Id,
        DataClearVisibleFilterAction.Id,
        DataRunReadOnlyQueryAction.Id,
        DataSaveWorkbookAsAction.Id,
    ];

    private DataCuiSurfaceDefinition(CuiDocument document, IReadOnlyDictionary<string, CuiElement> elements)
    {
        Document = document;
        Elements = elements;
    }

    public CuiDocument Document { get; }
    public IReadOnlyDictionary<string, CuiElement> Elements { get; }

    public static DataCuiSurfaceDefinition LoadDefault() =>
        Load(Path.Combine(AppContext.BaseDirectory, "DataWorkspace.cui"));

    public static DataCuiSurfaceDefinition Load(string filePath)
    {
        var document = new CuiMarkupLoader().Load(filePath);
        var elements = IndexElements(document.Root);

        foreach (var required in RequiredElements)
        {
            if (!elements.TryGetValue(required.Key, out var element))
                throw new InvalidDataException($"Data CUI is missing required element '{required.Key}'.");
            if (!string.Equals(element.Name, required.Value, StringComparison.Ordinal))
                throw new InvalidDataException($"Data CUI element '{required.Key}' must be a {required.Value}.");
        }

        var actions = DescendantsAndSelf(document.Root)
            .SelectMany(element => element.Attributes
                .Where(attribute => attribute.Key.EndsWith("Action", StringComparison.Ordinal) ||
                                    string.Equals(attribute.Key, "action", StringComparison.Ordinal))
                .Select(attribute => attribute.Value))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredAction in RequiredActions)
        {
            if (!actions.Contains(requiredAction))
                throw new InvalidDataException($"Data CUI is missing typed action '{requiredAction}'.");
        }

        return new DataCuiSurfaceDefinition(document, elements);
    }

    private static IReadOnlyDictionary<string, CuiElement> IndexElements(CuiElement root)
    {
        var result = new Dictionary<string, CuiElement>(StringComparer.Ordinal);
        foreach (var element in DescendantsAndSelf(root))
        {
            if (!element.Attributes.TryGetValue("id", out var id))
                continue;
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("Data CUI element IDs cannot be blank.");
            if (!result.TryAdd(id, element))
                throw new InvalidDataException($"Data CUI element ID '{id}' is duplicated.");
        }
        return result;
    }

    private static IEnumerable<CuiElement> DescendantsAndSelf(CuiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in DescendantsAndSelf(child))
            yield return descendant;
    }
}
