using System.Security.Cryptography;
using System.Text;

namespace HavenOS.Apps.Wave;

internal static class WavePcmFormatsWorkflowTest
{
    public static int Run()
    {
        var directory = Directory.CreateTempSubdirectory("wave-pcm-formats-").FullName;
        try
        {
            foreach (var (tag, bits) in new (ushort, ushort)[] { (1, 8), (1, 16), (1, 24), (1, 32), (3, 32), (3, 64) })
            {
                foreach (var extensible in new[] { false, true })
                {
                    var source = Path.Combine(directory, $"source-{tag}-{bits}-{extensible}.wav");
                    Write(source, tag, bits, extensible);
                    var originalHash = Hash(source);
                    var preview = PcmWaveformReader.Decode(source);
                    Require(preview.FormatTag == tag && preview.BitsPerSample == bits, "Source representation changed.");
                    Require(preview.DataSize / preview.BlockAlign == 5, "Source frame count changed.");
                    var trimmed = Path.Combine(directory, $"trim-{tag}-{bits}-{extensible}.wav");
                    Require(PcmWaveTrimmer.Trim(source, .00002, .00008, trimmed).Succeeded, "Raw precision-preserving trim failed.");
                    var trimPreview = PcmWaveformReader.Decode(trimmed);
                    Require(trimPreview.DataSize / trimPreview.BlockAlign == 3 && trimPreview.FormatTag == tag
                        && trimPreview.BitsPerSample == bits && trimPreview.FormatPayload!.SequenceEqual(preview.FormatPayload!),
                        "Trim changed source precision, layout or frame count.");
                    using (var before = File.OpenRead(source))
                    using (var after = File.OpenRead(trimmed))
                    {
                        before.Position = preview.DataOffset + preview.BlockAlign;
                        after.Position = trimPreview.DataOffset;
                        var expectedBytes = new byte[3 * preview.BlockAlign];
                        var actualBytes = new byte[expectedBytes.Length];
                        before.ReadExactly(expectedBytes); after.ReadExactly(actualBytes);
                        Require(expectedBytes.SequenceEqual(actualBytes), "Trim changed raw source samples.");
                    }
                    var project = WaveProjectStore.Create("Formats", 48000, 1);
                    project = WaveProjectStore.AddWavClip(project, project.Tracks[0].TrackId, source, 0);
                    var saved = Path.Combine(directory, "project.9to1w");
                    WaveProjectStore.Save(saved, project);
                    project = WaveProjectStore.Open(saved);
                    var output = Path.Combine(directory, $"output-{tag}-{bits}-{extensible}.wav");
                    Require(WaveProjectExporter.ExportPcm16(project, output) == 5, "Export frame count changed.");
                    using var stream = File.OpenRead(output);
                    var decoded = PcmWaveformReader.Decode(output);
                    stream.Position = decoded.DataOffset;
                    using var reader = new BinaryReader(stream);
                    foreach (var expected in new short[] { short.MinValue, -16384, 0, 16384, bits == 8 ? (short)32512 : short.MaxValue })
                        Require(reader.ReadInt16() == expected, "Export sample conversion was incorrect.");
                    Require(Hash(source) == originalHash, "Import/edit/export modified source audio.");
                }
            }
            var invalid = Path.Combine(directory, "nonfinite.wav");
            Write(invalid, 3, 64, false, true);
            try { PcmWaveformReader.Decode(invalid); throw new Exception("Non-finite source accepted."); }
            catch (InvalidDataException) { }
            var malformed = Path.Combine(directory, "malformed.wav");
            Write(malformed, 1, 16, false);
            var bytes = File.ReadAllBytes(malformed);
            bytes[4]--;
            File.WriteAllBytes(malformed, bytes);
            try { PcmWaveformReader.Decode(malformed); throw new Exception("Invalid RIFF boundary accepted."); }
            catch (InvalidDataException) { }
            Write(malformed, 1, 16, false);
            bytes = File.ReadAllBytes(malformed);
            var duplicate = bytes.Concat(bytes.Skip(12).Take(24)).ToArray();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(duplicate.AsSpan(4), (uint)duplicate.Length - 8);
            File.WriteAllBytes(malformed, duplicate);
            try { PcmWaveformReader.Decode(malformed); throw new Exception("Duplicate WAV format accepted."); }
            catch (InvalidDataException) { }
            var overflow = Path.Combine(directory, "overflow.wav");
            Write(overflow, 3, 64, false, overflow: true);
            var extreme = WaveProjectStore.Create("Finite range", 48000, 1);
            extreme = WaveProjectStore.AddWavClip(extreme, extreme.Tracks[0].TrackId, overflow, 0);
            var partial = Path.Combine(directory, "must-not-publish.wav");
            try { WaveProjectExporter.ExportPcm16(extreme, partial); throw new Exception("Non-finite processing accepted."); }
            catch (InvalidDataException) { }
            Require(!File.Exists(partial), "Invalid processing published partial audio.");
            Console.WriteLine("Wave PCM/float formats passed: six representations plus extensible variants, exact declared PCM16 conversion, source hash, save/reopen, frame boundaries and non-finite rejection.");
            return 0;
        }
        finally { Directory.Delete(directory, true); }
    }
    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    private static void Write(string path, ushort tag, ushort bits, bool extensible, bool nonfinite = false, bool overflow = false)
    {
        using var data = new MemoryStream();
        using (var samples = new BinaryWriter(data, Encoding.UTF8, true))
        {
            foreach (var sample in new[] { -1d, -.5, 0, .5, bits == 8 ? 127d / 128 : 32767d / 32768 })
            {
                if (tag == 3)
                {
                    if (bits == 32) samples.Write((float)sample);
                    else samples.Write(sample == 0 && nonfinite ? double.NaN : sample == 0 && overflow ? double.MaxValue : sample);
                }
                else if (bits == 8) samples.Write((byte)(sample * 128 + 128));
                else if (bits == 16) samples.Write((short)(sample * 32768));
                else if (bits == 24)
                {
                    var value = (int)(sample * 8388608);
                    samples.Write((byte)value); samples.Write((byte)(value >> 8)); samples.Write((byte)(value >> 16));
                }
                else samples.Write((int)(sample * 2147483648));
            }
        }
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file, Encoding.UTF8);
        var formatSize = extensible ? 40 : 16;
        var factSize = tag == 3 ? 12 : 0;
        writer.Write("RIFF"u8); writer.Write((uint)(4 + 8 + formatSize + factSize + 8 + data.Length + (data.Length & 1))); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write((uint)formatSize); writer.Write(extensible ? (ushort)0xfffe : tag);
        writer.Write((ushort)1); writer.Write(48000u); writer.Write(48000u * bits / 8); writer.Write((ushort)(bits / 8)); writer.Write(bits);
        if (extensible)
        {
            writer.Write((ushort)22); writer.Write(bits); writer.Write(4u);
            writer.Write(new Guid(tag == 1 ? "00000001-0000-0010-8000-00aa00389b71" : "00000003-0000-0010-8000-00aa00389b71").ToByteArray());
        }
        if (tag == 3) { writer.Write("fact"u8); writer.Write(4u); writer.Write(5u); }
        writer.Write("data"u8); writer.Write((uint)data.Length); writer.Write(data.ToArray());
        if ((data.Length & 1) != 0) writer.Write((byte)0);
    }
}
