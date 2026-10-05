using System.Diagnostics;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace NineToOne.Accounts.Native;

/// <summary>
/// Explicit Windows Access helper owner. Access is not CAKE identity or authority.
/// Await CloseAndDrainAsync from an independent owner, never from an operation it joins.
/// </summary>
public sealed class CloudflaredNativeCakeAccessCredentialSource : INativeCakeAccessCredentialSource
{
    public const string OfficialRelease = "2026.9.3";
    public const string OfficialSourceCommit = "96d39adbc812dc7363834bda908970c1a2560a72";
    public const string OfficialWindowsSha256 = "f096265ec2fcbe9bb6e2d64268db167ced3fcbb83d894bdb9e2fcdb26f2ea7e2";
    public const long OfficialWindowsBytes = 55366080;
    public const string ApplicationOrigin = "https://cake-id-release-validation.jcbailey008.workers.dev/";
    private const string ProtectedApplication = ApplicationOrigin + "account";
    private const int MaximumCaptureBytes = 16384;
    private const int MaximumWaiters = 128;

    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ICloudflaredOwnedHelperHost _host;
    private readonly string _executable;
    private readonly TimeProvider _clock;
    private DateTimeOffset _credentialExpiresAt;
    private readonly IOException _expiredRefusal = new("The Access credential has expired; explicitly retire this owner before a new login.");
    private readonly List<Waiter> _waiters = [];
    private readonly List<Command> _commands = [];
    private readonly InvalidOperationException _capacityRefusal =
        new("Access credential wait custody is full; retire this owner.");
    private readonly ObjectDisposedException _closedRefusal =
        new(nameof(CloudflaredNativeCakeAccessCredentialSource));
    private Task? _acquisition;
    private Task? _close;
    private Task<IDisposable>? _actualPin;
    private IDisposable? _executableLease;
    private string? _credential;
    private bool _closing, _capacitySealed;

    // The integrator supplies a reviewed, protected absolute helper path; no discovery,
    // download, PATH search, environment read or default provider is performed here.
    public CloudflaredNativeCakeAccessCredentialSource(string trustedAbsoluteExecutablePath)
        : this(trustedAbsoluteExecutablePath, new SystemCloudflaredOwnedHelperHost(), requireWindows: true) { }

    internal CloudflaredNativeCakeAccessCredentialSource(string path, ICloudflaredOwnedHelperHost host,
        bool requireWindows = false, TimeProvider? clock = null)
    {
        if (requireWindows && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The reviewed Access helper requires Windows.");
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("An explicit absolute reviewed helper path is required.", nameof(path));
        _executable = Path.GetFullPath(path);
        _host = host;
        _clock = clock ?? TimeProvider.System;
    }

    internal Task? OriginalAcquisitionTask { get { lock (_sync) return _acquisition; } }
    internal Task[] OriginalHelperExitTasks
    { get { lock (_sync) return _commands.Select(x => x.Exit).OfType<Task>().ToArray(); } }
    internal Task[] OriginalStreamTasks
    { get { lock (_sync) return _commands.SelectMany(x => new Task?[] { x.Output, x.Error }).OfType<Task>().ToArray(); } }

    public ValueTask<string?> AcquireForRequestAsync(Uri exactApplicationOrigin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactApplicationOrigin);
        if (!exactApplicationOrigin.IsAbsoluteUri ||
            exactApplicationOrigin.AbsoluteUri != ApplicationOrigin)
            throw new UnauthorizedAccessException("Access credentials are restricted to the configured exact origin.");
        lock (_sync)
        {
            DemandAdmission();
            cancellationToken.ThrowIfCancellationRequested();
            _waiters.RemoveAll(x => x.Task?.IsCompletedSuccessfully == true);
            if (_waiters.Count >= MaximumWaiters)
            { _capacitySealed = true; throw _capacityRefusal; }

            TaskCompletionSource? acquisitionGate = null;
            if (_acquisition is null)
            {
                acquisitionGate = NewGate();
                _acquisition = AcquireOriginalAsync(acquisitionGate.Task);
            }
            var record = new Waiter();
            var waiterGate = NewGate();
            var actual = WaitOriginalAsync(waiterGate.Task, _acquisition, cancellationToken, record);
            record.Task = actual;
            _waiters.Add(record);
            // Both originals are published before native acquisition/cancellation callbacks.
            acquisitionGate?.SetResult();
            waiterGate.SetResult();
            return new ValueTask<string?>(actual);
        }
    }

    private void DemandAdmission()
    {
        if (_closing) throw _closedRefusal;
        if (_capacitySealed) throw _capacityRefusal;
    }

    private async Task<string?> WaitOriginalAsync(Task gate, Task original, CancellationToken caller, Waiter record)
    {
        await gate.ConfigureAwait(false);
        List<Exception> causes = [];
        string? result = null;
        try
        {
            // Caller cancellation withdraws only this wait. It cannot abandon or kill
            // the shared actual helper. The independent owner must explicitly close it.
            await original.WaitAsync(caller).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_closing) throw _closedRefusal;
                if (_clock.GetUtcNow() >= _credentialExpiresAt)
                { _credential = null; throw _expiredRefusal; }
                result = _credential ?? throw new IOException("Access helper returned no usable credential.");
            }
        }
        catch (Exception error) { AddTask(causes, original, error); }
        lock (_sync) record.Causes = causes.ToArray();
        Throw(causes);
        return result;
    }

    private async Task AcquireOriginalAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        List<Exception> causes = [];
        try
        {
            _stop.Token.ThrowIfCancellationRequested();
            // The original verification task is captured before awaiting its native I/O.
            var actualPin = _host.AcquirePinnedExecutableAsync(_executable, _stop.Token);
            _actualPin = actualPin;
            if (actualPin is null) throw new IOException("The original helper verification task was not returned.");
            _executableLease = await actualPin.ConfigureAwait(false);
            if (_executableLease is null) throw new IOException("The original helper image lease was not returned.");
            _stop.Token.ThrowIfCancellationRequested();
            var login = await RunCommandOriginalAsync(["access", "login", "--quiet", "--app=" + ProtectedApplication])
                .ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(login);
            _stop.Token.ThrowIfCancellationRequested();
            var encoded = await RunCommandOriginalAsync(["access", "token", "--app=" + ProtectedApplication])
                .ConfigureAwait(false);
            try
            {
                var value = new UTF8Encoding(false, true).GetString(encoded).TrimEnd('\r', '\n');
                if (value.Length is < 1 or > MaximumCaptureBytes ||
                    value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
                    throw new IOException("Access helper output is empty or malformed.");
                // This maintained SDK read is DENY-ONLY expiry metadata, never a
                // signature/issuer/audience/Access/CAKE identity verification.
                var metadata = new JsonWebTokenHandler { MaximumTokenSizeInBytes = MaximumCaptureBytes }
                    .ReadJsonWebToken(value);
                if (!metadata.TryGetPayloadValue<long>("exp", out var expiration) ||
                    expiration is < -62135596800L or > 253402300799L)
                    throw new IOException("Access helper output has no representable integer expiry.");
                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiration);
                lock (_sync)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    if (_closing) throw _closedRefusal;
                    if (expiresAt <= _clock.GetUtcNow()) throw _expiredRefusal;
                    _credentialExpiresAt = expiresAt;
                    _credential = value;
                }
            }
            finally { CryptographicOperations.ZeroMemory(encoded); }
        }
        catch (Exception error)
        {
            if (_actualPin is { IsFaulted: true } pin) AddTask(causes, pin, error);
            else Add(causes, error);
        }
        finally
        {
            // An unresolved native process keeps the SAME image lease retained.
            // Failed close never grants resource settlement or permission to reuse it.
            bool settled;
            lock (_sync) settled = _commands.All(x => x.NoChild || x.Exit?.IsCompletedSuccessfully == true);
            if (settled && _executableLease is { } lease)
            {
                try { lease.Dispose(); _executableLease = null; }
                catch (Exception error) { Add(causes, error); }
            }
        }
        Throw(causes);
    }

    private async Task<byte[]> RunCommandOriginalAsync(string[] arguments)
    {
        Command record;
        lock (_sync)
        {
            DemandAdmission();
            _stop.Token.ThrowIfCancellationRequested();
            record = new Command();
            _commands.Add(record); // Partial acquisition record precedes every host callback.
        }
        List<Exception> causes = [];
        byte[]? output = null, stderr = null;
        try
        {
            try
            {
                record.Child = _host.Create(_executable, arguments);
                lock (_sync)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    if (_closing) throw _closedRefusal;
                    record.NoChild = false; // Start may acquire a native child even if it throws.
                    if (!record.Child.Start())
                    { record.NoChild = true; throw new IOException("Access helper did not start."); }
                }
            }
            catch (Exception error) { Add(causes, error); }
            finally
            {
                if (record.Child is { } child && !record.NoChild)
                {
                    // Independently attempt each actual stream/exit acquisition even when
                    // Start threw. No failed acquisition becomes a CompletedTask witness.
                    try
                    {
                        record.OutputStream = child.OpenStandardOutput();
                        var outputGate = NewGate();
                        record.Output = CaptureOriginalAsync(outputGate.Task, record.OutputStream);
                        outputGate.SetResult();
                    }
                    catch (Exception error) { Add(causes, error); }
                    try
                    {
                        record.ErrorStream = child.OpenStandardError();
                        var errorGate = NewGate();
                        record.Error = CaptureOriginalAsync(errorGate.Task, record.ErrorStream);
                        errorGate.SetResult();
                    }
                    catch (Exception error) { Add(causes, error); }
                    try { record.Exit = child.WaitForExitAsync(); }
                    catch (Exception error) { Add(causes, error); }
                }
                // Explicit close may now stop the acquired SAME child. It first waits
                // for this acquisition gate, so it never kills an unassociated object.
                record.CaptureFinished.SetResult();
            }

            if (record.Exit is { } exit)
                try { await exit.ConfigureAwait(false); }
                catch (Exception error) { AddTask(causes, exit, error); }
            if (record.Output is { } actualOutput)
                try { output = await actualOutput.ConfigureAwait(false); }
                catch (Exception error) { AddTask(causes, actualOutput, error); }
            if (record.Error is { } actualError)
                try { stderr = await actualError.ConfigureAwait(false); }
                catch (Exception error) { AddTask(causes, actualError, error); }
            if (record.Exit?.IsCompletedSuccessfully == true && record.Child is { } originalChild)
                try
                {
                    if (originalChild.ExitCode != 0)
                        Add(causes, new IOException("Access helper exited unsuccessfully; private stderr is not disclosed."));
                }
                catch (Exception error) { Add(causes, error); }
            if (record.Exit is null && !record.NoChild)
                Add(causes, new IOException("The original helper exit was not acquired; settlement is unknown."));
        }
        catch (Exception error) { Add(causes, error); }
        finally
        {
            if (stderr is not null) CryptographicOperations.ZeroMemory(stderr);
            if (record.NoChild || record.Exit?.IsCompletedSuccessfully == true)
            {
                // Full stream pipelines were independently joined above before disposal.
                try { record.OutputStream?.Dispose(); } catch (Exception error) { Add(causes, error); }
                try { record.ErrorStream?.Dispose(); } catch (Exception error) { Add(causes, error); }
                try { lock (record.NativeSync) record.Child?.Dispose(); }
                catch (Exception error) { Add(causes, error); }
            }
            lock (_sync) record.Causes = causes.ToArray();
        }
        if (causes.Count != 0 && output is not null) CryptographicOperations.ZeroMemory(output);
        Throw(causes);
        return output ?? throw new IOException("The original helper stdout was not acquired.");
    }

    private static async Task<byte[]> CaptureOriginalAsync(Task gate, Stream original)
    {
        await gate.ConfigureAwait(false);
        var retained = new byte[MaximumCaptureBytes];
        var block = new byte[4096];
        var count = 0;
        Exception? overflow = null;
        try
        {
            while (true)
            {
                // One conversion/observation of this SAME native read ValueTask.
                var actual = original.ReadAsync(block.AsMemory(), CancellationToken.None).AsTask();
                int amount;
                try { amount = await actual.ConfigureAwait(false); }
                catch (Exception error)
                {
                    List<Exception> causes = [];
                    if (overflow is not null) Add(causes, overflow);
                    AddTask(causes, actual, error);
                    Throw(causes);
                    throw;
                }
                if (amount == 0) break;
                if (overflow is null && amount <= MaximumCaptureBytes - count)
                { Buffer.BlockCopy(block, 0, retained, count, amount); count += amount; }
                else overflow ??= new IOException("Access helper output exceeded its private capture limit.");
                Array.Clear(block);
                // Overflow still drains the SAME pipe to EOF. It is a retained failure,
                // not an allocation/CPU bound or proof that the native writer stopped.
            }
            if (overflow is not null) ExceptionDispatchInfo.Capture(overflow).Throw();
            return retained.AsSpan(0, count).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(retained);
            CryptographicOperations.ZeroMemory(block);
        }
    }

    public Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return _close;
            var gate = NewGate();
            _close = CloseOriginalAsync(gate.Task);
            _closing = true;
            _credential = null;
            gate.SetResult(); // SAME close is published before cancellation/native callbacks.
            return _close;
        }
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task CloseOriginalAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        List<Exception> causes = [];
        try { _stop.Cancel(); } catch (Exception error) { Add(causes, error); }
        Command[] commands;
        Waiter[] waiters;
        Task? acquisition;
        lock (_sync) { commands = _commands.ToArray(); waiters = _waiters.ToArray(); acquisition = _acquisition; }
        foreach (var command in commands)
        {
            try { await command.CaptureFinished.Task.ConfigureAwait(false); }
            catch (Exception error) { AddTask(causes, command.CaptureFinished.Task, error); }
            if (command.Child is not { } child || command.NoChild ||
                command.Exit?.IsCompletedSuccessfully == true) continue;
            try
            {
                lock (command.NativeSync)
                {
                    // Recheck settlement inside the SAME child-disposal exclusion.
                    if (command.Exit?.IsCompletedSuccessfully != true && !child.HasExited)
                        child.KillOwnedHelper();
                }
                // Only this exact helper is stopped, never a process tree/system browser.
            }
            catch (Exception error) { Add(causes, error); }
        }
        if (acquisition is { } actualAcquisition)
            try { await actualAcquisition.ConfigureAwait(false); }
            catch (Exception error) { AddTask(causes, actualAcquisition, error); }
        // Each actual exit/stream is also independently observed. Awaiting an enclosing
        // callback alone cannot replace an absent or faulted original native witness.
        foreach (var command in commands)
        {
            foreach (var original in new Task?[] { command.Exit, command.Output, command.Error })
                if (original is { } actual)
                    try { await actual.ConfigureAwait(false); }
                    catch (Exception error) { AddTask(causes, actual, error); }
            foreach (var error in command.Causes) Add(causes, error);
            if (!command.NoChild && command.Exit?.IsCompletedSuccessfully != true)
                Add(causes, new IOException("Original Access helper exit remains unverified."));
        }
        foreach (var waiter in waiters)
        {
            if (waiter.Task is not { } actual) continue;
            try { await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (actual.IsFaulted) AddTask(causes, actual, error);
                else if (waiter.Causes.Length != 0)
                    foreach (var original in waiter.Causes) Add(causes, original);
                else Add(causes, error);
            }
        }
        lock (_sync)
        {
            _credential = null;
            _waiters.RemoveAll(x => x.Task?.IsCompletedSuccessfully == true);
            if (_capacitySealed) Add(causes, _capacityRefusal);
        }
        try { _stop.Dispose(); } catch (Exception error) { Add(causes, error); }
        if (causes.Count == 0)
            lock (_sync) { _commands.Clear(); _waiters.Clear(); _acquisition = null; _actualPin = null; }
        Throw(causes);
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Add(List<Exception> causes, Exception original)
    { if (!causes.Any(x => ReferenceEquals(x, original))) causes.Add(original); }
    private static void AddTask(List<Exception> causes, Task actual, Exception observed)
    {
        Add(causes, observed);
        if (actual.IsFaulted && actual.Exception is { } compound)
            foreach (var original in compound.InnerExceptions) Add(causes, original);
    }
    private static void Throw(List<Exception> causes)
    {
        if (causes.Count == 1) ExceptionDispatchInfo.Capture(causes[0]).Throw();
        if (causes.Count > 1) throw new AggregateException("Access helper originals and cleanup failed.", causes);
    }
    private sealed class Waiter { internal Task? Task; internal Exception[] Causes = []; }
    private sealed class Command
    {
        internal readonly object NativeSync = new();
        internal readonly TaskCompletionSource CaptureFinished = NewGate();
        internal ICloudflaredOwnedHelperProcess? Child;
        internal bool NoChild = true;
        internal Stream? OutputStream, ErrorStream;
        internal Task<byte[]>? Output, Error;
        internal Task? Exit;
        internal Exception[] Causes = [];
    }
}

// Internal process seam is for meaningful synthetic ownership tests. Production
// always uses the sealed system host below; no wire/provider caller can replace it.
internal interface ICloudflaredOwnedHelperHost
{
    Task<IDisposable> AcquirePinnedExecutableAsync(string absolutePath, CancellationToken token);
    ICloudflaredOwnedHelperProcess Create(string absolutePath, IReadOnlyList<string> arguments);
}
internal interface ICloudflaredOwnedHelperProcess : IDisposable
{
    bool Start();
    Stream OpenStandardOutput();
    Stream OpenStandardError();
    Task WaitForExitAsync();
    int ExitCode { get; }
    bool HasExited { get; }
    void KillOwnedHelper();
}
internal sealed class SystemCloudflaredOwnedHelperHost : ICloudflaredOwnedHelperHost
{
    public async Task<IDisposable> AcquirePinnedExecutableAsync(string path, CancellationToken token)
    {
        FileStream? original = null;
        List<Exception> causes = [];
        try
        {
            // No PATH/cache/environment lookup. Refuse reparse-point inputs.
            for (var node = new FileInfo(path) as FileSystemInfo; node is not null;
                 node = node is FileInfo file ? file.Directory : ((DirectoryInfo)node).Parent)
                if ((node.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The reviewed helper path contains a reparse point.");
            original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (original.Length != CloudflaredNativeCakeAccessCredentialSource.OfficialWindowsBytes)
                throw new IOException("Access helper length does not match the reviewed release.");
            var actual = SHA256.HashDataAsync(original, token).AsTask();
            byte[] hash;
            try { hash = await actual.ConfigureAwait(false); }
            catch
            {
                if (actual.IsFaulted && actual.Exception is { } compound && compound.InnerExceptions.Count > 1)
                    ExceptionDispatchInfo.Capture(compound).Throw();
                throw;
            }
            if (Convert.ToHexStringLower(hash) != CloudflaredNativeCakeAccessCredentialSource.OfficialWindowsSha256)
                throw new IOException("Access helper SHA256 does not match the reviewed release.");
            token.ThrowIfCancellationRequested();
            // Retain the SAME read lease through native settlement. On Windows the
            // share mode refuses file writes/deletion; protected directory custody
            // is still needed for the path-based CreateProcess image boundary.
            var lease = original; original = null; return lease;
        }
        catch (Exception error) { causes.Add(error); }
        finally
        {
            if (original is not null)
                try { original.Dispose(); } catch (Exception error) { causes.Add(error); }
        }
        if (causes.Count == 1) ExceptionDispatchInfo.Capture(causes[0]).Throw();
        throw new AggregateException("Pinned helper acquisition and cleanup failed.", causes);
    }
    public ICloudflaredOwnedHelperProcess Create(string absolutePath, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(absolutePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(absolutePath)!
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new SystemCloudflaredOwnedHelperProcess(new Process { StartInfo = start });
    }
}
internal sealed class SystemCloudflaredOwnedHelperProcess(Process original) : ICloudflaredOwnedHelperProcess
{
    public bool Start() => original.Start();
    public Stream OpenStandardOutput() => original.StandardOutput.BaseStream;
    public Stream OpenStandardError() => original.StandardError.BaseStream;
    public Task WaitForExitAsync() => original.WaitForExitAsync(CancellationToken.None);
    public int ExitCode => original.ExitCode;
    public bool HasExited => original.HasExited;
    public void KillOwnedHelper() => original.Kill(entireProcessTree: false);
    public void Dispose() => original.Dispose();
}
