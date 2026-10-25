using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperTextLineEndingsTests
{
    [Fact]
    public void Inspection_counts_CRLF_once_and_reports_mixed_endings_and_exact_final_newline_presence()
    {
        var result = DeveloperTextLineEndings.Inspect("A\r\nB\nC\rD\r");
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new DeveloperTextLineEndingInfo(1, 1, 2, true), result.Value); Assert.True(result.Value!.IsMixed); Assert.Equal(4, result.Value.TotalBreaks);
        Assert.False(DeveloperTextLineEndings.Inspect("A\r\nB").Value!.EndsInNewline);
        Assert.Equal(new DeveloperTextLineEndingInfo(0, 0, 0, false), DeveloperTextLineEndings.Inspect("").Value);
    }

    [Theory]
    [InlineData(DeveloperTextLineEndingTarget.Lf, "\uFEFFA\n😀B\nC\nD\t\u2028tail\n")]
    [InlineData(DeveloperTextLineEndingTarget.CrLf, "\uFEFFA\r\n😀B\r\nC\r\nD\t\u2028tail\r\n")]
    public void Normalization_preserves_every_other_UTF16_unit_and_existing_final_newline(DeveloperTextLineEndingTarget target, string expected)
    {
        const string source = "\uFEFFA\r\n😀B\nC\rD\t\u2028tail\r";
        var result = DeveloperTextLineEndings.Normalize(source, target);
        Assert.True(result.Succeeded, result.Error?.Message); Assert.Equal(expected, result.Value!.Text);
        Assert.Equal(3, result.Value.ChangedBreaks); Assert.False(result.Value.After.IsMixed); Assert.True(result.Value.After.EndsInNewline);
        Assert.Equal(result.Value.After, DeveloperTextLineEndings.Inspect(expected).Value);
        Assert.Equal(4, result.Value.After.TotalBreaks);
    }

    [Fact]
    public void Conversion_never_adds_an_absent_final_newline_and_conforming_source_keeps_same_text_reference()
    {
        const string source = "A\nB";
        var result = DeveloperTextLineEndings.Normalize(source, DeveloperTextLineEndingTarget.CrLf);
        Assert.True(result.Succeeded); Assert.Equal("A\r\nB", result.Value!.Text); Assert.False(result.Value.After.EndsInNewline);
        var unchanged = DeveloperTextLineEndings.Normalize(source, DeveloperTextLineEndingTarget.Lf);
        Assert.Same(source, unchanged.Value!.Text); Assert.Equal(0, unchanged.Value.ChangedBreaks);
        var empty = DeveloperTextLineEndings.Normalize("", DeveloperTextLineEndingTarget.CrLf);
        Assert.True(empty.Succeeded); Assert.Equal("", empty.Value!.Text); Assert.Equal(0, empty.Value.After.TotalBreaks);
    }

    [Fact]
    public void Oversized_or_binary_input_and_CRLF_growth_refuse_without_partial_text()
    {
        Assert.False(DeveloperTextLineEndings.Inspect(new string('a', DeveloperTextLineEndings.MaximumCharacters + 1)).Succeeded);
        Assert.False(DeveloperTextLineEndings.Inspect("A\0B").Succeeded);
        var growth = DeveloperTextLineEndings.Normalize(new string('\n', DeveloperTextLineEndings.MaximumCharacters / 2 + 1), DeveloperTextLineEndingTarget.CrLf);
        Assert.False(growth.Succeeded); Assert.Null(growth.Value); Assert.Contains("2,000,000", growth.Error!.Message);
        var unknown = DeveloperTextLineEndings.Normalize("A\nB", (DeveloperTextLineEndingTarget)99);
        Assert.False(unknown.Succeeded); Assert.Null(unknown.Value);
    }

    [Fact]
    public void Exact_cancelled_request_returns_no_inspection_or_conversion()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => { _ = DeveloperTextLineEndings.Inspect("A\r\nB", cancelled.Token); }));
        Assert.Throws<OperationCanceledException>((Action)(() => { _ = DeveloperTextLineEndings.Normalize("A\r\nB", DeveloperTextLineEndingTarget.Lf, cancelled.Token); }));
    }
}
