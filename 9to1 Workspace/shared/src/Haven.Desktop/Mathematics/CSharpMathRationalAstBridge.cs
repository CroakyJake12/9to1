using System.Globalization;
using System.Numerics;
using AngouriMath;
using CSharpMath.Atom;
using Haven.Core.Mathematics;
using PeterO.Numbers;
using Atoms = CSharpMath.Atom.Atoms;
using Rational = AngouriMath.Entity.Number.Rational;

namespace Haven.Desktop.Mathematics;

/// <summary>Project the maintained CSharpMath AST into the maintained Angouri
/// parser. Operator precedence and expression parsing belong to those parsers.
/// Exact literals travel as unique variables, then are replaced by explicit
/// EInteger/ERational values, so the parser never rounds a decimal literal.
/// This does not adopt CSharpMath.Evaluation's Angouri1.4/tolerance converter.</summary>
internal sealed class CSharpMathRationalAstBridge(MathExpressionEvaluationPolicy policy,
    CancellationToken cancellationToken)
{
    // The public declared letter may be e or i. Those names have constant/numeric
    // semantics in the maintained parser, so bind one ordinary internal variable.
    internal const string ParserVariableName = "zzastvariable";

    private readonly MathRationalEvaluationLimits _limits = policy.EffectiveLimits;
    private readonly Dictionary<string, Rational> _literals = new(StringComparer.Ordinal);
    private int _atoms;
    private int _generatedCharacters;

    public Entity Read(string source)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Length > _limits.MaxSourceCharacters)
            throw new RationalAdmissionException("MathSourceBudgetExceeded");
        // Reuse the existing maintained syntax preflight before its parser.
        var syntax = new CSharpMathSyntaxAdapter().Parse(source,
            new MathServiceLimits(MaxSourceCharacters: _limits.MaxSourceCharacters,
                MaxSyntaxDepth: _limits.MaxAstDepth));
        if (!syntax.Succeeded) throw new RationalAdmissionException("MathParseError");
        var list = LaTeXParser.MathListFromLaTeX(source).Match(
            value => value.Clone(true),
            _ => throw new RationalAdmissionException("MathParseError"));
        var projected = List(list, 0);
        cancellationToken.ThrowIfCancellationRequested();
        // This is the maintained expression parser, not a new precedence parser.
        var parsed = MathS.FromString(projected, useCache: false);
        var result = parsed.Replace(node => node is Entity.Variable variable &&
            _literals.TryGetValue(variable.Name, out var literal) ? literal : node);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private string List(MathList list, int depth)
    {
        if (depth > _limits.MaxAstDepth || list.Count == 0)
            throw new RationalAdmissionException("MathAstDepthOrEmptyExpression");
        var tokens = new List<string>(list.Count);
        foreach (var atom in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_atoms > _limits.MaxAstAtoms || atom.Subscript.Count != 0)
                throw new RationalAdmissionException("MathAstBudgetOrUnsupportedSubscript");
            var supportsPower = false;
            string token;
            switch (atom)
            {
                case Atoms.Number number:
                    token = Literal(number.Nucleus); supportsPower = true; break;
                case Atoms.Variable variable when variable.Nucleus == policy.Variable &&
                    variable.FontStyle != FontStyle.Roman:
                    token = ParserVariableName; supportsPower = true; break;
                case Atoms.Fraction fraction when fraction.HasRule &&
                    string.IsNullOrEmpty(fraction.LeftDelimiter.Nucleus) &&
                    string.IsNullOrEmpty(fraction.RightDelimiter.Nucleus):
                    token = "(" + List(fraction.Numerator, depth + 1) + ")/(" +
                        List(fraction.Denominator, depth + 1) + ")";
                    token = "(" + token + ")"; supportsPower = true; break;
                case Atoms.Inner inner when
                    (inner.LeftBoundary.Nucleus, inner.RightBoundary.Nucleus) is ("(", ")") or ("[", "]"):
                    token = "(" + List(inner.InnerList, depth + 1) + ")";
                    supportsPower = true; break;
                case Atoms.BinaryOperator binary when binary.Nucleus is "+" or "−" or "×" or "·" or "÷":
                    token = binary.Nucleus switch { "−" => "-", "×" or "·" => "*", "÷" => "/", _ => "+" }; break;
                case Atoms.UnaryOperator unary when unary.Nucleus is "+" or "−":
                    token = unary.Nucleus == "−" ? "-" : "+"; break;
                case Atoms.Ordinary ordinary when ordinary.Nucleus == "/": token = "/"; break;
                case Atoms.Open open when open.Nucleus == "(": token = "("; break;
                case Atoms.Close close when close.Nucleus == ")": token = ")"; supportsPower = true; break;
                case Atoms.Space space when atom.Superscript.Count == 0:
                    // Spacing is presentation metadata, not an expression operator.
                    _ = space; continue;
                default: throw new RationalAdmissionException("RationalSyntaxUnsupported");
            }
            if (atom.Superscript.Count != 0)
            {
                if (!supportsPower) throw new RationalAdmissionException("RationalPowerTargetUnsupported");
                // A scripted closing parenthesis belongs to its maintained-parser
                // group; wrapping the closing token alone would change that group.
                token = (atom is Atoms.Close ? token : "(" + token + ")") +
                    "^(" + IntegerPower(atom.Superscript, depth + 1) + ")";
            }
            _generatedCharacters = checked(_generatedCharacters + token.Length + 1);
            if (_generatedCharacters > _limits.MaxProofBytes)
                throw new RationalAdmissionException("MathGeneratedSyntaxBudgetExceeded");
            tokens.Add(token);
        }
        if (tokens.Count == 0) throw new RationalAdmissionException("MathEmptyExpression");
        return string.Join(" ", tokens);
    }

    private string IntegerPower(MathList script, int depth)
    {
        if (depth > _limits.MaxAstDepth) throw new RationalAdmissionException("MathAstDepthExceeded");
        var sign = 1; var index = 0;
        if (script.Count == 2 && script[0] is Atoms.UnaryOperator unary && unary.Nucleus is "+" or "−")
        { sign = unary.Nucleus == "−" ? -1 : 1; index = 1; }
        if (script.Count != index + 1 || script[index] is not Atoms.Number number ||
            number.Subscript.Count != 0 || number.Superscript.Count != 0 ||
            number.Nucleus.Length == 0 || !number.Nucleus.All(char.IsAsciiDigit) ||
            !int.TryParse(number.Nucleus, NumberStyles.None, CultureInfo.InvariantCulture, out var power) ||
            power > _limits.MaxAbsoluteIntegerPower)
            throw new RationalAdmissionException("RationalIntegerPowerUnsupported");
        _atoms = checked(_atoms + script.Count);
        if (_atoms > _limits.MaxAstAtoms) throw new RationalAdmissionException("MathAstBudgetExceeded");
        cancellationToken.ThrowIfCancellationRequested();
        return (sign * power).ToString(CultureInfo.InvariantCulture);
    }

    private string Literal(string source)
    {
        // Parse only a numeric atom's lexical digits, not mathematical expressions.
        // No double/decimal conversion or generic Angouri Number parser is used.
        if (source.Length == 0 || source.Count(c => c == '.') > 1 ||
            source.Any(c => c != '.' && !char.IsAsciiDigit(c)))
            throw new RationalAdmissionException("ExactRationalLiteralUnsupported");
        var dot = source.IndexOf('.');
        if (dot == 0 || dot == source.Length - 1)
            throw new RationalAdmissionException("ExactRationalLiteralUnsupported");
        var digits = source.Replace(".", "", StringComparison.Ordinal);
        if (digits.Length > _limits.MaxLiteralDigits)
            throw new RationalAdmissionException("MathLiteralBudgetExceeded");
        var numerator = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        var denominator = dot < 0 ? BigInteger.One : BigInteger.Pow(10, source.Length - dot - 1);
        if (numerator.GetBitLength() > _limits.MaxCoefficientBits ||
            denominator.GetBitLength() > _limits.MaxCoefficientBits)
            throw new RationalAdmissionException("MathCoefficientBudgetExceeded");
        var literal = Rational.Create(EInteger.FromString(numerator.ToString(CultureInfo.InvariantCulture)),
            EInteger.FromString(denominator.ToString(CultureInfo.InvariantCulture)));
        // Angouri's maintained VARIABLE grammar admits digits only following an
        // underscore. A bare trailing digit would instead become an implicit
        // power token, so use the exact verified identifier production.
        var name = "zzastliteral_" + _literals.Count.ToString(CultureInfo.InvariantCulture);
        _literals.Add(name, literal);
        return name;
    }
}

internal sealed class RationalAdmissionException(string diagnostic) : Exception(diagnostic)
{
    public string Diagnostic { get; } = diagnostic;
}
