using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Haven.Core.Media;

namespace HavenOS.Apps.Wave;

public sealed record WaveProject(
    int SchemaVersion,
    Guid ProjectId,
    int SampleRate,
    int Channels,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    List<WaveTrack> Tracks);

public sealed record WaveTrack(Guid TrackId, string Name, List<WaveClip> Clips,
    double Gain = 1, double Pan = 0, bool Mute = false, bool Solo = false, bool RecordArm = false);

public sealed record WaveClip(
    Guid ClipId,
    Guid SourceReferenceId,
    string SourcePath,
    string SourceSha256,
    long SourceStartFrame,
    long FrameCount,
    long TimelineStartFrame,
    double Gain = 1,
    long FadeInFrames = 0,
    long FadeOutFrames = 0,
    string? SourceFileID = null,
    string? SourceRevisionID = null,
    WaveAudioDerivation? AudioDerivation = null);

/// <summary>Reproducibility evidence for ephemeral audio decoding; original Files bytes remain canonical.</summary>
public sealed record WaveAudioDerivation(string DecodedSha256, string DecodeProfile, MediaAudioDecoderEvidence Runtime);

public static class WaveProjectStore
{
    private const int CurrentSchemaVersion = 5;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static WaveProject Create(string? name, int sampleRate = 48000, int channels = 2)
    {
        if (sampleRate <= 0 || channels is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Project audio configuration is invalid.");
        var now = DateTimeOffset.UtcNow;
        return new WaveProject(CurrentSchemaVersion, Guid.NewGuid(), sampleRate, channels, 0, now, now,
            [new WaveTrack(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? "Audio 1" : name.Trim(), [])]);
    }

    public static WaveProject AddWavClip(WaveProject project, Guid trackId, string sourcePath, double timelineStartSeconds)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!double.IsFinite(timelineStartSeconds) || timelineStartSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(timelineStartSeconds), "Timeline start must be a finite non-negative time.");
        var track = project.Tracks.SingleOrDefault(candidate => candidate.TrackId == trackId)
            ?? throw new InvalidDataException("TrackNotFound: the target track does not exist.");
        var preview = PcmWaveformReader.Decode(Path.GetFullPath(sourcePath));
        if (preview.SampleRate != project.SampleRate || preview.Channels != project.Channels)
            throw new NotSupportedException($"CodecUnsupported: source is {preview.SampleRate} Hz/{preview.Channels} channel(s); this project requires {project.SampleRate} Hz/{project.Channels} channel(s).");
        string sourceSha256;
        using (var source = File.OpenRead(preview.SourcePath))
            sourceSha256 = Convert.ToHexString(SHA256.HashData(source));
        var startFrame = checked((long)Math.Round(timelineStartSeconds * project.SampleRate, MidpointRounding.AwayFromZero));
        var frames = preview.DataSize / preview.BlockAlign;
        var clip = new WaveClip(Guid.NewGuid(), Guid.NewGuid(), preview.SourcePath, sourceSha256, 0, frames, startFrame);
        var tracks = project.Tracks.Select(item => item.TrackId == trackId
            ? item with { Clips = [.. item.Clips, clip] }
            : item).ToList();
        var now = DateTimeOffset.UtcNow;
        return project with { Tracks = tracks, Revision = checked(project.Revision + 1), ModifiedAt = now };
    }

    public static void Save(string path, WaveProject project, long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        Validate(project);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("Project destination has no parent directory.");
        Directory.CreateDirectory(directory);
        using var commitLock = new FileStream(fullPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (expectedRevision is { } expected)
        {
            if (File.Exists(fullPath))
            {
                var persisted = Open(fullPath);
                if (expected < 0 || persisted.ProjectId != project.ProjectId || persisted.Revision != expected)
                    throw new InvalidOperationException("RevisionConflict");
            }
            else if (expected >= 0) throw new FileNotFoundException("ProjectNotFound", fullPath);
        }
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, project, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static WaveProject Open(string path)
    {
        try
        {
            using var stream = File.OpenRead(Path.GetFullPath(path));
            var project = JsonSerializer.Deserialize<WaveProject>(stream, JsonOptions)
                ?? throw new InvalidDataException("Project file is empty or invalid.");
            // v2 stored clip ranges without mixer/fade fields. Constructor defaults preserve
            // its exact audible output while migration adds editable structured processing.
            if (project.SchemaVersion is 2 or 3 or 4) project = project with { SchemaVersion = CurrentSchemaVersion };
            Validate(project);
            return project;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Wave project JSON is malformed.", exception);
        }
    }

    public static void Validate(WaveProject project)
    {
        if (project.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported Wave project schema version {project.SchemaVersion}.");
        if (project.ProjectId == Guid.Empty || project.SampleRate <= 0 || project.Channels is < 1 or > 32 || project.Revision < 0
            || project.Tracks is null || project.Tracks.Any(track => track.TrackId == Guid.Empty || string.IsNullOrWhiteSpace(track.Name) || track.Clips is null
                || !double.IsFinite(track.Gain) || track.Gain is < 0 or > 64 || !double.IsFinite(track.Pan) || track.Pan is < -1 or > 1))
            throw new InvalidDataException("Wave project contains invalid required data.");
        if (project.Tracks.Select(track => track.TrackId).Distinct().Count() != project.Tracks.Count)
            throw new InvalidDataException("Wave project contains duplicate track identities.");
        var clips = project.Tracks.SelectMany(track => track.Clips).ToList();
        if (clips.Any(clip => clip.ClipId == Guid.Empty || clip.SourceReferenceId == Guid.Empty || (string.IsNullOrWhiteSpace(clip.SourcePath) && string.IsNullOrWhiteSpace(clip.SourceFileID))
            || (clip.SourceFileID is not null && (string.IsNullOrWhiteSpace(clip.SourceFileID) || string.IsNullOrWhiteSpace(clip.SourceRevisionID)))
            || (clip.SourceFileID is null && clip.SourceRevisionID is not null)
            || clip.SourceSha256 is null || clip.SourceSha256.Length != 64 || !clip.SourceSha256.All(Uri.IsHexDigit)
            || clip.SourceStartFrame < 0 || clip.FrameCount <= 0 || clip.TimelineStartFrame < 0
            || !double.IsFinite(clip.Gain) || clip.Gain is < 0 or > 64
            || clip.FadeInFrames < 0 || clip.FadeOutFrames < 0 || clip.FadeInFrames > clip.FrameCount || clip.FadeOutFrames > clip.FrameCount))
            throw new InvalidDataException("Wave project contains an invalid clip.");
        if (clips.Select(clip => clip.ClipId).Distinct().Count() != clips.Count)
            throw new InvalidDataException("Wave project contains duplicate clip identities.");
        foreach (var clip in clips.Where(clip => clip.AudioDerivation is not null))
        {
            var derivation = clip.AudioDerivation!;
            if (clip.SourceFileID is null || derivation.DecodedSha256 is null || derivation.DecodedSha256.Length != 64
                || !derivation.DecodedSha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(derivation.DecodeProfile)
                || derivation.Runtime is null || string.IsNullOrWhiteSpace(derivation.Runtime.Engine)
                || string.IsNullOrWhiteSpace(derivation.Runtime.ObservedVersion)
                || derivation.Runtime.ExecutableSha256 is null || derivation.Runtime.ExecutableSha256.Length != 64
                || !derivation.Runtime.ExecutableSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Wave project contains invalid decoding evidence.");
        }
    }
}

public static class WaveProjectExporter
{
    private const int FramesPerBlock = 4096;

    public static long ExportPcm16(WaveProject project, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        var anySolo = project.Tracks.Any(track => track.Solo && !track.Mute);
        var timelineClips = project.Tracks.SelectMany(track => track.Clips).ToList();
        var clips = project.Tracks.Where(track => !track.Mute && (!anySolo || track.Solo)).SelectMany(track => track.Clips).ToList();
        if (timelineClips.Count == 0)
            throw new InvalidDataException("The project has no clips to export.");

        var sources = new List<(WaveClip Clip, WaveformPreview Preview)>();
        foreach (var clip in clips)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(clip.SourcePath))
                throw new FileNotFoundException($"SourceUnavailable: source for clip {clip.ClipId} is missing at its recorded path; relink is not available in this standalone slice.", clip.SourcePath);
            using var source = File.OpenRead(clip.SourcePath);
            var hash = Convert.ToHexString(SHA256.HashData(source));
            if (!string.Equals(hash, clip.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SourceChanged: source content for clip {clip.ClipId} no longer matches the imported revision.");
            var preview = PcmWaveformReader.Decode(clip.SourcePath);
            if (preview.SampleRate != project.SampleRate || preview.Channels != project.Channels)
                throw new InvalidDataException($"SourceChanged: source format for clip {clip.ClipId} no longer matches the project configuration.");
            var sourceFrameCount = preview.DataSize / preview.BlockAlign;
            if (checked(clip.SourceStartFrame + clip.FrameCount) > sourceFrameCount)
                throw new InvalidDataException($"SourceChanged: clip {clip.ClipId} source range exceeds the current audio.");
            sources.Add((clip, preview));
        }

        var outputFrames = timelineClips.Max(clip => checked(clip.TimelineStartFrame + clip.FrameCount));
        var blockAlign = checked((ushort)(project.Channels * sizeof(short)));
        var dataBytes = checked((ulong)outputFrames * blockAlign);
        if (dataBytes == 0 || dataBytes > uint.MaxValue - 36)
            throw new InvalidDataException("The project is empty or exceeds the standard RIFF/WAV size limit.");
        var fullOutput = Path.GetFullPath(outputPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (sources.Any(item => string.Equals(Path.GetFullPath(item.Clip.SourcePath), fullOutput, comparison)))
            throw new InvalidOperationException("Wave will not export over a source file.");

        var created = false;
        try
        {
            using var output = new FileStream(fullOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36u + (uint)dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16u);
            writer.Write((ushort)1);
            writer.Write((ushort)project.Channels);
            writer.Write((uint)project.SampleRate);
            writer.Write(checked((uint)project.SampleRate * blockAlign));
            writer.Write(blockAlign);
            writer.Write((ushort)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write((uint)dataBytes);

            var mixed = new double[FramesPerBlock * project.Channels];
            var encoded = new byte[mixed.Length * sizeof(short)];
            for (long blockStart = 0; blockStart < outputFrames; blockStart += FramesPerBlock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var blockFrames = (int)Math.Min(FramesPerBlock, outputFrames - blockStart);
                Array.Clear(mixed);
                foreach (var item in sources)
                {
                    var clip = item.Clip;
                    var track = project.Tracks.Single(track => track.Clips.Any(candidate => candidate.ClipId == clip.ClipId));
                    var overlapStart = Math.Max(blockStart, clip.TimelineStartFrame);
                    var overlapEnd = Math.Min(blockStart + blockFrames, checked(clip.TimelineStartFrame + clip.FrameCount));
                    if (overlapStart >= overlapEnd) continue;
                    using var source = File.OpenRead(clip.SourcePath);
                    source.Position = checked(item.Preview.DataOffset + (clip.SourceStartFrame + overlapStart - clip.TimelineStartFrame) * item.Preview.BlockAlign);
                    using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
                    for (var frame = overlapStart; frame < overlapEnd; frame++)
                    {
                        var destinationFrame = checked((int)(frame - blockStart));
                        var clipOffset = frame - clip.TimelineStartFrame;
                        var fadeIn = clip.FadeInFrames == 0 ? 1 : Math.Min(1d, clipOffset / (double)clip.FadeInFrames);
                        var fadeOut = clip.FadeOutFrames == 0 ? 1 : Math.Min(1d, (clip.FrameCount - 1 - clipOffset) / (double)clip.FadeOutFrames);
                        var gain = track.Gain * clip.Gain * fadeIn * fadeOut;
                        for (var channel = 0; channel < project.Channels; channel++)
                        {
                            var channelGain = project.Channels == 2 ? channel == 0 ? Math.Min(1, 1 - track.Pan) : Math.Min(1, 1 + track.Pan) : 1;
                            var destination = destinationFrame * project.Channels + channel;
                            mixed[destination] += PcmWaveformReader.ReadNormalizedSample(reader, item.Preview.FormatTag, item.Preview.BitsPerSample) * 32768d * gain * channelGain;
                            if (!double.IsFinite(mixed[destination]))
                                throw new InvalidDataException("Audio processing exceeded finite numeric range.");
                        }
                    }
                }

                for (var sampleIndex = 0; sampleIndex < blockFrames * project.Channels; sampleIndex++)
                {
                    var sample = (short)Math.Clamp(Math.Round(mixed[sampleIndex]), short.MinValue, short.MaxValue);
                    encoded[sampleIndex * 2] = (byte)sample;
                    encoded[sampleIndex * 2 + 1] = (byte)(sample >> 8);
                }
                output.Write(encoded, 0, blockFrames * project.Channels * sizeof(short));
            }

            output.Flush(flushToDisk: true);
            foreach (var item in sources)
            {
                using var source = File.OpenRead(item.Clip.SourcePath);
                var currentHash = Convert.ToHexString(SHA256.HashData(source));
                if (!string.Equals(currentHash, item.Clip.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SourceChanged: source content for clip {item.Clip.ClipId} changed during export.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return outputFrames;
        }
        catch
        {
            if (created)
            {
                try { File.Delete(fullOutput); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }
}
