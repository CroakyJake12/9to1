/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Terminal/TerminalAppSurface.cs.
 * What: Standalone HavenOS Terminal app boundary over the existing terminal-session contracts.
 * Why: A Terminal app must preserve Haven's persistent session and permission behavior without owning or bypassing the host shell implementation.
 */
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Terminal;

public enum TerminalAppAvailability
{
    Available,
    HostCapabilityUnavailable,
    Disposed
}

public enum TerminalAppCommandState
{
    Succeeded,
    Failed,
    Cancelled,
    RequiresApproval,
    Denied,
    Unavailable
}

public sealed record TerminalAppHostCapabilities(
    ITerminalSessionFactory? SessionFactory,
    Func<PermissionMode>? CommandPermission,
    TerminalCommandActivityHub? ActivityHub = null);

public sealed record TerminalAppCommandResult(
    TerminalAppCommandState State,
    string Command,
    string Message,
    TerminalSessionCommandResult? SessionResult = null,
    TerminalAppApproval? Approval = null);

/// <summary>
/// An ephemeral review snapshot. Command is the exact, potentially sensitive input; it must not
/// be written to history or logs. The caller must show this target and command before approval.
/// </summary>
public sealed record TerminalAppApproval(
    Guid Id,
    string Command,
    TerminalSessionMetadata Target,
    int? ProcessId);

/// <summary>
/// Owns one live Terminal app session while delegating all shell execution to the host-provided
/// <see cref="ITerminalSessionFactory"/>. If required host capabilities are absent, the surface
/// remains visible but command execution is unavailable; it never falls back to direct process launch.
/// </summary>
public sealed class TerminalAppSurface : IDisposable
{
    private const string MissingCapabilityMessage = "Terminal is unavailable because the host terminal-session capability is not available.";
    private readonly TerminalAppHostCapabilities _host;
    private readonly string _initialDirectory;
    private readonly List<string> _history = [];
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _pendingGate = new();
    private ITerminalSession? _session;
    private PendingApproval? _pending;
    private bool _disposed;

    public TerminalAppSurface(TerminalAppHostCapabilities host, string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _initialDirectory = ResolveStartDirectory(initialDirectory);

        if (host.SessionFactory is null || host.CommandPermission is null)
        {
            Availability = TerminalAppAvailability.HostCapabilityUnavailable;
            UnavailableReason = MissingCapabilityMessage;
            return;
        }

        if (host.ActivityHub is not null)
            host.ActivityHub.ActivityPublished += OnActivityPublished;

        TryCreateInitialSession();
    }

    public TerminalAppAvailability Availability { get; private set; }
    public string? UnavailableReason { get; private set; }
    public bool IsAvailable => Availability == TerminalAppAvailability.Available && _session is not null;
    public bool HasPendingApproval { get { lock (_pendingGate) return _pending is not null; } }
    public IReadOnlyList<string> History => _history;
    public TerminalSessionMetadata? SessionMetadata => _session?.Metadata;
    public string WorkingDirectory => _session?.Metadata.CurrentWorkingDirectory ?? _initialDirectory;

    public event EventHandler<TerminalSessionOutput>? OutputReceived;
    public event EventHandler<TerminalSessionMetadata>? MetadataChanged;
    public event EventHandler<TerminalCommandActivity>? AgentActivityObserved;
    public event EventHandler? TranscriptClearRequested;

    public async Task<TerminalAppCommandResult> SubmitAsync(string command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var value = command ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return new(TerminalAppCommandState.Failed, string.Empty, "Enter a command to run.");

        var safeCommand = SensitiveTextRedactor.Redact(value, 8_000);
        if (cancellationToken.IsCancellationRequested)
            return new(TerminalAppCommandState.Cancelled, safeCommand, "Command cancelled before submission.");
        if (!_operationGate.Wait(0))
            return new(TerminalAppCommandState.Failed, safeCommand, "Another Terminal operation is in progress.");

        try
        {
            ThrowIfDisposed();
            lock (_pendingGate)
            {
                if (_pending is not null)
                    return new(TerminalAppCommandState.Failed, safeCommand, "Resolve the existing command approval first.");
            }
            _history.Add(safeCommand);

            if (IsTranscriptClearCommand(value))
            {
                TranscriptClearRequested?.Invoke(this, EventArgs.Empty);
                return new(TerminalAppCommandState.Succeeded, safeCommand, "Transcript clear requested.");
            }

            if (!TryGetExecutionHost(out var session, out var permission))
                return Unavailable(safeCommand);

            var policy = TerminalCommandPolicy.Evaluate(permission());
            if (policy.Decision == TerminalPermissionDecision.Denied)
                return new(TerminalAppCommandState.Denied, safeCommand, policy.Reason);

            if (policy.Decision == TerminalPermissionDecision.RequiresApproval)
            {
                var metadata = session.Metadata;
                if (!CanReview(metadata))
                    return new(TerminalAppCommandState.Unavailable, safeCommand, "The shell or its working directory is not ready for review.");

                var approval = new TerminalAppApproval(Guid.NewGuid(), value, metadata, session.ProcessId);
                lock (_pendingGate)
                {
                    _pending = new PendingApproval(session, approval);
                    // Recheck under the event lock so a metadata change during capture is not lost.
                    if (!MatchesTarget(_pending, session.Metadata))
                        _pending.Invalidated = true;
                }
                return new(TerminalAppCommandState.RequiresApproval, safeCommand, policy.Reason, Approval: approval);
            }

            return await ExecuteCoreAsync(session, value, safeCommand, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<TerminalAppCommandResult> ApprovePendingAsync(Guid approvalId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
            return new(TerminalAppCommandState.Cancelled, string.Empty, "Approval cancelled before submission.");
        if (!_operationGate.Wait(0))
            return new(TerminalAppCommandState.Failed, string.Empty, "Another Terminal operation is in progress.");

        try
        {
            ThrowIfDisposed();
            PendingApproval pending;
            lock (_pendingGate)
            {
                if (_pending is null || _pending.Review.Id != approvalId)
                    return new(TerminalAppCommandState.Failed, string.Empty, "This command approval is no longer pending.");
                pending = _pending;
                _pending = null; // Consume before dispatch; repeated activation cannot run it again.
            }

            var safeCommand = SensitiveTextRedactor.Redact(pending.Review.Command, 8_000);
            if (!TryGetExecutionHost(out var session, out var permission))
                return Unavailable(safeCommand);
            if (pending.Invalidated || !ReferenceEquals(session, pending.Session) || !MatchesTarget(pending, session.Metadata))
                return new(TerminalAppCommandState.Denied, safeCommand, "The reviewed shell or target changed. Submit the command for a new review.");

            var currentPermission = permission();
            var policy = TerminalCommandPolicy.Evaluate(currentPermission);
            if (policy.Decision == TerminalPermissionDecision.Denied)
                return new(TerminalAppCommandState.Denied, safeCommand, policy.Reason);
            policy = TerminalCommandPolicy.Evaluate(currentPermission, approvedOnce: true);
            if (policy.Decision != TerminalPermissionDecision.Allowed)
                return new(TerminalAppCommandState.Denied, safeCommand, policy.Reason);

            return await ExecuteCoreAsync(session, pending.Review.Command, safeCommand, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public TerminalAppCommandResult DenyPending(Guid approvalId)
    {
        ThrowIfDisposed();
        lock (_pendingGate)
        {
            if (_pending is null || _pending.Review.Id != approvalId)
                return new(TerminalAppCommandState.Failed, string.Empty, "This command approval is no longer pending.");
            var safeCommand = SensitiveTextRedactor.Redact(_pending.Review.Command, 8_000);
            _pending = null;
            return new(TerminalAppCommandState.Denied, safeCommand, "Command denied by user.");
        }
    }

    public async Task<bool> SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !_operationGate.Wait(0))
            return false;

        try
        {
            ThrowIfDisposed();
            if (!IsAvailable || _session is null)
                return false;
            lock (_pendingGate)
            {
                if (_pending is not null)
                    _pending.Invalidated = true;
            }
            await _session.SetWorkingDirectoryAsync(Path.GetFullPath(path), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public bool NewSession()
    {
        ThrowIfDisposed();
        if (!_operationGate.Wait(0))
            return false;
        try
        {
        ThrowIfDisposed();
        if (_host.SessionFactory is null || _host.CommandPermission is null)
        {
            SetUnavailable(MissingCapabilityMessage);
            return false;
        }

        ITerminalSession replacement;
        try
        {
            replacement = _host.SessionFactory.Create(_initialDirectory, "Terminal");
        }
        catch (Exception)
        {
            // A failed replacement must not take down the session that is still serving commands.
            // The bool result reports the failure while preserving the current availability/state.
            return false;
        }

        var previous = _session;
        Detach(previous);
        _session = replacement;
        Attach(replacement);
        previous?.Dispose();
        lock (_pendingGate) _pending = null;
        _history.Clear();
        Availability = TerminalAppAvailability.Available;
        UnavailableReason = null;
        return true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsAvailable || _session is null)
            return;

        await _session.InterruptAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TerminalAppCommandResult> ExecuteCoreAsync(
        ITerminalSession session,
        string command,
        string safeCommand,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await session.ExecuteAsync(Guid.NewGuid(), command, cancellationToken).ConfigureAwait(false);
            var state = result.Cancelled
                ? TerminalAppCommandState.Cancelled
                : result.ExitCode == 0
                    ? TerminalAppCommandState.Succeeded
                    : TerminalAppCommandState.Failed;
            var exit = result.ExitCode?.ToString() ?? "unknown";
            var message = result.Cancelled
                ? "Command interrupted. The host session may have restarted its shell process."
                : $"Command completed with exit code {exit}.";
            return new(state, safeCommand, message, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(TerminalAppCommandState.Cancelled, safeCommand, "Command interrupted.");
        }
        catch (Exception ex)
        {
            return new(TerminalAppCommandState.Failed, safeCommand, SensitiveTextRedactor.Redact(ex.Message, 2_000));
        }
    }

    private bool TryGetExecutionHost(out ITerminalSession session, out Func<PermissionMode> permission)
    {
        if (IsAvailable && _session is not null && _host.CommandPermission is not null)
        {
            session = _session;
            permission = _host.CommandPermission;
            return true;
        }

        session = null!;
        permission = null!;
        return false;
    }

    private TerminalAppCommandResult Unavailable(string safeCommand) =>
        new(TerminalAppCommandState.Unavailable, safeCommand, UnavailableReason ?? MissingCapabilityMessage);

    private void TryCreateInitialSession()
    {
        try
        {
            _session = _host.SessionFactory!.Create(_initialDirectory, "Terminal");
            Attach(_session);
            Availability = TerminalAppAvailability.Available;
            UnavailableReason = null;
        }
        catch (Exception ex)
        {
            SetUnavailable("Terminal host could not create a shell session: " + SensitiveTextRedactor.Redact(ex.Message, 2_000));
        }
    }

    private void SetUnavailable(string reason)
    {
        Detach(_session);
        _session?.Dispose();
        _session = null;
        lock (_pendingGate) _pending = null;
        Availability = TerminalAppAvailability.HostCapabilityUnavailable;
        UnavailableReason = reason;
    }

    private void Attach(ITerminalSession session)
    {
        session.OutputReceived += OnOutputReceived;
        session.MetadataChanged += OnMetadataChanged;
    }

    private void Detach(ITerminalSession? session)
    {
        if (session is null)
            return;
        session.OutputReceived -= OnOutputReceived;
        session.MetadataChanged -= OnMetadataChanged;
    }

    private void OnOutputReceived(object? sender, TerminalSessionOutput output) => OutputReceived?.Invoke(this, output);
    private void OnMetadataChanged(object? sender, TerminalSessionMetadata metadata)
    {
        lock (_pendingGate)
        {
            if (_pending is not null && (!ReferenceEquals(sender, _pending.Session) || !MatchesTarget(_pending, metadata)))
                _pending.Invalidated = true;
        }
        MetadataChanged?.Invoke(this, metadata);
    }
    private void OnActivityPublished(object? sender, TerminalCommandActivity activity)
    {
        if (activity.Origin == TerminalCommandOrigin.Agent)
            AgentActivityObserved?.Invoke(this, activity);
    }

    private static bool IsTranscriptClearCommand(string command) =>
        command.Equals("clear", StringComparison.OrdinalIgnoreCase) || command.Equals("cls", StringComparison.OrdinalIgnoreCase);

    private static bool CanReview(TerminalSessionMetadata metadata) =>
        metadata.SessionId != Guid.Empty
        && metadata.State == TerminalSessionLifecycleState.Ready
        && !string.IsNullOrWhiteSpace(metadata.ShellRuntime)
        && !string.IsNullOrWhiteSpace(metadata.CurrentWorkingDirectory);

    private static bool MatchesTarget(PendingApproval pending, TerminalSessionMetadata current)
    {
        var reviewed = pending.Review.Target;
        return CanReview(current)
            && current.SessionId == reviewed.SessionId
            && current.EnvironmentId == reviewed.EnvironmentId
            && current.ShellProfileId == reviewed.ShellProfileId
            && current.ShellRuntime == reviewed.ShellRuntime
            && current.CurrentWorkingDirectory == reviewed.CurrentWorkingDirectory
            && current.Generation == reviewed.Generation
            && current.Revision == reviewed.Revision
            && current.IsElevated == reviewed.IsElevated
            && current.Mode == reviewed.Mode
            && pending.Session.ProcessId == pending.Review.ProcessId;
    }

    private sealed class PendingApproval(ITerminalSession session, TerminalAppApproval review)
    {
        public ITerminalSession Session { get; } = session;
        public TerminalAppApproval Review { get; } = review;
        public bool Invalidated { get; set; }
    }

    private static string ResolveStartDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            return Path.GetFullPath(path);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : Environment.CurrentDirectory;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_host.ActivityHub is not null)
            _host.ActivityHub.ActivityPublished -= OnActivityPublished;
        Detach(_session);
        _session?.Dispose();
        _session = null;
        lock (_pendingGate) _pending = null;
        Availability = TerminalAppAvailability.Disposed;
    }
}
