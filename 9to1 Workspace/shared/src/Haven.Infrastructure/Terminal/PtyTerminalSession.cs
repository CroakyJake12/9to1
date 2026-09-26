using System.Text;
using Haven.Application;

namespace Haven.Infrastructure.Terminal;

/// <summary>Failure from a Terminal host capability with a stable machine-readable descriptor.</summary>
public sealed class PtyTerminalException(TerminalFailure failure, Exception? innerException = null)
    : InvalidOperationException(failure.Message, innerException)
{
    public TerminalFailure Failure { get; } = failure;
}

/// <summary>
/// Session factory for one registered execution environment. Environment identity is supplied by
/// the owning host; the factory never derives it from a display name or working-directory path.
/// </summary>
public sealed class PtyTerminalSessionFactory : ITerminalInteractiveSessionFactory
{
    private readonly TerminalEnvironmentDescriptor _environment;
    private readonly IReadOnlyList<TerminalShellProfile> _profiles;
    private readonly IPtyProcessFactory _processFactory;

    public PtyTerminalSessionFactory(
        TerminalEnvironmentDescriptor environment,
        IPtyProcessFactory? processFactory = null,
        IReadOnlyList<TerminalShellProfile>? shellProfiles = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
        _processFactory = processFactory ?? new PlatformPtyProcessFactory();
        _profiles = shellProfiles ?? TerminalShellProfileDiscovery.Discover(environment);
    }

    public IReadOnlyList<TerminalEnvironmentDescriptor> ListEnvironments() => [_environment];

    public IReadOnlyList<TerminalShellProfile> ListShellProfiles(TerminalEnvironmentId? environmentId = null)
    {
        if (environmentId is { } requested && requested != _environment.Id) return [];
        return _profiles;
    }

    public ITerminalSession Create(string initialDirectory, string? displayName = null)
    {
        var profile = _profiles.FirstOrDefault(static candidate => candidate.State == TerminalEnvironmentConnectionState.Ready)
            ?? throw Unavailable("ShellUnavailable", "No installed shell is available for this environment.", "terminal.shells");
        var request = new TerminalSessionStartRequest(_environment.Id, profile.Id, initialDirectory, Title: displayName);
        return CreateAsync(request).GetAwaiter().GetResult();
    }

    public ITerminalInteractiveSession Create(TerminalSessionStartRequest request) =>
        CreateAsync(request).GetAwaiter().GetResult();

    public async Task<ITerminalInteractiveSession> CreateAsync(
        TerminalSessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.EnvironmentId != _environment.Id)
            throw Unavailable("EnvironmentUnavailable", "The selected environment is not owned by this provider.", request.EnvironmentId.ToString());
        if (_environment.State != TerminalEnvironmentConnectionState.Ready)
            throw Unavailable("EnvironmentDisconnected", _environment.UnavailableReason ?? "The selected environment is unavailable.", _environment.Id.ToString(), retryable: true);
        if (_environment.Kind != TerminalEnvironmentKind.LocalHost)
            throw Unavailable("CapabilityUnavailable", $"The {_environment.Kind} provider has not supplied a PTY adapter.", _environment.Id.ToString());
        if ((_environment.Capabilities & TerminalEnvironmentCapability.InteractivePty) == 0)
            throw Unavailable("CapabilityUnavailable", "The selected environment does not advertise interactive PTY support.", _environment.Id.ToString());

        var profile = _profiles.FirstOrDefault(candidate => string.Equals(candidate.Id, request.ShellProfileId, StringComparison.Ordinal));
        if (profile is null || profile.State != TerminalEnvironmentConnectionState.Ready)
            throw Unavailable("ShellUnavailable", profile?.UnavailableReason ?? "The selected shell profile is missing or unavailable.", request.ShellProfileId);
        if (!string.Equals(profile.ProviderId, _environment.ProviderId, StringComparison.Ordinal))
            throw Unavailable("ShellUnavailable", "The selected shell profile belongs to another environment provider.", profile.Id);
        if (!_processFactory.IsSupported)
            throw Unavailable("CapabilityUnavailable", _processFactory.UnavailableReason ?? "No native PTY adapter is available.", _environment.Id.ToString());
        if ((_environment.Capabilities & TerminalEnvironmentCapability.Resize) == 0)
            throw Unavailable("CapabilityUnavailable", "The selected environment does not advertise PTY resize support.", _environment.Id.ToString());

        var directory = Path.GetFullPath(request.WorkingDirectory);
        if (!Directory.Exists(directory))
            throw Unavailable("WorkingDirectoryUnavailable", "The selected working directory does not exist in this environment.", directory);
        if (request.Columns == 0 || request.Rows == 0)
            throw Unavailable("PtyCreationFailed", "Terminal dimensions must be greater than zero.", _environment.Id.ToString());

        IPtyProcess process;
        try
        {
            process = await _processFactory.StartAsync(
                new PtyProcessStartOptions(profile.Executable, profile.Arguments, directory, new PtySize(request.Columns, request.Rows)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Unavailable("PtyCreationFailed", "The interactive shell could not be started.", profile.Id, retryable: true, innerException: ex);
        }

        return new PtyTerminalSession(process, _environment, profile, request.Title, directory);
    }

    private static PtyTerminalException Unavailable(
        string code,
        string message,
        string target,
        bool retryable = false,
        Exception? innerException = null) =>
        new(new TerminalFailure(code, message, target, Recoverable: true, Retryable: retryable), innerException);
}

/// <summary>Shell discovery that reports missing known profiles instead of failing at process spawn.</summary>
public static class TerminalShellProfileDiscovery
{
    private static readonly (string Id, string Name, string[] Candidates, string[] Arguments)[] WindowsProfiles =
    [
        ("powershell-core", "PowerShell", ["pwsh.exe", "pwsh"], ["-NoLogo"]),
        ("windows-powershell", "Windows PowerShell", ["powershell.exe", "powershell"], ["-NoLogo"]),
        ("command-prompt", "Command Prompt", ["cmd.exe", "cmd"], ["/D"]),
        ("bash", "Bash", ["bash.exe", "bash"], ["--login"]),
        ("zsh", "Zsh", ["zsh.exe", "zsh"], ["-l"]),
        ("fish", "Fish", ["fish.exe", "fish"], [])
    ];

    private static readonly (string Id, string Name, string[] Candidates, string[] Arguments)[] UnixProfiles =
    [
        ("bash", "Bash", ["bash", "/bin/bash", "/usr/bin/bash"], ["--login"]),
        ("zsh", "Zsh", ["zsh", "/bin/zsh", "/usr/bin/zsh"], ["-l"]),
        ("fish", "Fish", ["fish", "/usr/bin/fish", "/usr/local/bin/fish"], []),
        ("posix-sh", "POSIX Shell", ["sh", "/bin/sh", "/usr/bin/sh"], ["-l"])
    ];

    public static IReadOnlyList<TerminalShellProfile> Discover(TerminalEnvironmentDescriptor environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Kind != TerminalEnvironmentKind.LocalHost)
            return [];
        var profiles = OperatingSystem.IsWindows() ? WindowsProfiles : UnixProfiles;
        return profiles.Select(profile =>
        {
            var executable = FindExecutable(profile.Candidates);
            return new TerminalShellProfile(
                profile.Id,
                profile.Name,
                executable ?? string.Empty,
                profile.Arguments,
                environment.ProviderId,
                SupportsShellIntegration: false,
                State: executable is null ? TerminalEnvironmentConnectionState.Unavailable : TerminalEnvironmentConnectionState.Ready,
                UnavailableReason: executable is null ? $"{profile.Name} is not installed or is not available on PATH." : null);
        }).ToArray();
    }

    private static string? FindExecutable(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (Path.IsPathRooted(candidate))
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                continue;
            }
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var path = Path.Combine(directory, candidate);
                    if (File.Exists(path)) return Path.GetFullPath(path);
                    if (!OperatingSystem.IsWindows()) continue;
                    foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
                    {
                        path = Path.Combine(directory, candidate + extension);
                        if (File.Exists(path)) return Path.GetFullPath(path);
                    }
                }
                catch (ArgumentException) { }
                catch (IOException) { }
            }
        }
        return null;
    }
}

internal sealed class PtyTerminalSession : ITerminalInteractiveSession
{
    private readonly IPtyProcess _process;
    private readonly TerminalEnvironmentDescriptor _environment;
    private readonly TerminalShellProfile _profile;
    private readonly object _gate = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private TerminalSessionMetadata _metadata;
    private bool _disposed;

    public PtyTerminalSession(
        IPtyProcess process,
        TerminalEnvironmentDescriptor environment,
        TerminalShellProfile profile,
        string? title,
        string initialDirectory)
    {
        _process = process;
        _environment = environment;
        _profile = profile;
        var id = Guid.NewGuid();
        _metadata = new TerminalSessionMetadata(id, profile.DisplayName,
            string.IsNullOrWhiteSpace(title) ? "Terminal" : title.Trim(), initialDirectory, initialDirectory,
            TerminalSessionLifecycleState.Ready, DateTimeOffset.UtcNow, 0, false)
        {
            EnvironmentId = environment.Id,
            ShellProfileId = profile.Id,
            Mode = TerminalSessionMode.Command,
            Revision = 1,
            IsElevated = environment.IsElevated
        };
        _process.OutputReceived += OnProcessOutput;
        _process.StateChanged += OnProcessStateChanged;
        if (_process.State != PtyProcessState.Running) ApplyProcessState(_process.State, "The process ended while the Terminal session was being created.");
    }

    public TerminalSessionMetadata Metadata { get { lock (_gate) return _metadata; } }
    public TerminalEnvironmentDescriptor Environment => _environment;
    public TerminalSessionMode Mode { get { lock (_gate) return _metadata.Mode; } }
    public int? ProcessId => _process.State == PtyProcessState.Running ? _process.ProcessId : null;
    public event EventHandler<TerminalSessionOutput>? OutputReceived;
    public event EventHandler<TerminalSessionMetadata>? MetadataChanged;

    public Task SetModeAsync(TerminalSessionMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        TerminalSessionMetadata changed;
        lock (_gate)
        {
            if (_metadata.Mode == mode) return Task.CompletedTask;
            changed = _metadata = _metadata with { Mode = mode, Revision = _metadata.Revision + 1 };
        }
        MetadataChanged?.Invoke(this, changed);
        return Task.CompletedTask;
    }

    public ValueTask SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Mode != TerminalSessionMode.Command)
            throw Unavailable("CommandRejected", "Raw shell input is disabled while Terminal is in AI Mode.", Metadata.SessionId.ToString("N"));
        return _process.WriteAsync(input, cancellationToken);
    }

    public ValueTask ResizeAsync(ushort columns, ushort rows, CancellationToken cancellationToken = default) =>
        _process.ResizeAsync(new PtySize(columns, rows), cancellationToken);

    public Task SignalAsync(TerminalProcessSignal signal, CancellationToken cancellationToken = default) =>
        _process.SignalAsync(signal switch
        {
            TerminalProcessSignal.Interrupt => PtySignal.Interrupt,
            TerminalProcessSignal.Terminate => PtySignal.Terminate,
            TerminalProcessSignal.Kill => PtySignal.Kill,
            _ => throw new ArgumentOutOfRangeException(nameof(signal))
        }, cancellationToken).AsTask();

    public Task<TerminalSessionCommandResult> ExecuteAsync(Guid commandId, string command, CancellationToken cancellationToken)
    {
        if (commandId == Guid.Empty) throw new ArgumentException("A command ID is required.", nameof(commandId));
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("A command is required.", nameof(command));
        if (Mode != TerminalSessionMode.Command)
            throw Unavailable("CommandRejected", "Raw shell commands cannot execute while Terminal is in AI Mode.", Metadata.SessionId.ToString("N"));

        // ExecuteAsync promises a command-scoped result. A plain PTY cannot identify that command's
        // completion or exit code safely, so callers must use SendInputAsync for interactive input.
        throw Unavailable("CapabilityUnavailable", "This shell profile has no verified command-boundary integration; use interactive input for this PTY session.", _profile.Id);
    }

    public Task InterruptAsync(CancellationToken cancellationToken) => SignalAsync(TerminalProcessSignal.Interrupt, cancellationToken);

    public Task SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        throw Unavailable("CapabilityUnavailable", "This shell profile does not provide verified working-directory integration.", _profile.Id);
    }

    private void OnProcessOutput(object? sender, PtyOutputChunk chunk)
    {
        var characters = new char[Encoding.UTF8.GetMaxCharCount(chunk.Bytes.Length)];
        _decoder.Convert(chunk.Bytes.Span, characters, flush: false, out _, out var charsUsed, out _);
        var output = new TerminalSessionOutput(Metadata.SessionId, null, TerminalOutputStream.StandardOutput,
            new string(characters, 0, charsUsed), DateTimeOffset.UtcNow) { RawBytes = chunk.Bytes.ToArray() };
        var handlers = OutputReceived;
        if (handlers is null) return;
        foreach (EventHandler<TerminalSessionOutput> handler in handlers.GetInvocationList())
        {
            try { handler(this, output); }
            catch { /* A UI subscriber cannot break native PTY output pumping. */ }
        }
    }

    private void OnProcessStateChanged(object? sender, PtyProcessStateChange change) => ApplyProcessState(change.State, change.Detail);

    private void ApplyProcessState(PtyProcessState state, string? detail)
    {
        TerminalSessionMetadata changed;
        lock (_gate)
        {
            var next = state switch
            {
                PtyProcessState.Running => TerminalSessionLifecycleState.Ready,
                PtyProcessState.Exited => TerminalSessionLifecycleState.Ended,
                PtyProcessState.Faulted => TerminalSessionLifecycleState.Faulted,
                PtyProcessState.Disposed => TerminalSessionLifecycleState.Disposed,
                _ => TerminalSessionLifecycleState.Faulted
            };
            if (_metadata.State == next) return;
            changed = _metadata = _metadata with { State = next, Revision = _metadata.Revision + 1 };
        }
        MetadataChanged?.Invoke(this, changed);
        if (!string.IsNullOrWhiteSpace(detail))
            OutputReceived?.Invoke(this, new TerminalSessionOutput(changed.SessionId, null, TerminalOutputStream.System,
                SensitiveTextRedactor.Redact(detail, 2_000), DateTimeOffset.UtcNow));
    }

    private static PtyTerminalException Unavailable(string code, string message, string target) =>
        new(new TerminalFailure(code, message, target, Recoverable: true));

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _process.OutputReceived -= OnProcessOutput;
        _process.StateChanged -= OnProcessStateChanged;
        await _process.DisposeAsync().ConfigureAwait(false);
        ApplyProcessState(PtyProcessState.Disposed, null);
    }
}
