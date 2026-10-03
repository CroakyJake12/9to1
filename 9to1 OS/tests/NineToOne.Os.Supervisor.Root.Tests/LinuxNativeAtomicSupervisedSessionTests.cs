using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;
using Xunit;

namespace NineToOne.Os.Supervisor.Root.Tests;

// Genuine hosted root lane. The helper is selected from the actual protected signed
// installation; STARTED is never substituted for final UID/executable observation.
// The separately protected sleep target is a root protocol primitive, not a signed
// installed Home/owner runtime. Its exact protected bytes are rechecked before GO.
[SupportedOSPlatform("linux")]
public sealed class LinuxNativeAtomicSupervisedSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Signed_original_helper_observes_real_uid_drop_and_original_child_exit(bool naturalExit)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = deadline.Token;
        Assert.Equal("unix-euid:0", await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct));
        var status = await LinuxProcBoundedObservation.ReadAsync($"/proc/{Environment.ProcessId}/status", 65536, ct);
        Assert.NotNull(status);
        var caps = Assert.Single(status.Split('\n'), row => row.StartsWith("CapEff:", StringComparison.Ordinal))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, caps.Length);
        Assert.True(ulong.TryParse(caps[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var effective));
        Assert.NotEqual(0ul, effective & (1ul << 5)); // Observe existing CAP_KILL; this fixture grants none.
        Assert.True(uint.TryParse(Environment.GetEnvironmentVariable("ASTRA_SUPERVISED_TEST_UID"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var uid)); Assert.NotEqual(0u, uid); Assert.NotEqual(uint.MaxValue, uid);
        Assert.True(uint.TryParse(Environment.GetEnvironmentVariable("ASTRA_SUPERVISED_TEST_GID"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var gid)); Assert.NotEqual(0u, gid); Assert.NotEqual(uint.MaxValue, gid);
        var userHome = Environment.GetEnvironmentVariable("ASTRA_SUPERVISED_TEST_HOME");
        Assert.False(string.IsNullOrWhiteSpace(userHome)); Assert.True(Path.IsPathFullyQualified(userHome!));
        var prepared = await LinuxRootAtomicSpawnHelperPreparation.ReadForAdministratorAsync(ct);
        Assert.NotNull(prepared); Assert.True(await prepared.IsCurrentForAdministratorAsync(ct));
        var expectedHelper = Environment.GetEnvironmentVariable("ASTRA_NATIVE_ATOMIC_HELPER_SHA256");
        Assert.False(string.IsNullOrWhiteSpace(expectedHelper));
        Assert.Equal(expectedHelper!.ToLowerInvariant(), Convert.ToHexStringLower(
            SHA256.HashData(await File.ReadAllBytesAsync(prepared.ExecutablePath, ct))));
        var originalTarget = await LinuxRootOwnedFiles.ReadAsync("/usr/bin/sleep", 1024 * 1024, ct);
        Assert.NotNull(originalTarget);
        var originalTargetHash = SHA256.HashData(originalTarget);
        var targetChecks = 0;
        async Task<bool> OriginalProtectedTargetCurrent(CancellationToken token)
        {
            Interlocked.Increment(ref targetChecks);
            if (!LinuxRootOwnedFiles.RegularFileImmutable("/usr/bin/sleep")) return false;
            var current = await LinuxRootOwnedFiles.ReadAsync("/usr/bin/sleep", 1024 * 1024, token);
            return current is not null && CryptographicOperations.FixedTimeEquals(originalTargetHash, SHA256.HashData(current));
        }
        var directory = Path.Combine("/run", "astra-signed-native-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var endpoint = Path.Combine(directory, "control.sock");
        LinuxNativeAtomicSupervisedSession? session = null; Exception? primary = null; bool exitObserved = false;
        try
        {
            session = await LinuxNativeAtomicSupervisedSession.StartForAdministratorAsync(prepared, endpoint,
                directory, userHome!, ["--reuid", uid.ToString(CultureInfo.InvariantCulture), "--regid",
                gid.ToString(CultureInfo.InvariantCulture), "--clear-groups", "--no-new-privs", "--",
                "/usr/bin/sleep", naturalExit ? "5" : "120"], OriginalProtectedTargetCurrent, ct);
            Assert.NotNull(session); Assert.Equal(1, Volatile.Read(ref targetChecks));
            var originalId = session.OriginalProcessId; var originalStart = session.OriginalStartTicks;
            LinuxProcessIdentity? final = null;
            for (var attempt = 0; attempt < 500; attempt++)
            {
                final = await LinuxProcessIdentity.ReadAsync(new HomeNativeObservedPeer(originalId,
                    "unix-euid:" + uid.ToString(CultureInfo.InvariantCulture)), ct);
                if (final?.ExecutablePath == "/usr/bin/sleep") break;
                await Task.Delay(10, ct);
            }
            Assert.NotNull(final); Assert.Equal("/usr/bin/sleep", final.ExecutablePath);
            Assert.Equal(originalStart.ToString(CultureInfo.InvariantCulture), final.StartTime);
            Assert.True(await session.IsOriginalLiveAsync(ct));
            if (naturalExit) await session.OriginalExitObservedTask.WaitAsync(TimeSpan.FromSeconds(30), ct);
            else await session.DisposeAsync();
            await session.OriginalExitObservedTask.WaitAsync(TimeSpan.FromSeconds(30), ct);
            exitObserved = session.OriginalExitObserved;
            Assert.True(exitObserved); Assert.False(await session.IsOriginalLiveAsync(ct));
            Assert.Equal(originalId, session.OriginalProcessId); Assert.Equal(originalStart, session.OriginalStartTicks);
            Assert.False(File.Exists(endpoint)); // Removal follows actual child+helper exit witnesses.
            await session.DisposeAsync(); // The same owned cleanup task remains idempotent.
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (session is not null)
            {
                try { await session.DisposeAsync(); }
                catch (Exception error) { primary = primary is null ? error : new AggregateException("Original native observation and exact cleanup both failed.", primary, error); }
                try { exitObserved = session.OriginalExitObserved; }
                catch (Exception error) { primary = primary is null ? error : new AggregateException("Original native observation and exact cleanup both failed.", primary, error); }
            }
            // Failed admission retains routing evidence; do not claim an unseen child exit.
            if (exitObserved && !File.Exists(endpoint))
            {
                try { Directory.Delete(directory); }
                catch (Exception error) { primary = primary is null ? error : new AggregateException("Original native observation and exact cleanup both failed.", primary, error); }
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
}
