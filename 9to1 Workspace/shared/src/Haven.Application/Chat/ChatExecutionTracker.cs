using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Tracks response progress and privacy-safe latency milestones without storing user content or secrets.
/// </summary>
public sealed class ChatExecutionTracker : IAsyncDisposable
{
    public static readonly TimeSpan VisibilityDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan EtaDelay = TimeSpan.FromMinutes(1);

    private sealed class OriginalContext(ChatExecutionTracker owner, OriginalContext? parent)
    {
        internal readonly ChatExecutionTracker Owner = owner;
        internal readonly OriginalContext? Parent = parent;
        internal volatile bool Active = true;
        internal volatile bool WithdrawalCallbacksActive;
    }
    private static readonly AsyncLocal<OriginalContext?> ActiveOriginalContext = new();
    [ThreadStatic] private static OriginalContext? ActiveSynchronousWithdrawal;
    private OriginalContext? _timerContext;
    private Task? _originalWithdrawal;
    private bool IsInsideOwningOriginal()
    {
        bool ContainsLive(OriginalContext? context)
        {
            for (; context is not null; context = context.Parent)
                if (ReferenceEquals(context.Owner, this) && (context.Active || context.WithdrawalCallbacksActive)) return true;
            return false;
        }
        return ContainsLive(ActiveOriginalContext.Value) || ContainsLive(ActiveSynchronousWithdrawal);
    }

    private readonly object _gate = new();
    private readonly List<ChatExecutionLogEntry> _log = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<ChatEtaRequest, CancellationToken, Task<string?>>? _etaProvider;
    private readonly Task _originalTimers;
    private Task? _originalClose;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    private ChatExecutionStage _stage;
    private string _status;
    private bool _visible;
    private bool _finished;
    private TimeSpan? _eta;

    // Preserve the original two-parameter CLR constructor and its own operation identity.
    public ChatExecutionTracker(ChatExecutionStage initialStage, Func<ChatEtaRequest, CancellationToken, Task<string?>>? etaProvider)
        : this(initialStage, etaProvider, operationId: null)
    {
    }

    public ChatExecutionTracker(
        ChatExecutionStage initialStage = ChatExecutionStage.Preparing,
        Func<ChatEtaRequest, CancellationToken, Task<string?>>? etaProvider = null,
        Guid? operationId = null)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("A canonical operation identity cannot be empty.", nameof(operationId));
        OperationId = operationId ?? Guid.NewGuid();
        Performance = new ChatPerformanceTrace(OperationId, _startedAt);
        Performance.TryMark(
            ChatPerformanceMilestone.SendClicked,
            timestamp: _startedAt);

        _stage = initialStage;
        _status = ChatExecutionStageText.Get(initialStage);
        _etaProvider = etaProvider;
        _log.Add(new ChatExecutionLogEntry(_startedAt, initialStage, _status));
        MarkStageStart(initialStage);
        _originalTimers = RunTimersAsync(_lifetime.Token);
    }

    public Guid OperationId { get; }

    public ChatPerformanceTrace Performance { get; }

    public event Action<ChatExecutionSnapshot>? Changed;

    public ChatExecutionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CreateSnapshot(DateTimeOffset.UtcNow);
            }
        }
    }

    public bool MarkPerformance(
        ChatPerformanceMilestone milestone,
        ChatPerformanceDimensions? dimensions = null,
        DateTimeOffset? timestamp = null) =>
        Performance.TryMark(milestone, dimensions, timestamp);

    public void Update(
        ChatExecutionStage stage,
        string? summary = null,
        string? detail = null,
        bool succeeded = true,
        ChatPerformanceDimensions? performanceDimensions = null)
    {
        ChatExecutionSnapshot snapshot;
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            _stage = stage;
            _status = string.IsNullOrWhiteSpace(summary)
                ? ChatExecutionStageText.Get(stage)
                : summary.Trim();

            var now = DateTimeOffset.UtcNow;
            _log.Add(new ChatExecutionLogEntry(
                now,
                stage,
                _status,
                TrimDetail(detail),
                succeeded));
            snapshot = CreateSnapshot(now);
        }

        MarkStageStart(stage, performanceDimensions);
        Changed?.Invoke(snapshot);
    }

    public void Complete(
        string summary = "Completed",
        ChatPerformanceDimensions? performanceDimensions = null)
    {
        Performance.TryMark(
            ChatPerformanceMilestone.CompletionReceived,
            performanceDimensions);
        Finish(ChatExecutionStage.Completed, summary, succeeded: true);
    }

    public void Fail(string summary = "Failed", string? detail = null) =>
        Finish(ChatExecutionStage.Failed, summary, succeeded: false, detail);

    public void Cancel() =>
        Finish(ChatExecutionStage.Cancelled, "Cancelled", succeeded: false);

    private void Finish(
        ChatExecutionStage stage,
        string summary,
        bool succeeded,
        string? detail = null)
    {
        ChatExecutionSnapshot snapshot;
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _stage = stage;
            _status = summary;
            var now = DateTimeOffset.UtcNow;
            _log.Add(new ChatExecutionLogEntry(
                now,
                stage,
                summary,
                TrimDetail(detail),
                succeeded));
            snapshot = CreateSnapshot(now);
        }

        RequestStop();
        Changed?.Invoke(snapshot);
    }

    private void MarkStageStart(
        ChatExecutionStage stage,
        ChatPerformanceDimensions? dimensions = null)
    {
        switch (stage)
        {
            case ChatExecutionStage.LoadingModel:
                Performance.TryMark(
                    ChatPerformanceMilestone.ModelSelectionStarted,
                    dimensions);
                break;
            case ChatExecutionStage.LoadingContext:
                Performance.TryMark(
                    ChatPerformanceMilestone.ContextAssemblyStarted,
                    dimensions);
                break;
            case ChatExecutionStage.SelectingCapabilities:
                Performance.TryMark(
                    ChatPerformanceMilestone.ToolSelectionStarted,
                    dimensions);
                break;
            case ChatExecutionStage.Generating:
                Performance.TryMark(
                    ChatPerformanceMilestone.ProviderRequestStarted,
                    dimensions);
                break;
        }
    }

    private async Task RunTimersAsync(CancellationToken cancellationToken)
    {
        var previousContext = ActiveOriginalContext.Value;
        var originalContext = new OriginalContext(this, previousContext);
        _timerContext = originalContext;
        ActiveOriginalContext.Value = originalContext;
        try
        {
            await Task.Delay(VisibilityDelay, cancellationToken).ConfigureAwait(false);
            PublishVisible();

            await Task.Delay(
                EtaDelay - VisibilityDelay,
                cancellationToken).ConfigureAwait(false);
            await RequestEtaAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally { originalContext.Active = false; ActiveOriginalContext.Value = previousContext; }
    }

    private void PublishVisible()
    {
        ChatExecutionSnapshot? snapshot = null;
        lock (_gate)
        {
            if (!_finished && !_visible)
            {
                _visible = true;
                snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
            }
        }

        if (snapshot is not null)
        {
            PublishChanged(snapshot);
        }
    }

    private async Task RequestEtaAsync(CancellationToken cancellationToken)
    {
        if (_etaProvider is null)
        {
            return;
        }

        ChatEtaRequest request;
        lock (_gate)
        {
            if (_finished || _eta is not null)
            {
                return;
            }

            request = new ChatEtaRequest(
                OperationId,
                _stage,
                _status,
                DateTimeOffset.UtcNow - _startedAt,
                _log.Select(item => item.Summary).TakeLast(12).ToArray());
        }

        string? answer;
        try
        {
            answer = await _etaProvider(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            return;
        }

        if (!ChatEtaFormatter.TryParseClearEstimate(answer, out var estimate))
        {
            return;
        }

        ChatExecutionSnapshot snapshot;
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            _eta = estimate;
            snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
        }

        PublishChanged(snapshot);
    }

    private void PublishChanged(ChatExecutionSnapshot snapshot)
    {
        try { Changed?.Invoke(snapshot); }
        catch (OperationCanceledException callbackFailure)
        {
            // A synchronous observer is not an actual canceled timer/owner task. Envelope
            // its exact cause before the timer's async builder or token catch can relabel it.
            throw new AggregateException("An original synchronous tracker observer failed.", callbackFailure);
        }
    }

    private ChatExecutionSnapshot CreateSnapshot(DateTimeOffset now) =>
        new(
            OperationId,
            _stage,
            _status,
            _startedAt,
            now,
            _visible,
            _eta,
            _log.ToArray());

    private static string? TrimDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 800 ? trimmed : trimmed[..800] + "…";
    }

    /// <summary>Requests only timer withdrawal. External DisposeAsync still joins the same original close.</summary>
    public void RequestStop()
    {
        TaskCompletionSource publication;
        lock (_gate)
        {
            if (_originalWithdrawal is not null) return;
            publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalWithdrawal = WithdrawOriginalAsync(publication.Task);
        }
        // Cancellation callbacks execute outside the state gate, after actual task custody.
        publication.SetResult();
    }

    private async Task WithdrawOriginalAsync(Task publication)
    {
        await publication.ConfigureAwait(false);
        var previousContext = ActiveOriginalContext.Value;
        var previousSynchronous = ActiveSynchronousWithdrawal;
        var originalContext = new OriginalContext(this, previousContext);
        ActiveOriginalContext.Value = originalContext;
        ActiveSynchronousWithdrawal = originalContext;
        var timerContext = _timerContext;
        if (timerContext is not null) timerContext.WithdrawalCallbacksActive = true;
        try { _lifetime.Cancel(); }
        catch (OperationCanceledException actualCallback)
        { throw new AggregateException("An original synchronous cancellation callback failed.", actualCallback); }
        finally
        {
            if (timerContext is not null) timerContext.WithdrawalCallbacksActive = false;
            originalContext.Active = false;
            ActiveSynchronousWithdrawal = previousSynchronous;
            ActiveOriginalContext.Value = previousContext;
        }
    }

    internal void DemandExternalOriginalProcessJoin()
    {
        if (IsInsideOwningOriginal())
            throw new InvalidOperationException("An original tracker callback cannot join its owning process drain.");
    }

    public ValueTask DisposeAsync()
    {
        if (IsInsideOwningOriginal())
            throw new InvalidOperationException("An original tracker callback cannot join its own timer/close; request stop and let an external owner join.");
        lock (_gate)
        {
            if (_originalClose is not null) return new ValueTask(_originalClose);
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = CloseOriginalAsync(published.Task);
            published.SetResult();
            return new ValueTask(_originalClose);
        }
    }

    private async Task CloseOriginalAsync(Task publication)
    {
        await publication.ConfigureAwait(false);
        var previousContext = ActiveOriginalContext.Value;
        var originalContext = new OriginalContext(this, previousContext);
        ActiveOriginalContext.Value = originalContext;
        try
        {
        var failures = new List<Exception>();
        var faultedOrUnknownCancellation = false;
        void Retain(Exception cause, Task? original = null)
        {
            IEnumerable<Exception> direct = original?.Exception is { } aggregate ? aggregate.InnerExceptions : new[] { cause };
            foreach (var error in direct)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, error))) failures.Add(error);
                if (error is OperationCanceledException && (original is null || original.IsFaulted)) faultedOrUnknownCancellation = true;
            }
        }
        try { if (!_finished) Cancel(); } catch (Exception error) { Retain(error); }
        try { RequestStop(); } catch (Exception error) { Retain(error); }
        Task? actualWithdrawal;
        lock (_gate) actualWithdrawal = _originalWithdrawal;
        if (actualWithdrawal is null) Retain(new InvalidOperationException("No actual tracker cancellation original was published."));
        else try { await actualWithdrawal.ConfigureAwait(false); } catch (Exception error) { Retain(error, actualWithdrawal); }
        try { await _originalTimers.ConfigureAwait(false); } catch (Exception error) { Retain(error, _originalTimers); }
        try { _lifetime.Dispose(); } catch (Exception error) { Retain(error); }
        if (failures.Count == 1 && failures[0] is OperationCanceledException && faultedOrUnknownCancellation)
            throw new AggregateException("An original faulted timer or synchronous tracker callback is not canceled-task evidence.", failures[0]);
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original tracker callbacks/timer cleanup failed.", failures);
        }
        finally { originalContext.Active = false; ActiveOriginalContext.Value = previousContext; }
    }
}

public sealed record ChatEtaRequest(
    Guid OperationId,
    ChatExecutionStage Stage,
    string CurrentStatus,
    TimeSpan Elapsed,
    IReadOnlyList<string> RecentActivity);
