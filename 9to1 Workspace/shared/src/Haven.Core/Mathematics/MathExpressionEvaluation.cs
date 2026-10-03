namespace Haven.Core.Mathematics;

/// <summary>The domain is part of the requested mathematical meaning. A backend
/// must refuse readings it cannot prove; its library's default is not this policy.</summary>
public enum MathEvaluationDomain { Real, Complex }

/// <summary>Explicit evaluator admission bounds, separate from rendering/storage
/// limits. They bound admitted syntax and retained results, not wall time or memory.
/// A supervised execution boundary must enforce any hard resource deadline.</summary>
public sealed record MathRationalEvaluationLimits(int MaxSourceCharacters = 256,
    int MaxAstAtoms = 64, int MaxAstDepth = 8, int MaxLiteralDigits = 18,
    int MaxAbsoluteIntegerPower = 8, int MaxExpandedDegree = 16,
    int MaxCoefficientBits = 1024, int MaxNormalizedNodes = 512,
    int MaxProofBytes = 16 * 1024)
{
    public void Validate(MathServiceLimits? sharedLimits = null)
    {
        var shared = sharedLimits ?? new(); shared.Validate();
        if (MaxSourceCharacters < 1 || MaxSourceCharacters > shared.MaxSourceCharacters ||
            MaxAstAtoms < 1 || MaxAstDepth < 1 || MaxAstDepth > shared.MaxSyntaxDepth ||
            MaxLiteralDigits < 1 || MaxLiteralDigits > MaxSourceCharacters ||
            MaxAbsoluteIntegerPower < 1 || MaxAbsoluteIntegerPower > 32 || MaxExpandedDegree < 1 ||
            MaxCoefficientBits < 1 || MaxNormalizedNodes < 1 || MaxProofBytes < 1 ||
            MaxProofBytes > shared.MaxSerializedBytes)
            throw new ArgumentOutOfRangeException(nameof(MathRationalEvaluationLimits));
    }
}

/// <summary>Conservative single-variable rational scope. Unsupported expressions,
/// domains, assumptions and units remain NeedsReview rather than being approximated.</summary>
public sealed record MathExpressionEvaluationPolicy(MathEvaluationDomain Domain = MathEvaluationDomain.Real,
    string Variable = "x", MathRationalEvaluationLimits? Limits = null)
{
    public MathRationalEvaluationLimits EffectiveLimits => Limits ?? new();
    public void Validate(MathServiceLimits? sharedLimits = null)
    {
        if (!Enum.IsDefined(Domain) || string.IsNullOrEmpty(Variable) || Variable.Length != 1 || Variable[0] is < 'a' or > 'z')
            throw new ArgumentException("InvalidMathEvaluationPolicy", nameof(MathExpressionEvaluationPolicy));
        EffectiveLimits.Validate(sharedLimits);
    }
}

public enum MathExpressionEquivalenceOutcome { Equivalent, NeedsReview }

/// <summary>Evidence from the configured evaluator, bound to both original
/// canonical versions and their declared reading. This is not an approval, a
/// durable mutation, or an automatic Forms grading policy. An unproved difference
/// cannot become Incorrect merely because normalized/domain trees differ.</summary>
public sealed record MathExpressionEquivalenceResult(MathExpressionEquivalenceOutcome Outcome,
    MathExpressionReference Expected, MathExpressionReference Candidate,
    MathEvaluationDomain Domain, string Variable, string Implementation,
    string? OriginalDomainDigest = null, string? NormalFormDigest = null,
    string? Diagnostic = null, string? MaintainedNormalizationDigest = null);
