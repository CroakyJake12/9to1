using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace NineToOne.Os.Shell;

public sealed record CompatibilityPackageMetadata(string Format, IReadOnlyList<string> Architectures, string? DeclaredApplicationIdentity);

/// <summary>Format metadata only: no signer verification, backend eligibility or installation permission.</summary>
public static class CompatibilityPackageMetadataReader
{
    public static async Task<CompatibilityPackageMetadata> ReadArchiveOrInstallerAsync(Stream stream, byte[] prefix, CancellationToken ct)
    {
        if (prefix.AsSpan(0, 8).SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }))
        {
            if (stream.Length < 512) throw new InvalidDataException("The compound installer header is truncated.");
            stream.Position = 0; var header = new byte[512]; await stream.ReadExactlyAsync(header, ct);
            var major = U16(header, 26); var shift = U16(header, 30);
            if (U16(header, 28) != 0xfffe || !((major == 3 && shift == 9) || (major == 4 && shift == 12)))
                throw new InvalidDataException("The compound installer version is unsupported.");
            var offset = ((long)U32(header, 48) + 1) * (1 << shift);
            if (offset > stream.Length - 128) throw new InvalidDataException("The installer root directory is outside the file.");
            stream.Position = offset; var root = new byte[128]; await stream.ReadExactlyAsync(root, ct);
            if (root[66] != 5 || new Guid(root.AsSpan(80, 16)) != new Guid("000c1084-0000-0000-c000-000000000046"))
                throw new InvalidDataException("This compound file is not a Windows Installer database.");
            // SummaryInformation architecture is not yet resolved; an empty list cannot establish backend eligibility.
            return new("windows-msi", Array.Empty<string>(), null);
        }
        if (!prefix.AsSpan(0, 4).SequenceEqual(new byte[] { 0x50, 0x4b, 0x03, 0x04 }))
            throw new NotSupportedException("The canonical source is not a recognised Windows executable, MSI or APK package.");
        await CheckArchiveBoundsAsync(stream, ct);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var manifests = archive.Entries.Where(e => e.FullName == "AndroidManifest.xml").ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("An APK must contain exactly one Android manifest.");
        var manifest = manifests[0];
        if (manifest.Length < 8 || manifest.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("The Android manifest exceeds its supported size bound.");
        var raw = new byte[(int)manifest.Length];
        await using (var content = manifest.Open())
        {
            await content.ReadExactlyAsync(raw, ct);
            if (await content.ReadAsync(new byte[1], ct) != 0) throw new InvalidDataException("Android manifest decompression exceeds the declared length.");
        }
        var package = ReadAndroidIdentity(raw);
        var architectures = archive.Entries.Select(e => e.FullName.Split('/')).Where(p => p.Length == 3 && p[0] == "lib" && p[2].EndsWith(".so", StringComparison.Ordinal))
            .Select(p => p[1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (architectures.Any(a => a.Length is 0 or > 64 || a.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')))
            throw new InvalidDataException("The APK declares an invalid ABI directory.");
        return new("android-apk", Array.AsReadOnly(architectures), "android:" + package);
    }

    private static async Task CheckArchiveBoundsAsync(Stream stream, CancellationToken ct)
    {
        var length = (int)Math.Min(stream.Length, 65557); var tail = new byte[length];
        stream.Position = stream.Length - length; await stream.ReadExactlyAsync(tail, ct);
        for (var i = length - 22; i >= 0; i--)
        {
            if (U32(tail, i) != 0x06054b50 || i + 22 + U16(tail, i + 20) != length) continue;
            var entries = U16(tail, i + 10); var directoryLength = U32(tail, i + 12); var directoryOffset = U32(tail, i + 16);
            if (U16(tail, i + 4) != 0 || U16(tail, i + 6) != 0 || entries != U16(tail, i + 8) ||
                entries is 0 or > 4096 || directoryLength > 4 * 1024 * 1024 || (long)directoryOffset + directoryLength > stream.Length - length + i)
                throw new InvalidDataException("The APK central directory exceeds supported single-file bounds (ZIP64/multi-disk archives are unsupported).");
            return;
        }
        throw new InvalidDataException("The APK archive has no bounded final central directory.");
    }

    public static string ReadAndroidIdentity(byte[] raw)
    {
        if (raw.Length < 8) throw new InvalidDataException("The Android manifest is truncated.");
        string? package;
        var first = 0;
        while (first < raw.Length && raw[first] is 9 or 10 or 13 or 32) first++;
        if (first < raw.Length && (raw[first] == '<' || (raw[first] == 0xef && raw.Length - first >= 3 && raw[first + 1] == 0xbb && raw[first + 2] == 0xbf)))
        {
            using var input = new MemoryStream(raw, writable: false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
            if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "manifest" || reader.NamespaceURI.Length != 0)
                throw new InvalidDataException("The APK XML root is not manifest.");
            package = reader.GetAttribute("package");
            var nodes = 1;
            while (reader.Read())
                if (++nodes > 65536 || reader.Depth > 128) throw new InvalidDataException("The APK XML exceeds its structural bound.");
        }
        else package = ReadBinaryIdentity(raw);
        if (package is null || package.Length > 255 || !Regex.IsMatch(package, @"\A[A-Za-z_][A-Za-z_0-9]*(?:\.[A-Za-z_][A-Za-z_0-9]*)+\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("The APK has no valid declared Android package identity.");
        return package;
    }

    private static string? ReadBinaryIdentity(byte[] raw)
    {
        if (raw.Length < 8 || U16(raw, 0) != 3 || U16(raw, 2) != 8 || U32(raw, 4) != raw.Length)
            throw new InvalidDataException("The binary Android XML header is invalid.");
        string[] strings = []; var position = 8; string? package = null;
        var elements = new Stack<(uint Namespace, uint Name)>(); var rootSeen = false; var rootClosed = false;
        while (position < raw.Length)
        {
            if (position > raw.Length - 8) throw new InvalidDataException("The binary Android XML chunk is truncated.");
            var kind = U16(raw, position); var header = U16(raw, position + 2); var size = U32(raw, position + 4);
            if (header < 8 || size < header || size > raw.Length - position) throw new InvalidDataException("Android XML chunk bounds are invalid.");
            var chunk = raw.AsSpan(position, (int)size).ToArray();
            if (kind == 1)
            {
                if (header < 28 || rootSeen || strings.Length != 0) throw new InvalidDataException("The Android string pool is invalid or repeated.");
                var count = U32(chunk, 8); var flags = U32(chunk, 16); var start = U32(chunk, 20);
                if (count > 65536 || (long)header + count * 4 > size || start < (long)header + count * 4 || start > size)
                    throw new InvalidDataException("The Android string offsets are invalid.");
                strings = new string[count];
                long decodedCharacters = 0;
                for (var n = 0; n < strings.Length; n++)
                {
                    var offset = (long)start + U32(chunk, header + n * 4);
                    if (offset >= size) throw new InvalidDataException("The Android string is outside its pool.");
                    strings[n] = ReadPoolString(chunk, (int)offset, (flags & 0x100) != 0);
                    decodedCharacters += strings[n].Length;
                    if (decodedCharacters > 2 * 1024 * 1024)
                        throw new InvalidDataException("The Android string pool exceeds its decoded text bound.");
                }
            }
            else if (kind == 0x0102)
            {
                if (header != 16 || size < 36) throw new InvalidDataException("The Android start element is invalid.");
                var name = U32(chunk, 20); var attributesStart = U16(chunk, 24); var attributesSize = U16(chunk, 26); var count = U16(chunk, 28);
                if (name >= strings.Length || rootClosed || (!rootSeen && strings[name] != "manifest") || attributesStart < 20 || attributesSize < 20 ||
                    16L + attributesStart + (long)attributesSize * count > size) throw new InvalidDataException("The first Android XML element must be a bounded manifest.");
                var root = !rootSeen; rootSeen = true;
                if (elements.Count >= 128) throw new InvalidDataException("The Android XML exceeds its nesting bound.");
                elements.Push((U32(chunk, 16), name));
                var packages = new List<string>();
                for (var n = 0; n < count; n++)
                {
                    var offset = 16 + attributesStart + attributesSize * n;
                    var ns = U32(chunk, offset); var attribute = U32(chunk, offset + 4); var value = U32(chunk, offset + 8);
                    if (attribute >= strings.Length || U16(chunk, offset + 12) != 8) throw new InvalidDataException("The Android attribute is invalid.");
                    if (ns != uint.MaxValue && ns >= strings.Length || value != uint.MaxValue && value >= strings.Length)
                        throw new InvalidDataException("The Android attribute string reference is invalid.");
                    if (!root || ns != uint.MaxValue || strings[attribute] != "package") continue;
                    var index = value != uint.MaxValue ? value : chunk[offset + 15] == 3 ? U32(chunk, offset + 16) : uint.MaxValue;
                    if (index >= strings.Length) throw new InvalidDataException("The Android package string is invalid.");
                    packages.Add(strings[index]);
                }
                if (root)
                {
                    if (packages.Count != 1) throw new InvalidDataException("The APK manifest package identity is absent or ambiguous.");
                    package = packages[0];
                }
            }
            else if (kind == 0x0103)
            {
                if (header != 16 || size != 24 || elements.Count == 0 ||
                    elements.Pop() != (U32(chunk, 16), U32(chunk, 20)))
                    throw new InvalidDataException("The Android XML closing element does not match.");
                if (elements.Count == 0) rootClosed = true;
            }
            else if (kind is 0x0100 or 0x0101)
            {
                if (header != 16 || size != 24 || U32(chunk, 20) >= strings.Length ||
                    (U32(chunk, 16) != uint.MaxValue && U32(chunk, 16) >= strings.Length))
                    throw new InvalidDataException("The Android XML namespace is invalid.");
            }
            else if (kind == 0x0104)
            {
                if (header != 16 || size != 28 || elements.Count == 0 || U32(chunk, 16) >= strings.Length || U16(chunk, 20) != 8)
                    throw new InvalidDataException("The Android XML text is invalid.");
            }
            else if (kind != 1 && !(kind == 0x0180 && !rootSeen && header == 8 && (size - 8) % 4 == 0))
                throw new InvalidDataException("The Android XML chunk kind is unsupported.");
            position += (int)size;
        }
        if (!rootClosed || elements.Count != 0) throw new InvalidDataException("The Android XML root is not completely closed.");
        return package;
    }

    private static string ReadPoolString(byte[] chunk, int position, bool utf8)
    {
        int Length()
        {
            if (position >= chunk.Length) throw new InvalidDataException("The Android string length is truncated.");
            if (utf8)
            {
                var first = chunk[position++];
                if ((first & 0x80) == 0) return first;
                if (position >= chunk.Length) throw new InvalidDataException("The Android string length is truncated.");
                return ((first & 0x7f) << 8) | chunk[position++];
            }
            var value = U16(chunk, position); position += 2;
            if ((value & 0x8000) == 0) return value;
            var remainder = U16(chunk, position); position += 2; return ((value & 0x7fff) << 16) | remainder;
        }
        var characters = Length(); var bytes = utf8 ? Length() : (long)characters * 2; var terminator = utf8 ? 1 : 2;
        if (bytes > chunk.Length - position - terminator || chunk.AsSpan(position + (int)bytes, terminator).IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("The Android string termination is invalid.");
        try { Encoding encoding = utf8 ? new UTF8Encoding(false, true) : new UnicodeEncoding(false, false, true); return encoding.GetString(chunk, position, (int)bytes); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("The Android string encoding is invalid.", error); }
    }

    private static ushort U16(byte[] bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - 2) throw new InvalidDataException("Package metadata is truncated.");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    }
    private static uint U32(byte[] bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException("Package metadata is truncated.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    }
}
