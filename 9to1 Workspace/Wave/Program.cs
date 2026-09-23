using System.Globalization;
using System.Text;

namespace HavenOS.Apps.Wave;

internal sealed record WaveformPreview(
    string SourcePath,
    double DurationSeconds,
    int SampleRate,
    int Channels,
    float[] Peaks,
    long DataOffset,
    long DataSize,
    ushort BlockAlign,
    uint ByteRate);

internal sealed record WaveSurfaceState(bool IsLoaded, string Message, WaveformPreview? Preview)
{
    public static WaveSurfaceState Failed(string message) => new(false, message, null);
    public static WaveSurfaceState Loaded(WaveformPreview preview) => new(true, "Audio loaded.", preview);
}

internal static class WaveSurface
{
    public static WaveSurfaceState Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return WaveSurfaceState.Failed("Choose a local PCM WAV file.");

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return WaveSurfaceState.Failed("The selected audio file does not exist.");

            return WaveSurfaceState.Loaded(PcmWaveformReader.Decode(fullPath));
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or OverflowException)
        {
            return WaveSurfaceState.Failed("Wave could not decode this file. Only valid 16-bit PCM WAV audio is supported by this first standalone slice.");
        }
    }
}

internal static class PcmWaveformReader
{
    private const int PeakCount = 512;
    private const int FramesPerProbe = 1024;

    public static WaveformPreview Decode(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (ReadFourCc(reader) != "RIFF") throw new InvalidDataException("Missing RIFF header.");
        _ = reader.ReadUInt32();
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

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadFourCc(reader);
            var chunkSize = reader.ReadUInt32();
            var chunkStart = stream.Position;
            var chunkEnd = checked(chunkStart + chunkSize);
            if (chunkEnd > stream.Length) throw new InvalidDataException("WAV chunk exceeds file length.");

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16) throw new InvalidDataException("WAV fmt chunk is too small.");
                formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                byteRate = reader.ReadUInt32();
                blockAlign = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
                hasFormat = true;
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkStart;
                dataSize = chunkSize;
            }

            stream.Position = chunkEnd;
            if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
                stream.Position++;
        }

        if (!hasFormat || dataOffset < 0 || dataSize <= 0)
            throw new InvalidDataException("WAV format or data chunk is missing.");
        if (formatTag != 1 || bitsPerSample != 16)
            throw new NotSupportedException("Only 16-bit PCM WAV audio is supported.");
        if (channels == 0 || sampleRate == 0 || sampleRate > (uint)int.MaxValue)
            throw new InvalidDataException("WAV format values are invalid.");

        var expectedBlockAlign = channels * sizeof(short);
        if (blockAlign != expectedBlockAlign)
            throw new InvalidDataException("WAV block alignment is unsupported.");
        if (dataSize % blockAlign != 0)
            throw new InvalidDataException("WAV data ends with an incomplete audio frame.");

        var totalFrames = dataSize / blockAlign;
        if (totalFrames <= 0) throw new InvalidDataException("WAV contains no audio frames.");

        var peaks = new float[PeakCount];
        for (var bucket = 0; bucket < PeakCount; bucket++)
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
                    var sample = reader.ReadInt16();
                    var amplitude = Math.Abs((int)sample) / 32768f;
                    peak = Math.Max(peak, amplitude);
                }
            }

            peaks[bucket] = Math.Clamp(peak, 0f, 1f);
        }

        var durationSeconds = totalFrames / (double)sampleRate;
        return new WaveformPreview(Path.GetFullPath(path), durationSeconds, (int)sampleRate, channels, peaks, dataOffset, dataSize, blockAlign, byteRate);
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length != 4) throw new EndOfStreamException();
        return Encoding.ASCII.GetString(bytes);
    }
}

internal static class WaveConsoleSurface
{
    private const string Levels = " ▁▂▃▄▅▆▇█";

    public static string Render(WaveformPreview preview, int columns = 64)
    {
        columns = Math.Clamp(columns, 8, 128);
        var builder = new StringBuilder(columns);
        for (var column = 0; column < columns; column++)
        {
            var sourceIndex = columns == 1
                ? 0
                : (int)Math.Round(column * (preview.Peaks.Length - 1d) / (columns - 1d));
            var peak = Math.Clamp(preview.Peaks[sourceIndex], 0f, 1f);
            var level = Math.Clamp((int)Math.Round(peak * (Levels.Length - 1)), 0, Levels.Length - 1);
            builder.Append(Levels[level]);
        }

        return builder.ToString();
    }
}

internal sealed record WaveTrimResult(bool Succeeded, string Message, string? OutputPath = null)
{
    public static WaveTrimResult Failed(string message) => new(false, message);
    public static WaveTrimResult Saved(string path, double durationSeconds) => new(true, $"Trimmed audio saved ({durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s).", path);
}

internal static class PcmWaveTrimmer
{
    private const int CopyBufferSize = 64 * 1024;

    public static WaveTrimResult Trim(string? inputPath, double startSeconds, double endSeconds, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
            return WaveTrimResult.Failed("Choose an input WAV file and a new output path.");
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds))
            return WaveTrimResult.Failed("Trim times must be finite numbers of seconds.");

        var loaded = WaveSurface.Load(inputPath);
        if (!loaded.IsLoaded || loaded.Preview is null)
            return WaveTrimResult.Failed(loaded.Message);

        var preview = loaded.Preview;
        string fullInput;
        string fullOutput;
        try
        {
            fullInput = Path.GetFullPath(inputPath);
            fullOutput = Path.GetFullPath(outputPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return WaveTrimResult.Failed("Choose valid local input and output paths.");
        }

        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(fullInput, fullOutput, pathComparison))
            return WaveTrimResult.Failed("Wave never trims over the source file. Choose a different output path.");
        if (startSeconds < 0 || endSeconds <= startSeconds || endSeconds > preview.DurationSeconds)
            return WaveTrimResult.Failed($"Trim range must satisfy 0 <= start < end <= {preview.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds.");

        var frameCount = preview.DataSize / preview.BlockAlign;
        var startFrame = Math.Min(frameCount, (long)Math.Ceiling(startSeconds * preview.SampleRate));
        var endFrame = Math.Min(frameCount, (long)Math.Ceiling(endSeconds * preview.SampleRate));
        if (endFrame <= startFrame)
            return WaveTrimResult.Failed("The trim range does not contain a complete audio frame.");

        var outputDataSize = checked((uint)((endFrame - startFrame) * preview.BlockAlign));
        if (outputDataSize > uint.MaxValue - 36)
            return WaveTrimResult.Failed("The selected audio is too large for a standard RIFF/WAV output file.");

        var outputCreated = false;
        try
        {
            using var input = File.OpenRead(fullInput);
            input.Position = checked(preview.DataOffset + startFrame * preview.BlockAlign);
            using var output = new FileStream(fullOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            outputCreated = true;
            using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36u + outputDataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16u);
                writer.Write((ushort)1);
                writer.Write((ushort)preview.Channels);
                writer.Write((uint)preview.SampleRate);
                writer.Write(preview.ByteRate);
                writer.Write(preview.BlockAlign);
                writer.Write((ushort)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(outputDataSize);
            }

            CopyExactly(input, output, outputDataSize);
            return WaveTrimResult.Saved(fullOutput, (endFrame - startFrame) / (double)preview.SampleRate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException)
        {
            if (!outputCreated && File.Exists(fullOutput))
                return WaveTrimResult.Failed("The output file already exists. Wave will not overwrite it.");
            if (outputCreated)
            {
                try { File.Delete(fullOutput); }
                catch (IOException) { return WaveTrimResult.Failed("Trim failed and a partial output file may remain. Remove it before retrying."); }
                catch (UnauthorizedAccessException) { return WaveTrimResult.Failed("Trim failed and a partial output file may remain. Remove it before retrying."); }
            }
            return WaveTrimResult.Failed("Wave could not write the trimmed output file. The input was left unchanged.");
        }
    }

    private static void CopyExactly(Stream input, Stream output, long byteCount)
    {
        var buffer = new byte[CopyBufferSize];
        while (byteCount > 0)
        {
            var requested = (int)Math.Min(buffer.Length, byteCount);
            var read = input.Read(buffer, 0, requested);
            if (read == 0) throw new EndOfStreamException("The WAV data chunk ended before the selected frames were copied.");
            output.Write(buffer, 0, read);
            byteCount -= read;
        }
    }
}

internal static class WaveSelfTest
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenOS-Wave-SelfTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var tonePath = Path.Combine(directory, "tone.wav");
            WriteTone(tonePath, sampleRate: 8000, seconds: 1);
            var loaded = WaveSurface.Load(tonePath);
            Require(loaded.IsLoaded, loaded.Message);
            Require(loaded.Preview is not null, "Loaded state did not include a preview.");
            var preview = loaded.Preview!;
            Require(preview.Peaks.Length == 512, "Expected 512 bounded waveform peaks.");
            Require(preview.SampleRate == 8000, "Sample rate was not preserved.");
            Require(preview.Channels == 1, "Channel count was not preserved.");
            Require(preview.DurationSeconds is >= .99 and <= 1.01, "Duration was outside the expected one-second window.");
            Require(preview.Peaks.Any(peak => peak > .2f), "Generated waveform did not contain real signal peaks.");
            Require(preview.Peaks.All(peak => peak is >= 0f and <= 1f), "Waveform peaks escaped the bounded range.");

            var trimmedPath = Path.Combine(directory, "trimmed.wav");
            var trim = PcmWaveTrimmer.Trim(tonePath, .25, .75, trimmedPath);
            Require(trim.Succeeded && trim.OutputPath == trimmedPath, trim.Message);
            var trimmed = WaveSurface.Load(trimmedPath);
            Require(trimmed.IsLoaded && trimmed.Preview is not null, "Trimmed output did not load as a valid WAV file.");
            Require(trimmed.Preview!.DurationSeconds is >= .499 and <= .501, "Trimmed output did not contain the selected half-second.");
            var sourceBytes = File.ReadAllBytes(tonePath);
            var trimmedBytes = File.ReadAllBytes(trimmedPath);
            var expectedSelection = sourceBytes.AsSpan(44 + 2000 * sizeof(short), 4000 * sizeof(short)).ToArray();
            var actualSelection = trimmedBytes.AsSpan(44).ToArray();
            Require(expectedSelection.SequenceEqual(actualSelection), "Trimmed output did not preserve the exact selected PCM frames.");

            var protectedPath = Path.Combine(directory, "protected.wav");
            File.WriteAllText(protectedPath, "keep existing file");
            var protectedBytes = File.ReadAllBytes(protectedPath);
            var overwrite = PcmWaveTrimmer.Trim(tonePath, 0, .5, protectedPath);
            Require(!overwrite.Succeeded && overwrite.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) && File.ReadAllBytes(protectedPath).SequenceEqual(protectedBytes), "Trim must report and preserve an existing output file.");
            var inPlace = PcmWaveTrimmer.Trim(tonePath, 0, .5, tonePath);
            Require(!inPlace.Succeeded && File.ReadAllBytes(tonePath).SequenceEqual(sourceBytes), "Trim must leave its source file unchanged.");
            var invalidRange = PcmWaveTrimmer.Trim(tonePath, .75, .25, Path.Combine(directory, "invalid-range.wav"));
            Require(!invalidRange.Succeeded, "An inverted trim range must be rejected.");

            var shortPath = Path.Combine(directory, "short.wav");
            WriteTone(shortPath, sampleRate: 16, seconds: 1);
            var shortAudio = WaveSurface.Load(shortPath);
            Require(shortAudio.IsLoaded && shortAudio.Preview?.Peaks.Length == 512, "Very short PCM audio must remain bounded and decodable.");

            var invalidPath = Path.Combine(directory, "invalid.wav");
            File.WriteAllText(invalidPath, "not a wave file");
            var invalid = WaveSurface.Load(invalidPath);
            Require(!invalid.IsLoaded && invalid.Preview is null, "Corrupt audio must fail closed.");

            Console.WriteLine("Wave self-test passed: PCM waveform load, exact trim/export, overwrite protection, short-file bounds, and fail-closed invalid input.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void WriteTone(string path, int sampleRate, int seconds)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = checked(sampleRate * seconds);
        var dataSize = checked(sampleCount * channels * bitsPerSample / 8);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked(36 + dataSize));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(checked(sampleRate * channels * bitsPerSample / 8));
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        for (var index = 0; index < sampleCount; index++)
        {
            var sample = Math.Sin(2 * Math.PI * 440 * index / sampleRate);
            writer.Write((short)(sample * short.MaxValue * .65));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.Ordinal))
        {
            try
            {
                WaveSelfTest.Run();
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Wave self-test failed: {exception.Message}");
                return 1;
            }
        }

        var isTrimCommand = args.Length == 5 && string.Equals(args[0], "--trim", StringComparison.Ordinal);
        if (args.Length != 1 && !isTrimCommand)
        {
            Console.WriteLine("HavenOS Wave — first standalone surface");
            Console.WriteLine("Usage: HavenOS.Wave <local-pcm-wave-file>");
            Console.WriteLine("       HavenOS.Wave --trim <input.wav> <start-seconds> <end-seconds> <new-output.wav>");
            Console.WriteLine("Validation: HavenOS.Wave --self-test");
            return 2;
        }

        if (args.Length == 5 && string.Equals(args[0], "--trim", StringComparison.Ordinal))
        {
            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var startSeconds)
                || !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var endSeconds))
            {
                Console.Error.WriteLine("Trim times must be numbers of seconds using invariant decimal notation.");
                return 2;
            }

            var result = PcmWaveTrimmer.Trim(args[1], startSeconds, endSeconds, args[4]);
            if (!result.Succeeded)
            {
                Console.Error.WriteLine(result.Message);
                return 1;
            }

            Console.WriteLine($"{result.Message} {result.OutputPath}");
            return 0;
        }

        var state = WaveSurface.Load(args[0]);
        if (!state.IsLoaded || state.Preview is null)
        {
            Console.Error.WriteLine(state.Message);
            return 1;
        }

        var preview = state.Preview;
        Console.WriteLine($"Wave — {Path.GetFileName(preview.SourcePath)}");
        Console.WriteLine($"{preview.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture)}s · {preview.SampleRate} Hz · {preview.Channels} channel(s)");
        Console.WriteLine(WaveConsoleSurface.Render(preview));
        return 0;
    }
}
