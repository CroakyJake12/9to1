namespace CakeOS.Cui.Language;

public enum CuiDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record CuiDiagnostic(
    string Code,
    CuiDiagnosticSeverity Severity,
    string Message,
    CuiSourceSpan Span,
    CuiSourceSpan? RelatedSpan = null)
{
    public override string ToString() =>
        $"{Span.SourceName}({Span.Start.Line},{Span.Start.Column}): {Severity.ToString().ToLowerInvariant()} {Code}: {Message}"
        + (RelatedSpan is { } related
            ? $" Related location: {related.SourceName}({related.Start.Line},{related.Start.Column})."
            : string.Empty);
}

public sealed class CuiDiagnosticBag
{
    private readonly List<CuiDiagnostic> _diagnostics = [];

    public IReadOnlyList<CuiDiagnostic> Diagnostics => _diagnostics;

    public void Clear() => _diagnostics.Clear();

    public void Error(string code, string message, CuiSourceSpan span, CuiSourceSpan? relatedSpan = null) =>
        _diagnostics.Add(new CuiDiagnostic(code, CuiDiagnosticSeverity.Error, message, span, relatedSpan));

    public void Warning(string code, string message, CuiSourceSpan span, CuiSourceSpan? relatedSpan = null) =>
        _diagnostics.Add(new CuiDiagnostic(code, CuiDiagnosticSeverity.Warning, message, span, relatedSpan));
}
