using System.Text.RegularExpressions;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

// Pure production matcher smoke, not a substitute for Home/kernel/native acceptance.
public sealed class DeveloperProjectSearchTests
{
    [Fact]
    public void Literal_default_is_case_sensitive_and_keeps_one_actual_match_per_line()
    {
        var matcher = new DeveloperProjectSearchMatcher("alpha", new());
        var rows = matcher.MatchText("src/main.cui", "alpha alpha\r\nALPHA\rbeta alpha\n", 201);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 1, 3 }, rows.Select(value => value.LineNumber));
        Assert.Equal(new[] { 1, 6 }, rows.Select(value => value.ColumnNumber));
        Assert.Equal("alpha alpha", rows[0].LineText);
        Assert.All(rows, row => Assert.Equal("src/main.cui", row.RelativePath));
        var text = "alpha alpha\r\nALPHA\rbeta alpha\n";
        Assert.All(rows, row => Assert.Equal("alpha", text.Substring(row.CharacterOffset, row.Length)));
        Assert.All(rows, row => Assert.Equal(DeveloperManualSourceEditOwner.Sha256(text), row.ContentSha256));
    }
    [Fact]
    public void Explicit_case_insensitive_search_uses_actual_original_coordinates()
    {
        var matcher = new DeveloperProjectSearchMatcher("alpha", new(MatchCase: false));
        var text = "🔎 ALPHA\nalpha";
        var rows = matcher.MatchText("notes.cui", text, 201);
        Assert.Equal(2, rows.Count); Assert.Equal(4, rows[0].ColumnNumber); // UTF16 editor coordinates
        Assert.Equal("ALPHA", text.Substring(rows[0].CharacterOffset, rows[0].Length));
        Assert.Equal("alpha", text.Substring(rows[1].CharacterOffset, rows[1].Length));
    }
    [Fact]
    public void Regex_is_real_per_line_matching_and_supports_explicit_case_option()
    {
        var matcher = new DeveloperProjectSearchMatcher(@"^item-[0-9]+$", new(UseRegex: true, MatchCase: false));
        var text = "item-7\r\nITEM-42\nitem-x\nother item-8";
        var rows = matcher.MatchText("data.txt", text, 201);
        Assert.Equal(new[] { "item-7", "ITEM-42" }, rows.Select(row => text.Substring(row.CharacterOffset, row.Length)));
        Assert.Equal(new[] { 1, 2 }, rows.Select(row => row.LineNumber));
    }
    [Fact]
    public void Invalid_and_zero_width_regex_are_explicit_failures_not_empty_success()
    {
        Assert.Throws<ArgumentException>(() => new DeveloperProjectSearchMatcher("[", new(UseRegex: true)));
        Assert.Throws<ArgumentException>(() => new DeveloperProjectSearchMatcher("^", new(UseRegex: true)));
        var matcher = new DeveloperProjectSearchMatcher("(?=needle)", new(UseRegex: true));
        Assert.Throws<ArgumentException>(() => matcher.MatchText("a.cui", "needle", 201));
        Assert.Throws<ArgumentException>(() => new DeveloperProjectSearchMatcher(" ", new()));
        Assert.Throws<ArgumentException>(() => new DeveloperProjectSearchMatcher(new string('x', 4001), new()));
    }
    [Fact]
    public void Actual_result_bound_preserves_deterministic_first_matching_lines()
    {
        var matcher = new DeveloperProjectSearchMatcher("needle", new());
        var text = string.Join('\n', Enumerable.Range(0, 400).Select(i => "needle " + i));
        var rows = matcher.MatchText("many.txt", text, 201);
        Assert.Equal(201, rows.Count); Assert.Equal(201, rows[^1].LineNumber);
        Assert.Equal(1, new DeveloperProjectSearchMatcher("needle", new()).MatchText("many.txt", text, 1).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => matcher.MatchText("many.txt", text, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => matcher.MatchText("many.txt", text, 202));
    }
    [Fact]
    public void Preview_is_bounded_near_real_match_while_legacy_line_prefix_is_preserved()
    {
        var text = new string('a', 6000) + "needle" + new string('b', 6000);
        var row = Assert.Single(new DeveloperProjectSearchMatcher("needle", new()).MatchText("long.cui", text, 201));
        Assert.Equal(6001, row.ColumnNumber); Assert.Equal(6, row.Length);
        Assert.Contains("needle", row.Preview); Assert.InRange(row.Preview.Length, 1, 242);
        Assert.Equal(4096, row.LineText.Length); Assert.Equal(new string('a', 4096), row.LineText);
        Assert.Equal(DeveloperManualSourceEditOwner.Sha256(text), row.ContentSha256);
    }
    [Fact]
    public void Original_cancellation_is_checked_during_pure_matching()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new DeveloperProjectSearchMatcher("x", new()).MatchText("a.cui", "x", 201, canceled.Token));
    }
    [Fact]
    public void Catastrophic_regex_is_bounded_by_real_dotnet_timeout()
    {
        var matcher = new DeveloperProjectSearchMatcher("^(a+)+$", new(UseRegex: true));
        Assert.Throws<RegexMatchTimeoutException>(() => matcher.MatchText("slow.txt", new string('a', 100_000) + "!", 201));
    }
}
