using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using AngouriMath;
using Haven.Application.Mathematics;
using Haven.Core.Mathematics;
using PeterO.Numbers;
using Rational = AngouriMath.Entity.Number.Rational;
using Integer = AngouriMath.Entity.Number.Integer;

namespace Haven.Desktop.Mathematics;

/// <summary>A conservative exact rational capability. Original denominator and
/// zero/negative-power conditions are sealed before maintained normalization.
/// Neither numerical samples nor the library's tolerant Boolean evaluator are
/// equivalence evidence. This adapter does not install a Forms marking policy.</summary>
public sealed class AngouriRationalExpressionEvaluator : IMathExpressionEvaluator
{
    public const string Implementation = "CSharpMath/1.0.0-pre.1+AngouriMath/2.5.0/exact-rational-original-domain-v1";

    public async ValueTask<MathExpressionEquivalenceResult> CompareAsync(MathExpression expected,
        MathExpression candidate, MathExpressionEvaluationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var heldExpected = MathObjectCodec.Capture(expected);
        var heldCandidate = MathObjectCodec.Capture(candidate);
        if (heldExpected.ExpressionID == heldCandidate.ExpressionID && heldExpected.Revision == heldCandidate.Revision &&
            !MathObjectCodec.Encode(heldExpected).AsSpan().SequenceEqual(MathObjectCodec.Encode(heldCandidate)))
            return new(MathExpressionEquivalenceOutcome.NeedsReview,
                new(heldExpected.ExpressionID, heldExpected.Revision), new(heldCandidate.ExpressionID, heldCandidate.Revision),
                policy.Domain, policy.Variable, Implementation, Diagnostic: "ConflictingMathExpressionIdentity");
        // Await this original task through settlement. No detached timeout task,
        // WaitAsync deadline or claim of a hard CPU/memory/wall-time boundary.
        return await Task.Run(() => Compare(heldExpected, heldCandidate, policy,
            cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static MathExpressionEquivalenceResult Compare(MathExpression expected,
        MathExpression candidate, MathExpressionEvaluationPolicy policy, CancellationToken token)
    {
        var expectedReference = new MathExpressionReference(expected.ExpressionID, expected.Revision);
        var candidateReference = new MathExpressionReference(candidate.ExpressionID, candidate.Revision);
        MathExpressionEquivalenceResult Refuse(string diagnostic) => new(
            MathExpressionEquivalenceOutcome.NeedsReview, expectedReference, candidateReference,
            policy.Domain, policy.Variable, Implementation, Diagnostic: diagnostic);
        MathS.Multithreading.SetLocalCancellationToken(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var expectedProof = new RationalProof(policy, token).Read(expected.LaTeX);
            var candidateProof = new RationalProof(policy, token).Read(candidate.LaTeX);
            if (!StringComparer.Ordinal.Equals(expectedProof.Domain, candidateProof.Domain))
                return Refuse("OriginalDomainsNotProvedEquivalent");
            if (!StringComparer.Ordinal.Equals(expectedProof.NormalForm, candidateProof.NormalForm))
                return Refuse("RationalNormalFormsNotProvedEquivalent");
            token.ThrowIfCancellationRequested();
            return new(MathExpressionEquivalenceOutcome.Equivalent, expectedReference, candidateReference,
                policy.Domain, policy.Variable, Implementation,
                Digest($"{policy.Domain}:{policy.Variable}:{expectedProof.Domain}"),
                Digest(expectedProof.NormalForm), MaintainedNormalizationDigest:
                Digest(expectedProof.MaintainedNormalForm + "\n" + candidateProof.MaintainedNormalForm));
        }
        catch (RationalAdmissionException error) { return Refuse(error.Diagnostic); }
        catch (AngouriMath.Core.Exceptions.ParseException) { return Refuse("MathParseError"); }
        catch (OverflowException) { return Refuse("MathExpansionArithmeticBudgetExceeded"); }
        finally
        {
            // This AsyncLocal belongs to the original child task; no process-wide
            // library settings or the caller's execution context are modified.
            MathS.Multithreading.SetLocalCancellationToken(CancellationToken.None);
        }
    }

    private static string Digest(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class RationalProof(MathExpressionEvaluationPolicy policy, CancellationToken token)
    {
        private readonly MathRationalEvaluationLimits _limits = policy.EffectiveLimits;
        private readonly SortedSet<string> _conditions = new(StringComparer.Ordinal);
        private int _originalNodes;

        public (string Domain, string NormalForm, string MaintainedNormalForm) Read(string source)
        {
            var original = new CSharpMathRationalAstBridge(policy, token).Read(source);
            var admitted = Admit(original, 0);
            var normal = Normalize(admitted.Expression);
            var domain = string.Join(";", _conditions);
            RequireProofSize(domain);
            token.ThrowIfCancellationRequested();
            var maintainedNormalForm = Fingerprint(normal);
            // The maintained normalizer's cancelled-factor condition is retained
            // and hashed, never evaluated/simplified. Compare its rational value
            // only after independently sealing ALL original domain conditions.
            // Raw input Providedf nodes cannot pass Admit, so these wrappers can
            // only come from the maintained exact rational transformation.
            var value = normal is Entity.Providedf(var expression, _) ? expression : normal;
            return (domain, Fingerprint(value), maintainedNormalForm);
        }

        private Admitted Admit(Entity node, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (++_originalNodes > _limits.MaxNormalizedNodes || depth > _limits.MaxAstDepth * 4)
                throw new RationalAdmissionException("MathOriginalTreeBudgetExceeded");
            switch (node)
            {
                case Rational number:
                    RequireCoefficient(number); return new(number, 0, 0);
                case Entity.Variable variable when variable.GetType() == typeof(Entity.Variable) &&
                    variable.Name == CSharpMathRationalAstBridge.ParserVariableName:
                    return new(variable, 1, 0);
                case Entity.Sumf(var left, var right):
                    return Binary(Admit(left, depth + 1), Admit(right, depth + 1), '+');
                case Entity.Minusf(var left, var right):
                    return Binary(Admit(left, depth + 1), Admit(right, depth + 1), '-');
                case Entity.Mulf(var left, var right):
                    return Binary(Admit(left, depth + 1), Admit(right, depth + 1), '*');
                case Entity.Divf(var left, var right):
                {
                    var dividend = Admit(left, depth + 1); var divisor = Admit(right, depth + 1);
                    RequireNonzero(divisor.Expression);
                    return Binary(dividend, divisor, '/');
                }
                case Entity.Powf(var basis, Integer exponent):
                {
                    if (exponent.EInteger.Abs().CompareTo(EInteger.FromInt32(_limits.MaxAbsoluteIntegerPower)) > 0)
                        throw new RationalAdmissionException("RationalIntegerPowerUnsupported");
                    var power = exponent.EInteger.ToInt32Checked(); var originalBase = Admit(basis, depth + 1);
                    // 0^0 is indeterminate in the maintained model. A variable^0
                    // therefore retains its original exclusion just like a reciprocal.
                    if (power <= 0) RequireNonzero(originalBase.Expression);
                    if (originalBase.Expression is Rational constant)
                    {
                        var value = ERational.One;
                        for (var i = 0; i < Math.Abs(power); i++)
                        {
                            token.ThrowIfCancellationRequested();
                            value = value.Multiply(constant.ERational);
                            RequireCoefficient(Rational.Create(value));
                        }
                        if (power < 0) value = ERational.One.Divide(value);
                        var result = Rational.Create(value); RequireCoefficient(result);
                        return new(result, 0, 0);
                    }
                    return Bound(new Entity.Powf(originalBase.Expression, exponent),
                        checked((power < 0 ? originalBase.DenominatorDegree : originalBase.NumeratorDegree) * Math.Abs(power)),
                        checked((power < 0 ? originalBase.NumeratorDegree : originalBase.DenominatorDegree) * Math.Abs(power)));
                }
                default: throw new RationalAdmissionException("RationalEntityUnsupported");
            }
        }

        private Admitted Binary(Admitted left, Admitted right, char operation)
        {
            if (left.Expression is Rational a && right.Expression is Rational b)
            {
                // ERational arithmetic is exact. Never use generic Number.IsZero,
                // Equalsf/EvalBoolean or decimal/double coefficient conversion.
                var value = operation switch
                {
                    '+' => a.ERational.Add(b.ERational), '-' => a.ERational.Subtract(b.ERational),
                    '*' => a.ERational.Multiply(b.ERational), '/' => a.ERational.Divide(b.ERational),
                    _ => throw new InvalidOperationException()
                };
                var result = Rational.Create(value); RequireCoefficient(result); return new(result, 0, 0);
            }
            return operation switch
            {
                '+' => Bound(new Entity.Sumf(left.Expression, right.Expression),
                    checked(Math.Max(left.NumeratorDegree + right.DenominatorDegree,
                        right.NumeratorDegree + left.DenominatorDegree)), checked(left.DenominatorDegree + right.DenominatorDegree)),
                '-' => Bound(new Entity.Minusf(left.Expression, right.Expression),
                    checked(Math.Max(left.NumeratorDegree + right.DenominatorDegree,
                        right.NumeratorDegree + left.DenominatorDegree)), checked(left.DenominatorDegree + right.DenominatorDegree)),
                '*' => Bound(new Entity.Mulf(left.Expression, right.Expression),
                    checked(left.NumeratorDegree + right.NumeratorDegree), checked(left.DenominatorDegree + right.DenominatorDegree)),
                '/' => Bound(new Entity.Divf(left.Expression, right.Expression),
                    checked(left.NumeratorDegree + right.DenominatorDegree), checked(left.DenominatorDegree + right.NumeratorDegree)),
                _ => throw new InvalidOperationException()
            };
        }

        private Admitted Bound(Entity expression, int numerator, int denominator)
        {
            if (numerator > _limits.MaxExpandedDegree || denominator > _limits.MaxExpandedDegree)
                throw new RationalAdmissionException("MathExpandedDegreeBudgetExceeded");
            return new(expression, numerator, denominator);
        }

        private void RequireNonzero(Entity original)
        {
            if (original is Rational constant)
            {
                if (constant.ERational.Numerator.IsZero)
                    throw new RationalAdmissionException("MathOriginalDomainEmptyOrIndeterminate");
                return; // Proven exactly, including arbitrarily small admitted rationals.
            }
            var normal = Normalize(original);
            var value = normal is Entity.Providedf(var expression, _) ? expression : normal;
            if (value is Rational number && number.ERational.Numerator.IsZero)
                throw new RationalAdmissionException("MathOriginalDomainEmptyOrIndeterminate");
            // Child-domain conditions were captured recursively before this call.
            // Keep the complete maintained Providedf tree, rather than dropping it.
            _conditions.Add("nonzero(" + Fingerprint(normal) + ")");
            RequireProofSize(string.Join(";", _conditions));
        }

        private Entity Normalize(Entity original)
        {
            token.ThrowIfCancellationRequested();
            var normal = original.CanonicalizeAsRationalFunction();
            token.ThrowIfCancellationRequested();
            return normal ?? throw new RationalAdmissionException("MaintainedRationalNormalizationUnavailable");
        }

        private string Fingerprint(Entity expression)
        {
            var count = 0;
            string Read(Entity node, int depth)
            {
                token.ThrowIfCancellationRequested();
                if (++count > _limits.MaxNormalizedNodes || depth > _limits.MaxAstDepth * 4)
                    throw new RationalAdmissionException("MathNormalizedTreeBudgetExceeded");
                return node switch
                {
                    Rational value => RationalText(value),
                    Entity.Variable variable when variable.GetType() == typeof(Entity.Variable) &&
                    variable.Name == CSharpMathRationalAstBridge.ParserVariableName => "var(" + policy.Variable + ")",
                    Entity.Sumf(var a, var b) => "add(" + Read(a, depth + 1) + "," + Read(b, depth + 1) + ")",
                    Entity.Minusf(var a, var b) => "sub(" + Read(a, depth + 1) + "," + Read(b, depth + 1) + ")",
                    Entity.Mulf(var a, var b) => "mul(" + Read(a, depth + 1) + "," + Read(b, depth + 1) + ")",
                    Entity.Divf(var a, var b) => "div(" + Read(a, depth + 1) + "," + Read(b, depth + 1) + ")",
                    Entity.Powf(var a, Integer power) => "pow(" + Read(a, depth + 1) + "," + RationalText(power) + ")",
                    Entity.Providedf(var value, var predicate) => "provided(" + Read(value, depth + 1) + "," + Read(predicate, depth + 1) + ")",
                    Entity.Notf(var value) => "not(" + Read(value, depth + 1) + ")",
                    Entity.Andf(var a, var b) => "and(" + Read(a, depth + 1) + "," + Read(b, depth + 1) + ")",
                    Entity.Equalsf(var a, var b) => "equals(" + Read(a, depth + 1) + "," + Read(b, depth + 1) + ")",
                    Entity.Boolean(var value) => value ? "true" : throw new RationalAdmissionException("MathNormalizedDomainEmpty"),
                    _ => throw new RationalAdmissionException("RationalNormalizedEntityUnsupported")
                };
            }
            var result = Read(expression, 0); RequireProofSize(result); return result;
        }

        private string RationalText(Rational value)
        {
            RequireCoefficient(value);
            return "q(" + value.ERational.Numerator.ToString() + "/" + value.ERational.Denominator.ToString() + ")";
        }

        private void RequireCoefficient(Rational value)
        {
            if (BigInteger.Abs(BigInteger.Parse(value.ERational.Numerator.ToString(), CultureInfo.InvariantCulture)).GetBitLength() > _limits.MaxCoefficientBits ||
                BigInteger.Parse(value.ERational.Denominator.ToString(), CultureInfo.InvariantCulture).GetBitLength() > _limits.MaxCoefficientBits)
                throw new RationalAdmissionException("MathCoefficientBudgetExceeded");
        }

        private void RequireProofSize(string value)
        {
            if (Encoding.UTF8.GetByteCount(value) > _limits.MaxProofBytes)
                throw new RationalAdmissionException("MathProofBudgetExceeded");
        }

        private sealed record Admitted(Entity Expression, int NumeratorDegree, int DenominatorDegree);
    }
}
