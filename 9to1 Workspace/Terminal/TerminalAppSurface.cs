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
    TerminalCommandActivityHub? ActivityHub = null,
    ITerminalAdviceService? Advice = null,
    ITerminalActionBroker? NaturalLanguageActions = null);

public sealed record TerminalPendingCommandReview(Guid Id, string Preview);

public sealed record TerminalAppCommandResult(
    TerminalAppCommandState State,
    string Command,
    string Message,
    TerminalSessionCommandResult? SessionResult = null);

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
    private ITerminalSession? _session;
    private string? _pendingCommand;
    private Guid _pendingCommandId;
    private Guid _pendingSessionId;
    private string? _pendingDirectory;
    private long _pendingSessionRevision;
    private long _pendingResolutionGeneration;
    private bool _disposed;
    private readonly object _resolutionGate = new();
    private long _resolutionGeneration;
    private TerminalResolvedAction? _resolvedAction;
    private string? _resolvedDirectory;

    public TerminalAppSurface(TerminalAppHostCapabilities host, string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _initialDirectory = ResolveStartDirectory(initialDirectory);

        if (host.SessionFactory is null || host.CommandPermission is null)
        {
            InvalidateResolution();
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
    public TerminalInputMode Mode { get; private set; } = TerminalInputMode.Command;
    public string ModeLabel => Mode == TerminalInputMode.Command ? "Command" : "AI";
    public TerminalResolvedAction? ResolvedAction { get { lock(_resolutionGate) return _resolvedAction; } }
    public void SetMode(TerminalInputMode mode)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        lock(_resolutionGate){Mode = mode; InvalidateResolution();} _pendingCommand = null;
    }
    public async Task<TerminalAppCommandResult> ExecuteResolvedActionAsync(string actionID,string? verificationReference,CancellationToken ct=default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        TerminalResolvedAction? action;
        string? directory;
        long generation;
        lock(_resolutionGate)
        {
            action=_resolvedAction;
            if(action is null || action.Id.ToString("D")!=actionID || _host.NaturalLanguageActions is null || Mode!=TerminalInputMode.AI ||
                _session is null || _session.Metadata.SessionId!=action.SessionId || _session.Metadata.EnvironmentId!=action.EnvironmentId || WorkingDirectory!=_resolvedDirectory)
                return new(TerminalAppCommandState.Unavailable,"","Resolved action is unavailable for the current session context.");
            directory = _resolvedDirectory;
            InvalidateResolution();
            generation = _resolutionGeneration;
        }
        var result=await _host.NaturalLanguageActions.ExecuteAsync(action,verificationReference,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!result.Executed && result.State == "PermissionRequired")
        {
            lock (_resolutionGate)
            {
                if (!_disposed && generation == _resolutionGeneration && Mode == TerminalInputMode.AI &&
                    _session?.Metadata.SessionId == action.SessionId && _session.Metadata.EnvironmentId == action.EnvironmentId && WorkingDirectory == directory)
                {
                    _resolvedAction = action;
                    _resolvedDirectory = directory;
                    return new(TerminalAppCommandState.RequiresApproval, "", result.ResultText ?? result.Message);
                }
            }
            return new(TerminalAppCommandState.Unavailable, "", "The session context changed while approval was requested.");
        }
        return new(result.Executed?TerminalAppCommandState.Succeeded:TerminalAppCommandState.Denied,"",result.ResultText??result.Message);
    }
    private TerminalAdviceContext AdviceContext()
    {
        var metadata=_session?.Metadata??throw new InvalidOperationException("SessionUnavailable");
        var environment=metadata.EnvironmentId??throw new InvalidOperationException("EnvironmentUnavailable");
        return new(metadata.SessionId,environment,Mode==TerminalInputMode.AI?TerminalSessionMode.Ai:TerminalSessionMode.Command,
            metadata.ShellProfileId??metadata.ShellRuntime,WorkingDirectory,_history.LastOrDefault(),null,string.Empty);
    }
    public bool HasPendingApproval => _pendingCommand is not null;
    public Guid? PendingCommandId { get { lock (_resolutionGate) return _pendingCommand is null ? null : _pendingCommandId; } }
    public string? PendingCommandPreview { get { lock (_resolutionGate) return _pendingCommand is null ? null : SensitiveTextRedactor.Redact(_pendingCommand, 8_000); } }
    public TerminalPendingCommandReview? PendingCommandReview { get { lock (_resolutionGate) return _pendingCommand is null ? null : new(_pendingCommandId, SensitiveTextRedactor.Redact(_pendingCommand, 8_000)); } }
    public IReadOnlyList<string> History => _history;
    public TerminalSessionMetadata? SessionMetadata => _session?.Metadata;
    /// <summary>The actual owned session for trusted in-process host composition; never a restored descriptor or replacement process.</summary>
    public ITerminalInteractiveSession? InteractiveSession => _disposed ? null : _session as ITerminalInteractiveSession;
    public string WorkingDirectory => _session?.Metadata.CurrentWorkingDirectory ?? _initialDirectory;

    public event EventHandler<TerminalSessionOutput>? OutputReceived;
    public event EventHandler<TerminalSessionMetadata>? MetadataChanged;
    public event EventHandler<TerminalCommandActivity>? AgentActivityObserved;
    public event EventHandler? TranscriptClearRequested;

    /// <summary>Routes actual viewport input through the same current command policy.
    /// A viewport must retain its attached session ID; it cannot retarget a replacement session.</summary>
    public async ValueTask SendInteractiveInputAsync(Guid expectedSessionId, ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ITerminalSession captured;
        long generation;
        lock (_resolutionGate)
        {
            if (!TryGetExecutionHost(out captured, out _))
                throw new UnauthorizedAccessException("The Terminal session is unavailable.");
            generation = _resolutionGeneration;
        }
        await RequireSessionAdmissionAsync(captured, cancellationToken).ConfigureAwait(false);
        ValueTask dispatched;
        lock (_resolutionGate)
        {
            if (_disposed || generation != _resolutionGeneration || !ReferenceEquals(_session, captured) ||
                Mode != TerminalInputMode.Command || !TryGetExecutionHost(out var session, out var permission) ||
                session is not ITerminalInteractiveSession interactive || session.Metadata.SessionId != expectedSessionId ||
                TerminalCommandPolicy.Evaluate(permission()).Decision != TerminalPermissionDecision.Allowed)
                throw new UnauthorizedAccessException("Interactive input is unavailable for the current session, mode, or command permission.");
            _pendingCommand = null;
            dispatched = interactive.SendInputAsync(input, cancellationToken);
        }
        await dispatched.ConfigureAwait(false);
    }

    private ValueTask RequireSessionAdmissionAsync(ITerminalSession session, CancellationToken ct) =>
        _host.SessionFactory is ITerminalSessionAdmission admission
            ? admission.RequireSessionAsync(session.Metadata, ct) : ValueTask.CompletedTask;

    public async Task<TerminalAppCommandResult> SubmitAsync(string command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var value = command ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return new(TerminalAppCommandState.Failed, string.Empty, "Enter a command to run.");

        InvalidateResolution();
        var safeCommand = SensitiveTextRedactor.Redact(value, 8_000);
        if (Mode == TerminalInputMode.Command && (value == "$Ask" || value.StartsWith("$Ask ",StringComparison.Ordinal)))
        {
            if (_host.Advice is null || _session?.Metadata.EnvironmentId is null) return new(TerminalAppCommandState.Unavailable,safeCommand,"AI advice is unavailable.");
            var answer=await _host.Advice.AskAsync(AdviceContext(),value.Length>4 ? value[4..].Trim() : "",cancellationToken).ConfigureAwait(false);
            _history.Add(safeCommand);
            return new(TerminalAppCommandState.Succeeded,safeCommand,answer.Explanation);
        }
        if (Mode == TerminalInputMode.AI)
        {
            if (_host.NaturalLanguageActions is null || _session?.Metadata.EnvironmentId is null) return new(TerminalAppCommandState.Unavailable,safeCommand,"Natural-language action resolution is unavailable.");
            ITerminalSession capturedSession;long generation;string directory;TerminalSessionMetadata metadata;
            lock(_resolutionGate){capturedSession=_session!;metadata=capturedSession.Metadata;generation=_resolutionGeneration;directory=WorkingDirectory;}
            var resolved=await _host.NaturalLanguageActions.ResolveAsync(metadata.SessionId,metadata.EnvironmentId!.Value,value,cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock(_resolutionGate)
            {
                if(_disposed || generation!=_resolutionGeneration || !ReferenceEquals(capturedSession,_session) || Mode!=TerminalInputMode.AI || WorkingDirectory!=directory ||
                    resolved.SessionId!=metadata.SessionId || resolved.EnvironmentId!=metadata.EnvironmentId || resolved.Id==Guid.Empty || resolved.AffectedObjects is null)
                    return new(TerminalAppCommandState.Cancelled,safeCommand,"Session context changed or the resolved action target did not match. Resolve the request again.");
                _resolvedAction=resolved with{AffectedObjects=Array.AsReadOnly(resolved.AffectedObjects.ToArray())};_resolvedDirectory=directory;
                _history.Add(safeCommand);
                return new(TerminalAppCommandState.RequiresApproval,safeCommand,resolved.Summary);
            }
        }
        _history.Add(safeCommand);

        if (IsTranscriptClearCommand(value))
        {
            _pendingCommand = null;
            TranscriptClearRequested?.Invoke(this, EventArgs.Empty);
            return new(TerminalAppCommandState.Succeeded, safeCommand, "Transcript clear requested.");
        }

        if (!TryGetExecutionHost(out var session, out var permission))
            return Unavailable(safeCommand);

        var policy = TerminalCommandPolicy.Evaluate(permission());
        if (policy.Decision == TerminalPermissionDecision.Denied)
        {
            _pendingCommand = null;
            return new(TerminalAppCommandState.Denied, safeCommand, policy.Reason);
        }

        if (policy.Decision == TerminalPermissionDecision.RequiresApproval)
        {
            lock (_resolutionGate)
            {
                if (!ReferenceEquals(_session, session) || Mode != TerminalInputMode.Command)
                    return new(TerminalAppCommandState.Cancelled, safeCommand, "The command session changed before review.");
                _pendingCommand = value;
                _pendingCommandId = Guid.NewGuid();
                _pendingSessionId = session.Metadata.SessionId;
                _pendingSessionRevision = session.Metadata.Revision;
                _pendingDirectory = WorkingDirectory;
                _pendingResolutionGeneration = _resolutionGeneration;
            }
            return new(TerminalAppCommandState.RequiresApproval, safeCommand, policy.Reason);
        }

        _pendingCommand = null;
        return await ExecuteCoreAsync(session, value, safeCommand, cancellationToken).ConfigureAwait(false);
    }

    public Task<TerminalAppCommandResult> ApprovePendingAsync(CancellationToken cancellationToken = default) =>
        ApprovePendingAsync(PendingCommandId ?? Guid.Empty, cancellationToken);

    public Task<TerminalAppCommandResult> ApprovePendingAsync(Guid expectedCommandId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_resolutionGate)
        {
            if (_pendingCommand is null)
                return Task.FromResult(new TerminalAppCommandResult(TerminalAppCommandState.Failed, "", "There is no command awaiting approval."));
            if (_pendingCommandId != expectedCommandId)
                return Task.FromResult(new TerminalAppCommandResult(TerminalAppCommandState.Denied, "", "The displayed command review changed."));
            var command = _pendingCommand;
            var safeCommand = SensitiveTextRedactor.Redact(command, 8_000);
            _pendingCommand = null;
            if (!TryGetExecutionHost(out var session, out var permission)) return Task.FromResult(Unavailable(safeCommand));
            if (Mode != TerminalInputMode.Command || session.Metadata.SessionId != _pendingSessionId ||
                session.Metadata.Revision != _pendingSessionRevision || WorkingDirectory != _pendingDirectory ||
                _resolutionGeneration != _pendingResolutionGeneration)
                return Task.FromResult(new TerminalAppCommandResult(TerminalAppCommandState.Denied, safeCommand, "The command session context changed. Submit it again."));
            var policy = TerminalCommandPolicy.Evaluate(permission(), approvedOnce: true);
            if (policy.Decision != TerminalPermissionDecision.Allowed)
                return Task.FromResult(new TerminalAppCommandResult(TerminalAppCommandState.Denied, safeCommand, policy.Reason));
            return ExecuteCoreAsync(session, command, safeCommand, cancellationToken, approvedOnce: true);
        }
    }

    public TerminalAppCommandResult DenyPending() => DenyPending(PendingCommandId ?? Guid.Empty);

    public TerminalAppCommandResult DenyPending(Guid expectedCommandId)
    {
        ThrowIfDisposed();
        lock (_resolutionGate)
        {
            if (_pendingCommand is null)
                return new(TerminalAppCommandState.Failed, string.Empty, "There is no command awaiting approval.");
            if (_pendingCommandId != expectedCommandId)
                return new(TerminalAppCommandState.Denied, string.Empty, "The displayed command review changed.");
            var safeCommand = SensitiveTextRedactor.Redact(_pendingCommand, 8_000);
            _pendingCommand = null;
            return new(TerminalAppCommandState.Denied, safeCommand, "Command denied by user.");
        }
    }

    public async Task<bool> SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsAvailable || _session is null || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            var session = _session;
            var fullPath = Path.GetFullPath(path);
            await RequireSessionAdmissionAsync(session, cancellationToken).ConfigureAwait(false);
            Task dispatched;
            lock (_resolutionGate)
            {
                if (_disposed || !ReferenceEquals(_session, session)) return false;
                InvalidateResolution();
                dispatched = session.SetWorkingDirectoryAsync(fullPath, cancellationToken);
            }
            await dispatched.ConfigureAwait(false);
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
    }

    public bool NewSession()
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

        ITerminalSession? previous;
        lock (_resolutionGate)
        {
            if (_disposed) { replacement.Dispose(); return false; }
            InvalidateResolution();
            previous = _session;
            Detach(previous);
            _session = replacement;
            Attach(replacement);
            _pendingCommand = null;
            _history.Clear();
            Availability = TerminalAppAvailability.Available;
            UnavailableReason = null;
        }
        try { previous?.Dispose(); }
        finally { MetadataChanged?.Invoke(this, replacement.Metadata); }
        return true;
    }

    public async Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsAvailable || _session is null)
            return;

        var session = _session;
        await RequireSessionAdmissionAsync(session, cancellationToken).ConfigureAwait(false);
        Task dispatched;
        lock (_resolutionGate)
        {
            if (_disposed || !ReferenceEquals(_session, session))
                throw new UnauthorizedAccessException("The Terminal session changed before interruption.");
            dispatched = session.InterruptAsync(cancellationToken);
        }
        await dispatched.ConfigureAwait(false);
    }

    private async Task<TerminalAppCommandResult> ExecuteCoreAsync(
        ITerminalSession session,
        string command,
        string safeCommand,
        CancellationToken cancellationToken,
        bool approvedOnce = false)
    {
        long generation;
        TerminalSessionMetadata metadata;
        lock (_resolutionGate) { generation = _resolutionGeneration; metadata = session.Metadata; }
        try
        {
            await RequireSessionAdmissionAsync(session, cancellationToken).ConfigureAwait(false);
            Task<TerminalSessionCommandResult> dispatched;
            lock (_resolutionGate)
            {
                if (_disposed || generation != _resolutionGeneration || !ReferenceEquals(_session, session) ||
                    session.Metadata.Revision != metadata.Revision || Mode != TerminalInputMode.Command ||
                    _host.CommandPermission is null ||
                    TerminalCommandPolicy.Evaluate(_host.CommandPermission(), approvedOnce).Decision != TerminalPermissionDecision.Allowed)
                    return new(TerminalAppCommandState.Denied, safeCommand, "The command authority or session context changed before dispatch.");
                dispatched = session.ExecuteAsync(Guid.NewGuid(), command, cancellationToken);
            }
            var result = await dispatched.ConfigureAwait(false);
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
        catch (UnauthorizedAccessException ex)
        {
            return new(TerminalAppCommandState.Denied, safeCommand, SensitiveTextRedactor.Redact(ex.Message, 2_000));
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
        _pendingCommand = null;
        InvalidateResolution();
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
    private void OnMetadataChanged(object? sender, TerminalSessionMetadata metadata) => MetadataChanged?.Invoke(this, metadata);
    private void OnActivityPublished(object? sender, TerminalCommandActivity activity)
    {
        if (activity.Origin == TerminalCommandOrigin.Agent)
            AgentActivityObserved?.Invoke(this, activity);
    }

    private static bool IsTranscriptClearCommand(string command) =>
        command.Equals("clear", StringComparison.OrdinalIgnoreCase) || command.Equals("cls", StringComparison.OrdinalIgnoreCase);

    private static string ResolveStartDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            return Path.GetFullPath(path);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : Environment.CurrentDirectory;
    }

    private void InvalidateResolution()
    {
        lock(_resolutionGate){_resolutionGeneration++;_resolvedAction=null;_resolvedDirectory=null;_pendingCommand=null;}
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        ITerminalSession? previous;
        lock (_resolutionGate)
        {
            if (_disposed) return;
            _disposed = true;
            InvalidateResolution();
            if (_host.ActivityHub is not null)
                _host.ActivityHub.ActivityPublished -= OnActivityPublished;
            previous = _session;
            Detach(previous);
            _session = null;
            _pendingCommand = null;
            Availability = TerminalAppAvailability.Disposed;
        }
        previous?.Dispose();
    }
}
