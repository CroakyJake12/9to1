using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperTextSearchTests
{
    [Fact]
    public void Find_next_previous_and_wrap_use_actual_literal_ranges()
    {
        const string text = "one two one";
        Assert.Equal(new DeveloperTextMatch(0, 3, false), DeveloperTextSearch.Find(text, "one", 0, false, true));
        Assert.Equal(new DeveloperTextMatch(8, 3, false), DeveloperTextSearch.Find(text, "one", 3, false, true));
        Assert.Equal(new DeveloperTextMatch(0, 3, true), DeveloperTextSearch.Find(text, "one", text.Length, false, true));
        Assert.Equal(new DeveloperTextMatch(0, 3, false), DeveloperTextSearch.Find(text, "one", 8, true, true));
        Assert.Equal(new DeveloperTextMatch(8, 3, true), DeveloperTextSearch.Find(text, "one", 0, true, true));
        Assert.Null(DeveloperTextSearch.Find(text, "ONE", 0, false, true));
        Assert.Equal(0, DeveloperTextSearch.Find(text, "ONE", 0, false, false)!.Start);
    }

    [Theory]
    [InlineData("a.*b.*", ".*", "$1", true, "a$1b$1", 2)]
    [InlineData("aaaa", "aa", "x", true, "xx", 2)]
    [InlineData("aa", "a", "aa", true, "aaaa", 2)]
    [InlineData("foofoo", "foo", "", true, "", 2)]
    [InlineData("Alpha alpha ALPHA", "alpha", "x", false, "x x x", 3)]
    [InlineData("Alpha alpha ALPHA", "alpha", "x", true, "Alpha x ALPHA", 1)]
    public void Replace_all_is_literal_nonoverlapping_and_never_reprocesses_inserted_text(
        string text, string query, string replacement, bool matchCase, string expected, int count)
    {
        var result = DeveloperTextSearch.ReplaceAll(text, query, replacement, matchCase);
        Assert.Null(result.Error); Assert.Equal(expected, result.Text); Assert.Equal(count, result.Matches);
    }

    [Fact]
    public void Stale_match_binary_replacement_and_overflow_preserve_the_complete_original_draft()
    {
        const string current = "beta alpha";
        var stale = DeveloperTextSearch.ReplaceMatch(current, "alpha", "replacement", new(0, 5, false), true);
        Assert.NotNull(stale.Error); Assert.Same(current, stale.Text); Assert.Equal(0, stale.Matches);
        var binary = DeveloperTextSearch.ReplaceAll(current, "alpha", "bad\0replacement", true);
        Assert.NotNull(binary.Error); Assert.Same(current, binary.Text);
        var full = new string('a', DeveloperSourceReviewSession.MaximumTextCharacters);
        var tooLarge = DeveloperTextSearch.ReplaceAll(full, "a", "aa", true);
        Assert.NotNull(tooLarge.Error); Assert.Same(full, tooLarge.Text); Assert.Equal(0, tooLarge.Matches);
    }

    [Fact]
    public void Cancelled_search_or_replacement_never_returns_a_partial_success()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => DeveloperTextSearch.Find("source", "o", 0, false, true, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => DeveloperTextSearch.ReplaceAll("source", "o", "new", true, cancelled.Token));
    }
}
