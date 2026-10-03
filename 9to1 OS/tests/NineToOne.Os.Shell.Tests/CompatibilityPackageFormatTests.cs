using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace NineToOne.Os.Shell.Tests;

public sealed class CompatibilityPackageFormatTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadsActualBinaryAndroidStringPoolAndRootAttribute(bool utf8)
    {
        Assert.Equal("com.example.astra", CompatibilityPackageMetadataReader.ReadAndroidIdentity(BinaryManifest(utf8)));
    }

    [Fact]
    public void BinaryIdentityCannotHideMalformedTrailingChunks()
    {
        var original = BinaryManifest(true); var bytes = new byte[original.Length + 8]; original.CopyTo(bytes, 0);
        W32(bytes, 4, (uint)bytes.Length); W16(bytes, original.Length, 0x0103); W16(bytes, original.Length + 2, 16); W32(bytes, original.Length + 4, 24);
        Assert.Throws<InvalidDataException>(() => CompatibilityPackageMetadataReader.ReadAndroidIdentity(bytes));
    }

    [Fact]
    public void BinaryIdentityRequiresMatchingCompleteRoot()
    {
        var bytes = BinaryManifest(false); W32(bytes, bytes.Length - 4, 1);
        Assert.Throws<InvalidDataException>(() => CompatibilityPackageMetadataReader.ReadAndroidIdentity(bytes));
        bytes = BinaryManifest(false)[..^24]; W32(bytes, 4, (uint)bytes.Length);
        Assert.Throws<InvalidDataException>(() => CompatibilityPackageMetadataReader.ReadAndroidIdentity(bytes));
    }

    [Fact]
    public async Task ApkHasDeclaredPackageAndMultipleAbisWithoutTrustOrExtensionAuthority()
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifest = archive.CreateEntry("AndroidManifest.xml").Open()) manifest.Write(BinaryManifest(true));
            archive.CreateEntry("lib/arm64-v8a/libexample.so"); archive.CreateEntry("lib/x86_64/libexample.so");
        }
        bytes.Position = 0; var prefix = new byte[64]; await bytes.ReadExactlyAsync(prefix);
        var result = await CompatibilityPackageMetadataReader.ReadArchiveOrInstallerAsync(bytes, prefix, default);
        Assert.Equal("android-apk", result.Format); Assert.Equal("android:com.example.astra", result.DeclaredApplicationIdentity);
        Assert.Equal(new[] { "arm64-v8a", "x86_64" }, result.Architectures);
    }

    [Fact]
    public async Task DuplicateManifestCannotChooseAnArbitraryApplicationIdentity()
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in new[] { "com.example.first", "com.example.second" })
            { using var content = archive.CreateEntry("AndroidManifest.xml").Open(); content.Write(Encoding.UTF8.GetBytes($"<manifest package=\"{name}\"/>")); }
        bytes.Position = 0; var prefix = new byte[64]; await bytes.ReadExactlyAsync(prefix);
        await Assert.ThrowsAsync<InvalidDataException>(() => CompatibilityPackageMetadataReader.ReadArchiveOrInstallerAsync(bytes, prefix, default));
    }

    [Fact]
    public void XmlExternalEntitiesAreNotPackageMetadata()
    {
        Assert.Throws<XmlException>(() => CompatibilityPackageMetadataReader.ReadAndroidIdentity(Encoding.UTF8.GetBytes("<!DOCTYPE manifest [<!ENTITY external SYSTEM 'file:///etc/passwd'>]><manifest package='com.example.astra'>&external;</manifest>")));
    }

    [Fact]
    public async Task RecognisesMsiDatabaseButDoesNotInventItsUnresolvedArchitecture()
    {
        var bytes = new byte[1024]; new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }.CopyTo(bytes, 0);
        W16(bytes, 26, 3); W16(bytes, 28, 0xfffe); W16(bytes, 30, 9); bytes[512 + 66] = 5;
        new Guid("000c1084-0000-0000-c000-000000000046").ToByteArray().CopyTo(bytes, 512 + 80);
        using var stream = new MemoryStream(bytes, writable: false);
        var result = await CompatibilityPackageMetadataReader.ReadArchiveOrInstallerAsync(stream, bytes[..64], default);
        Assert.Equal("windows-msi", result.Format); Assert.Empty(result.Architectures); Assert.Null(result.DeclaredApplicationIdentity);
        bytes[512 + 80] = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => CompatibilityPackageMetadataReader.ReadArchiveOrInstallerAsync(stream, bytes[..64], default));
    }

    private static byte[] BinaryManifest(bool utf8)
    {
        var values = new[] { "manifest", "package", "com.example.astra" };
        using var strings = new MemoryStream(); var offsets = new List<int>();
        foreach (var value in values)
        {
            offsets.Add((int)strings.Position);
            if (utf8)
            { var encoded = Encoding.UTF8.GetBytes(value); strings.WriteByte((byte)value.Length); strings.WriteByte((byte)encoded.Length); strings.Write(encoded); strings.WriteByte(0); }
            else
            { strings.WriteByte((byte)value.Length); strings.WriteByte(0); strings.Write(Encoding.Unicode.GetBytes(value)); strings.WriteByte(0); strings.WriteByte(0); }
        }
        var poolSize = 28 + offsets.Count * 4 + (int)strings.Length; poolSize = (poolSize + 3) & ~3;
        var bytes = new byte[8 + poolSize + 56 + 24]; W16(bytes, 0, 3); W16(bytes, 2, 8); W32(bytes, 4, (uint)bytes.Length);
        var p = 8; W16(bytes, p, 1); W16(bytes, p + 2, 28); W32(bytes, p + 4, (uint)poolSize); W32(bytes, p + 8, (uint)values.Length);
        W32(bytes, p + 16, utf8 ? 0x100u : 0); W32(bytes, p + 20, 40);
        for (var i = 0; i < offsets.Count; i++) W32(bytes, p + 28 + i * 4, (uint)offsets[i]);
        strings.ToArray().CopyTo(bytes, p + 40);
        p += poolSize; W16(bytes, p, 0x0102); W16(bytes, p + 2, 16); W32(bytes, p + 4, 56);
        W32(bytes, p + 12, uint.MaxValue); W32(bytes, p + 16, uint.MaxValue); W32(bytes, p + 20, 0);
        W16(bytes, p + 24, 20); W16(bytes, p + 26, 20); W16(bytes, p + 28, 1);
        p += 36; W32(bytes, p, uint.MaxValue); W32(bytes, p + 4, 1); W32(bytes, p + 8, 2); W16(bytes, p + 12, 8); bytes[p + 15] = 3; W32(bytes, p + 16, 2);
        p += 20; W16(bytes, p, 0x0103); W16(bytes, p + 2, 16); W32(bytes, p + 4, 24);
        W32(bytes, p + 12, uint.MaxValue); W32(bytes, p + 16, uint.MaxValue); W32(bytes, p + 20, 0);
        return bytes;
    }
    private static void W16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void W32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
