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

    public CuiDiagnostic Diagnostic { get; }
}
