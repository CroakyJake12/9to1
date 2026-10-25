using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperTextNavigationTests
{
    [Theory]
    [InlineData("one\r\ntwo\nthree\rfour", "2", 5)]
    [InlineData("one\r\ntwo\nthree\rfour", "2:4", 8)]
    [InlineData("one\r\ntwo\nthree\rfour", "4:1", 15)]
    [InlineData("one\n", "2:1", 4)]
    [InlineData("", "1", 0)]
    [InlineData("🔎x", "1:3", 2)]
    public void Actual_line_endings_and_UTF16_positions_map_without_changing_text(string text, string location, int expected)
    {
        Assert.True(DeveloperTextNavigation.TryResolve(text, location, out var offset));
        Assert.Equal(expected, offset);
    }

    [Theory]
    [InlineData("one", "0")]
    [InlineData("one", "2")]
    [InlineData("one", "1:0")]
    [InlineData("one", "1:5")]
    [InlineData("one", "1:2:3")]
    [InlineData("one", "2147483648")]
    [InlineData("one", "1:2147483647")]
    [InlineData("🔎x", "1:2")]
    public void Missing_or_invalid_positions_and_split_surrogates_are_explicit_refusals(string text, string location) =>
        Assert.False(DeveloperTextNavigation.TryResolve(text, location, out _));

    [Fact]
    public void Large_source_navigation_is_cancellable_and_keeps_exact_last_line_offset()
    {
        var text = string.Join('\n', Enumerable.Repeat("row", 200_000));
        Assert.True(DeveloperTextNavigation.TryResolve(text, "200000:2", out var offset));
        Assert.Equal(text.Length - 2, offset);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => DeveloperTextNavigation.TryResolve(text, "200000:2", out _, cancelled.Token));
    }
}
