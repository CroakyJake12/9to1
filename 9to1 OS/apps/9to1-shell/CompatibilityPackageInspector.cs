using System.Buffers.Binary;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Application.Compatibility;

namespace NineToOne.Os.Shell;

public sealed record CompatibilityInspectedPackage(Guid FileId, string ContentRevision, string MetadataRevision, string Name, string PackageContentIdentity,
    string Format, IReadOnlyList<string> Architectures, string? DeclaredApplicationIdentity, long Length, string Sha256);

/// <summary>Canonical Files admission and package metadata inspection. No publisher-trust, installation or execution grant.</summary>
public sealed class CompatibilityPackageInspector(ICompatibilityPackageContentSource files,
    IAuthenticatedResourceActorSource actors)
{
    public const long MaximumBytes = 2L * 1024 * 1024 * 1024;

    public async Task<CompatibilityInspectedPackage> InspectAsync(Guid fileId, string expectedContentRevision,
        CancellationToken cancellationToken = default)
    {
        if (fileId == Guid.Empty || string.IsNullOrWhiteSpace(expectedContentRevision) || expectedContentRevision.Length > 512)
            throw new ArgumentException("A canonical Files identity and content revision are required.");
        var actor = await actors.GetCurrentAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("Open the current Home profile before inspecting a package.");
        await using var lease = await files.ReadAsync(fileId, expectedContentRevision, MaximumBytes, cancellationToken);
        var source = lease.Source;
        if (source.FileId != fileId || source.ContentRevision != expectedContentRevision || source.ObservedActor != actor ||
            string.IsNullOrWhiteSpace(source.MetadataRevision) || source.MetadataRevision.Length > 512 ||
            string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 4096 ||
            source.Length < 64 || source.Length > MaximumBytes || source.Sha256.Length != 64 ||
            source.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
            throw new IOException("The Files owner did not supply a matching bounded content revision.");
        await lease.RevalidateAsync(cancellationToken);
        await using var stream = await lease.OpenReadAsync(cancellationToken);
        if (!stream.CanRead || !stream.CanSeek || stream.CanWrite || stream.Length != source.Length)
            throw new IOException("Files must supply a read-only seekable immutable package revision.");
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long consumed = 0;
        while (consumed < source.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, source.Length - consumed)), cancellationToken);
            if (read == 0) throw new IOException("The canonical package stream ended early.");
            hash.AppendData(buffer, 0, read); consumed += read;
        }
        if (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0 || stream.Length != source.Length)
            throw new IOException("The package stream exceeds its canonical Files revision length.");
        var digest = Convert.ToHexString(hash.GetHashAndReset());
        if (!digest.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Package bytes differ from the canonical Files content proof.");
        stream.Position = 0;
        var header = new byte[64]; await stream.ReadExactlyAsync(header, cancellationToken);
        async Task<CompatibilityInspectedPackage> FinishAsync(CompatibilityPackageMetadata metadata)
        {
            await lease.RevalidateAsync(cancellationToken);
            if (await actors.GetCurrentAsync(cancellationToken) != actor)
                throw new UnauthorizedAccessException("Home changed while inspecting the package.");
            cancellationToken.ThrowIfCancellationRequested();
            // Content identity survives moving the source file, not changing installer bytes/version.
            var prefix = metadata.Format == "android-apk" ? "android-package:sha256:" : "windows-package:sha256:";
            return new(fileId, expectedContentRevision, source.MetadataRevision, source.Name, prefix + digest.ToLowerInvariant(),
                metadata.Format, metadata.Architectures, metadata.DeclaredApplicationIdentity, source.Length, digest);
        }
        if (header[0] != 'M' || header[1] != 'Z')
            return await FinishAsync(await CompatibilityPackageMetadataReader.ReadArchiveOrInstallerAsync(stream, header, cancellationToken));
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(60));
        if (offset < 64 || offset > source.Length - 24) throw new InvalidDataException("The PE header offset is outside the package.");
        stream.Position = offset;
        var pe = new byte[24]; await stream.ReadExactlyAsync(pe, cancellationToken);
        if (pe[0] != 'P' || pe[1] != 'E' || pe[2] != 0 || pe[3] != 0)
            throw new InvalidDataException("The DOS image is not a supported PE executable.");
        var machine = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(4));
        var sections = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(6));
        var optional = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(20));
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(22));
        if (sections is 0 or > 96 || optional < 2 || (long)offset + 24 + optional + sections * 40 > source.Length ||
            (flags & 0x0002) == 0 || (flags & 0x2000) != 0)
            throw new InvalidDataException("The PE structure is truncated, invalid or a DLL.");
        var architecture = machine switch { 0x014c => "x86", 0x8664 => "x86_64", 0xaa64 => "arm64", _ => null };
        if (architecture is null) throw new NotSupportedException("This PE architecture has no declared compatibility backend.");
        if (optional < (machine == 0x014c ? 96 : 112))
            throw new InvalidDataException("The PE optional header omits its required architecture fields.");
        var magicBytes = new byte[2]; await stream.ReadExactlyAsync(magicBytes, cancellationToken);
        if (BinaryPrimitives.ReadUInt16LittleEndian(magicBytes) != (machine == 0x014c ? 0x10b : 0x20b))
            throw new InvalidDataException("PE architecture and optional header disagree.");
        return await FinishAsync(new("windows-exe", Array.AsReadOnly(new[] { architecture }), null));
    }
}
