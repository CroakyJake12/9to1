using Avalonia.Threading;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class NewDashboardPage
{
    private HomeDashboardAttentionSource? _attentionSource;
    private HomeDashboardAttentionRequest? _attentionRequest;
    private int _attentionCount;
    private long _attentionReadGeneration;
    private object? _attentionReviewClaim;
    private string? _attentionStatus;
    private DateTimeOffset? _attentionObservedAt;

    internal void ConfigureHomeBriefingAttention(HomeDashboardAttentionSource source)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(source);
        if (_attentionSource is not null && !ReferenceEquals(_attentionSource, source))
            throw new InvalidOperationException("Retain the Dashboard's original Home attention source.");
        _attentionSource = source;
        PublishAttentionBindings();
    }

    private bool AttentionIsCurrent(HomeDashboardAttentionSource source, long activation, long read) =>
        !_disposed && !_homeWork.IsRetiring && _dashboardActive && ShowsHomeBriefing &&
        ReferenceEquals(source, _attentionSource) && activation == _activationGeneration && read == _attentionReadGeneration;

    private void PublishAttentionBindings()
    {
        var available = _attentionSource is not null && ShowsHomeBriefing;
        var pending = available && _attentionRequest is not null;
        _dashboardBindings.Set("HasDashboardAttention", pending);
        _dashboardBindings.Set("DashboardAttentionKey", _attentionRequest?.Fingerprint ?? string.Empty);
        _dashboardBindings.Set("DashboardAttentionTitle", _attentionRequest is { } request
            ? $"{request.CallerName} is waiting for your review" : string.Empty);
        _dashboardBindings.Set("DashboardAttentionDetail", "Review the selected access request in Home permissions.");
        _dashboardBindings.Set("DashboardAttentionFreshness", _attentionObservedAt is { } observed
            ? $"{_attentionCount} pending · Checked {observed.ToLocalTime():t}" : string.Empty);
        _dashboardBindings.Set("CanReviewDashboardAttention", pending && _dashboardActive && !_disposed &&
            !_homeWork.IsRetiring && _attentionReviewClaim is null);
        _dashboardBindings.Set("DashboardAttentionStatus", _attentionStatus ?? string.Empty);
        _dashboardBindings.Set("HasDashboardAttentionStatus", available && !string.IsNullOrEmpty(_attentionStatus));
        _dashboardBindings.Set("CanRefreshDashboardAttention", available && _dashboardActive && !_disposed && !_homeWork.IsRetiring);
    }

    private async Task RefreshDashboardAttentionAsync(CancellationToken token)
    {
        if (_attentionSource is not { } source || !ShowsHomeBriefing || !_dashboardActive) return;
        var activation = _activationGeneration;
        var read = ++_attentionReadGeneration;
        _attentionRequest = null;
        _attentionCount = 0;
        _attentionObservedAt = null;
        _attentionStatus = "Checking requests…";
        PublishAttentionBindings();
        // Binding publication can synchronously retire or replace this page.
        if (!AttentionIsCurrent(source, activation, read)) return;
        try
        {
            var ownerToken = (_homeWork.Executing ?? throw new InvalidOperationException("The attention read has no Dashboard owner.")).Token;
            var snapshot = await AwaitHomeSourceAsync(source.ReadAsync(ownerToken));
            if (token.IsCancellationRequested || !AttentionIsCurrent(source, activation, read)) return;
            _attentionRequest = snapshot.Pending.FirstOrDefault();
            _attentionCount = snapshot.Pending.Count;
            _attentionObservedAt = snapshot.ObservedAt;
            _attentionStatus = null;
            PublishAttentionBindings();
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested && AttentionIsCurrent(source, activation, read))
            {
                _attentionRequest = null;
                _attentionStatus = "Requests are unavailable. Refresh to try again.";
                PublishAttentionBindings();
            }
            // AwaitHomeSourceAsync retains the actual failure even when the rest of the
            // Dashboard can still render. Closing joins that original, including late I/O.
            if (error is OperationCanceledException && token.IsCancellationRequested) throw;
        }
    }

    private ValueTask ReviewDashboardAttentionAsync(string capturedKey, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        var source = _attentionSource;
        var request = _attentionRequest;
        var activation = _activationGeneration;
        var read = _attentionReadGeneration;
        if (source is null || request is null || request.Fingerprint != capturedKey || _attentionReviewClaim is not null ||
            !AttentionIsCurrent(source, activation, read))
            return ValueTask.FromException(new InvalidOperationException("This request is no longer available for review."));
        var claim = _attentionReviewClaim = new object();
        _attentionStatus = "Opening the selected request…";
        PublishAttentionBindings();
        if (!AttentionIsCurrent(source, activation, read) || !ReferenceEquals(_attentionRequest, request))
        {
            if (ReferenceEquals(_attentionReviewClaim, claim)) _attentionReviewClaim = null;
            return ValueTask.CompletedTask;
        }
        return new(RunHomeWorkAsync(async token =>
        {
            bool Current() => !token.IsCancellationRequested && AttentionIsCurrent(source, activation, read) &&
                ReferenceEquals(_attentionRequest, request) && ReferenceEquals(_attentionReviewClaim, claim);
            try
            {
                if (!Current()) return;
                var ownerToken = (_homeWork.Executing ?? throw new InvalidOperationException("The attention review has no Dashboard owner.")).Token;
                var opened = await AwaitHomeSourceAsync(source.ReviewAsync(request, Current, ownerToken));
                if (!Current()) return;
                _attentionStatus = opened ? "Opened the selected request in Home permissions."
                    : "This request is no longer waiting for review. Refresh to see current requests.";
                if (!opened) { _attentionRequest = null; _attentionCount = 0; _attentionObservedAt = null; }
            }
            catch
            {
                if (Current())
                {
                    _attentionRequest = null;
                    _attentionCount = 0;
                    _attentionObservedAt = null;
                    _attentionStatus = "The selected request could not be opened. Refresh to try again.";
                }
                throw;
            }
            finally
            {
                if (ReferenceEquals(_attentionReviewClaim, claim))
                {
                    _attentionReviewClaim = null;
                    if (!_disposed && !_homeWork.IsRetiring) PublishAttentionBindings();
                }
            }
        }, cancellationToken));
    }
}
