using System.Text;

namespace Haven.Infrastructure.Media;

// Shared extraction of the maintained Wave WAV/sample primitive. A decoded path
// is data only; actual Files/media leases remain the caller's responsibility.
public sealed record PcmWaveformPreview(
    string SourcePath,
    double DurationSeconds,
    int SampleRate,
    int Channels,
    float[] Peaks,
    long DataOffset,
    long DataSize,
    ushort BlockAlign,
    uint ByteRate,
    ushort FormatTag = 1,
    ushort BitsPerSample = 16,
    byte[]? FormatPayload = null);


public static class PcmWaveformReader
{
    private const int PeakCount = 512;
    private const int FramesPerProbe = 1024;

    public static PcmWaveformPreview Decode(string path) => DecodeCore(path, buildPeaks: true);
    private static PcmWaveformPreview DecodeCore(string path, bool buildPeaks)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (ReadFourCc(reader) != "RIFF") throw new InvalidDataException("Missing RIFF header.");
        var riffSize = reader.ReadUInt32();
        if ((long)riffSize + 8 != stream.Length) throw new InvalidDataException("RIFF size does not match file length.");
        if (ReadFourCc(reader) != "WAVE") throw new InvalidDataException("Missing WAVE header.");

        ushort formatTag = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        uint byteRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        long dataOffset = -1;
        long dataSize = 0;
        var hasFormat = false;
        byte[]? formatPayload = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadFourCc(reader);
            var chunkSize = reader.ReadUInt32();
            var chunkStart = stream.Position;
            var chunkEnd = checked(chunkStart + chunkSize);
            if (chunkEnd > stream.Length) throw new InvalidDataException("WAV chunk exceeds file length.");

            if (chunkId == "fmt ")
            {
                if (hasFormat) throw new InvalidDataException("WAV contains multiple format chunks.");
                if (chunkSize is < 16 or > 128) throw new NotSupportedException("WAV format metadata exceeds supported bounds.");
                formatPayload = reader.ReadBytes(checked((int)chunkSize));
                stream.Position = chunkStart;
                formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                byteRate = reader.ReadUInt32();
                blockAlign = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
                if (formatTag == 0xfffe)
                {
                    if (chunkSize < 40) throw new InvalidDataException("Invalid extensible WAV format.");
                    var extensionSize = reader.ReadUInt16();
                    if (extensionSize < 22 || extensionSize + 18 > chunkSize) throw new InvalidDataException("Invalid extensible WAV extension size.");
                    var validBits = reader.ReadUInt16();
                    _ = reader.ReadUInt32(); // Channel order remains the source's canonical interleaved order.
                    var subtype = new Guid(reader.ReadBytes(16));
                    if (validBits == 0 || validBits > bitsPerSample) throw new InvalidDataException("Invalid WAV valid-bit count.");
                    formatTag = subtype == new Guid("00000001-0000-0010-8000-00aa00389b71") ? (ushort)1
                        : subtype == new Guid("00000003-0000-0010-8000-00aa00389b71") ? (ushort)3
                        : throw new NotSupportedException("Unsupported extensible WAV subtype.");
                }
                hasFormat = true;
            }
            else if (chunkId == "data")
            {
                if (dataOffset >= 0) throw new NotSupportedException("Multiple WAV data chunks require a different container decoder.");
                dataOffset = chunkStart;
                dataSize = chunkSize;
            }

            stream.Position = chunkEnd;
            if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
                stream.Position++;
        }

        if (!hasFormat || dataOffset < 0 || dataSize <= 0)
            throw new InvalidDataException("WAV format or data chunk is missing.");
        if (!(formatTag == 1 && bitsPerSample is 8 or 16 or 24 or 32)
            && !(formatTag == 3 && bitsPerSample is 32 or 64))
            throw new NotSupportedException("Unsupported WAV sample representation.");
        if (channels == 0 || sampleRate == 0 || sampleRate > (uint)int.MaxValue)
            throw new InvalidDataException("WAV format values are invalid.");

        var expectedBlockAlign = channels * (bitsPerSample / 8);
        if (blockAlign != expectedBlockAlign)
            throw new InvalidDataException("WAV block alignment is unsupported.");
        if (byteRate != checked((ulong)sampleRate * blockAlign))
            throw new InvalidDataException("WAV byte rate does not match its frame configuration.");
        if (dataSize % blockAlign != 0)
            throw new InvalidDataException("WAV data ends with an incomplete audio frame.");

        var totalFrames = dataSize / blockAlign;
        if (totalFrames <= 0) throw new InvalidDataException("WAV contains no audio frames.");

        var peaks = new float[PeakCount];
        for (var bucket = 0; buildPeaks && bucket < PeakCount; bucket++)
        {
            var bucketStart = Math.Min(totalFrames - 1, totalFrames * bucket / PeakCount);
            var bucketEnd = Math.Clamp(totalFrames * (bucket + 1) / PeakCount, bucketStart + 1, totalFrames);
            var probeFrames = Math.Min(FramesPerProbe, bucketEnd - bucketStart);
            var probeStart = bucketStart + Math.Max(0, (bucketEnd - bucketStart - probeFrames) / 2);
            stream.Position = checked(dataOffset + probeStart * blockAlign);

            var peak = 0f;
            for (long frame = 0; frame < probeFrames; frame++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var amplitude = (float)Math.Min(1, Math.Abs(ReadNormalizedSample(reader, formatTag, bitsPerSample)));
                    peak = Math.Max(peak, amplitude);
                }
            }

            peaks[bucket] = Math.Clamp(peak, 0f, 1f);
        }

        var durationSeconds = totalFrames / (double)sampleRate;
        return new PcmWaveformPreview(Path.GetFullPath(path), durationSeconds, (int)sampleRate, channels, peaks, dataOffset, dataSize, blockAlign, byteRate, formatTag, bitsPerSample, formatPayload);
    }

    /// <summary>Full min/max envelope of the requested original source range,
    /// independently per channel. Memory is bounded by channels × buckets;
    /// every selected frame is examined, so a transient is never skipped by a probe.
    /// Files/source identity and permissions are not established by analysis.</summary>
    public static PcmWaveformRange AnalyzeRange(string path, long sourceStartFrame, long frameCount,
        int requestedBuckets = 512, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceStartFrame < 0 || frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (requestedBuckets is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(requestedBuckets));
        var source = DecodeCore(path, buildPeaks: false);
        if (source.Channels > 32) throw new NotSupportedException("Waveform analysis supports at most 32 source channels.");
        var totalFrames = source.DataSize / source.BlockAlign;
        if (frameCount > totalFrames || sourceStartFrame > totalFrames - frameCount)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "The requested waveform range exceeds the original source.");
        var bucketCount = checked((int)Math.Min(frameCount, requestedBuckets));
        var channels = Enumerable.Range(0, source.Channels)
            .Select(_ => new PcmWaveformChannel(new float[bucketCount], new float[bucketCount])).ToArray();
        using var stream = File.OpenRead(source.SourcePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        stream.Position = checked(source.DataOffset + sourceStartFrame * source.BlockAlign);
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var start = checked(frameCount * bucket / bucketCount);
            var end = checked(frameCount * (bucket + 1) / bucketCount);
            for (var channel = 0; channel < channels.Length; channel++)
            { channels[channel].MinimumSamples[bucket] = float.PositiveInfinity; channels[channel].MaximumSamples[bucket] = float.NegativeInfinity; }
            for (var frame = start; frame < end; frame++)
            {
                if ((frame & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                for (var channel = 0; channel < channels.Length; channel++)
                {
                    var actual = (float)Math.Clamp(ReadNormalizedSample(reader, source.FormatTag, source.BitsPerSample), -1d, 1d);
                    channels[channel].MinimumSamples[bucket] = Math.Min(channels[channel].Minimum[bucket], actual);
                    channels[channel].MaximumSamples[bucket] = Math.Max(channels[channel].Maximum[bucket], actual);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(sourceStartFrame, frameCount, source.SampleRate, Array.AsReadOnly(channels));
    }

    public static double ReadNormalizedSample(BinaryReader reader, ushort formatTag, ushort bitsPerSample)
    {
        var sample = (formatTag, bitsPerSample) switch
        {
            (1, 8) => (reader.ReadByte() - 128) / 128d,
            (1, 16) => reader.ReadInt16() / 32768d,
            (1, 24) => ReadSigned24(reader) / 8388608d,
            (1, 32) => reader.ReadInt32() / 2147483648d,
            (3, 32) => reader.ReadSingle(),
            (3, 64) => reader.ReadDouble(),
            _ => throw new NotSupportedException("Unsupported WAV sample representation.")
        };
        if (!double.IsFinite(sample)) throw new InvalidDataException("WAV contains a non-finite sample.");
        return sample;
    }
    private static int ReadSigned24(BinaryReader reader)
    {
        var sample = reader.ReadByte() | reader.ReadByte() << 8 | reader.ReadByte() << 16;
        return (sample & 0x800000) != 0 ? sample | unchecked((int)0xff000000) : sample;
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length != 4) throw new EndOfStreamException();
        return Encoding.ASCII.GetString(bytes);
    }
}


public sealed class PcmWaveformChannel
{
    internal float[] MinimumSamples { get; }
    internal float[] MaximumSamples { get; }
    public IReadOnlyList<float> Minimum { get; }
    public IReadOnlyList<float> Maximum { get; }
    internal PcmWaveformChannel(float[] minimum, float[] maximum)
    { MinimumSamples = minimum; MaximumSamples = maximum; Minimum = Array.AsReadOnly(minimum); Maximum = Array.AsReadOnly(maximum); }
}
public sealed record PcmWaveformRange(long SourceStartFrame, long FrameCount, int SampleRate,
    IReadOnlyList<PcmWaveformChannel> Channels)
{
    public int BucketCount => Channels.Count == 0 ? 0 : Channels[0].Minimum.Count;
}
