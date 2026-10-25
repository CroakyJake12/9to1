using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real connected OS processes, pidfds and executable changes. These fixtures
/// never issue an installed-package identity, controlled-launch authority or permission.</summary>
[SupportedOSPlatform("linux")]
public sealed class HomeNativeLinuxPeerProcessTests
{
    private sealed class PeerPidFdFactAttribute : FactAttribute
    {
        public PeerPidFdFactAttribute()
        {
            if (!OperatingSystem.IsLinux() || Environment.OSVersion.Version < new Version(6, 5))
                Skip = "Physical peer-pidfd fixture requires Linux6.5+; older kernels retain local Home and explicitly refuse protected IPC unless backported.";
        }
    }

    [PeerPidFdFact]
    public async Task Actual_connected_process_and_running_image_are_retained_without_granting_installed_identity()
    {
        await using var rig = await Rig.CreateAsync();
        using var pin = HomeNativePeerObservation.RetainAcceptedUnixProcess(rig.Accepted);
        Assert.NotNull(pin);
        var observed = pin.Observe();
        Assert.NotNull(observed);
        Assert.Equal(rig.Child.Id, observed.ProcessId);
        Assert.Equal(observed, HomeNativePeerObservation.FromAcceptedUnixSocket(rig.Accepted));
        pin.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pin.Observe());
    }

    [PeerPidFdFact]
    public async Task Actual_exit_retires_the_original_process_even_while_socket_credentials_keep_its_numeric_PID()
    {
        await using var rig = await Rig.CreateAsync();
        using var pin = HomeNativePeerObservation.RetainAcceptedUnixProcess(rig.Accepted);
        Assert.NotNull(pin);
        var original = pin.Observe();
        await rig.Child.StandardInput.WriteLineAsync("exit");
        await rig.Child.WaitForExitAsync(rig.Token);
        Assert.Equal(0, rig.Child.ExitCode);
        Assert.Equal(original, HomeNativePeerObservation.FromAcceptedUnixSocket(rig.Accepted));
        Assert.Null(pin.Observe());
        Assert.Null(HomeNativePeerObservation.RetainAcceptedUnixProcess(rig.Accepted));
    }

    [PeerPidFdFact]
    public async Task Exec_of_another_real_image_retires_the_same_PID_without_waiting_for_process_exit()
    {
        await using var rig = await Rig.CreateAsync();
        using var pin = HomeNativePeerObservation.RetainAcceptedUnixProcess(rig.Accepted);
        Assert.NotNull(pin);
        var original = pin.Observe();
        await rig.Child.StandardInput.WriteLineAsync("exec");
        while (Path.GetFileName(File.ResolveLinkTarget($"/proc/{rig.Child.Id}/exe", returnFinalTarget: false)?.FullName) != "sleep")
            await Task.Delay(10, rig.Token);
        Assert.False(rig.Child.HasExited);
        Assert.Equal(original, HomeNativePeerObservation.FromAcceptedUnixSocket(rig.Accepted));
        Assert.Null(pin.Observe());
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(15));
        internal CancellationToken Token => _deadline.Token;
        private readonly string _path = Path.Combine("/tmp", "hpp-" + Guid.NewGuid().ToString("N")[..12]);
        private Socket? _listener;
        internal Socket Accepted = null!;
        internal Process Child = null!;
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                Directory.CreateDirectory(rig._path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var address = new UnixDomainSocketEndPoint(Path.Combine(rig._path, "peer.sock"));
                rig._listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                rig._listener.Bind(address); rig._listener.Listen(1);
                var start = new ProcessStartInfo("/usr/bin/python3")
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.Environment.Clear(); start.Environment["PYTHONNOUSERSITE"] = "1";
                start.ArgumentList.Add("-u"); start.ArgumentList.Add("-c");
                start.ArgumentList.Add("import os,socket,sys\ns=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)\ns.connect(sys.argv[1])\nprint('ready',flush=True)\ncommand=sys.stdin.readline().strip()\nif command=='exec': os.execv('/usr/bin/sleep',['sleep','30'])\n");
                start.ArgumentList.Add(address.ToString()!);
                rig.Child = Process.Start(start) ?? throw new InvalidOperationException("The actual process fixture did not start.");
                rig.Accepted = await rig._listener.AcceptAsync(rig.Token);
                Assert.Equal("ready", await rig.Child.StandardOutput.ReadLineAsync(rig.Token));
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            List<Exception> errors = [];
            try
            {
                if (Child is not null)
                {
                    if (!Child.HasExited) Child.Kill(); // only this newly owned fixture process
                    await Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception cause) { errors.Add(cause); }
            try { Child?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            try { Accepted?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            try { _listener?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            try { if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true); } catch (Exception cause) { errors.Add(cause); }
            _deadline.Dispose();
            HomeUnixCoreTransport.Throw(errors);
        }
    }
}
