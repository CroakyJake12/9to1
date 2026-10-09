using System.Text.Json;

namespace HavenOS.Apps.Motion;

internal static class MotionCaptionWorkflowTest
{
    public static int Run()
    {
        const string source = "\uFEFF1\r\n00:00:01,001 --> 00:00:02,002\r\nHello &amp; welcome\r\nDeuxième ligne &lt;literal&gt;\r\n\r\n"
            + "2\r\n00:00:03,003 --> 00:00:04,004\r\nSecond cue\r\n";
        var track = MotionCaptions.ImportSrt(source, "English", "en-GB", 30000, 1001);
        if (track.Cues.Count != 2 || track.Cues[0].StartFrame != 30 || track.Cues[0].EndFrame != 60
            || track.Cues[1].StartFrame != 90 || track.Cues[1].EndFrame != 120
            || track.Cues[0].Text != "Hello & welcome\nDeuxième ligne <literal>") return 90;

        var srt = MotionCaptions.ExportSrt(track, 30000, 1001);
        var fromSrt = MotionCaptions.ImportSrt(srt, track.Name, track.Language, 30000, 1001);
        if (!SameContent(track, fromSrt) || !srt.Contains("00:00:01,001", StringComparison.Ordinal)) return 91;
        var vtt = MotionCaptions.ExportWebVtt(track, 30000, 1001);
        var fromVtt = MotionCaptions.ImportWebVtt(vtt, track.Name, track.Language, 30000, 1001);
        if (!SameContent(track, fromVtt) || !track.Cues.Select(cue => cue.CueId).SequenceEqual(fromVtt.Cues.Select(cue => cue.CueId))) return 92;

        var firstId = track.Cues[0].CueId;
        var corrected = MotionCaptions.UpdateCue(track, firstId, 31, 61, "Corrected\r\ncaption");
        if (corrected.TrackId != track.TrackId || corrected.Cues[0].CueId != firstId
            || corrected.Cues[0].Text != "Corrected\ncaption" || corrected.Cues[0].StartFrame != 31
            || track.Cues[0].StartFrame != 30 || track.Cues[0].Text == corrected.Cues[0].Text) return 93;
        var split = MotionCaptions.SplitCue(corrected, firstId, 45, "Corrected", "caption");
        if (split.Cues.Count != 3 || split.TrackId != track.TrackId || split.Cues[0].CueId != firstId
            || split.Cues[0].EndFrame != 45 || split.Cues[1].StartFrame != 45 || split.Cues[1].EndFrame != 61
            || split.Cues[1].CueId == firstId || split.Cues[2] != track.Cues[1]) return 94;
        var merged = MotionCaptions.MergeCues(split, split.Cues[1].CueId, firstId);
        if (merged.Cues.Count != 2 || merged.Cues[0] != corrected.Cues[0] || merged.Cues[1] != track.Cues[1]) return 95;

        // External arrays, with-expressions and JSON round-trips cannot expose mutable cue storage.
        var sourceArray = track.Cues.ToArray();
        var frozen = new MotionCaptionTrack(Guid.NewGuid(), "Frozen", "fr", sourceArray);
        var withFrozen = frozen with { Cues = sourceArray };
        sourceArray[0] = sourceArray[0] with { Text = "Changed outside the track" };
        var reopened = JsonSerializer.Deserialize<MotionCaptionTrack>(JsonSerializer.Serialize(withFrozen));
        if (frozen.Cues[0].Text != track.Cues[0].Text || withFrozen.Cues[0].Text != track.Cues[0].Text
            || reopened is null || !SameContent(withFrozen, reopened)
            || !Rejects(() => ((IList<MotionCaptionCue>)frozen.Cues)[0] = sourceArray[0])) return 96;

        // Beyond double's integer precision, exact rational conversion still preserves the frame.
        const long distantStart = 9007199254740993;
        var distant = new MotionCaptionTrack(Guid.NewGuid(), "Distant", "en",
            [new MotionCaptionCue(Guid.NewGuid(), distantStart, distantStart + 24, "Exact frame clock")]);
        if (!SameContent(distant, MotionCaptions.ImportSrt(MotionCaptions.ExportSrt(distant, 24, 1), "Distant", "en", 24, 1))) return 97;
        var overlapping = new MotionCaptionTrack(Guid.NewGuid(), "Speakers", "en",
            [new MotionCaptionCue(Guid.NewGuid(), 30, 60, "Second speaker"),
             new MotionCaptionCue(Guid.NewGuid(), 30, 60, "First speaker")]);
        if (!SameContent(overlapping, MotionCaptions.ImportSrt(MotionCaptions.ExportSrt(overlapping, 30, 1), "Speakers", "en", 30, 1))) return 97;
        var shortVtt = MotionCaptions.ImportWebVtt("WEBVTT\n\nNOTE explanation\nignored\n\ncustom-id\n00:01.000 --> 00:02.000\nShort timestamps\n", "Short", "en", 25, 1);
        if (shortVtt.Cues.Count != 1 || shortVtt.Cues[0].StartFrame != 25 || shortVtt.Cues[0].EndFrame != 50) return 98;

        foreach (var invalid in new[]
        {
            "1\n00:60:00,000 --> 01:01:00,000\nBad minutes\n",
            "1\n00:00:02,000 --> 00:00:01,000\nReversed\n",
            "1\n00:00:00,000 --> 00:00:00,001\nSub-frame\n",
            "1\n00:00:01,000 --> 00:00:02,000\n<b>Unsupported markup</b>\n",
            "1\n00:00:01,000 --> 00:00:02,000\n\n",
            "1\n00:00:01,000 --> 00:00:02,000\nBad\u0000control\n",
            "1\n999999999999999999999:00:00,000 --> 999999999999999999999:00:01,000\nOverflow\n"
        })
            if (!Rejects(() => MotionCaptions.ImportSrt(invalid, "Invalid", "en", 30, 1))) return 99;
        if (!Rejects(() => MotionCaptions.ImportWebVtt("WEBVTT\n\n00:01.000 --> 00:02.000 position:50%\nPositioned\n", "Invalid", "en", 30, 1))
            || !Rejects(() => MotionCaptions.ImportWebVtt("WEBVTT\n\nSTYLE\n::cue { color: red; }\n", "Invalid", "en", 30, 1))
            || !Rejects(() => MotionCaptions.SplitCue(track, firstId, 30, "Invalid", "Boundary"))
            || !Rejects(() => MotionCaptions.MergeCues(split, split.Cues[0].CueId, split.Cues[2].CueId))
            || !Rejects(() => MotionCaptions.UpdateCue(track, firstId, -1, 2, "Negative"))
            || !Rejects(() => MotionCaptions.UpdateCue(track, Guid.NewGuid(), 0, 2, "Missing"))
            || !Rejects(() => MotionCaptions.ExportSrt(track, 0, 1))
            || !Rejects(() => MotionCaptions.ExportSrt(new MotionCaptionTrack(Guid.NewGuid(), "Too fast", "en",
                [new MotionCaptionCue(Guid.NewGuid(), 1, 2, "Below millisecond precision")]), 3000, 1))
            || !Rejects(() => MotionCaptions.Validate(track with { Cues = [track.Cues[0], track.Cues[0]] }))) return 100;
        return 0;
    }

    private static bool SameContent(MotionCaptionTrack first, MotionCaptionTrack second) =>
        first.Cues.Select(cue => (cue.StartFrame, cue.EndFrame, cue.Text))
            .SequenceEqual(second.Cues.Select(cue => (cue.StartFrame, cue.EndFrame, cue.Text)));

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException
            or InvalidOperationException or KeyNotFoundException or NotSupportedException) { return true; }
    }
}
