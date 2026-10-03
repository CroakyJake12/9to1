using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

// Actual managed FileStream and kernel fdinfo/proc identity. This does not enroll an installed
// product, instantiate the genuine Home profile, or turn a public witness into supervisor issuance.
public sealed class LinuxHomeLeaseKernelWitnessTests
{
    [Fact]
    public async Task ActualExclusiveKernelDescriptorIsCurrentOnlyWhileOriginalHandleHeld()
    {
        var path = Path.Combine(Path.GetTempPath(), "home-lock-witness-" + Guid.NewGuid().ToString("N"));
        FileStream? held = null;
        try
        {
            held = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var principal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(default);
            Assert.NotNull(principal);
            var peer = new HomeNativeObservedPeer(Environment.ProcessId, principal);
            var witness = await LinuxHomeLeaseKernelWitness.ObserveAsync(peer, path, default);
            Assert.NotNull(witness); // Unsupported/skipped kernel flock must fail the prerequisite, never skip.
            Assert.True(await witness.IsCurrentAsync(default));
            held.Dispose(); held = null;
            Assert.False(await witness.IsCurrentAsync(default));
            Assert.Null(await LinuxHomeLeaseKernelWitness.ObserveAsync(peer, path, default));
        }
        finally { held?.Dispose(); File.Delete(path); }
    }
    [Fact]
    public async Task ReplacingActualPathCannotRetargetOriginalKernelWitness()
    {
        var path = Path.Combine(Path.GetTempPath(), "home-lock-witness-" + Guid.NewGuid().ToString("N"));
        var moved = path + ".original";
        try
        {
            using var held = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var principal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(default);
            Assert.NotNull(principal);
            var peer = new HomeNativeObservedPeer(Environment.ProcessId, principal);
            var witness = await LinuxHomeLeaseKernelWitness.ObserveAsync(peer, path, default);
            Assert.NotNull(witness);
            File.Move(path, moved); await File.WriteAllTextAsync(path, "replacement");
            Assert.False(await witness.IsCurrentAsync(default));
            Assert.Equal("replacement", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); File.Delete(moved); }
    }
}
