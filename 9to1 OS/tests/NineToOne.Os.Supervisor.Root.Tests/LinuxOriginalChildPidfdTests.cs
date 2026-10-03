using System.Runtime.Versioning;
using System.Diagnostics;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Supervisor.Root.Tests;

// Actual OS child and kernel pidfd protocol. This is not an installed Home or lease issuer.
[SupportedOSPlatform("linux")]
public sealed class LinuxOriginalChildPidfdTests
{
    [Fact]
    public async Task ActualPrivatelySpawnedStartupHandleDrainsOriginalChildBeforeDisposal()
    {
        Assert.Equal("unix-euid:0", await new OperatingSystemPrincipalSource().GetPrincipalAsync(default));
        using var child = StartBoundedChild(); LinuxOriginalSpawnPidfd? original = null;
        try
        {
            Assert.True(LinuxOriginalSpawnPidfd.PlatformHandleAvailable());
            original = await LinuxOriginalSpawnPidfd.CaptureAsync(child, default); Assert.NotNull(original);
            Assert.True(await original!.TerminateAndDrainSameActualSpawnAsync());
            Assert.True(child.HasExited);
            Assert.False(await original.TerminateSameActualSpawnAsync(default));
        }
        finally
        {
            if (original is not null) { await original.TerminateAndDrainSameActualSpawnAsync(); original.Dispose(); }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
        }
    }
    [Fact]
    public async Task ActualOriginalKernelHandleSignalsOriginalChildAndCannotReviveAfterExit()
    {
        Assert.Equal("unix-euid:0", await new OperatingSystemPrincipalSource().GetPrincipalAsync(default));
        using var child = StartBoundedChild(); LinuxOriginalChildPidfd? original = null;
        try
        {
            var peer = new HomeNativeObservedPeer(child.Id, "unix-euid:0");
            var process = await LinuxProcessIdentity.ReadAsync(peer, default); Assert.NotNull(process);
            original = await LinuxOriginalChildPidfd.ObserveAsync(peer, process!, default); Assert.NotNull(original);
            Assert.True(await original!.IsOriginalCurrentAsync(default));
            Assert.True(await original.TerminateOriginalAsync(default));
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(await original.IsOriginalCurrentAsync(default));
            Assert.False(await original.TerminateOriginalAsync(default));
        }
        finally
        {
            if (original is not null) { await original.TerminateOriginalAsync(default); original.Dispose(); }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
        }
    }
    [Fact]
    public async Task AlteredOriginalStartCannotAcquireKernelHandleWhileActualOriginalRemainsCurrent()
    {
        Assert.Equal("unix-euid:0", await new OperatingSystemPrincipalSource().GetPrincipalAsync(default));
        using var child = StartBoundedChild(); LinuxOriginalChildPidfd? original = null;
        try
        {
            var peer = new HomeNativeObservedPeer(child.Id, "unix-euid:0");
            var process = await LinuxProcessIdentity.ReadAsync(peer, default); Assert.NotNull(process);
            Assert.Null(await LinuxOriginalChildPidfd.ObserveAsync(peer, process! with { StartTime = process!.StartTime + "foreign" }, default));
            original = await LinuxOriginalChildPidfd.ObserveAsync(peer, process, default); Assert.NotNull(original);
            Assert.True(await original!.IsOriginalCurrentAsync(default));
        }
        finally
        {
            if (original is not null) { await original.TerminateOriginalAsync(default); original.Dispose(); }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));
        }
    }
    private static Process StartBoundedChild()
    {
        var start = new ProcessStartInfo("/usr/bin/sleep") { UseShellExecute = false };
        start.ArgumentList.Add("30"); start.Environment.Clear(); start.Environment["PATH"] = "/usr/bin:/bin";
        return Process.Start(start) ?? throw new InvalidOperationException("Actual maintained OS sleep executable required.");
    }
}
