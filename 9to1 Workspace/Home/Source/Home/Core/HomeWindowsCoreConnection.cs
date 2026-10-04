using System.IO.Pipes;
using System.Threading.Channels;

namespace HavenOS.Home.Core;

/// <summary>One physical accepted Windows pipe. The platform retains the pipe and Home lease;
/// this handle owns only its original serve/session/reader/publication tasks and cancellation.
/// Core.Read/1 grants no consequential package/resource operation.</summary>
public sealed class HomeWindowsCoreConnection : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly HomeNativeCoreApiSessions _sessions;
    private readonly HomeNativeSessionLease _lease;
    private readonly CancellationToken _originalLifetime;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _sync = new();
    private readonly Task _serve;
    private Task? _close;
    private bool _closing;
    private bool _originalEof;

    private HomeWindowsCoreConnection(NamedPipeServerStream pipe, HomeNativeCoreApiSessions sessions,
        HomeNativeSessionLease lease, CancellationToken originalLifetime)
    {
        _pipe = pipe; _sessions = sessions; _lease = lease; _originalLifetime = originalLifetime;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalLifetime);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _serve = ServeOriginalAsync(start.Task);
        start.SetResult(); // THIS handle retains the SAME serve task before the first callback/read/admission.
    }

    /// <summary>The trusted accepting owner supplies its connected server pipe and already-held canonical Home lease.</summary>
    public static HomeWindowsCoreConnection AttachAccepted(NamedPipeServerStream originalAcceptedPipe,
        HomeNativeCoreApiSessions sessions, HomeNativeSessionLease originalHeldLease,
        CancellationToken originalConnectionLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalAcceptedPipe);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(originalHeldLease);
        originalConnectionLifetime.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !originalAcceptedPipe.IsConnected ||
            !originalConnectionLifetime.CanBeCanceled || !originalHeldLease.IsHeld)
            throw new UnauthorizedAccessException("Original connected Windows pipe, live lifetime and held Home lease are required.");
        return new(originalAcceptedPipe, sessions, originalHeldLease, originalConnectionLifetime);
    }

    public Task OriginalServeTask => _serve;

    private async Task ServeOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        HomeNativeCoreApiSessions.Session? session = null;
        Task<HomeNativeCoreApiSessions.Session?>? originalAdmission = null;
        Task? originalReader = null;
        Task? originalPublication = null;
        CancellationTokenSource? firstDeadline = null;
        CancellationTokenSource? firstActive = null;
        List<Exception> failures = [];
        try
        {
            firstDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            firstActive = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, firstDeadline.Token);
            var initial = HomeUnixCoreProtocol.ReadRequest(
                await HomeUnixDiscoveryTransport.ReadFrameAsync(_pipe, firstActive.Token).ConfigureAwait(false));
            originalAdmission = _sessions.AcceptWindowsPipeAsync(_pipe, _lease,
                _lifetime.Token, _lifetime.Token).AsTask();
            session = await originalAdmission.ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Original installed Windows Home Core session is unavailable.");
            var frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
            {
                SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
            });
            originalReader = ReadOriginalAsync(frames.Writer);
            HomeUnixCoreRequest? request = initial;
            HomeNativeFilesFrame? filesRequest = null;
            while (true)
            {
                if (filesRequest is { } originalFilesRequest)
                {
                    var originalFilesReply = await session.InvokeOriginalFilesAsync(originalFilesRequest.Request,
                        _lifetime.Token).ConfigureAwait(false);
                    var payload = HomeNativeFilesProtocol.Payload(originalFilesRequest, originalFilesReply);
                    originalPublication = PublishOriginalFilesAsync(_pipe, session, originalFilesReply, payload, _lifetime.Token);
                }
                else
                {
                    var payload = await DispatchOriginalAsync(session, request!, _lifetime.Token).ConfigureAwait(false);
                    originalPublication = HomeUnixCoreTransport.PublishOriginalAsync(_pipe, session, payload, _lifetime.Token);
                }
                await originalPublication.ConfigureAwait(false);
                if (!await frames.Reader.WaitToReadAsync(_lifetime.Token).ConfigureAwait(false)) break;
                var nextFrame = await frames.Reader.ReadAsync(_lifetime.Token).ConfigureAwait(false);
                if (HomeNativeFilesProtocol.IsFilesFrame(nextFrame))
                { filesRequest = HomeNativeFilesProtocol.ReadRequest(nextFrame); request = null; }
                else
                { request = HomeUnixCoreProtocol.ReadRequest(nextFrame); filesRequest = null; }
            }
        }
        // The actual first-frame deadline remains a timeout even if an owner close arrives
        // while its continuation is held; late cancellation flags do not prove first cause.
        catch (OperationCanceledException error) when (firstDeadline is { IsCancellationRequested: true } &&
            firstActive is not null && error.CancellationToken == firstActive.Token)
        { HomeUnixCoreTransport.Add(failures, new TimeoutException("Windows Home first-frame deadline exceeded.", error)); }
        catch (OperationCanceledException error) when ((Volatile.Read(ref _closing) || Volatile.Read(ref _originalEof)) && !_originalLifetime.IsCancellationRequested &&
            _lifetime.IsCancellationRequested && (error.CancellationToken == _lifetime.Token ||
                firstDeadline is { IsCancellationRequested: false } && firstActive is not null && error.CancellationToken == firstActive.Token))
        { /* Only this handle's exact transport-token cancellation is retired by its own close. */ }
        catch (OperationCanceledException error) when (_originalLifetime.IsCancellationRequested &&
            _lifetime.IsCancellationRequested && (error.CancellationToken == _lifetime.Token ||
                firstDeadline is { IsCancellationRequested: false } && firstActive is not null && error.CancellationToken == firstActive.Token))
        { HomeUnixCoreTransport.Add(failures, new OperationCanceledException("Original Windows Home connection ended.", error, _originalLifetime)); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        finally
        {
            try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            Task? sessionDrain = null;
            try { if (session is not null) sessionDrain = session.DisposeAsync().AsTask(); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { if (originalAdmission is not null) await originalAdmission.ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { if (originalReader is not null) await originalReader.ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { if (sessionDrain is not null) await sessionDrain.ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { if (originalPublication is not null) await originalPublication.ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { firstActive?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { firstDeadline?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            // No borrowed pipe/lease/provider disposal. Its owner closes physical resources after THIS drain.
        }
        HomeUnixCoreTransport.Throw(failures);
    }

    private static async Task<byte[]> DispatchOriginalAsync(HomeNativeCoreApiSessions.Session session,
        HomeUnixCoreRequest request, CancellationToken token) => request.Operation switch
    {
        "GetState" => HomeUnixCoreProtocol.Payload(request, await session.GetStateAsync(token).ConfigureAwait(false)),
        "GetServices" => HomeUnixCoreProtocol.Payload(request, await session.GetServicesAsync(token).ConfigureAwait(false)),
        "GetService" => HomeUnixCoreProtocol.Payload(request, await session.GetServiceAsync(request.ServiceId!, token).ConfigureAwait(false)),
        "GetCompatibility" => HomeUnixCoreProtocol.Payload(request, await session.GetCompatibilityAsync(request.Compatibility!, token).ConfigureAwait(false)),
        _ => throw new InvalidDataException("Unsupported Windows Home Core operation.")
    };

    private static async Task PublishOriginalFilesAsync(NamedPipeServerStream pipe,
        HomeNativeCoreApiSessions.Session session, HomeNativeFilesReply originalReply,
        byte[] originalPayload, CancellationToken token)
    {
        await session.DemandOriginalFilesReplyCurrentAsync(originalReply, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await HomeUnixDiscoveryTransport.WriteFrameAsync(pipe, originalPayload, token).ConfigureAwait(false);
    }

    private async Task ReadOriginalAsync(ChannelWriter<byte[]> frames)
    {
        Exception? primary = null;
        try
        {
            while (true)
            {
                var payload = await HomeUnixCoreTransport.ReadAfterFirstFrameAsync(_pipe, _lifetime.Token).ConfigureAwait(false);
                if (payload is null) { Volatile.Write(ref _originalEof, true); return; }
                if (!frames.TryWrite(payload)) throw new InvalidDataException("Windows Home pipeline exceeds one retained frame.");
            }
        }
        catch (OperationCanceledException error) when (_lifetime.IsCancellationRequested && error.CancellationToken == _lifetime.Token)
        { /* Exact reader-owned cancellation; foreign/API cancellation remains an original failure. */ }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            frames.TryComplete(primary);
            try { _lifetime.Cancel(); }
            catch (Exception cleanup) when (primary is not null && !ReferenceEquals(primary, cleanup))
            { throw new AggregateException("Original Windows frame reader and cancellation failed.", primary, cleanup); }
        }
    }

    public Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseOriginalAsync(start.Task);
            start.SetResult(); // Publish the SAME close before cancellation callbacks.
            return _close;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task CloseOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _serve.ConfigureAwait(false); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        HomeUnixCoreTransport.Throw(failures);
    }
}
