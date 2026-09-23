using CakeOS.Cui;
using CakeOS.Cui.Language;

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

    private DataCuiSurfaceDefinition(CuiDocument document, IReadOnlyDictionary<string, CuiComponent> elements)
    {
        Document = document;
        Elements = elements;
    }

    public CuiDocument Document { get; }
    public IReadOnlyDictionary<string, CuiComponent> Elements { get; }

    public static DataCuiSurfaceDefinition LoadDefault() =>
        Load(Path.Combine(AppContext.BaseDirectory, "DataWorkspace.cui"));

    public static DataCuiSurfaceDefinition Load(string filePath)
    {
        var document = new CuiRichParser().ParseFile(filePath);
        var elements = IndexElements(document.Components);

        foreach (var required in RequiredElements)
        {
            if (!elements.TryGetValue(required.Key, out var element))
                throw new InvalidDataException($"Data CUI is missing required element '{required.Key}'.");
            if (!string.Equals(element.Type, required.Value, StringComparison.Ordinal))
                throw new InvalidDataException($"Data CUI element '{required.Key}' must be a {required.Value}.");
        }

        var actions = DescendantsAndSelf(document.Components)
            .SelectMany(element => element.AuthoredAttributeNames()
                .Where(attribute => attribute.EndsWith("Action", StringComparison.Ordinal) ||
                                    string.Equals(attribute, "action", StringComparison.Ordinal))
                .Select(attribute => element.TryGetLiteralAttribute(attribute, out var value) ? value : null)
                .Where(value => value is not null)
                .Select(value => value!))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredAction in RequiredActions)
        {
            if (!actions.Contains(requiredAction))
                throw new InvalidDataException($"Data CUI is missing typed action '{requiredAction}'.");
        }

        return new DataCuiSurfaceDefinition(document, elements);
    }

    private static IReadOnlyDictionary<string, CuiComponent> IndexElements(IReadOnlyList<CuiComponent> roots)
    {
        var result = new Dictionary<string, CuiComponent>(StringComparer.Ordinal);
        foreach (var element in DescendantsAndSelf(roots))
        {
            if (!element.TryGetLiteralAttribute("id", out var id))
                continue;
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("Data CUI element IDs cannot be blank.");
            if (!result.TryAdd(id, element))
                throw new InvalidDataException($"Data CUI element ID '{id}' is duplicated.");
        }
        return result;
    }

    private static IEnumerable<CuiComponent> DescendantsAndSelf(IReadOnlyList<CuiComponent> roots)
    {
        foreach (var root in roots)
        foreach (var component in root.DescendantsAndSelf())
            yield return component;
    }
}
