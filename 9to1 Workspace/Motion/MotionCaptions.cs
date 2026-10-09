using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace HavenOS.Apps.Motion;

/// <summary>A plain-text caption on the sequence's integer frame clock; EndFrame is exclusive.</summary>
public sealed record MotionCaptionCue(Guid CueId, long StartFrame, long EndFrame, string Text);

public sealed record MotionCaptionTrack(Guid TrackId, string Name, string Language, IReadOnlyList<MotionCaptionCue> Cues)
{
    private readonly IReadOnlyList<MotionCaptionCue> _cues = Snapshot(Cues);

    // Neither the caller's source array nor a later with-expression can mutate this track's cues.
    public IReadOnlyList<MotionCaptionCue> Cues
    {
        get => _cues;
        init => _cues = Snapshot(value);
    }

    private static IReadOnlyList<MotionCaptionCue> Snapshot(IReadOnlyList<MotionCaptionCue> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Array.AsReadOnly(value.ToArray());
    }
}

/// <summary>
/// Plain-text SRT/WebVTT interchange and immutable cue editing. Subtitle timestamps use
/// milliseconds; conversion uses exact integer ratios, rounding to the nearest frame or
/// millisecond (ties upward), never floating-point seconds. Import creates native identities;
/// WebVTT also preserves GUID cue identifiers emitted by this exporter.
/// </summary>
public static class MotionCaptions
{
    private static readonly Regex Timing = new(@"^(\S+)\s+-->\s+(\S+)$", RegexOptions.CultureInvariant);
    private static readonly Regex SrtTimestamp = new(@"^([0-9]{2,}):([0-5][0-9]):([0-5][0-9]),([0-9]{3})$", RegexOptions.CultureInvariant);
    private static readonly Regex VttTimestamp = new(@"^(?:([0-9]{2,}):)?([0-5][0-9]):([0-5][0-9])\.([0-9]{3})$", RegexOptions.CultureInvariant);
    private static readonly Regex Markup = new(@"<[^>]*>", RegexOptions.CultureInvariant);

    public static MotionCaptionTrack ImportSrt(string content, string name, string language,
        int frameRateNumerator, int frameRateDenominator) =>
        Import(content, name, language, frameRateNumerator, frameRateDenominator, webVtt: false);

    public static MotionCaptionTrack ImportWebVtt(string content, string name, string language,
        int frameRateNumerator, int frameRateDenominator) =>
        Import(content, name, language, frameRateNumerator, frameRateDenominator, webVtt: true);

    public static string ExportSrt(MotionCaptionTrack track, int frameRateNumerator, int frameRateDenominator) =>
        Export(track, frameRateNumerator, frameRateDenominator, webVtt: false);

    public static string ExportWebVtt(MotionCaptionTrack track, int frameRateNumerator, int frameRateDenominator) =>
        Export(track, frameRateNumerator, frameRateDenominator, webVtt: true);

    public static void Validate(MotionCaptionTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.TrackId == Guid.Empty || string.IsNullOrWhiteSpace(track.Name)
            || string.IsNullOrWhiteSpace(track.Language))
            throw new InvalidDataException("Caption tracks require identity, name and language.");
        var ids = new HashSet<Guid>();
        foreach (var cue in track.Cues)
        {
            if (cue is null || cue.CueId == Guid.Empty || !ids.Add(cue.CueId)
                || cue.StartFrame < 0 || cue.EndFrame <= cue.StartFrame)
                throw new InvalidDataException("Caption cues require unique identity and a positive, non-negative frame range.");
            ValidateText(cue.Text);
        }
    }

    public static MotionCaptionTrack UpdateCue(MotionCaptionTrack track, Guid cueId,
        long startFrame, long endFrame, string text)
    {
        Validate(track);
        var cue = Find(track, cueId);
        return WithCues(track, track.Cues.Select(item => item.CueId == cueId
            ? cue with { StartFrame = startFrame, EndFrame = endFrame, Text = Normalize(text) } : item));
    }

    public static MotionCaptionTrack SplitCue(MotionCaptionTrack track, Guid cueId,
        long splitFrame, string leftText, string rightText)
    {
        Validate(track);
        var cue = Find(track, cueId);
        if (splitFrame <= cue.StartFrame || splitFrame >= cue.EndFrame)
            throw new ArgumentOutOfRangeException(nameof(splitFrame), "A caption split must be strictly inside the cue.");
        var left = cue with { EndFrame = splitFrame, Text = Normalize(leftText) };
        var right = new MotionCaptionCue(Guid.NewGuid(), splitFrame, cue.EndFrame, Normalize(rightText));
        return WithCues(track, track.Cues.Where(item => item.CueId != cueId).Append(left).Append(right));
    }

    /// <summary>Merge neighboring cues, retaining the earlier cue's identity and the complete time range.</summary>
    public static MotionCaptionTrack MergeCues(MotionCaptionTrack track, Guid firstCueId,
        Guid secondCueId, string? mergedText = null)
    {
        Validate(track);
        if (firstCueId == secondCueId)
            throw new ArgumentException("Merging requires two different caption cues.");
        Find(track, firstCueId);
        Find(track, secondCueId);
        var ordered = Ordered(track.Cues).ToArray();
        var firstIndex = Array.FindIndex(ordered, cue => cue.CueId == firstCueId);
        var secondIndex = Array.FindIndex(ordered, cue => cue.CueId == secondCueId);
        if (Math.Abs(firstIndex - secondIndex) != 1)
            throw new InvalidOperationException("Only neighboring caption cues can be merged.");
        var left = ordered[Math.Min(firstIndex, secondIndex)];
        var right = ordered[Math.Max(firstIndex, secondIndex)];
        var merged = left with
        {
            EndFrame = Math.Max(left.EndFrame, right.EndFrame),
            Text = Normalize(mergedText ?? left.Text + "\n" + right.Text)
        };
        return WithCues(track, track.Cues.Where(cue => cue.CueId != left.CueId && cue.CueId != right.CueId).Append(merged));
    }

    private static MotionCaptionTrack Import(string content, string name, string language,
        int numerator, int denominator, bool webVtt)
    {
        ValidateClock(numerator, denominator);
        var lines = Normalize(content).TrimStart('\uFEFF').Split('\n');
        var cursor = 0;
        if (webVtt)
        {
            if (lines.Length == 0 || !(lines[0] == "WEBVTT" || lines[0].StartsWith("WEBVTT ", StringComparison.Ordinal)
                || lines[0].StartsWith("WEBVTT\t", StringComparison.Ordinal)) || lines[0].Contains("-->", StringComparison.Ordinal))
                throw new InvalidDataException("A WebVTT document must begin with WEBVTT.");
            cursor = 1;
            if (cursor < lines.Length && !string.IsNullOrWhiteSpace(lines[cursor]))
                throw new InvalidDataException("WebVTT header metadata is not supported by plain-text caption import.");
        }

        var cues = new List<MotionCaptionCue>();
        while (cursor < lines.Length)
        {
            while (cursor < lines.Length && string.IsNullOrWhiteSpace(lines[cursor])) cursor++;
            if (cursor == lines.Length) break;
            var start = cursor;
            while (cursor < lines.Length && !string.IsNullOrWhiteSpace(lines[cursor])) cursor++;
            var block = lines[start..cursor];
            if (webVtt && (block[0] == "NOTE" || block[0].StartsWith("NOTE ", StringComparison.Ordinal)
                || block[0].StartsWith("NOTE\t", StringComparison.Ordinal))) continue;
            if (webVtt && (block[0] == "STYLE" || block[0] == "REGION"))
                throw new InvalidDataException("Styled or positioned WebVTT is not supported by plain-text caption import.");

            var timingIndex = 0;
            var cueId = Guid.NewGuid();
            if (!webVtt)
            {
                if (!BigInteger.TryParse(block[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) || ordinal <= 0)
                    throw new InvalidDataException("Each SRT cue must begin with a positive numeric index.");
                timingIndex = 1;
            }
            else if (!block[0].Contains("-->", StringComparison.Ordinal))
            {
                timingIndex = 1;
                if (Guid.TryParse(block[0], out var importedId) && importedId != Guid.Empty) cueId = importedId;
            }
            if (block.Length < timingIndex + 2)
                throw new InvalidDataException("Caption cues require a timing line and non-empty text.");
            var match = Timing.Match(block[timingIndex]);
            if (!match.Success)
                throw new InvalidDataException("Invalid caption timing, or unsupported cue positioning settings.");
            var startMs = ParseTimestamp(match.Groups[1].Value, webVtt);
            var endMs = ParseTimestamp(match.Groups[2].Value, webVtt);
            if (endMs <= startMs) throw new InvalidDataException("Caption end time must follow start time.");
            var text = string.Join('\n', block[(timingIndex + 1)..]);
            if (Markup.IsMatch(text))
                throw new InvalidDataException("Caption markup is not supported; encode literal angle brackets as entities.");
            cues.Add(new MotionCaptionCue(cueId, ToFrame(startMs, numerator, denominator),
                ToFrame(endMs, numerator, denominator), WebUtility.HtmlDecode(text)));
        }
        return WithCues(new MotionCaptionTrack(Guid.NewGuid(), name, language, []), cues);
    }

    private static string Export(MotionCaptionTrack track, int numerator, int denominator, bool webVtt)
    {
        Validate(track);
        ValidateClock(numerator, denominator);
        var result = new StringBuilder(webVtt ? "WEBVTT\n\n" : "");
        var ordinal = 0;
        foreach (var cue in Ordered(track.Cues))
        {
            var text = Normalize(cue.Text);
            if (text.Split('\n').Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Subtitle cue text cannot contain blank separator lines.");
            var startMs = ToMilliseconds(cue.StartFrame, numerator, denominator);
            var endMs = ToMilliseconds(cue.EndFrame, numerator, denominator);
            // Millisecond formats cannot represent every frame at unusually high frame rates.
            if (endMs <= startMs || ToFrame(startMs, numerator, denominator) != cue.StartFrame
                || ToFrame(endMs, numerator, denominator) != cue.EndFrame)
                throw new InvalidDataException("Subtitle millisecond precision cannot preserve these frame boundaries.");
            result.Append(webVtt ? cue.CueId.ToString("D") : (++ordinal).ToString(CultureInfo.InvariantCulture)).Append('\n');
            result.Append(FormatTimestamp(startMs, webVtt)).Append(" --> ").Append(FormatTimestamp(endMs, webVtt)).Append('\n');
            result.Append(WebUtility.HtmlEncode(text)).Append("\n\n");
        }
        return result.ToString();
    }

    private static MotionCaptionTrack WithCues(MotionCaptionTrack track, IEnumerable<MotionCaptionCue> cues)
    {
        // Validate before sorting so malformed null cues receive the same deliberate validation error.
        var candidate = track with { Cues = cues.ToArray() };
        Validate(candidate);
        return candidate with { Cues = Ordered(candidate.Cues).ToArray() };
    }

    // LINQ's stable sort preserves source/list order for coincident cues, including SRT
    // round-trips whose format cannot carry the native GUIDs. This also preserves stacking order.
    private static IOrderedEnumerable<MotionCaptionCue> Ordered(IEnumerable<MotionCaptionCue> cues) =>
        cues.OrderBy(cue => cue.StartFrame).ThenBy(cue => cue.EndFrame);

    private static MotionCaptionCue Find(MotionCaptionTrack track, Guid cueId) =>
        track.Cues.SingleOrDefault(cue => cue.CueId == cueId) ?? throw new KeyNotFoundException("CaptionCueNotFound");

    private static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
            throw new InvalidDataException("Caption text must be non-empty plain text without control characters.");
    }

    private static void ValidateClock(int numerator, int denominator)
    {
        if (numerator <= 0 || denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(numerator), "The sequence frame rate must be a positive rational number.");
    }

    private static BigInteger ParseTimestamp(string timestamp, bool webVtt)
    {
        var match = (webVtt ? VttTimestamp : SrtTimestamp).Match(timestamp);
        if (!match.Success) throw new InvalidDataException("Invalid subtitle timestamp.");
        var hours = match.Groups[1].Success ? BigInteger.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : BigInteger.Zero;
        return ((hours * 60 + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)) * 60
            + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)) * 1000
            + int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
    }

    private static long ToFrame(BigInteger milliseconds, int numerator, int denominator)
    {
        var divisor = (BigInteger)denominator * 1000;
        var frame = (milliseconds * numerator + divisor / 2) / divisor;
        if (frame > long.MaxValue) throw new InvalidDataException("Subtitle timestamp exceeds the sequence frame range.");
        return (long)frame;
    }

    private static BigInteger ToMilliseconds(long frame, int numerator, int denominator) =>
        ((BigInteger)frame * denominator * 1000 + numerator / 2) / numerator;

    private static string FormatTimestamp(BigInteger milliseconds, bool webVtt) =>
        (milliseconds / 3600000).ToString("00", CultureInfo.InvariantCulture) + ":"
        + (milliseconds / 60000 % 60).ToString("00", CultureInfo.InvariantCulture) + ":"
        + (milliseconds / 1000 % 60).ToString("00", CultureInfo.InvariantCulture) + (webVtt ? "." : ",")
        + (milliseconds % 1000).ToString("000", CultureInfo.InvariantCulture);
}
