using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

// Actual procfs ingress only, not a trusted installed peer or root bootstrap enrollment.
public sealed class LinuxProcBoundedObservationTests
{
    [Fact]
    public async Task ActualOwnKernelStatusIsReadableWithinBound()
    {
        var text = await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/status", 65536, default);
        Assert.NotNull(text); Assert.Contains($"Pid:\t{Environment.ProcessId}", text);
        Assert.Contains("Uid:", text);
    }
    [Fact]
    public async Task ActualPopulatedProcFileCannotPassOneByteIngressBound()
    {
        Assert.Null(await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/status", 1, default));
        Assert.NotNull(await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/status", 65536, default));
    }
    [Fact]
    public async Task MissingActualProcProcessIsInert()
    {
        Assert.Null(await LinuxProcBoundedObservation.ReadAsync("/proc/2147483647/status", 65536, default));
    }
    [Fact]
    public async Task ActualProcSelfAliasDoesNotReplaceExactNumericProcessObservation()
    {
        Assert.True(File.Exists("/proc/self/status"));
        Assert.Null(await LinuxProcBoundedObservation.ReadAsync("/proc/self/status", 65536, default));
        Assert.NotNull(await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/status", 65536, default));
    }
}
