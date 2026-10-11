using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Compiler;

/// <summary>
/// Indexes the CUI source family inside a declared project root so Import declarations
/// resolve against real indexed files rather than a caller-supplied guess.
/// </summary>
public sealed class CuiSourceIndex
{
    private readonly CuiRichParser _parser = new();

    public IReadOnlyList<CuiImportedSymbols> Index(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);

        var files = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Where(CuiProjectItems.IsCuiSourceFamily)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var entries = new List<CuiImportedSymbols>(files.Length);
        foreach (var file in files)
        {
            var document = _parser.ParseFile(file);

            var symbols = document.Components
                .SelectMany(component => component.DescendantsAndSelf())
                .Where(component => component.IsDefinition)
                .Select(component => component.Name ?? component.Type)
                .Concat(document.Templates.Keys)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            var imports = document.Imports
                .Select(entry => entry.Source)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            entries.Add(new CuiImportedSymbols(
                Path.GetRelativePath(fullRoot, file).Replace('\\', '/'),
                symbols,
                imports));
        }

        return entries;
    }
}