using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Haven.Core.Forms;

namespace Haven.Core.Mathematics;

public enum MathNumericComparison { Exact, AbsoluteTolerance, PercentageTolerance }
public sealed record MathNumericRule(Guid RuleID, decimal Expected, MathNumericComparison Comparison,
    decimal Tolerance = 0, int? SignificantFigures = null, int? DecimalPlaces = null,
    string? Units = null, bool IgnoreUnitsCase = false);
public enum MathMarkingOutcome { Correct, Incorrect, NeedsReview }
public sealed record MathMarkingResult(MathMarkingOutcome Outcome, Guid RuleID,
    string Provenance, string? Diagnostic = null);
public sealed record MathNumericRepresentation(decimal Value, int DecimalPlaces, int? SignificantFigures);

/// <summary>Bounded exact decimal input. Overprecision is refused before .NET decimal
/// parsing can silently round. Integer trailing zero significance stays explicit/ambiguous.</summary>
public static class MathNumericLiteral
{
    public static MathNumericRepresentation Read(string? literal)
    {
        if (string.IsNullOrEmpty(literal) || literal.Length > 128) throw new InvalidDataException("InvalidMathNumber");
        var sign = literal[0] == '-'; var start = sign ? 1 : 0;
        var exponentIndex = literal.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? literal[start..] : literal[start..exponentIndex];
        var exponent = 0;
        if (exponentIndex >= 0 && (!int.TryParse(literal[(exponentIndex + 1)..],
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent) || exponent is < -128 or > 128))
            throw new InvalidDataException("InvalidMathNumberExponent");
        var decimalIndex = mantissa.IndexOf('.');
        var integer = decimalIndex < 0 ? mantissa : mantissa[..decimalIndex];
        var fraction = decimalIndex < 0 ? "" : mantissa[(decimalIndex + 1)..];
        if (integer.Length == 0 || integer.Any(x => x is < '0' or > '9') ||
            integer.Length > 1 && integer[0] == '0' ||
            fraction.Any(x => x is < '0' or > '9') || decimalIndex >= 0 && fraction.Length == 0)
            throw new InvalidDataException("InvalidMathNumber");
        var digits = integer + fraction;
        var coefficient = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        var originalScale = checked(fraction.Length - exponent);
        if (originalScale > 28) throw new InvalidDataException("MathNumberPrecisionUnsupported");
        var scale = originalScale;
        while (scale > 0 && coefficient != 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        if (scale < 0) { coefficient *= BigInteger.Pow(10, -scale); scale = 0; }
        if (scale > 28 || coefficient > new BigInteger(decimal.MaxValue))
            throw new InvalidDataException("MathNumberPrecisionUnsupported");
        var value = new decimal(unchecked((int)(uint)(coefficient & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue)),
            unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue)), sign, (byte)scale);
        var nonLeadingZeros = digits.TrimStart('0');
        int? significant = nonLeadingZeros.Length == 0 ? Math.Max(1, fraction.Length)
            : decimalIndex < 0 && exponentIndex < 0 && integer.EndsWith('0') ? null : nonLeadingZeros.Length;
        return new(value, Math.Max(0, originalScale), significant);
    }
}

/// <summary>Shared typed numeric policies delegate value matching to the existing Forms
/// deterministic engine. Symbolic/AI/human work is an explicit later capability.</summary>
public static class MathMarking
{
    public static MathMarkingResult Evaluate(MathAnswer answer, MathNumericRule rule, MathServiceLimits? limits = null)
    {
        MathObjectCodec.Validate(answer, limits);
        if (rule.RuleID == Guid.Empty || !Enum.IsDefined(rule.Comparison) || rule.Tolerance < 0 ||
            rule.Comparison == MathNumericComparison.Exact && rule.Tolerance != 0 ||
            rule.SignificantFigures is < 1 or > 29 || rule.DecimalPlaces is < 0 or > 28 ||
            rule.Units is { } units && (string.IsNullOrWhiteSpace(units) || units.Length > (limits ?? new()).MaxSourceCharacters))
            throw new ArgumentException("InvalidMathNumericRule", nameof(rule));
        if (answer.Value is not NumericMathAnswer numeric)
            return new(MathMarkingOutcome.NeedsReview, rule.RuleID, "deterministic-numeric", "NumericAnswerRequired");
        var representation = MathNumericLiteral.Read(numeric.Literal);
        if (!string.Equals(numeric.Units, rule.Units, rule.IgnoreUnitsCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return new(MathMarkingOutcome.Incorrect, rule.RuleID, "deterministic-units", "UnitsMismatch");
        if (rule.SignificantFigures is { } figures)
        {
            if (representation.SignificantFigures is null)
                return new(MathMarkingOutcome.NeedsReview, rule.RuleID, "deterministic-precision", "AmbiguousSignificantFigures");
            if (representation.SignificantFigures != figures)
                return new(MathMarkingOutcome.Incorrect, rule.RuleID, "deterministic-precision", "SignificantFiguresMismatch");
        }
        if (rule.DecimalPlaces is { } places && representation.DecimalPlaces != places)
            return new(MathMarkingOutcome.Incorrect, rule.RuleID, "deterministic-precision", "DecimalPlacesMismatch");
        decimal tolerance;
        try
        {
            tolerance = rule.Comparison switch
            {
                MathNumericComparison.Exact => 0,
                MathNumericComparison.AbsoluteTolerance => rule.Tolerance,
                MathNumericComparison.PercentageTolerance => ExactPercentageTolerance(rule.Expected, rule.Tolerance),
                _ => throw new ArgumentException("InvalidMathNumericRule")
            };
        }
        catch (Exception error) when (error is OverflowException or InvalidDataException)
        {
            return new(MathMarkingOutcome.NeedsReview, rule.RuleID, "deterministic-numeric", "ToleranceRangeUnsupported");
        }
        var value = JsonSerializer.SerializeToElement(representation.Value);
        var existing = FormMarking.Evaluate(value,
            [new(rule.RuleID, FormMarkingRuleKind.NumberWithinTolerance, 1, ExpectedNumber: rule.Expected, Tolerance: tolerance)], 1);
        return new(existing.Outcome == FormMarkingOutcome.Correct ? MathMarkingOutcome.Correct : MathMarkingOutcome.Incorrect,
            rule.RuleID, "Forms.NumberWithinTolerance", existing.Diagnostic);
    }

    private static decimal ExactPercentageTolerance(decimal expected, decimal percentage)
    {
        static (BigInteger Coefficient, int Scale) Parts(decimal value)
        {
            var bits = decimal.GetBits(value);
            return ((new BigInteger((uint)bits[2]) << 64) |
                (new BigInteger((uint)bits[1]) << 32) | (uint)bits[0], (bits[3] >> 16) & 0xff);
        }
        var e = Parts(expected); var p = Parts(percentage);
        var coefficient = e.Coefficient * p.Coefficient; var scale = e.Scale + p.Scale + 2;
        if (coefficient.IsZero) return 0;
        while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        return MathNumericLiteral.Read(coefficient.ToString(CultureInfo.InvariantCulture) + "e-" +
            scale.ToString(CultureInfo.InvariantCulture)).Value;
    }
}
