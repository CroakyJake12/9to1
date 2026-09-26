using System.Numerics;

namespace Haven.Core.Media;

/// <summary>A stable identity for a media asset independent of its storage location.</summary>
public readonly record struct MediaAssetId(Guid Value)
{
    public static MediaAssetId New() => new(Guid.NewGuid());
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString("N");
}

/// <summary>
/// An exact rational number of seconds represented by one timeline tick. Audio samples use
/// 1/sample-rate; video frames use frame-rate-denominator/frame-rate-numerator.
/// </summary>
public readonly record struct MediaTimebase
{
    public long SecondsNumerator { get; }
    public long SecondsDenominator { get; }
    public bool IsValid => SecondsNumerator > 0 && SecondsDenominator > 0;

    public MediaTimebase(long secondsNumerator, long secondsDenominator)
    {
        if (secondsNumerator <= 0) throw new ArgumentOutOfRangeException(nameof(secondsNumerator));
        if (secondsDenominator <= 0) throw new ArgumentOutOfRangeException(nameof(secondsDenominator));
        var divisor = GreatestCommonDivisor(secondsNumerator, secondsDenominator);
        SecondsNumerator = secondsNumerator / divisor;
        SecondsDenominator = secondsDenominator / divisor;
    }

    public static MediaTimebase Nanoseconds { get; } = new(1, 1_000_000_000);

    public static MediaTimebase SamplesPerSecond(int sampleRate) =>
        sampleRate > 0 ? new MediaTimebase(1, sampleRate) : throw new ArgumentOutOfRangeException(nameof(sampleRate));

    public static MediaTimebase FramesPerSecond(int numerator, int denominator = 1) =>
        numerator > 0 && denominator > 0
            ? new MediaTimebase(denominator, numerator)
            : throw new ArgumentOutOfRangeException(nameof(numerator));

    public MediaTime At(long ticks) => new(ticks, this);

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return left;
    }
}

/// <summary>A signed timeline value whose integer ticks retain exact sample/frame boundaries.</summary>
public readonly record struct MediaTime(long Ticks, MediaTimebase Timebase) : IComparable<MediaTime>
{
    public bool IsValid => Timebase.IsValid;

    public MediaTime ConvertTo(MediaTimebase target, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        EnsureValid();
        if (!target.IsValid) throw new ArgumentException("Target timebase is invalid.", nameof(target));

        var numerator = (BigInteger)Ticks * Timebase.SecondsNumerator * target.SecondsDenominator;
        var denominator = (BigInteger)Timebase.SecondsDenominator * target.SecondsNumerator;
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (!remainder.IsZero)
        {
            var comparison = (BigInteger.Abs(remainder) * 2).CompareTo(denominator);
            var roundAway = rounding switch
            {
                MidpointRounding.AwayFromZero => comparison >= 0,
                MidpointRounding.ToEven => comparison > 0 || (comparison == 0 && !quotient.IsEven),
                MidpointRounding.ToZero => false,
                MidpointRounding.ToNegativeInfinity => numerator.Sign < 0,
                MidpointRounding.ToPositiveInfinity => numerator.Sign > 0,
                _ => throw new ArgumentOutOfRangeException(nameof(rounding))
            };
            if (roundAway) quotient += numerator.Sign;
        }
        return new MediaTime(checked((long)quotient), target);
    }

    public int CompareTo(MediaTime other)
    {
        EnsureValid();
        other.EnsureValid();
        var left = (BigInteger)Ticks * Timebase.SecondsNumerator * other.Timebase.SecondsDenominator;
        var right = (BigInteger)other.Ticks * other.Timebase.SecondsNumerator * Timebase.SecondsDenominator;
        return left.CompareTo(right);
    }

    public static bool operator <(MediaTime left, MediaTime right) => left.CompareTo(right) < 0;
    public static bool operator >(MediaTime left, MediaTime right) => left.CompareTo(right) > 0;
    public static bool operator <=(MediaTime left, MediaTime right) => left.CompareTo(right) <= 0;
    public static bool operator >=(MediaTime left, MediaTime right) => left.CompareTo(right) >= 0;

    public static MediaTime operator +(MediaTime left, MediaTime right)
    {
        EnsureSameTimebase(left, right);
        return left with { Ticks = checked(left.Ticks + right.Ticks) };
    }

    public static MediaTime operator -(MediaTime left, MediaTime right)
    {
        EnsureSameTimebase(left, right);
        return left with { Ticks = checked(left.Ticks - right.Ticks) };
    }

    private void EnsureValid()
    {
        if (!IsValid) throw new InvalidOperationException("Media time has an uninitialized or invalid timebase.");
    }

    private static void EnsureSameTimebase(MediaTime left, MediaTime right)
    {
        left.EnsureValid();
        right.EnsureValid();
        if (left.Timebase != right.Timebase)
            throw new InvalidOperationException("Media times must use the same timebase before arithmetic.");
    }
}

/// <summary>A non-empty half-open interval [Start, Start + Duration) in one timebase.</summary>
public readonly record struct MediaTimeRange
{
    public MediaTime Start { get; }
    public MediaTime Duration { get; }
    public MediaTime End => Start + Duration;

    public MediaTimeRange(MediaTime start, MediaTime duration)
    {
        if (!start.IsValid || !duration.IsValid || start.Timebase != duration.Timebase)
            throw new ArgumentException("Range start and duration must use the same valid timebase.");
        if (duration.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(duration), "Media ranges must have positive duration.");
        Start = start;
        Duration = duration;
        _ = checked(start.Ticks + duration.Ticks);
    }

    public bool Contains(MediaTime position) => position >= Start && position < End;
}

public enum MediaTrackKind { Audio, Video, Subtitle }

/// <summary>A backend-neutral timeline clip projection. Source and timeline ranges are half-open.</summary>
public sealed record MediaTimelineClip(
    Guid ClipId,
    Guid TrackId,
    MediaAssetId AssetId,
    MediaTime SourceStart,
    MediaTime Duration,
    MediaTime TimelineStart)
{
    public MediaTimeRange SourceRange => new(SourceStart, Duration);
    public MediaTimeRange TimelineRange => new(TimelineStart, Duration);

    public void Validate()
    {
        if (ClipId == Guid.Empty || TrackId == Guid.Empty || AssetId.IsEmpty
            || !SourceStart.IsValid || !Duration.IsValid || !TimelineStart.IsValid
            || SourceStart.Timebase != Duration.Timebase || Duration.Timebase != TimelineStart.Timebase
            || SourceStart.Ticks < 0 || TimelineStart.Ticks < 0 || Duration.Ticks <= 0)
            throw new InvalidDataException("Media clip identity or time range is invalid.");
        _ = SourceRange;
        _ = TimelineRange;
    }
}

/// <summary>Deterministic non-destructive edits over immutable clip records.</summary>
public static class MediaTimelineEdits
{
    public static (MediaTimelineClip Left, MediaTimelineClip Right) Split(MediaTimelineClip clip, MediaTime timelinePosition, Guid? rightClipId = null)
    {
        clip.Validate();
        if (timelinePosition.Timebase != clip.TimelineStart.Timebase)
            throw new ArgumentException("Split position must use the clip timebase.", nameof(timelinePosition));
        var offset = timelinePosition.Ticks - clip.TimelineStart.Ticks;
        if (offset <= 0 || offset >= clip.Duration.Ticks)
            throw new ArgumentOutOfRangeException(nameof(timelinePosition), "Split must fall strictly inside the clip.");
        var newId = rightClipId ?? Guid.NewGuid();
        if (newId == Guid.Empty || newId == clip.ClipId)
            throw new ArgumentException("The right clip requires a distinct stable ID.", nameof(rightClipId));
        var left = clip with { Duration = clip.Duration with { Ticks = offset } };
        var right = clip with
            {
                ClipId = newId,
                SourceStart = clip.SourceStart with { Ticks = checked(clip.SourceStart.Ticks + offset) },
                Duration = clip.Duration with { Ticks = clip.Duration.Ticks - offset },
                TimelineStart = timelinePosition
            };
        left.Validate();
        right.Validate();
        return (left, right);
    }

    public static MediaTimelineClip TrimStart(MediaTimelineClip clip, MediaTime amount)
    {
        clip.Validate();
        EnsureCompatiblePositive(clip, amount);
        if (amount.Ticks >= clip.Duration.Ticks) throw new ArgumentOutOfRangeException(nameof(amount));
        var trimmed = clip with
        {
            SourceStart = clip.SourceStart with { Ticks = checked(clip.SourceStart.Ticks + amount.Ticks) },
            TimelineStart = clip.TimelineStart with { Ticks = checked(clip.TimelineStart.Ticks + amount.Ticks) },
            Duration = clip.Duration with { Ticks = clip.Duration.Ticks - amount.Ticks }
        };
        trimmed.Validate();
        return trimmed;
    }

    public static MediaTimelineClip TrimEnd(MediaTimelineClip clip, MediaTime amount)
    {
        clip.Validate();
        EnsureCompatiblePositive(clip, amount);
        if (amount.Ticks >= clip.Duration.Ticks) throw new ArgumentOutOfRangeException(nameof(amount));
        var trimmed = clip with { Duration = clip.Duration with { Ticks = clip.Duration.Ticks - amount.Ticks } };
        trimmed.Validate();
        return trimmed;
    }

    public static MediaTimelineClip Move(MediaTimelineClip clip, MediaTime newTimelineStart)
    {
        clip.Validate();
        if (!newTimelineStart.IsValid || newTimelineStart.Timebase != clip.TimelineStart.Timebase || newTimelineStart.Ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(newTimelineStart));
        var moved = clip with { TimelineStart = newTimelineStart };
        moved.Validate();
        return moved;
    }

    private static void EnsureCompatiblePositive(MediaTimelineClip clip, MediaTime amount)
    {
        if (!amount.IsValid || amount.Timebase != clip.Duration.Timebase || amount.Ticks <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Edit amount must be positive and use the clip timebase.");
    }
}

/// <summary>Stable Files identity plus an operation-scoped URI; the URI is never project identity.</summary>
public sealed record MediaAssetSource(
    MediaAssetId AssetId,
    Guid HostedItemId,
    Uri SourceUri,
    string? SourceRevisionId = null)
{
    public void Validate()
    {
        if (AssetId.IsEmpty || HostedItemId == Guid.Empty) throw new InvalidDataException("Media source requires stable asset and HostedItemId values.");
        if (SourceUri is null || !SourceUri.IsAbsoluteUri || SourceUri.Scheme is not ("file" or "https"))
            throw new InvalidDataException("Media source URI must be an absolute local file or HTTPS URI resolved by Files.");
        if (SourceRevisionId is { Length: > 256 }) throw new InvalidDataException("Media source revision identity is too long.");
    }
}

public enum MediaPlaybackState { Stopped, Paused, Playing, Ended, Failed }

public enum MediaEngineErrorCode
{
    BackendUnavailable,
    UnsupportedSource,
    SourceUnavailable,
    CodecUnsupported,
    PipelineRejected,
    PipelineFailed,
    OperationCancelled,
    InvalidTimeRange,
    RevisionConflict,
    ExportFailed,
    PermissionDenied,
}

public sealed record MediaEngineError(
    MediaEngineErrorCode Code,
    string Message,
    string Action,
    string? TargetId,
    bool IsRecoverable,
    bool CanRetry,
    TimeSpan? RetryAfter = null);

public sealed record MediaEngineResult<T>(T? Value, MediaEngineError? Error)
{
    public bool IsSuccess => Error is null;
    public static MediaEngineResult<T> Success(T value) => new(value, null);
    public static MediaEngineResult<T> Failure(MediaEngineError error) => new(default, error);
}

public sealed record MediaEngineCapabilities(
    bool GStreamerAvailable,
    bool GesAvailable,
    string? GStreamerVersion,
    bool AudioPlaybackAvailable,
    bool VideoPlaybackAvailable,
    bool TimelineRenderAvailable,
    IReadOnlyList<string> MissingComponents);

public interface IMediaPlaybackSession : IAsyncDisposable
{
    MediaPlaybackState State { get; }
    Task<MediaEngineResult<MediaPlaybackState>> SetStateAsync(MediaPlaybackState state, CancellationToken cancellationToken = default);
    Task<MediaEngineResult<MediaTime>> GetPositionAsync(CancellationToken cancellationToken = default);
    Task<MediaEngineResult<MediaTime>> SeekAsync(MediaTime position, CancellationToken cancellationToken = default);
}

/// <summary>Shared media boundary consumed by Wave, Motion and eligible 9to1 surfaces.</summary>
public interface IMediaEngine
{
    Task<MediaEngineCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<MediaEngineResult<IMediaPlaybackSession>> OpenPlaybackAsync(MediaAssetSource source, CancellationToken cancellationToken = default);
}
