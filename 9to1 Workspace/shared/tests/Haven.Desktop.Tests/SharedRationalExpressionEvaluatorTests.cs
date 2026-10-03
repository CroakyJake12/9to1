using Haven.Core.Mathematics;
using Haven.Desktop.Mathematics;

namespace Haven.Desktop.Tests;

public sealed class SharedRationalExpressionEvaluatorTests
{
    [Theory]
    [InlineData(@"\frac{1}{2}", @"\frac{2}{4}")]
    [InlineData("1.20", @"\frac{6}{5}")]
    [InlineData("1+2", "3")]
    [InlineData("(x+1)^2", "x^2+2x+1")]
    [InlineData(@"\frac{x^2}{1}", "x^2")]
    [InlineData(@"\frac{1}{x}+\frac{1}{x}", @"\frac{2}{x}")]
    [InlineData(@"\frac{1}{x}+\frac{2}{x}", @"\frac{2}{x}+\frac{1}{x}")]
    public async Task Exact_rational_proof_binds_original_versions_and_original_domain(string expected, string candidate)
    {
        var original = new MathExpression(Guid.NewGuid(), 4, expected);
        var submitted = new MathExpression(Guid.NewGuid(), 9, candidate);
        var beforeExpected = MathObjectCodec.Encode(original); var beforeCandidate = MathObjectCodec.Encode(submitted);
        var result = await new AngouriRationalExpressionEvaluator().CompareAsync(original, submitted, new(), TestContext.Current.CancellationToken);
        Assert.Equal(MathExpressionEquivalenceOutcome.Equivalent, result.Outcome);
        Assert.Equal(new MathExpressionReference(original.ExpressionID, original.Revision), result.Expected);
        Assert.Equal(new MathExpressionReference(submitted.ExpressionID, submitted.Revision), result.Candidate);
        Assert.Equal(64, result.OriginalDomainDigest?.Length);
        Assert.Equal(64, result.NormalFormDigest?.Length);
        Assert.Equal(64, result.MaintainedNormalizationDigest?.Length);
        Assert.Null(result.Diagnostic);
        Assert.Equal(beforeExpected, MathObjectCodec.Encode(original));
        Assert.Equal(beforeCandidate, MathObjectCodec.Encode(submitted));
    }

    [Theory]
    [InlineData("e")]
    [InlineData("i")]
    public async Task Declared_letters_with_library_constant_meanings_bind_an_ordinary_variable_and_preserve_holes(string variable)
    {
        var original = new MathExpression(Guid.NewGuid(), 3, variable + "^2+2" + variable + "+1");
        var candidate = new MathExpression(Guid.NewGuid(), 7, "(" + variable + "+1)^2");
        var originalBytes = MathObjectCodec.Encode(original); var candidateBytes = MathObjectCodec.Encode(candidate);
        var evaluator = new AngouriRationalExpressionEvaluator();
        var domainDigests = new List<string>();
        foreach (var domain in new[] { MathEvaluationDomain.Real, MathEvaluationDomain.Complex })
        {
            var policy = new MathExpressionEvaluationPolicy(domain, variable);
            var result = await evaluator.CompareAsync(original, candidate, policy, TestContext.Current.CancellationToken);
            Assert.Equal(MathExpressionEquivalenceOutcome.Equivalent, result.Outcome);
            Assert.Equal(variable, result.Variable); Assert.Equal(domain, result.Domain);
            Assert.Equal(new MathExpressionReference(original.ExpressionID, original.Revision), result.Expected);
            Assert.Equal(new MathExpressionReference(candidate.ExpressionID, candidate.Revision), result.Candidate);
            Assert.Equal(64, result.OriginalDomainDigest?.Length);
            Assert.Equal(64, result.NormalFormDigest?.Length);
            Assert.Equal(64, result.MaintainedNormalizationDigest?.Length);
            Assert.Null(result.Diagnostic);
            domainDigests.Add(result.OriginalDomainDigest!);
            var hole = await Compare(@"\frac{" + variable + "}{" + variable + "}", "1", policy);
            Assert.Equal(MathExpressionEquivalenceOutcome.NeedsReview, hole.Outcome);
            Assert.Equal("OriginalDomainsNotProvedEquivalent", hole.Diagnostic);
            Assert.Equal(variable, hole.Variable); Assert.Equal(domain, hole.Domain);
            Assert.Null(hole.OriginalDomainDigest); Assert.Null(hole.NormalFormDigest);
        }
        Assert.NotEqual(domainDigests[0], domainDigests[1]);
        Assert.Equal(originalBytes, MathObjectCodec.Encode(original));
        Assert.Equal(candidateBytes, MathObjectCodec.Encode(candidate));
    }

    [Theory]
    [InlineData(@"\frac{0}{x}", "0")]
    [InlineData(@"\frac{1}{\frac{1}{x}}", "x")]
    [InlineData(@"\frac{x}{x}", "1")]
    [InlineData(@"\frac{x^2-1}{x-1}", "x+1")]
    [InlineData("x^0", "1")]
    [InlineData(@"\frac{1}{2x}", @"\frac{0.5}{x}")]
    public async Task Equal_normal_values_do_not_erase_holes_or_claim_unproved_domain_equivalence(string expected, string candidate)
    {
        var result = await Compare(expected, candidate);
        Assert.Equal(MathExpressionEquivalenceOutcome.NeedsReview, result.Outcome);
        Assert.Equal("OriginalDomainsNotProvedEquivalent", result.Diagnostic);
        Assert.Null(result.OriginalDomainDigest); Assert.Null(result.NormalFormDigest);
    }

    [Theory]
    [InlineData("0^0")]
    [InlineData(@"\frac{0}{0}")]
    [InlineData(@"\frac{0}{x-x}")]
    [InlineData(@"\frac{1}{x-x}")]
    public async Task Undefined_constant_or_identically_zero_denominator_is_refused(string source)
    {
        var result = await Compare(source, "1");
        Assert.Equal(MathExpressionEquivalenceOutcome.NeedsReview, result.Outcome);
        Assert.Equal("MathOriginalDomainEmptyOrIndeterminate", result.Diagnostic);
    }

    [Theory]
    [InlineData(@"\sin{x}")]
    [InlineData(@"\sqrt{x}")]
    [InlineData(@"x^{\frac{1}{2}}")]
    [InlineData("y")]
    [InlineData("x=1")]
    [InlineData("x<1")]
    [InlineData("x+")]
    public async Task Unsupported_or_malformed_syntax_remains_review_without_numeric_sampling(string source)
    {
        var result = await Compare(source, source);
        Assert.Equal(MathExpressionEquivalenceOutcome.NeedsReview, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
        Assert.Null(result.NormalFormDigest);
    }

    [Fact]
    public async Task Tiny_exact_nonzero_rational_is_preserved_before_domain_and_coefficient_checks()
    {
        var policy = new MathExpressionEvaluationPolicy(Limits: new(MaxLiteralDigits: 24));
        var fraction = @"\frac{1}{100000000000000000000}";
        var exact = await Compare(fraction, "0.00000000000000000001", policy);
        Assert.Equal(MathExpressionEquivalenceOutcome.Equivalent, exact.Outcome);
        var zero = await Compare(fraction, "0", policy);
        Assert.Equal(MathExpressionEquivalenceOutcome.NeedsReview, zero.Outcome);
        Assert.Equal("RationalNormalFormsNotProvedEquivalent", zero.Diagnostic);
        var reciprocal = await Compare(@"\frac{1}{0.00000000000000000001}", "100000000000000000000", policy);
        Assert.Equal(MathExpressionEquivalenceOutcome.Equivalent, reciprocal.Outcome);
    }

    [Fact]
    public async Task Domain_reading_is_explicit_and_sealed_even_for_the_same_rational_tree()
    {
        var real = await Compare("x+1", "1+x", new(MathEvaluationDomain.Real));
        var complex = await Compare("x+1", "1+x", new(MathEvaluationDomain.Complex));
        Assert.Equal(MathExpressionEquivalenceOutcome.Equivalent, real.Outcome);
        Assert.Equal(MathExpressionEquivalenceOutcome.Equivalent, complex.Outcome);
        Assert.Equal(MathEvaluationDomain.Real, real.Domain); Assert.Equal(MathEvaluationDomain.Complex, complex.Domain);
        Assert.NotEqual(real.OriginalDomainDigest, complex.OriginalDomainDigest);
    }

    [Fact]
    public async Task Same_ID_and_revision_cannot_name_two_different_canonical_bodies()
    {
        var original = new MathExpression(Guid.NewGuid(), long.MaxValue, "x");
        var changed = original with { LaTeX = "x+0" };
        var result = await new AngouriRationalExpressionEvaluator().CompareAsync(original, changed, new(), TestContext.Current.CancellationToken);
        Assert.Equal(MathExpressionEquivalenceOutcome.NeedsReview, result.Outcome);
        Assert.Equal("ConflictingMathExpressionIdentity", result.Diagnostic);
        Assert.Equal(long.MaxValue, original.Revision); Assert.Equal("x", original.LaTeX);
    }

    [Fact]
    public async Task Declared_source_literal_degree_and_coefficient_limits_refuse_original_input()
    {
        var source = await Compare("x+x+x+x", "1", new(Limits: new(MaxSourceCharacters: 4, MaxLiteralDigits: 4)));
        Assert.Equal("MathSourceBudgetExceeded", source.Diagnostic);
        var literal = await Compare("12345", "1", new(Limits: new(MaxLiteralDigits: 4)));
        Assert.Equal("MathLiteralBudgetExceeded", literal.Diagnostic);
        var degree = await Compare("(x+1)^4", "1", new(Limits: new(MaxExpandedDegree: 3)));
        Assert.Equal("MathExpandedDegreeBudgetExceeded", degree.Diagnostic);
        var coefficient = await Compare("256", "1", new(Limits: new(MaxCoefficientBits: 8)));
        Assert.Equal("MathCoefficientBudgetExceeded", coefficient.Diagnostic);
        Assert.All(new[] { source, literal, degree, coefficient }, result =>
            Assert.Equal(MathExpressionEquivalenceOutcome.NeedsReview, result.Outcome));
    }

    [Fact]
    public async Task Original_cancellation_propagates_without_mutating_the_canonical_expression()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var original = new MathExpression(Guid.NewGuid(), 2, "x+1"); var before = MathObjectCodec.Encode(original);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AngouriRationalExpressionEvaluator().CompareAsync(original, original, new(), cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(before, MathObjectCodec.Encode(original));
    }

    private static ValueTask<MathExpressionEquivalenceResult> Compare(string expected, string candidate,
        MathExpressionEvaluationPolicy? policy = null) => new AngouriRationalExpressionEvaluator().CompareAsync(
            new(Guid.NewGuid(), 1, expected), new(Guid.NewGuid(), 1, candidate), policy ?? new(), TestContext.Current.CancellationToken);
}
