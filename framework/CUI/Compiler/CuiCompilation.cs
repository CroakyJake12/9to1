using CakeOS.Cui.Language;

namespace CakeOS.Cui.Compiler;

public sealed record CuiCompilation(
    CuiDocument? Document,
    IReadOnlyList<CuiDiagnostic> Diagnostics)
{
    public CuiCompiledDocument? Output { get; init; }

    public bool Succeeded => Document is not null && Diagnostics.All(x => x.Severity != CuiDiagnosticSeverity.Error);
}

public sealed class CuiCompilationException : FormatException
{
    public CuiCompilationException(IReadOnlyList<CuiDiagnostic> diagnostics)
        : base(CreateMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<CuiDiagnostic> Diagnostics { get; }

    private static string CreateMessage(IReadOnlyList<CuiDiagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(x => x.Severity == CuiDiagnosticSeverity.Error)?.ToString()
        ?? "CUI compilation failed.";
}
