namespace HavenOS.Home.Core;
// DTOs are observations. Home owns actor/store/broker authority and privately pairs page handles.
public sealed partial class HomeNativeWindowsAppConnection
{
    private readonly List<Task> _originalFilesRequests = [];
    private Task<HomeNativeFilesReply>? _originalFilesPending;

    public Task<HomeNativeFilesReply> InvokeOriginalFilesAsync(HomeNativeFilesRequest request,
        CancellationToken cancellationToken = default)
    {
        HomeNativeFilesProtocol.Validate(request);
        var originalRequest = request with { };
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _lifetime.IsCancellationRequested, this);
            if (_initialization is { IsCompleted: false } || _originalFilesPending is { IsCompleted: false })
                throw new InvalidOperationException("The original app initialization or Files operation is still pending.");
            if (_originalFilesRequests.Count == 64)
                throw new InvalidOperationException("The bounded original Files operation custody is full.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = InvokeFilesCoreAsync(start.Task, originalRequest, cancellationToken);
            _originalFilesRequests.Add(original);
            _originalFilesPending = original;
            start.SetResult();
            return original;
        }
    }

    private async Task<HomeNativeFilesReply> InvokeFilesCoreAsync(Task start, HomeNativeFilesRequest request,
        CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        CancellationTokenSource? active = null;
        List<Exception> failures = [];
        HomeNativeFilesReply? reply = null;
        Task<HomeNativeFilesReply>? originalDomain = null;
        try
        {
            active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
            var startup = await _startup.CheckAsync(active.Token).ConfigureAwait(false);
            if (!startup.CanStartNormally)
                reply = new("Unavailable", startup.Code, startup.Message,
                    PermissionRequestId: startup.PermissionRequestId);
            else
            {
                active.Token.ThrowIfCancellationRequested();
                lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
                originalDomain = _startup.InvokeOriginalFilesAsync(request, active.Token);
                reply = await originalDomain.ConfigureAwait(false);
                // No later startup/actor await crosses Home's final Files reply fence.
                // This is an observed reply, not atomic cross-process revocation authority.
                active.Token.ThrowIfCancellationRequested();
                lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
            }
        }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        finally
        {
            try { if (originalDomain is not null) await originalDomain.ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { active?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        }
        HomeUnixCoreTransport.Throw(failures);
        return reply!;
    }

    private Task[] CaptureOriginalFilesRequests()
    { lock (_sync) return _originalFilesRequests.ToArray(); }
}
