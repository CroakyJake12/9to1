using System.Diagnostics;
using System.Globalization;
using Haven.Application;
using HavenOS.Home.Apps;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>One source-owned actual native supervisor/child. Only the registered
/// Root's retained logind and signed-code objects can create it. No public UID,
/// requested path, caller argv, observation record or approval flag can issue it.</summary>
internal sealed class LinuxHomeOriginalControlledProcess : IAsyncDisposable
{
    private readonly object _owner;
    private readonly LinuxHomeBootstrapActivationOwner.PublishedCode _code;
    private readonly LinuxHomeRegisteredRootServiceContext _kernel;
    private readonly LinuxHomeRegisteredRootServiceContext.InteractiveUser _user;
    private readonly HomePackageOriginalInstalledActivationRecord _package;
    private readonly CancellationTokenSource _lifetime;
    private readonly Process _supervisor;
    private readonly SafeFileHandle _helperImage, _image;
    private readonly LinuxOriginalDownloadIdentity _imageIdentity;
    private readonly object _gate = new();
    private readonly Task<string> _diagnostic;
    private Task? _prepare, _resume, _close;
    private Task<string>? _exit;
    private bool _retiring, _resumeAttempted;
    internal bool IsOwnedBy(object issuer) => ReferenceEquals(_owner, issuer);
    internal int ProcessId { get; private set; }
    internal string? ProcessStartIdentity { get; private set; }
    internal string ExecutableIdentity => "sha256:" + _package.ActivatedFiles.Single(value => value.RelativePath == _package.EntrypointRelativePath).Sha256;
    internal Process OriginalSupervisor => _supervisor;
    internal Task<string>? OriginalExit { get { lock (_gate) return _exit; } }

    private LinuxHomeOriginalControlledProcess(object actualRootOwner, LinuxHomeBootstrapActivationOwner.PublishedCode code,
        LinuxHomeRegisteredRootServiceContext kernel, LinuxHomeRegisteredRootServiceContext.InteractiveUser user,
        HomePackageOriginalInstalledActivationRecord package, SafeFileHandle helper, SafeFileHandle image,
        Process actualSupervisor, CancellationTokenSource actualLifetime)
    {
        _owner = actualRootOwner; _code = code; _kernel = kernel; _user = user; _package = package;
        _helperImage = helper; _image = image; _imageIdentity = LinuxOriginalDownloadPaths.ReadIdentity(image);
        _supervisor = actualSupervisor; _lifetime = actualLifetime;
        _diagnostic = LinuxHomeRootServiceRuntime.ReadBoundedErrors(_supervisor.StandardError, CancellationToken.None);
    }
    internal static async Task<LinuxHomeOriginalControlledProcess> CreateOriginalAsync(object rootOwner,
        LinuxHomeBootstrapActivationOwner.PublishedCode code, LinuxHomeRegisteredRootServiceContext kernel,
        LinuxHomeRegisteredRootServiceContext.InteractiveUser user, HomePackageOriginalInstalledActivationRecord root,
        HomePackageOriginalInstalledActivationRecord package, string originalEndpoint, CancellationToken token)
    {
        kernel.DemandOriginalCurrent(user); await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
        if (root.AppId != "root" || package.AppId == "root" || !code.Packages.Any(value => ReferenceEquals(value, root)) ||
            !code.Packages.Any(value => ReferenceEquals(value, package)))
            throw new UnauthorizedAccessException("The SAME original Root/non-root signed code cohort is required.");
        SafeFileHandle? helper = null, image = null; Process? supervisor = null; CancellationTokenSource? lifetime = null;
        LinuxHomeOriginalControlledProcess? actual = null;
        var supervisorStarted = false;
        var failures = new List<Exception>();
        try
        {
            helper = code.OpenOriginalFile(root, "home-root-launcher"); image = code.OpenOriginalFile(package, package.EntrypointRelativePath);
            var identity = LinuxOriginalDownloadPaths.ReadIdentity(image);
            if ((identity.Mode & 0x1ff) != 0x1ed || (LinuxOriginalDownloadPaths.ReadIdentity(helper).Mode & 0x1ff) != 0x1ed)
                throw new UnauthorizedAccessException("The SAME executable signed image/helper modes are required.");
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            supervisor = new() { StartInfo = LinuxHomeRootServiceRuntime.HelperStart(root, package, identity, user, originalEndpoint), EnableRaisingEvents = true };
            if (!supervisor.Start()) throw new IOException("The actual signed native supervisor did not start.");
            supervisorStarted = true;
            actual = new(rootOwner, code, kernel, user, package, helper, image, supervisor, lifetime);
            helper = null; image = null; supervisor = null; lifetime = null;
            using var observedHelper = File.OpenHandle("/proc/" + actual._supervisor.Id.ToString(CultureInfo.InvariantCulture) + "/exe", FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!LinuxOriginalDownloadPaths.ReadIdentity(actual._helperImage).SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(observedHelper)))
                throw new UnauthorizedAccessException("The actual supervisor must execute the SAME retained signed helper.");
            actual._prepare = actual.PrepareOriginal(token);
            await actual._prepare.ConfigureAwait(false);
            return actual;
        }
        catch (Exception cause) { failures.Add(cause); }
        if (actual is not null)
            try { await actual.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        else
        {
            if (supervisor is not null)
            {
                if (supervisorStarted)
                {
                    try { supervisor.StandardInput.Close(); } catch (Exception cause) { failures.Add(cause); }
                    try { if (!supervisor.HasExited) supervisor.Kill(false); } catch (Exception cause) { failures.Add(cause); }
                    try { await supervisor.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                }
                try { supervisor.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            }
            try { image?.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            try { helper?.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            try { lifetime?.Dispose(); } catch (Exception cause) { failures.Add(cause); }
        }
        throw new AggregateException("Original native controlled process acquisition failed; all actual resources were retained and independently retired.", failures);
    }
    private async Task PrepareOriginal(CancellationToken token)
    {
        using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var frame = await LinuxHomeRootServiceRuntime.ReadLine(_supervisor.StandardOutput, deadline.Token).ConfigureAwait(false);
        var parts = frame.Split(':');
        if (parts.Length != 3 || parts[0] != "OriginalLinuxHomeChildV1" || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var child) || child <= 0 ||
            parts[2] != _user.UserId.ToString(CultureInfo.InvariantCulture))
            throw new UnauthorizedAccessException("The native supervisor did not issue its SAME held child/principal.");
        ProcessId = child; _kernel.DemandOriginalCurrent(_user); await _code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
    }
    internal Task ResumeOriginalAsync(CancellationToken token)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_resume is not null) return _resume;
            return _resume = ResumeOriginal(token);
        }
    }
    private async Task ResumeOriginal(CancellationToken token)
    {
        using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        _kernel.DemandOriginalCurrent(_user); await _code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
        lock (_gate)
        {
            if (_retiring || _resumeAttempted || _prepare?.IsCompletedSuccessfully != true)
                throw new UnauthorizedAccessException("The SAME prepared native child may be dispatched once.");
            _resumeAttempted = true; // an attempted write is retained even if its acknowledgement is lost
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await _supervisor.StandardInput.WriteAsync("R".AsMemory(), deadline.Token).ConfigureAwait(false);
        await _supervisor.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
        if (await LinuxHomeRootServiceRuntime.ReadLine(_supervisor.StandardOutput, deadline.Token).ConfigureAwait(false) != "OriginalLinuxHomeExecV1")
            throw new IOException("The actual same native helper did not confirm a current post-exec image.");
        ProcessStartIdentity = ReadOriginalProcessStart();
        lock (_gate) _exit = LinuxHomeRootServiceRuntime.ReadLine(_supervisor.StandardOutput, _lifetime.Token);
        DemandOriginalCurrent();
    }
    internal void DemandOriginalCurrent()
    {
        _kernel.DemandOriginalCurrent(_user);
        lock (_gate)
        {
            if (_retiring || _supervisor.HasExited || _exit?.IsCompleted == true || !_resumeAttempted || ProcessStartIdentity is null)
                throw new UnauthorizedAccessException("The SAME actual controlled native child/supervisor must still be live.");
        }
        using var current = File.OpenHandle("/proc/" + ProcessId.ToString(CultureInfo.InvariantCulture) + "/exe", FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!_imageIdentity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(current)) || ReadOriginalProcessStart() != ProcessStartIdentity)
            throw new UnauthorizedAccessException("The original native child executable/start identity changed.");
        lock (_gate) if (_supervisor.HasExited || _exit?.IsCompleted == true) throw new UnauthorizedAccessException("The native child retired during its currentness observation.");
    }
    private string ReadOriginalProcessStart()
    {
        // No reusable public process-start parser exists in the maintained Linux
        // platform; the llama equivalents are private provider implementation.
        // The actual supervisor holds a pidfd and does not reap/recycle this PID.
        var stat = File.ReadAllText("/proc/" + ProcessId.ToString(CultureInfo.InvariantCulture) + "/stat");
        var close = stat.LastIndexOf(')');
        if (stat.Length > 64 * 1024 || close < 0)
            throw new InvalidDataException("The actual native child kernel process record is malformed.");
        var fields = stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20 || !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var start) || start == 0)
            throw new InvalidDataException("The actual native child start-time identity is unavailable.");
        return "linux-proc-start:" + start.ToString(CultureInfo.InvariantCulture);
    }
    internal Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate) { _retiring = true; return _close ??= Close(); }
        async Task Close()
        {
            var failures = new List<Exception>();
            try { _lifetime.Cancel(); } catch (Exception cause) { failures.Add(cause); }
            try { _supervisor.StandardInput.Close(); } catch (Exception cause) { failures.Add(cause); }
            foreach (var original in new[] { _prepare, _resume })
                if (original is not null) try { await original.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            Task? wait = null;
            try { wait = _supervisor.WaitForExitAsync(CancellationToken.None); } catch (Exception cause) { failures.Add(cause); }
            if (wait is not null)
                try { await wait.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false); }
                catch (TimeoutException cause)
                {
                    failures.Add(cause);
                    try { _supervisor.Kill(false); } catch (Exception failure) { failures.Add(failure); }
                    try { await wait.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
                }
                catch (Exception cause) { failures.Add(cause); }
            if (_exit is { } exit)
                try { await exit.ConfigureAwait(false); }
                catch (OperationCanceledException cause) when (_lifetime.IsCancellationRequested && cause.CancellationToken == _lifetime.Token) { }
                catch (EndOfStreamException) { /* actual explicit supervisor stdin retirement closes stdout */ }
                catch (Exception cause) { failures.Add(cause); }
            try
            {
                var diagnostic = await _diagnostic.ConfigureAwait(false);
                if (_supervisor.HasExited && _supervisor.ExitCode != 0) failures.Add(new IOException("The actual controlled supervisor failed (" + _supervisor.ExitCode + "): " + diagnostic));
            }
            catch (Exception cause) { failures.Add(cause); }
            foreach (var resource in new IDisposable[] { _supervisor, _helperImage, _image, _lifetime })
                try { resource.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0) throw new AggregateException("Original native controlled process retirement retained actual failures.", failures);
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
