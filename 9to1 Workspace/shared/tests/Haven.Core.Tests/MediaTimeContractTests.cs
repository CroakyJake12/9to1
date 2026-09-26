using Haven.Core.Media;

namespace Haven.Core.Tests;

public sealed class MediaTimeContractTests
{
    [Fact]
    public void SampleAndFrameTimebasesCompareByExactSeconds()
    {
        var sample = MediaTimebase.SamplesPerSecond(48_000).At(24_000);
        var frame = MediaTimebase.FramesPerSecond(24).At(12);

        Assert.Equal(0, sample.CompareTo(frame));
        Assert.True(sample <= frame);
        Assert.True(frame >= sample);
    }

    [Fact]
    public void ConvertToRoundsSignedValuesDeterministically()
    {
        var source = new MediaTimebase(1, 4);
        var target = new MediaTimebase(1, 2);

        Assert.Equal(1, source.At(1).ConvertTo(target, MidpointRounding.AwayFromZero).Ticks);
        Assert.Equal(-1, source.At(-1).ConvertTo(target, MidpointRounding.AwayFromZero).Ticks);
        Assert.Equal(0, source.At(1).ConvertTo(target, MidpointRounding.ToEven).Ticks);
        Assert.Equal(0, source.At(-1).ConvertTo(target, MidpointRounding.ToEven).Ticks);
    }

    [Fact]
    public void TimeRangeUsesHalfOpenBoundariesAndRejectsMixedTimebases()
    {
        var timebase = MediaTimebase.SamplesPerSecond(48_000);
        var range = new MediaTimeRange(timebase.At(100), timebase.At(25));

        Assert.True(range.Contains(timebase.At(100)));
        Assert.True(range.Contains(timebase.At(124)));
        Assert.False(range.Contains(timebase.At(125)));
        Assert.Throws<ArgumentException>(() => new MediaTimeRange(timebase.At(0), MediaTimebase.Nanoseconds.At(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTimeRange(timebase.At(0), timebase.At(0)));
    }

    [Fact]
    public void ClipValidationRejectsEmptyIdentityAndOverflowingRanges()
    {
        var timebase = MediaTimebase.SamplesPerSecond(48_000);
        var valid = new MediaTimelineClip(Guid.NewGuid(), Guid.NewGuid(), MediaAssetId.New(),
            timebase.At(0), timebase.At(10), timebase.At(0));
        valid.Validate();

        var invalid = valid with { ClipId = Guid.Empty };
        Assert.Throws<InvalidDataException>(invalid.Validate);

        var overflow = valid with { TimelineStart = timebase.At(long.MaxValue - 2), Duration = timebase.At(10) };
        Assert.Throws<OverflowException>(overflow.Validate);
    }

    [Fact]
    public void NonDestructiveTimelineEditsPreserveSourceAlignmentAndStableIdentity()
    {
        var timebase = MediaTimebase.FramesPerSecond(30);
        var clip = new MediaTimelineClip(Guid.NewGuid(), Guid.NewGuid(), MediaAssetId.New(),
            timebase.At(90), timebase.At(60), timebase.At(300));

        var (left, right) = MediaTimelineEdits.Split(clip, timebase.At(325), Guid.NewGuid());
        Assert.Equal(clip.ClipId, left.ClipId);
        Assert.Equal(25, left.Duration.Ticks);
        Assert.Equal(115, right.SourceStart.Ticks);
        Assert.Equal(35, right.Duration.Ticks);
        Assert.Equal(325, right.TimelineStart.Ticks);

        var trimmed = MediaTimelineEdits.TrimStart(clip, timebase.At(10));
        Assert.Equal(100, trimmed.SourceStart.Ticks);
        Assert.Equal(310, trimmed.TimelineStart.Ticks);
        Assert.Equal(50, trimmed.Duration.Ticks);
        var moved = MediaTimelineEdits.Move(clip, timebase.At(500));
        Assert.Equal(clip.ClipId, moved.ClipId);
        Assert.Equal(500, moved.TimelineStart.Ticks);
        Assert.Equal(clip.SourceStart, moved.SourceStart);
        Assert.Equal(clip.Duration, moved.Duration);
    }
}
