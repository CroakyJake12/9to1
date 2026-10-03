using Haven.Core.Mathematics;

namespace Haven.Application.Mathematics;

public sealed record MathSyntaxResult(bool Succeeded, string? CanonicalLaTeX,
    string? Diagnostic, string Implementation);

/// <summary>A maintained parser's bounded syntax capability, shared by visual/source modes.
/// Successful parsing is not a claim of algebraic equivalence or evaluator availability.</summary>
public interface IMathSyntaxAdapter
{
    MathSyntaxResult Parse(string source, MathServiceLimits limits);
}

public enum MathVisualOperation
{
    InsertSymbol, Fraction, Power, Subscript, SquareRoot, Logarithm, Sine,
    Cosine, Tangent, Integral, Sum, Product, Vector, Matrix, Set, Piecewise,
    Probability, Aligned
}
public sealed record MathVisualEdit(MathVisualOperation Operation, int SelectionStart,
    int SelectionLength, string Symbol = "");
public sealed record MathEditorState(MathExpression LastValid, string DraftLaTeX,
    string? Diagnostic, string ParserImplementation);
