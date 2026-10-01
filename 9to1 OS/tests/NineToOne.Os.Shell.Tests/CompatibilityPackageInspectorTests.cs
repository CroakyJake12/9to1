using System.Buffers.Binary;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Application.Compatibility;

namespace NineToOne.Os.Shell.Tests;

public sealed class CompatibilityPackageInspectorTests
{
    [Fact]
    public async Task InspectsActualHeaderAndCanonicalDigestThenDisposesFilesLease()
    {
        var fixture = new Fixture();
        var result = await fixture.Inspector.InspectAsync(fixture.StoreId, fixture.Actor, fixture.FileId, "revision1");
        Assert.Equal("windows-exe", result.Format); Assert.Equal("x86_64", Assert.Single(result.Architectures));
        Assert.Equal(fixture.FileId, result.FileId); Assert.Equal("revision1", result.ContentRevision);
        Assert.Equal("windows-package:sha256:" + fixture.Lease.Source.Sha256.ToLowerInvariant(), result.PackageContentIdentity);
        Assert.Equal(2, fixture.Lease.Revalidations); Assert.True(fixture.Lease.Disposed);
    }

    [Fact]
    public async Task ContentDigestMismatchAndDllNeverProducePackageProposal()
    {
        var mismatch = new Fixture(); mismatch.Lease.Bytes[1000]++;
        await Assert.ThrowsAsync<IOException>(() => mismatch.Inspector.InspectAsync(mismatch.StoreId, mismatch.Actor, mismatch.FileId, "revision1"));
        Assert.True(mismatch.Lease.Disposed);
        var dll = new Fixture();
        BinaryPrimitives.WriteUInt16LittleEndian(dll.Lease.Bytes.AsSpan(128 + 22), 0x2002); dll.RefreshProof();
        await Assert.ThrowsAsync<InvalidDataException>(() => dll.Inspector.InspectAsync(dll.StoreId, dll.Actor, dll.FileId, "revision1"));
        Assert.True(dll.Lease.Disposed);
    }

    [Fact]
    public async Task ChangedHomeSessionAfterInspectionRejectsAndReleasesLease()
    {
        var fixture = new Fixture();
        fixture.Lease.OnRevalidate = () => { if (fixture.Lease.Revalidations == 2) fixture.Actor = fixture.Actor with { AuthenticationRevision = "session2" }; };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Inspector.InspectAsync(fixture.StoreId, fixture.Actor, fixture.FileId, "revision1"));
        Assert.True(fixture.Lease.Disposed);
    }

    [Fact]
    public async Task SubstitutedFileRevisionIsRejectedBeforeReadingBytes()
    {
        var fixture = new Fixture(); fixture.Lease.Source = fixture.Lease.Source with { FileId = Guid.NewGuid() };
        await Assert.ThrowsAsync<IOException>(() => fixture.Inspector.InspectAsync(fixture.StoreId, fixture.Actor, fixture.FileId, "revision1"));
        Assert.Equal(0, fixture.Lease.Opened); Assert.True(fixture.Lease.Disposed);
    }

    [Fact]
    public async Task UnrecognisedPackageAndOutsideHeaderOffsetFailWithoutExecution()
    {
        var arbitrary = new Fixture(); arbitrary.Lease.Bytes[0] = 0; arbitrary.RefreshProof();
        await Assert.ThrowsAsync<NotSupportedException>(() => arbitrary.Inspector.InspectAsync(arbitrary.StoreId, arbitrary.Actor, arbitrary.FileId, "revision1"));
        var outside = new Fixture(); BinaryPrimitives.WriteUInt32LittleEndian(outside.Lease.Bytes.AsSpan(60), uint.MaxValue); outside.RefreshProof();
        await Assert.ThrowsAsync<InvalidDataException>(() => outside.Inspector.InspectAsync(outside.StoreId, outside.Actor, outside.FileId, "revision1"));
    }

    [Fact]
    public async Task PeMagicWithoutRequiredOptionalHeaderDoesNotEstablishExecutableFormat()
    {
        var fixture = new Fixture(); BinaryPrimitives.WriteUInt16LittleEndian(fixture.Lease.Bytes.AsSpan(148), 2); fixture.RefreshProof();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Inspector.InspectAsync(fixture.StoreId, fixture.Actor, fixture.FileId, "revision1"));
        Assert.True(fixture.Lease.Disposed);
    }

    [Fact]
    public async Task MissingOrForeignFilesStoreCannotReadOrExposePackageBytes()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Inspector.InspectAsync(Guid.Empty, fixture.Actor, fixture.FileId, "revision1"));
        Assert.Equal(0, fixture.ReadCalls); Assert.Equal(0, fixture.Lease.Opened);
        var originalStore = fixture.StoreId;
        fixture.Lease.Source = fixture.Lease.Source with { StoreId = Guid.NewGuid() };
        await Assert.ThrowsAsync<IOException>(() => fixture.Inspector.InspectAsync(originalStore, fixture.Actor, fixture.FileId, "revision1"));
        Assert.Equal(originalStore, fixture.ObservedExpectedStore); Assert.Equal(0, fixture.Lease.Opened);
    }
    [Fact]
    public async Task FilesStoreSubstitutionDuringLeaseRevalidationDeniesInspection()
    {
        var fixture = new Fixture();
        fixture.Lease.OnRevalidate = () => fixture.Lease.Source = fixture.Lease.Source with { StoreId = Guid.NewGuid() };
        await Assert.ThrowsAsync<IOException>(() => fixture.Inspector.InspectAsync(fixture.StoreId, fixture.Actor, fixture.FileId, "revision1"));
        Assert.True(fixture.Lease.Disposed); Assert.Equal(0, fixture.Lease.Opened);
    }

    [Fact]
    public async Task ForeignOriginalActorIsDeniedBeforeOwnerRead()
    {
        var fixture = new Fixture();
        var original = fixture.Actor;
        fixture.Actor = original with { AuthenticationRevision = "replacement-session" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Inspector.InspectAsync(fixture.StoreId, original, fixture.FileId, "revision1"));
        Assert.Equal(0, fixture.ReadCalls); Assert.Equal(0, fixture.Lease.Opened);
    }

    [Fact]
    public async Task InspectorForwardsExactOriginalActorToContentOwner()
    {
        var fixture = new Fixture();
        var original = fixture.Actor;
        await fixture.Inspector.InspectAsync(fixture.StoreId, original, fixture.FileId, "revision1");
        Assert.Same(original, fixture.ObservedExpectedActor);
    }

    private sealed class Fixture : ICompatibilityPackageContentSource, IAuthenticatedResourceActorSource
    {
        public Guid StoreId { get; } = Guid.NewGuid();
        public int ReadCalls; public Guid? ObservedExpectedStore; public AuthenticatedResourceActor? ObservedExpectedActor;
        public Guid FileId = Guid.NewGuid();
        public AuthenticatedResourceActor Actor = new("actor", "profile", null, null, "session1");
        public Lease Lease;
        public CompatibilityPackageInspector Inspector { get; }
        public Fixture()
        {
            var bytes = new byte[1024]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 128);
            bytes[128] = (byte)'P'; bytes[129] = (byte)'E';
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(132), 0x8664);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(134), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148), 240);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(150), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(152), 0x20b);
            Lease = new(bytes, new(FileId, "revision1", "metadata1", "installer.exe", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), Actor) { StoreId = StoreId });
            Inspector = new(this, this);
        }
        public void RefreshProof() => Lease.Source = Lease.Source with { Sha256 = Convert.ToHexString(SHA256.HashData(Lease.Bytes)) };
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor);
        public ValueTask<ICompatibilityPackageContentLease> ReadAsync(Guid expectedStoreId, AuthenticatedResourceActor expectedActor, Guid file, string revision, long maximumBytes, CancellationToken ct)
        { ReadCalls++; ObservedExpectedStore = expectedStoreId; ObservedExpectedActor = expectedActor; return ValueTask.FromResult<ICompatibilityPackageContentLease>(Lease); }
    }

    private sealed class Lease(byte[] bytes, CompatibilityPackageSource source) : ICompatibilityPackageContentLease
    {
        public byte[] Bytes = bytes;
        public CompatibilityPackageSource Source { get; set; } = source;
        public int Revalidations, Opened;
        public bool Disposed;
        public Action? OnRevalidate;
        public ValueTask<Stream> OpenReadAsync(CancellationToken ct) { Opened++; return ValueTask.FromResult<Stream>(new MemoryStream(Bytes, writable: false)); }
        public ValueTask RevalidateAsync(CancellationToken ct) { Revalidations++; OnRevalidate?.Invoke(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
