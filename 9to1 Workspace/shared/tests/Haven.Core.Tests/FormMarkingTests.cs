using System.Text.Json;
using Haven.Core.Forms;
using Xunit;

namespace Haven.Core.Tests;

public sealed class FormMarkingTests
{
    [Theory]
    [InlineData("cpu")]
    [InlineData("  Central   Processing Unit  ")]
    public void MultipleAcceptedAnswersUseExplicitAuthorNormalisation(string answer)
    {
        var rule = new FormMarkingRule(Guid.NewGuid(), FormMarkingRuleKind.AcceptedText, 2,
            AcceptedTexts: ["CPU", "Central Processing Unit"], Normalisation: new(IgnoreCase: true, Trim: true, CollapseWhitespace: true));
        var result = FormMarking.Evaluate(JsonSerializer.SerializeToElement(answer), [rule], 2);
        Assert.Equal(FormMarkingOutcome.Correct, result.Outcome);
        Assert.Equal(2, result.AwardedPoints);
        Assert.Equal(rule.RuleID, Assert.Single(result.MatchedRuleIDs));
    }

    [Fact]
    public void RulesPreserveTypedNumberAndChoiceIdentityAndPartialPoints()
    {
        var rule = new FormMarkingRule(Guid.NewGuid(), FormMarkingRuleKind.NumberWithinTolerance, 1,
            ExpectedNumber: 10, Tolerance: .1m);
        Assert.Equal(FormMarkingOutcome.Partial, FormMarking.Evaluate(JsonSerializer.SerializeToElement(10.05m), [rule], 2).Outcome);
        Assert.Equal(FormMarkingOutcome.Incorrect, FormMarking.Evaluate(JsonSerializer.SerializeToElement("10.05"), [rule], 2).Outcome);
        var choices = new FormMarkingRule(Guid.NewGuid(), FormMarkingRuleKind.ChoiceSet, 2, ChoiceIDs: ["id-a", "id-b"]);
        Assert.Equal(FormMarkingOutcome.Correct, FormMarking.Evaluate(JsonSerializer.SerializeToElement(new[] { "id-b", "id-a" }), [choices], 2).Outcome);
        Assert.Equal(FormMarkingOutcome.Incorrect, FormMarking.Evaluate(JsonSerializer.SerializeToElement(new[] { "id-a", "id-a", "id-b" }), [choices], 2).Outcome);
    }

    [Fact]
    public void RegexTesterDistinguishesFullAndPartialAndReportsInvalidPatterns()
    {
        Assert.True(FormMarking.TestRegex("CPU", "The CPU works", false));
        Assert.True(FormMarking.TestRegex("a|ab", "ab", true));
        Assert.False(FormMarking.TestRegex("CPU", "The CPU works", true));
        Assert.ThrowsAny<ArgumentException>(() => FormMarking.TestRegex("[", "test", true));
    }

    [Fact]
    public void CatastrophicRegexFailsClosedIntoReviewInsteadOfAwardingPoints()
    {
        var rule = new FormMarkingRule(Guid.NewGuid(), FormMarkingRuleKind.Regex, 2, Pattern: "(a+)+$");
        var result = FormMarking.Evaluate(JsonSerializer.SerializeToElement(new string('a', 2000) + "!"), [rule], 2);
        Assert.Equal(FormMarkingOutcome.NeedsReview, result.Outcome);
        Assert.Equal(0, result.AwardedPoints);
        Assert.Empty(result.MatchedRuleIDs);
        Assert.Equal("RegexTimeout", result.Diagnostic);
    }

    [Fact]
    public void BooleanRulesCombineWithoutDuplicatePointsAndRejectDuplicateIdentity()
    {
        var one = new FormMarkingRule(Guid.NewGuid(), FormMarkingRuleKind.AcceptedText, 1, AcceptedTexts: ["CPU"]);
        var two = new FormMarkingRule(Guid.NewGuid(), FormMarkingRuleKind.Regex, 1, Pattern: "CPU");
        var any = new FormMarkingRule(Guid.NewGuid(), FormMarkingRuleKind.Any, 2, Children: [one, two]);
        Assert.Equal(2, FormMarking.Evaluate(JsonSerializer.SerializeToElement("CPU"), [any], 2).AwardedPoints);
        Assert.Throws<ArgumentException>(() => FormMarking.Evaluate(JsonSerializer.SerializeToElement("CPU"), [one, one], 2));
    }
}
