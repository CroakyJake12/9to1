using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace NineToOne.Os.Supervisor.Root.Tests;

/// <summary>Hosted ROOT protocol primitive. Actual source-bound native helper and a
/// distinct genuine test UID/GID are mandatory. No approved product publisher,
/// signed installed owner, Home lease, actor or widget acceptance is inferred.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxNativeIssuedChildPidfdTests
{
    [Fact]
    public async Task ActualHelperDeathAfterRealUidDropStillAllowsOriginalReceivedPidfdDrain()
    {
        Assert.Equal("unix-euid:0", await new OperatingSystemPrincipalSource().GetPrincipalAsync(default));
        var rootStatus = await NineToOne.Os.Shell.Authority.LinuxProcBoundedObservation.ReadAsync(
            $"/proc/{Environment.ProcessId}/status", 65536, default);
        Assert.NotNull(rootStatus);
        var caps = Assert.Single(rootStatus.Split('\n'), line => line.StartsWith("CapEff:", StringComparison.Ordinal))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, caps.Length);
        Assert.True(ulong.TryParse(caps[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var capEff));
        Assert.NotEqual(0ul, capEff & (1ul << 5)); // existing genuine CAP_KILL, never provisioned by this fixture
        var helperPath = Environment.GetEnvironmentVariable("ASTRA_NATIVE_ATOMIC_HELPER_PATH");
        var helperSha = Environment.GetEnvironmentVariable("ASTRA_NATIVE_ATOMIC_HELPER_SHA256");
        Assert.False(string.IsNullOrWhiteSpace(helperPath)); Assert.False(string.IsNullOrWhiteSpace(helperSha));
        Assert.True(Path.IsPathFullyQualified(helperPath!));
        Assert.Equal(helperSha!.ToLowerInvariant(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(helperPath!))));
        Assert.True(uint.TryParse(Environment.GetEnvironmentVariable("ASTRA_SUPERVISED_TEST_UID"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var uid)); Assert.NotEqual(0u, uid); Assert.NotEqual(uint.MaxValue, uid);
        Assert.True(uint.TryParse(Environment.GetEnvironmentVariable("ASTRA_SUPERVISED_TEST_GID"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var gid)); Assert.NotEqual(0u, gid); Assert.NotEqual(uint.MaxValue, gid);
        var directory = Path.Combine("/run", "astra-native-pidfd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var endpoint = Path.Combine(directory, "control.sock"); var nonce = RandomNumberGenerator.GetBytes(32);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Seqpacket, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(endpoint)); listener.Listen(1);
        var start = new ProcessStartInfo(helperPath!) { UseShellExecute = false, WorkingDirectory = directory };
        foreach (var value in new[] { endpoint, Convert.ToHexStringLower(nonce), "/usr/bin/setpriv", "--reuid", uid.ToString(CultureInfo.InvariantCulture),
            "--regid", gid.ToString(CultureInfo.InvariantCulture), "--clear-groups", "--no-new-privs", "--", "/usr/bin/sleep", "120" }) start.ArgumentList.Add(value);
        start.Environment.Clear(); start.Environment["PATH"] = "/usr/bin:/bin";
        var helper = Process.Start(start) ?? throw new IOException("Actual native supervisor did not start.");
        NineToOne.Os.Shell.Authority.LinuxNativeIssuedChildPidfd? child = null;
        NineToOne.Os.Shell.Authority.LinuxOriginalSpawnPidfd? helperHandle = null;
        Socket? control = null; Exception? primary = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            control = await listener.AcceptAsync(deadline.Token);
            // Helper remains gated before child exec; capture THIS actual helper only.
            helperHandle = await NineToOne.Os.Shell.Authority.LinuxOriginalSpawnPidfd.CaptureAsync(helper, deadline.Token);
            Assert.NotNull(helperHandle);
            child = await NineToOne.Os.Shell.Authority.LinuxNativeIssuedChildPidfd.ReceiveCreatedAsync(control, helper, nonce, deadline.Token);
            Assert.NotNull(child); Assert.True(await child.IsOriginalLiveAsync(deadline.Token));
            var go = new byte[64]; BinaryPrimitives.WriteUInt32LittleEndian(go, 0x31505341);
            BinaryPrimitives.WriteUInt16LittleEndian(go.AsSpan(4), 1); BinaryPrimitives.WriteUInt16LittleEndian(go.AsSpan(6), 2);
            BinaryPrimitives.WriteUInt64LittleEndian(go.AsSpan(8), 1); nonce.CopyTo(go, 16);
            Assert.Equal(64, await control.SendAsync(go, SocketFlags.None, deadline.Token));
            var started = new byte[65]; Assert.Equal(64, await control.ReceiveAsync(started, SocketFlags.None, deadline.Token));
            Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(started.AsSpan(6)));
            // STARTED alone proves nothing: observe actual final child UID and executable.
            NineToOne.Os.Shell.Authority.LinuxProcessIdentity? observed = null;
            var timer = Stopwatch.StartNew();
            do
            {
                observed = await NineToOne.Os.Shell.Authority.LinuxProcessIdentity.ReadAsync(
                    new HomeNativeObservedPeer(child.OriginalProcessId, "unix-euid:" + uid.ToString(CultureInfo.InvariantCulture)), deadline.Token);
                if (observed?.ExecutablePath == "/usr/bin/sleep") break;
                await Task.Delay(10, deadline.Token);
            } while (timer.Elapsed < TimeSpan.FromSeconds(10));
            Assert.NotNull(observed); Assert.Equal("/usr/bin/sleep", observed.ExecutablePath);
            Assert.Equal(child.OriginalStartTicks.ToString(CultureInfo.InvariantCulture), observed.StartTime);
            Assert.True(await helperHandle.TerminateAndDrainSameActualSpawnAsync());
            // Credential change clears the inherited supplementary PDEATHSIG; real root
            // still owns the separately received ORIGINAL child descriptor.
            Assert.True(await child.IsOriginalLiveAsync(deadline.Token));
            Assert.True(await child.TerminateAndObserveOriginalExitAsync()); Assert.True(child.OriginalExitObserved());
        }
        catch (Exception error) { primary = error; }
        finally
        {
            var childExit = false; var helperExit = false;
            void Attempt(Action cleanup)
            { try { cleanup(); } catch (Exception error) { primary = primary is null ? error : new AggregateException("Original received child observation and exact cleanup both failed.", primary, error); } }
            try
            {
                if (child is not null)
                {
                    childExit = child.OriginalExitObserved() || await child.TerminateAndObserveOriginalExitAsync();
                    if (!childExit) throw new IOException("Original received child exit was not observed.");
                }
            }
            catch (Exception error) { primary = primary is null ? error : new AggregateException("Original received child observation and exact cleanup both failed.", primary, error); }
            // Closing the owned channel requests native parent drain even if receiver
            // admission failed. Always observe helper completion before fd teardown.
            Attempt(() => control?.Dispose());
            try
            {
                if (!helper.HasExited)
                {
                    try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)); }
                    catch (TimeoutException)
                    {
                        if (helperHandle is null || !await helperHandle.TerminateAndDrainSameActualSpawnAsync())
                            throw new IOException("Original native supervisor exit was not observed.");
                    }
                }
                helperExit = helper.HasExited;
                if (!helperExit) throw new IOException("Original native supervisor exit was not observed.");
                if (child is not null) childExit = child.OriginalExitObserved();
            }
            catch (Exception error) { primary = primary is null ? error : new AggregateException("Original received child observation and exact cleanup both failed.", primary, error); }
            Attempt(() => child?.Dispose()); Attempt(() => helperHandle?.Dispose()); Attempt(listener.Dispose); Attempt(helper.Dispose);
            // No cleanup exception replaces the primary, and routing is removed only
            // after BOTH original processes have actual observed exit.
            if (helperExit && childExit)
            { Attempt(() => File.Delete(endpoint)); Attempt(() => Directory.Delete(directory)); }
        }
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }
}
