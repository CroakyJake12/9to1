using Haven.Core.Mathematics;

namespace Haven.Application.Mathematics;

/// <summary>One canonical expression behind both editing modes. Invalid/unsupported input
/// retains recoverable draft text and the exact last valid identity/revision/value.</summary>
public sealed class MathEditorSession
{
    private readonly object _gate = new();
    private readonly IMathSyntaxAdapter _syntax;
    private readonly MathServiceLimits _limits;
    private MathExpression _valid;
    private string _draft;
    private string? _diagnostic;
    private string _implementation;

    public MathEditorSession(MathExpression initial, IMathSyntaxAdapter syntax, MathServiceLimits? limits = null)
    {
        _syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        _limits = limits ?? new(); _limits.Validate();
        _valid = MathObjectCodec.Capture(initial, _limits);
        var parsed = _syntax.Parse(initial.LaTeX, _limits);
        if (!parsed.Succeeded || string.IsNullOrWhiteSpace(parsed.CanonicalLaTeX))
            throw new InvalidDataException(parsed.Diagnostic ?? "InvalidInitialMathExpression");
        if (string.IsNullOrWhiteSpace(parsed.Implementation)) throw new InvalidOperationException("MathParserProvenanceMissing");
        _draft = initial.LaTeX; _implementation = parsed.Implementation;
    }

    public MathEditorState Snapshot()
    {
        lock (_gate) return new(MathObjectCodec.Capture(_valid, _limits), _draft, _diagnostic, _implementation);
    }

    public MathEditorState EditLaTeX(long expectedRevision, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate) return Edit(expectedRevision, source);
    }

    public MathEditorState EditVisual(long expectedRevision, MathVisualEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        lock (_gate)
        {
            RequireRevision(expectedRevision);
            if (_diagnostic is not null) throw new InvalidOperationException("ResolveMathDraftBeforeVisualEdit");
            if (!Enum.IsDefined(edit.Operation) || edit.SelectionStart < 0 || edit.SelectionLength < 0 ||
                edit.SelectionStart > _draft.Length || edit.SelectionLength > _draft.Length - edit.SelectionStart)
                throw new ArgumentOutOfRangeException(nameof(edit));
            var selected = _draft.Substring(edit.SelectionStart, edit.SelectionLength);
            var operand = selected.Length == 0 ? "1" : selected;
            var replacement = edit.Operation switch
            {
                MathVisualOperation.InsertSymbol => Symbol(edit.Symbol),
                MathVisualOperation.Fraction => @"\frac{" + operand + "}{1}",
                MathVisualOperation.Power => "{" + operand + "}^{2}",
                MathVisualOperation.Subscript => "{" + operand + "}_{1}",
                MathVisualOperation.SquareRoot => @"\sqrt{" + operand + "}",
                MathVisualOperation.Logarithm => @"\log{" + operand + "}",
                MathVisualOperation.Sine => @"\sin{" + operand + "}",
                MathVisualOperation.Cosine => @"\cos{" + operand + "}",
                MathVisualOperation.Tangent => @"\tan{" + operand + "}",
                MathVisualOperation.Integral => @"\int_{0}^{1}{" + operand + @"}\,dx",
                MathVisualOperation.Sum => @"\sum_{k=1}^{n}{" + operand + "}",
                MathVisualOperation.Product => @"\prod_{k=1}^{n}{" + operand + "}",
                MathVisualOperation.Vector => @"\begin{pmatrix}" + operand + @"\\1\end{pmatrix}",
                MathVisualOperation.Matrix => @"\begin{pmatrix}" + operand + @"&0\\0&1\end{pmatrix}",
                MathVisualOperation.Set => @"\left\{" + operand + @"\right\}",
                MathVisualOperation.Piecewise => @"\begin{cases}" + operand + @"&x\geq0\\0&x<0\end{cases}",
                MathVisualOperation.Probability => @"P\left(" + operand + @"\right)",
                MathVisualOperation.Aligned => @"\begin{aligned}" + operand + @"&=1\end{aligned}",
                _ => throw new ArgumentOutOfRangeException(nameof(edit))
            };
            return Edit(expectedRevision, string.Concat(_draft.AsSpan(0, edit.SelectionStart), replacement,
                _draft.AsSpan(edit.SelectionStart + edit.SelectionLength)));
        }
    }

    public MathEditorState RestoreLastValid(long expectedRevision)
    {
        lock (_gate)
        {
            RequireRevision(expectedRevision); _draft = _valid.LaTeX; _diagnostic = null;
            return Snapshot();
        }
    }
    private string Symbol(string symbol)
    {
        // Ordinary numeric/variable entry and named palette symbols need no LaTeX knowledge.
        // Raw commands are edited in source mode, where the same maintained parser validates them.
        var palette = symbol switch
        {
            "π" => @"\pi", "α" => @"\alpha", "β" => @"\beta", "θ" => @"\theta",
            "∞" => @"\infty", "≤" => @"\leq", "≥" => @"\geq", "≠" => @"\neq",
            _ => symbol
        };
        if (palette == symbol && (symbol.Length == 0 || symbol.Length > _limits.MaxSourceCharacters ||
            symbol.Any(x => !char.IsLetterOrDigit(x) && x is not ('+' or '-' or '=' or '<' or '>' or '.' or ',' or '(' or ')'))))
            throw new ArgumentException("UnsupportedVisualMathSymbol", nameof(symbol));
        return palette;
    }
    private MathEditorState Edit(long expectedRevision, string source)
    {
        RequireRevision(expectedRevision);
        if (source.Length > _limits.MaxSourceCharacters) throw new InvalidDataException("MathSourceBudgetExceeded");
        var parsed = _syntax.Parse(source, _limits);
        if (string.IsNullOrWhiteSpace(parsed.Implementation)) throw new InvalidOperationException("MathParserProvenanceMissing");
        if (parsed.Succeeded)
        {
            if (string.IsNullOrWhiteSpace(parsed.CanonicalLaTeX)) throw new InvalidOperationException("MathParserCanonicalValueMissing");
            if (parsed.CanonicalLaTeX.Length > _limits.MaxSourceCharacters)
            {
                _draft = source; _diagnostic = "CanonicalMathSourceBudgetExceeded";
                _implementation = parsed.Implementation; return Snapshot();
            }
            var next = MathObjectCodec.Capture(_valid with { LaTeX = parsed.CanonicalLaTeX,
                Revision = parsed.CanonicalLaTeX == _valid.LaTeX ? _valid.Revision : checked(_valid.Revision + 1) }, _limits);
            _valid = next;
        }
        _draft = source; _diagnostic = parsed.Succeeded ? null : parsed.Diagnostic ?? "InvalidMathSource";
        _implementation = parsed.Implementation;
        return Snapshot();
    }
    private void RequireRevision(long expectedRevision)
    {
        if (_valid.Revision != expectedRevision) throw new InvalidOperationException("RevisionConflict");
    }
}
