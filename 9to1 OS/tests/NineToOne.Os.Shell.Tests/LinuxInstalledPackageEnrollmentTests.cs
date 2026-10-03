using System.Runtime.Versioning;
using System.Security.Cryptography;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

// Actual publish filesystem only. Constructed receipt here is materialization metadata,
// not a trusted issuer, installed package, administrator enrollment or native launch.
[SupportedOSPlatform("linux")]
public sealed class LinuxInstalledPackageEnrollmentTests
{
    [Fact]
    public async Task ActualCompletePublishMaterializationMatchesWithoutChangingSource()
    {
        using var f = new Fixture(); var before = await File.ReadAllBytesAsync(f.File);
        await LinuxInstalledPackageEnrollment.ValidateSourceInventoryAsync(f.Root, f.Receipt, default);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.File));
        Assert.Single(Directory.EnumerateFileSystemEntries(f.Root));
    }
    [Fact]
    public async Task AdditionalActualFileCannotBeOmittedFromSignedInventory()
    {
        using var f = new Fixture(); await File.WriteAllTextAsync(Path.Combine(f.Root, "extra"), "unlisted");
        await Assert.ThrowsAsync<InvalidDataException>(() => LinuxInstalledPackageEnrollment.ValidateSourceInventoryAsync(f.Root, f.Receipt, default));
        Assert.True(File.Exists(Path.Combine(f.Root, "extra"))); // Validation never cleans caller data.
    }
    [Fact]
    public async Task ChangedActualPayloadDigestDeniesEvenWithSameByteLength()
    {
        using var f = new Fixture(); await File.WriteAllBytesAsync(f.File, [5, 6, 7, 8]);
        await Assert.ThrowsAsync<InvalidDataException>(() => LinuxInstalledPackageEnrollment.ValidateSourceInventoryAsync(f.Root, f.Receipt, default));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, await File.ReadAllBytesAsync(f.File));
    }
    [Fact]
    public async Task ActualPayloadSymlinkCannotEnrollExternalBytes()
    {
        using var f = new Fixture(); var external = f.Root + ".outside";
        await File.WriteAllBytesAsync(external, [1, 2, 3, 4]);
        try
        {
            File.Delete(f.File); File.CreateSymbolicLink(f.File, external);
            await Assert.ThrowsAsync<InvalidDataException>(() => LinuxInstalledPackageEnrollment.ValidateSourceInventoryAsync(f.Root, f.Receipt, default));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(external));
        }
        finally { File.Delete(external); }
    }
    [Fact]
    public async Task CommandInputReadsActualBoundedBytesWithoutChangingSource()
    {
        using var f = new Fixture();
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await LinuxInstalledPackageEnrollmentCommand.ReadBoundedInputAsync(f.File, 4, default));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(f.File));
    }
    [Fact]
    public async Task CommandInputRejectsActualOversizedFileBeforeReturningData()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => LinuxInstalledPackageEnrollmentCommand.ReadBoundedInputAsync(f.File, 3, default));
        Assert.Equal(4, new FileInfo(f.File).Length);
    }
    [Fact]
    public async Task CommandInputCannotFollowActualReceiptSymlink()
    {
        using var f = new Fixture(); var link = Path.Combine(f.Root, "receipt-link");
        File.CreateSymbolicLink(link, f.File);
        await Assert.ThrowsAsync<IOException>(() => LinuxInstalledPackageEnrollmentCommand.ReadBoundedInputAsync(link, 4, default));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(f.File));
    }
    [Fact]
    public void ActualOpenedDeviceDescriptorCannotBeUsedAsPublishInput()
    {
        Assert.Throws<IOException>(() => LinuxInstalledPackageEnrollment.OpenInputHandle("/dev/null"));
    }
    [Fact]
    public async Task GrowthAfterActualFileWasOpenedCannotExtendSignedHashWork()
    {
        using var f = new Fixture();
        using var handle = LinuxInstalledPackageEnrollment.OpenInputHandle(f.File);
        await using var stream = new FileStream(handle, FileAccess.Read);
        Assert.Equal(4, stream.Length);
        await using (var append = new FileStream(f.File, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            await append.WriteAsync(new byte[] { 5, 6, 7, 8 });
        await Assert.ThrowsAsync<InvalidDataException>(() => LinuxInstalledPackageEnrollment.HashExactSizeAsync(stream, 4, default));
        Assert.Equal(5, stream.Position); // Exactly the signed four bytes plus one EOF witness.
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, await File.ReadAllBytesAsync(f.File));
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "actual-publish-" + Guid.NewGuid().ToString("N"));
        public string File => Path.Combine(Root, "owner");
        public InstallationReceipt Receipt { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root); System.IO.File.WriteAllBytes(File, [1, 2, 3, 4]);
            const string install = "/opt/9to1/apps/fixture-materialization";
            Receipt = new(1, "fixture.materialization", 1, "linux.xdg-desktop", "desktop:fixture.desktop", "desktop:fixture.desktop",
                install, install + "/owner", "/usr/share/applications/fixture.desktop", new('a', 64),
                [new("owner", 4, Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 })))], ["home.widgets"], []);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
