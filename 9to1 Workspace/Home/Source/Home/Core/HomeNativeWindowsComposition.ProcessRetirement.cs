using Haven.Application;
namespace HavenOS.Home.Core;

public sealed partial class HomeNativeWindowsComposition
{
    private bool _originalProcessRetiring;
    private Task? _originalProcessRequest;
    private readonly CloudflareOriginalTaskLedger _originalProcessSources = new();
    private readonly CloudflareOriginalTaskLedger _originalProcessClosing = new();
    public Task? OriginalProcessRetirementRequestTask { get { lock (_sync) return _originalProcessRequest; } }
    public void DemandExternalOriginalProcessJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);

    /// <summary>Request only: permanently seals startup, stops its genuine process token,
    /// and retains the actual cancellation driver. Shared Home disposal is downstream of borrowers.</summary>
    public void RequestOriginalProcessRetirement()
    {
        TaskCompletionSource begin;
        lock (_sync)
        {
            _originalProcessRetiring = true;
            if (_originalProcessRequest is not null) return;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalProcessSources.BindOriginalOwner(this);
            _originalProcessRequest = RequestPublishedAsync(begin.Task);
        }
        begin.SetResult(); // Callbacks never execute while holding the composition gate.
    }
    private async Task RequestPublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        _originalProcessSources.Invoke(() => { _process.Cancel(); return true; });
        await _originalProcessSources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_originalProcessSources.OriginalErrors.Count != 0)
            throw new AggregateException("Original Home process request failed.", _originalProcessSources.OriginalErrors);
    }
    private void RunOriginalHomeProcessSource(Action body)
    {
        _originalProcessSources.BindOriginalOwner(this);
        _originalProcessSources.Invoke(() => { body(); return true; });
    }
    private void RetainOriginalHomeProcessSource(Task actual) { _ = _originalProcessSources.Track(actual); }
    private async Task JoinOriginalHomeProcessSourcesAsync(List<Exception> failures)
    {
        Task? request; lock (_sync) request = _originalProcessRequest;
        if (request is not null)
            try { await _originalProcessClosing.AwaitAsync(request).ConfigureAwait(false); }
            catch (Exception cause) { _originalProcessClosing.Capture(request, cause); }
        await _originalProcessSources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in _originalProcessSources.OriginalErrors) _originalProcessClosing.Retain(cause);
        foreach (var cause in _originalProcessClosing.OriginalErrors)
            if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
    }
}
