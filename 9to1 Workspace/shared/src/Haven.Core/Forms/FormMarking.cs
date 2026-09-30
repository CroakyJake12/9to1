using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Haven.Core.Forms;

public enum FormMarkingOutcome { Incorrect, Correct, Partial, NeedsReview }
public enum FormMarkingRuleKind { AcceptedText, Regex, NumberWithinTolerance, ChoiceSet, All, Any }
public sealed record FormTextNormalisation(bool IgnoreCase = false, bool Trim = false,
    bool CollapseWhitespace = false, bool NormaliseUnicode = false, bool IgnoreDiacritics = false,
    string IgnoredPunctuation = "");
public sealed record FormMarkingRule(Guid RuleID, FormMarkingRuleKind Kind, decimal Points,
    string[]? AcceptedTexts = null, FormTextNormalisation? Normalisation = null,
    string? Pattern = null, bool FullMatch = true, bool RegexIgnoreCase = false,
    decimal? ExpectedNumber = null, decimal Tolerance = 0, string[]? ChoiceIDs = null,
    FormMarkingRule[]? Children = null);
public sealed record FormMarkingResult(FormMarkingOutcome Outcome, decimal AwardedPoints,
    decimal MaximumPoints, IReadOnlyList<Guid> MatchedRuleIDs, string? Diagnostic = null);

/// <summary>One bounded deterministic engine used by Form/Test/Quiz; no model-generated grading.</summary>
public static class FormMarking
{
    private const int MaxDepth = 16;
    private const int MaxRules = 256;
    private const int MaxTextLength = 65536;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static FormMarkingResult Evaluate(JsonElement answer, IReadOnlyList<FormMarkingRule> rules, decimal maximumPoints)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (maximumPoints < 0) throw new ArgumentOutOfRangeException(nameof(maximumPoints));
        var ids = new HashSet<Guid>();
        var count = 0;
        foreach (var rule in rules) Validate(rule, maximumPoints, ids, ref count, 0);
        var matched = new List<Guid>();
        decimal points = 0;
        try
        {
            foreach (var rule in rules)
                if (Matches(answer, rule, matched)) points = Math.Max(points, rule.Points);
        }
        catch (RegexMatchTimeoutException)
        {
            return new(FormMarkingOutcome.NeedsReview, 0, maximumPoints, Array.Empty<Guid>(), "RegexTimeout");
        }
        var outcome = matched.Count == 0 ? FormMarkingOutcome.Incorrect
            : points == maximumPoints ? FormMarkingOutcome.Correct : FormMarkingOutcome.Partial;
        return new(outcome, points, maximumPoints, matched.Distinct().ToArray());
    }

    public static bool TestRegex(string pattern, string input, bool fullMatch, bool ignoreCase = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > MaxTextLength || pattern.Length > 4096) throw new ArgumentOutOfRangeException(nameof(input));
        var effectivePattern = fullMatch ? @"\A(?:" + pattern + @")\z" : pattern;
        var match = new Regex(effectivePattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), RegexTimeout).Match(input);
        return match.Success;
    }

    private static void Validate(FormMarkingRule rule, decimal maximum, HashSet<Guid> ids, ref int count, int depth)
    {
        if (depth > MaxDepth || ++count > MaxRules) throw new ArgumentException("Marking rules exceed supported bounds.");
        if (rule.RuleID == Guid.Empty || !ids.Add(rule.RuleID) || !Enum.IsDefined(rule.Kind) || rule.Points < 0 || rule.Points > maximum)
            throw new ArgumentException("Invalid marking identity, kind or points.");
        if (rule.Kind == FormMarkingRuleKind.AcceptedText && (rule.AcceptedTexts is not { Length: > 0 } || rule.AcceptedTexts.Any(x => x is null || x.Length > MaxTextLength)))
            throw new ArgumentException("Accepted text rules need bounded answers.");
        if (rule.Kind == FormMarkingRuleKind.Regex)
        {
            if (string.IsNullOrEmpty(rule.Pattern) || rule.Pattern.Length > 4096) throw new ArgumentException("Invalid regex pattern.");
            _ = new Regex(rule.Pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        if (rule.Kind == FormMarkingRuleKind.NumberWithinTolerance && (rule.ExpectedNumber is null || rule.Tolerance < 0))
            throw new ArgumentException("Numeric rules need a value and nonnegative tolerance.");
        if (rule.Kind == FormMarkingRuleKind.ChoiceSet && (rule.ChoiceIDs is null || rule.ChoiceIDs.Any(string.IsNullOrWhiteSpace) || rule.ChoiceIDs.Distinct(StringComparer.Ordinal).Count() != rule.ChoiceIDs.Length))
            throw new ArgumentException("Choice rules require unique stable option IDs.");
        if (rule.Kind is FormMarkingRuleKind.All or FormMarkingRuleKind.Any)
        {
            if (rule.Children is not { Length: > 0 }) throw new ArgumentException("Boolean rules need children.");
            foreach (var child in rule.Children) Validate(child, maximum, ids, ref count, depth + 1);
        }
        else if (rule.Children is { Length: > 0 }) throw new ArgumentException("Leaf rules cannot contain children.");
    }

    private static bool Matches(JsonElement answer, FormMarkingRule rule, List<Guid> matches)
    {
        var childMatches = new List<Guid>();
        var matched = rule.Kind switch
        {
            FormMarkingRuleKind.AcceptedText => answer.ValueKind == JsonValueKind.String && rule.AcceptedTexts!.Any(value => Normalise(value, rule.Normalisation) == Normalise(answer.GetString()!, rule.Normalisation)),
            FormMarkingRuleKind.Regex => answer.ValueKind == JsonValueKind.String && TestRegex(rule.Pattern!, answer.GetString()!, rule.FullMatch, rule.RegexIgnoreCase),
            FormMarkingRuleKind.NumberWithinTolerance => answer.ValueKind == JsonValueKind.Number && answer.TryGetDecimal(out var number) && WithinTolerance(number, rule.ExpectedNumber!.Value, rule.Tolerance),
            FormMarkingRuleKind.ChoiceSet => answer.ValueKind == JsonValueKind.Array && ChoiceSetMatches(answer, rule.ChoiceIDs!),
            FormMarkingRuleKind.All => rule.Children!.All(child => Matches(answer, child, childMatches)),
            FormMarkingRuleKind.Any => rule.Children!.Any(child => Matches(answer, child, childMatches)),
            _ => false
        };
        if (matched) { matches.Add(rule.RuleID); matches.AddRange(childMatches); }
        return matched;
    }

    private static bool WithinTolerance(decimal actual, decimal expected, decimal tolerance)
    {
        try { return Math.Abs(actual - expected) <= tolerance; }
        catch (OverflowException) { return false; }
    }
    private static bool ChoiceSetMatches(JsonElement answer, string[] choices)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in answer.EnumerateArray())
            if (item.ValueKind != JsonValueKind.String || !values.Add(item.GetString()!)) return false;
        return values.SetEquals(choices);
    }
    private static string Normalise(string input, FormTextNormalisation? options)
    {
        if (input.Length > MaxTextLength) throw new ArgumentOutOfRangeException(nameof(input));
        options ??= new();
        if (options.NormaliseUnicode) input = input.Normalize(NormalizationForm.FormC);
        if (options.IgnoreDiacritics)
            input = string.Concat(input.Normalize(NormalizationForm.FormD).EnumerateRunes()
                .Where(rune => Rune.GetUnicodeCategory(rune) is not UnicodeCategory.NonSpacingMark and not UnicodeCategory.SpacingCombiningMark and not UnicodeCategory.EnclosingMark)
                .Select(rune => rune.ToString())).Normalize(NormalizationForm.FormC);
        if (options.IgnoredPunctuation.Length > 0)
            input = string.Concat(input.Where(character => !options.IgnoredPunctuation.Contains(character)));
        if (options.CollapseWhitespace) input = string.Join(" ", input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (options.Trim) input = input.Trim();
        return options.IgnoreCase ? input.ToUpperInvariant() : input;
    }
}
