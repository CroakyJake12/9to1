using System.Security.Cryptography;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class NativeWindowsHomePackagePayloadStagerTests
{
    [WindowsPayloadFact]
    public async Task ExactPayloadStagesAndAbortRemovesOnlyOwnedStage()
    {
        WindowsOriginalFileCustody.RequireCapabilities();
        var directory = Path.Combine(Path.GetTempPath(), "9to1-payload-stage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var payload = RandomNumberGenerator.GetBytes(153_721);
            var originalPath = Path.Combine(directory, "original.payload");
            var priorPath = Path.Combine(directory, "prior.installed");
            await File.WriteAllBytesAsync(originalPath, payload);
            await File.WriteAllBytesAsync(priorPath, [9, 8, 7, 6]);
            using var root = WindowsOriginalFileCustody.RetainRoot(directory);
            using var parent = root.OpenDirectory(directory, mutable: true);
            using var source = WindowsOriginalFileCustody.OpenRelative(parent, "original.payload",
                WindowsOriginalFileCustody.FileRead, 1, WindowsOriginalCreateDisposition.OpenExisting,
                WindowsOriginalFileKind.File);
            var descriptor = Descriptor(payload);
            var stage = await NativeWindowsHomePackagePayloadStager.StageOriginalPayloadAsync(root,
                source, Path.Combine(directory, "original.payload"), parent, directory, descriptor,
                _ => Task.CompletedTask, _ => { }, CancellationToken.None);
            Assert.Equal(payload.LongLength, stage.PayloadBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), stage.PayloadSha256);
            Assert.Equal((ulong)payload.Length, stage.StageIdentity.Size);
            Assert.True(stage.StageIdentity.IsRegular);
            Assert.Same(stage.WriteHandle, stage.OriginalReadHandle);
            var bytes = new byte[payload.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await RandomAccess.ReadAsync(stage.OriginalReadHandle,
                    bytes.AsMemory(offset), offset);
                Assert.NotEqual(0, read);
                offset += read;
            }
            Assert.Equal(payload, bytes);
            var firstClose = stage.AbortAndDrainAsync();
            Assert.Same(firstClose, stage.DisposeAsync().AsTask());
            await firstClose;
            Assert.Throws<ObjectDisposedException>(() => _ = stage.OriginalReadHandle);
            Assert.Equal(new byte[] { 9, 8, 7, 6 }, await File.ReadAllBytesAsync(priorPath));
            Assert.Empty(Directory.EnumerateFiles(directory, ".9to1-home-payload-stage-*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [WindowsPayloadTheory]
    [InlineData("short")]
    [InlineData("tampered")]
    [InlineData("overlong")]
    public async Task InvalidPayloadDoesNotTouchPriorInstalledBytes(string failure)
    {
        WindowsOriginalFileCustody.RequireCapabilities();
        var directory = Path.Combine(Path.GetTempPath(), "9to1-payload-failure-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var expected = RandomNumberGenerator.GetBytes(4097);
            var actual = failure switch
            {
                "short" => expected[..^1],
                "tampered" => expected.Select((value, index) => index == 17 ? (byte)(value ^ 0xff) : value).ToArray(),
                _ => [.. expected, 0x41]
            };
            var priorPath = Path.Combine(directory, "prior.installed");
            await File.WriteAllBytesAsync(Path.Combine(directory, "original.payload"), actual);
            await File.WriteAllBytesAsync(priorPath, [1, 3, 3, 7]);
            using var root = WindowsOriginalFileCustody.RetainRoot(directory);
            using var parent = root.OpenDirectory(directory, mutable: true);
            using var source = WindowsOriginalFileCustody.OpenRelative(parent, "original.payload",
                WindowsOriginalFileCustody.FileRead, 1, WindowsOriginalCreateDisposition.OpenExisting,
                WindowsOriginalFileKind.File);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await NativeWindowsHomePackagePayloadStager.StageOriginalPayloadAsync(root, source,
                    Path.Combine(directory, "original.payload"), parent, directory, Descriptor(expected),
                    _ => Task.CompletedTask, _ => { }, CancellationToken.None));
            Assert.Equal(new byte[] { 1, 3, 3, 7 }, await File.ReadAllBytesAsync(priorPath));
            Assert.Empty(Directory.EnumerateFiles(directory, ".9to1-home-payload-stage-*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [WindowsPayloadFact]
    public async Task CancellationAfterFirstChunkDrainsAndDeletesOnlyOwnedStage()
    {
        WindowsOriginalFileCustody.RequireCapabilities();
        var directory = Path.Combine(Path.GetTempPath(), "9to1-payload-cancel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var payload = RandomNumberGenerator.GetBytes(256 * 1024);
            var priorBytes = new byte[] { 4, 2, 4, 2 };
            await File.WriteAllBytesAsync(Path.Combine(directory, "original.payload"), payload);
            await File.WriteAllBytesAsync(Path.Combine(directory, "prior.installed"), priorBytes);
            using var root = WindowsOriginalFileCustody.RetainRoot(directory);
            using var parent = root.OpenDirectory(directory, mutable: true);
            using var source = WindowsOriginalFileCustody.OpenRelative(parent, "original.payload",
                WindowsOriginalFileCustody.FileRead, 1, WindowsOriginalCreateDisposition.OpenExisting,
                WindowsOriginalFileKind.File);
            using var cancellation = new CancellationTokenSource();
            var currentChecks = 0;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await NativeWindowsHomePackagePayloadStager.StageOriginalPayloadAsync(root, source,
                    Path.Combine(directory, "original.payload"), parent, directory, Descriptor(payload),
                    _ =>
                    {
                        // Calls 1-2 precede stage creation; call 6 is after the first
                        // bounded write and before the next source chunk is accepted.
                        if (Interlocked.Increment(ref currentChecks) == 6) cancellation.Cancel();
                        return Task.CompletedTask;
                    }, _ => { }, cancellation.Token));

            Assert.True(currentChecks >= 6, "Cancellation must occur at a post-write ownership checkpoint.");
            Assert.Equal(priorBytes, await File.ReadAllBytesAsync(Path.Combine(directory, "prior.installed")));
            Assert.Empty(Directory.EnumerateFiles(directory, ".9to1-home-payload-stage-*.tmp"));
            Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(directory, "original.payload")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static HomePackageArtifactDescriptor Descriptor(byte[] payload) => new(
        1, "fixture.package", "fixture.app", "1.0.0", "stable", "desktop", "win-x64", "native",
        [], [], new string('a', 64), Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), payload.LongLength);

    private sealed class WindowsPayloadFactAttribute : FactAttribute
    {
        public WindowsPayloadFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                Skip = "Requires actual Windows64 native held-root file custody; other platforms remain UNRUN.";
        }
    }

    private sealed class WindowsPayloadTheoryAttribute : TheoryAttribute
    {
        public WindowsPayloadTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                Skip = "Requires actual Windows64 native held-root file custody; other platforms remain UNRUN.";
        }
    }
}
