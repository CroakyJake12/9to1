using System.Runtime.ExceptionServices;
namespace HavenOS.Home.Core;

// Uses the original client's single request slot and borrowed pipe. This creates no session or grant.
public sealed partial class HomeWindowsCoreClient
{
    internal Task<HomeNativeFilesReply> InvokeOriginalFilesAsync(HomeNativeFilesRequest request, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        HomeNativeFilesProtocol.Validate(request);
        var frame = new HomeNativeFilesFrame(HomeNativeFilesProtocol.Version, Guid.NewGuid().ToString("N"), request with { });
        var payload = HomeNativeFilesProtocol.Payload(frame);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _lifetime.IsCancellationRequested, this);
            if (_pending is { IsCompleted: false })
                throw new InvalidOperationException("One original Home pipe request is already pending.");
            var original = ExecuteOriginalFilesAsync(start.Task, frame, payload, caller);
            _pending = original;
            start.SetResult();
            return original;
        }
    }

    private async Task<HomeNativeFilesReply> ExecuteOriginalFilesAsync(Task start, HomeNativeFilesFrame frame,
        byte[] payload, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        CancellationTokenSource? active = null;
        List<Exception> failures = [];
        HomeNativeFilesReply? reply = null;
        try
        {
            active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
            active.CancelAfter(TimeSpan.FromSeconds(5));
            await DemandHostAsync(active.Token).ConfigureAwait(false);
            await HomeUnixDiscoveryTransport.WriteFrameAsync(_pipe, payload, active.Token).ConfigureAwait(false);
            reply = HomeNativeFilesProtocol.ReadResponse(
                await HomeUnixDiscoveryTransport.ReadFrameAsync(_pipe, active.Token).ConfigureAwait(false), frame);
            await DemandHostAsync(active.Token).ConfigureAwait(false);
            active.Token.ThrowIfCancellationRequested();
            lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
        }
        catch (Exception error)
        {
            HomeUnixCoreTransport.Add(failures, error);
            // An interrupted exchange is never reused as another original request's response.
            try { _lifetime.Cancel(); } catch (Exception cleanup) { HomeUnixCoreTransport.Add(failures, cleanup); }
        }
        finally
        {
            try { active?.Dispose(); } catch (Exception cleanup) { HomeUnixCoreTransport.Add(failures, cleanup); }
        }
        HomeUnixCoreTransport.Throw(failures);
        return reply!;
    }
}
