/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WorkspaceToolService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WorkspaceToolService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Text;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents workspace tool service and keeps its related state and behavior together.
/// </summary>
public sealed partial class WorkspaceToolService(IWorkspaceToolFinalFenceAuthority? originalAuthority = null) : IWorkspaceOriginalInvocationSource
{
    /// <summary>
    /// Stores max output characters locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaxOutputCharacters = 1_000_000;
    private readonly IWorkspaceToolFinalFenceAuthority? _originalAuthority = originalAuthority;

    // Preserve the original parameterless CLR constructor as well as source-local calls.
    public WorkspaceToolService() : this(null) { }

    /// <summary>
    /// Performs the resolve workspace path step owned by this component.
    /// </summary>
    public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new ArgumentException("A workspace root is required.", nameof(workspaceRoot));
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A workspace-relative path is required.", nameof(relativePath));

        var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (Path.IsPathRooted(relativePath))
        {
            var absoluteCandidate = Path.GetFullPath(relativePath);
            if (!IsWithinRoot(root, absoluteCandidate, comparison))
                throw new UnauthorizedAccessException("The requested path is outside the selected workspace.");
            return absoluteCandidate;
        }

        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithinRoot(root, candidate, comparison))
            throw new UnauthorizedAccessException("The requested path is outside the selected workspace.");
        return candidate;
    }

    /// <summary>
    /// Performs read text asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(workspaceRoot, relativePath);
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs write text atomic asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken) =>
        WriteTextAtomicCoreAsync(workspaceRoot, relativePath, content, null, cancellationToken);

    private async Task WriteTextAtomicCoreAsync(string workspaceRoot, string relativePath, string content,
        Invocation? invocation, CancellationToken cancellationToken)
    {
        if (invocation is not null)
        {
            await WriteOriginalWindowsAsync(workspaceRoot, relativePath, content, invocation, cancellationToken).ConfigureAwait(false);
            return;
        }
        var path = ResolveWorkspacePath(workspaceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".haven.tmp." + Guid.NewGuid().ToString("N");
        var errors = new List<Exception>();
        Task? originalTemporaryWrite = null;
        try
        {
            originalTemporaryWrite = File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
            await originalTemporaryWrite.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        catch (Exception error) { AddOriginalErrors(errors,
            originalTemporaryWrite?.IsCompleted == true && !originalTemporaryWrite.IsCompletedSuccessfully ? originalTemporaryWrite : null, error); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
        }
        ThrowOriginalErrors(errors, originalTemporaryWrite?.IsCanceled == true);
    }

    /// <summary>
    /// Performs search files asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken)
    {
        var root = ResolveWorkspacePath(workspaceRoot, ".");
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var results = new List<string>();
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path);
                if (relative.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(path).Contains(searchPattern, StringComparison.OrdinalIgnoreCase)) results.Add(relative);
                if (results.Count >= 500) break;
            }
            return results;
        }, cancellationToken);
    }

    /// <summary>
    /// Runs run process async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) =>
        RunProcessCoreAsync(request, null, cancellationToken);

    private async Task<ProcessResult> RunProcessCoreAsync(ProcessRequest request, Invocation? invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request = request with { Environment = request.Environment is null ? null : new Dictionary<string, string>(request.Environment) };
        if (!Directory.Exists(request.WorkingDirectory)) throw new DirectoryNotFoundException(request.WorkingDirectory);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                Arguments = request.Arguments,
                WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = request.DetachGui,
                CreateNoWindow = !request.DetachGui,
                WindowStyle = request.DetachGui ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
                RedirectStandardOutput = !request.DetachGui,
                RedirectStandardError = !request.DetachGui
            },
            EnableRaisingEvents = true
        };
        var started = Stopwatch.GetTimestamp();
        var errors = new List<Exception>();
        Task? originalExit = null;
        Task<string>? originalStdout = null;
        Task<string>? originalStderr = null;
        IAsyncDisposable? originalPin = null;
        Task<IAsyncDisposable?>? originalAcquire = null;
        OriginalEffect? effect = null;
        var admitted = false;
        var launched = false;
        var terminal = false;
        var callerRetirement = false;
        Task? originalJoined = null;
        int? processId = null;
        int? actualExitCode = null;
        ProcessResult? result = null;
        CancellationTokenSource? deadline = null;
        CancellationTokenRegistration originalCallerStop = default;
        CancellationTokenRegistration originalDeadlineStop = default;
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
        if (!request.DetachGui && Path.GetFileName(request.FileName).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        }
        if (request.Environment is not null)
            foreach (var pair in request.Environment) process.StartInfo.Environment[pair.Key] = pair.Value;

            if (invocation is not null)
            {
                effect = invocation.PrepareProcess(request);
                originalAcquire = invocation.Fence.AcquireOriginalCommitPinAsync(cancellationToken).AsTask();
                originalPin = await originalAcquire.ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The actual original task commit pin is unavailable.");
                cancellationToken.ThrowIfCancellationRequested();
                invocation.DemandIssued();
                var digest = WorkspaceToolOriginalDigest.Process(request);
                invocation.Fence.DemandOriginalEffect(invocation.Root, WorkspaceToolEffectKind.ProcessStart, request.WorkingDirectory, digest);
                launched = invocation.Fence.RunOriginalEffect(invocation.Root, WorkspaceToolEffectKind.ProcessStart,
                    request.WorkingDirectory, digest, () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        admitted = true;
                        return process.Start();
                    });
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                admitted = true;
                launched = process.Start();
            }
            if (!launched) throw new InvalidOperationException($"Could not start {request.FileName}.");
            processId = process.Id;
            if (!request.DetachGui)
            {
                originalExit = process.WaitForExitAsync(CancellationToken.None);
                originalStdout = ReadLimitedAsync(process.StandardOutput, CancellationToken.None);
                originalStderr = ReadLimitedAsync(process.StandardError, CancellationToken.None);
                originalJoined = Task.WhenAll(originalExit, originalStdout, originalStderr);
            }
            // The original attempt pin protects the finite native start, never the external
            // command's lifetime. Joining child tasks cannot hold the retirement/permission gate.
            if (originalPin is not null)
            {
                var releasedPin = originalPin;
                originalPin = null;
                var originalRelease = releasedPin.DisposeAsync().AsTask();
                await JoinOriginalAsync(originalRelease, errors).ConfigureAwait(false);
            }
            if (request.DetachGui)
                result = new ProcessResult(0, string.Empty, string.Empty, Stopwatch.GetElapsedTime(started), false);
            else
            {
                // These are the SAME uncanceled physical exit and stream originals. A caller
                // stop never substitutes a canceled wait or an empty fabricated stream result.
                if (errors.Count != 0)
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
                deadline = new CancellationTokenSource();
                originalCallerStop = cancellationToken.Register(() => stop.TrySetResult(false));
                originalDeadlineStop = deadline.Token.Register(() => stop.TrySetResult(true));
                deadline.CancelAfter(request.Timeout);
                var first = await Task.WhenAny(originalJoined!, stop.Task).ConfigureAwait(false);
                if (ReferenceEquals(first, stop.Task))
                {
                    // Classification is the actual first retained source callback, not a
                    // cancellation flag read after child cleanup or a later owner close.
                    var timedOut = await stop.Task.ConfigureAwait(false);
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    await JoinOriginalAsync(originalExit!, errors).ConfigureAwait(false);
                    await JoinOriginalAsync(originalStdout!, errors).ConfigureAwait(false);
                    await JoinOriginalAsync(originalStderr!, errors).ConfigureAwait(false);
                    if (!timedOut)
                    {
                        callerRetirement = true;
                        errors.Add(new OperationCanceledException("The original caller requested process retirement.", cancellationToken));
                    }
                    else if (errors.Count == 0)
                        result = new ProcessResult(-1, originalStdout!.Result, originalStderr!.Result, Stopwatch.GetElapsedTime(started), true);
                }
                else
                {
                    await JoinOriginalAsync(originalExit!, errors).ConfigureAwait(false);
                    await JoinOriginalAsync(originalStdout!, errors).ConfigureAwait(false);
                    await JoinOriginalAsync(originalStderr!, errors).ConfigureAwait(false);
                    if (errors.Count == 0)
                        result = new ProcessResult(process.ExitCode, originalStdout!.Result, originalStderr!.Result, Stopwatch.GetElapsedTime(started), false);
                }
                if (originalExit!.IsCompletedSuccessfully)
                {
                    actualExitCode = process.ExitCode;
                    terminal = originalStdout!.IsCompletedSuccessfully && originalStderr!.IsCompletedSuccessfully;
                }
            }
        }
        catch (Exception error) { AddOriginalErrors(errors,
            originalAcquire?.IsCompleted == true && !originalAcquire.IsCompletedSuccessfully ? originalAcquire : null, error); }
        finally
        {
            // Any failure after acquiring a child still owns its natural terminal state and
            // original pipes. Retire only this owned child; preserve kill/stream/exit causes.
            if (launched && !request.DetachGui && (originalExit is null || originalStdout is null || originalStderr is null || errors.Count != 0 && result is null))
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                if (originalExit is null)
                    try { originalExit = process.WaitForExitAsync(CancellationToken.None); }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            if (originalExit is not null) await JoinOriginalAsync(originalExit!, errors).ConfigureAwait(false);
            if (originalStdout is not null) await JoinOriginalAsync(originalStdout!, errors).ConfigureAwait(false);
            if (originalStderr is not null) await JoinOriginalAsync(originalStderr!, errors).ConfigureAwait(false);
            if (originalJoined is not null) await JoinOriginalAsync(originalJoined, errors).ConfigureAwait(false);
            if (originalExit?.IsCompletedSuccessfully == true)
            {
                try { actualExitCode = process.ExitCode; }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                terminal = originalStdout?.IsCompletedSuccessfully == true && originalStderr?.IsCompletedSuccessfully == true;
            }
            if (effect is not null) invocation!.FinishEffect(effect, admitted, launched, terminal, processId, actualExitCode,
                eligible: result is { ExitCode: 0, TimedOut: false } && !request.DetachGui && errors.Count == 0);
            Task? originalDispose = null;
            try
            {
                if (originalPin is not null)
                {
                    originalDispose = originalPin.DisposeAsync().AsTask();
                    await originalDispose.ConfigureAwait(false);
                }
            }
            catch (Exception error) { AddOriginalErrors(errors, originalDispose, error); }
            try { originalCallerStop.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { originalDeadlineStop.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { deadline?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { process.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
        }
        ThrowOriginalErrors(errors, callerRetirement || originalAcquire?.IsCanceled == true);
        return result ?? throw new InvalidOperationException("The original process produced no confirmed result.");
    }

    /// <summary>
    /// Reports whether within root applies to the current state.
    /// </summary>
    private static bool IsWithinRoot(string root, string candidate, StringComparison comparison)
    {
        if (candidate.Equals(root, comparison)) return true;
        var prefix = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// Performs read limited asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            var remaining = MaxOutputCharacters - builder.Length;
            if (remaining > 0) builder.Append(buffer, 0, Math.Min(read, remaining));
            if (read > remaining) truncated = true;
        }
        if (truncated) builder.AppendLine("\n[output truncated]");
        return builder.ToString();
    }

    /// <summary>
    /// Performs safe result asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task JoinOriginalAsync(Task original, List<Exception> errors)
    {
        try { await original.ConfigureAwait(false); }
        catch (Exception error) { AddOriginalErrors(errors, original, error); }
    }

    /// <summary>
    /// Attempts to kill and reports the result without using failure for normal control flow.
    /// </summary>
    private static void AddOriginalErrors(List<Exception> errors, Task? original, Exception observed)
    {
        if (original?.Exception is { } compound)
            foreach (var cause in compound.InnerExceptions) errors.Add(cause);
        else errors.Add(observed);
    }

    private static void ThrowOriginalErrors(List<Exception> errors, bool knownCancellation = false)
    {
        var unique = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (unique.Length == 1 && (unique[0] is not OperationCanceledException || knownCancellation))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(unique[0]).Throw();
        if (unique.Length != 0) throw new AggregateException("The original workspace effect and cleanup failed.", unique);
    }
}
