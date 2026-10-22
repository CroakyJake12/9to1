using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>A structured failure while lowering authored CUI into native controls.</summary>
public sealed class CuiRuntimeLoadException : Exception
{
    public CuiRuntimeLoadException(CuiDiagnostic diagnostic)
        : base(diagnostic?.Message ?? throw new ArgumentNullException(nameof(diagnostic)))
    {
        Diagnostic = diagnostic;
    }

    /// <summary>Preserves the exact native/property observer failure alongside its existing
    /// structured CUI diagnostic. The original diagnostic-only constructor remains unchanged.</summary>
    public CuiRuntimeLoadException(CuiDiagnostic diagnostic, Exception innerException)
        : base(diagnostic?.Message ?? throw new ArgumentNullException(nameof(diagnostic)),
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
        Diagnostic = diagnostic;
    }

    public CuiDiagnostic Diagnostic { get; }
}
