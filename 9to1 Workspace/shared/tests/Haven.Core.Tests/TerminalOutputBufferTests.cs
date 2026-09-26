using System.Text;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class TerminalOutputBufferTests
{
    [Fact]
    public void AppendingPastLimitDropsOldestOutputAndMarksRetainedBoundary()
    {
        var buffer = new TerminalOutputBuffer(maximumBytes: 5);
        var first = buffer.Append(Output("abc"));
        var second = buffer.Append(Output("def"));

        var snapshot = buffer.Snapshot();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, snapshot.RetainedBytes);
        Assert.True(snapshot.IsTruncated);
        Assert.Equal(first, snapshot.TruncatedThroughSequence);
        Assert.Equal("def", Assert.Single(snapshot.Entries).Output.Text);
        Assert.Contains("Earlier terminal output was discarded", snapshot.DisplayText);
        var search = buffer.Search("DEF");
        Assert.Null(search.Failure);
        Assert.Equal(second, Assert.Single(search.Matches).Sequence);
    }

    [Fact]
    public void OversizedChunkRetainsItsTailAndOriginalBytes()
    {
        var buffer = new TerminalOutputBuffer(maximumBytes: 4);
        var original = Encoding.UTF8.GetBytes("abcdef");
        buffer.Append(Output("abcdef") with { RawBytes = original });

        var retained = Assert.Single(buffer.Snapshot().Entries).Output;

        Assert.Equal(4, buffer.Snapshot().RetainedBytes);
        Assert.Equal("cdef", retained.Text);
        Assert.Equal(Encoding.UTF8.GetBytes("cdef"), retained.RawBytes!.Value.ToArray());
        Assert.Equal(2, buffer.Snapshot().TruncatedPrefixBytes);
    }

    [Fact]
    public void InvalidRegexAndUnboundedResultRequestReturnTypedFailures()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.Append(Output("terminal output"));

        Assert.Equal("InvalidRegularExpression", buffer.Search("[", regularExpression: true).Failure?.Code);
        Assert.Equal("InvalidSearchLimit", buffer.Search("terminal", maximumResults: 2_001).Failure?.Code);
    }

    [Fact]
    public void ClearRemovesContentAndTruncationState()
    {
        var buffer = new TerminalOutputBuffer(maximumBytes: 2);
        buffer.Append(Output("old"));

        buffer.Clear();

        var snapshot = buffer.Snapshot();
        Assert.Empty(snapshot.Entries);
        Assert.Equal(0, snapshot.RetainedBytes);
        Assert.False(snapshot.IsTruncated);
    }

    private static TerminalSessionOutput Output(string text) => new(
        Guid.NewGuid(), null, TerminalOutputStream.StandardOutput, text, DateTimeOffset.UtcNow);
}
