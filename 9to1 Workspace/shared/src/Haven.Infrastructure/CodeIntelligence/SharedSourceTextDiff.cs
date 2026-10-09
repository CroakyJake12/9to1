namespace Haven.Infrastructure;

public enum SourceTextDiffKind { Unchanged, Removed, Added }

/// <summary>Actual source line coordinates. Missing sides have no line number or text.</summary>
public sealed record SourceTextDiffLine(SourceTextDiffKind Kind, int? BeforeLineNumber,
    int? AfterLineNumber, string? BeforeText, string? AfterText);

/// <summary>Portable consumer of the same diff operations used by Dev's unified diff builder.</summary>
public static class SharedSourceTextDiff
{
    public const int MaximumTextCharacters = 8 * 1024 * 1024;
    public const int MaximumSourceLines = 100_000;
    public static IReadOnlyList<SourceTextDiffLine> Compare(string? before, string? after)
    {
        if (before?.Length > MaximumTextCharacters || after?.Length > MaximumTextCharacters)
            throw new ArgumentOutOfRangeException(nameof(before), "Source exceeds the text diff limit.");
        return UnifiedDiffBuilder.BuildSourceRows(before, after);
    }
}

internal static partial class UnifiedDiffBuilder
{
    internal static IReadOnlyList<SourceTextDiffLine> BuildSourceRows(string? before, string? after)
    {
        // Null denotes an absent file, independently of an existing empty file.
        static string[] Lines(string? source)
        {
            if (source is null) return [];
            var normalized = source.ReplaceLineEndings("\n");
            if (normalized.Count(c => c == '\n') >= SharedSourceTextDiff.MaximumSourceLines)
                throw new ArgumentOutOfRangeException(nameof(source), "Source exceeds the line diff limit.");
            return normalized.Split('\n');
        }
        var original = Lines(before); var current = Lines(after);
        // Build already short-circuits equal original text before the bounded-matrix fallback.
        // Preserve that same behaviour when exposing the actual unchanged line rows.
        var operations = string.Equals(before, after, StringComparison.Ordinal)
            ? original.Select(line => new DiffOperation(DiffKind.Equal, line)).ToArray()
            : Diff(original, current);
        var rows = new List<SourceTextDiffLine>(operations.Count);
        var left = 1; var right = 1;
        foreach (var operation in operations)
            rows.Add(operation.Kind switch
            {
                DiffKind.Equal => new(SourceTextDiffKind.Unchanged, left++, right++, operation.Line, operation.Line),
                DiffKind.Remove => new(SourceTextDiffKind.Removed, left++, null, operation.Line, null),
                _ => new(SourceTextDiffKind.Added, null, right++, null, operation.Line)
            });
        return rows;
    }
}
