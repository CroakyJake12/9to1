using Haven.Core;

namespace Haven.Application;

/// <summary>Actual source-owned request metadata. This observation neither decides the
/// request nor grants route, egress, spend, provider execution or continuation authority.</summary>
public sealed record TaskRunOriginalPermissionReview(RemediationRequest Request,
    TaskExecutionOwnerBinding Owner, bool CanRespond, PermissionDecision? AcknowledgedDecision);

public sealed partial class TaskRunCloudPermissionRemediationOwner
{
    private sealed class OriginalReview(Binding binding)
    {
        internal readonly Binding Binding = binding;
        internal Task<TaskRunOriginalPermissionReview> Driver = null!;
        internal readonly List<Task> Sources = [];
        internal bool Healthy => Driver.IsCompletedSuccessfully && Sources.All(task => task.IsCompletedSuccessfully);
    }
    private readonly List<OriginalReview> _originalReviews = [];

    public Task<TaskRunOriginalPermissionReview> ReviewOriginalAsync(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        OriginalReview work; TaskCompletionSource start;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            var binding = RequireRecord(id);
            _originalReviews.RemoveAll(static work => work.Healthy);
            if (_originalReviews.Count >= 128)
                throw new InvalidOperationException("Unresolved original permission reviews require inspection.");
            work = new(binding); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            work.Driver = ReviewPublishedOriginalAsync(work, start.Task);
            _originalReviews.Add(work); // SAME real driver published before any source callback.
        }
        start.SetResult(); return work.Driver;
    }
    private async Task<TaskRunOriginalPermissionReview> ReviewPublishedOriginalAsync(OriginalReview work, Task start)
    {
        await start.ConfigureAwait(false);
        // Caller cancellation withdraws admission only. Once acquired, this finite
        // owner read is joined through its actual outcome; a closed borrowing view
        // cannot poison the shared permission owner with presentation cancellation.
        var currentToken = _ownerToken;
        var phase = EnterOriginal(work.Driver);
        try
        {
            async Task<T> Read<T>(Func<Task<T>> source)
            {
                Task<T> actual;
                try { actual = CallOriginal(source); }
                catch (OperationCanceledException fault) { throw new AggregateException("The actual permission-review source faulted synchronously.", fault); }
                lock (_sync) work.Sources.Add(actual);
                return await ObserveAsync(actual).ConfigureAwait(false);
            }
            var record = work.Binding;
            _ = await Read(() => record.Publication).ConfigureAwait(false);
            currentToken.ThrowIfCancellationRequested();
            _ = await Read(() => DemandCurrentAsync(record, capture: false)).ConfigureAwait(false);
            var persisted = await Read(() => _repository.GetAsync(record.Id, currentToken)).ConfigureAwait(false);
            var expected = record.Published ?? throw new UnauthorizedAccessException("The original request was not published.");
            if (persisted is null || persisted.Id != expected.Id || persisted.ExecutionId != expected.ExecutionId ||
                persisted.ActionId != expected.ActionId || persisted.Type != RemediationType.PermissionRequest ||
                persisted.RequestingComponentId != ComponentId || persisted.RequestingComponentName != expected.RequestingComponentName ||
                persisted.Title != expected.Title || persisted.Explanation != expected.Explanation ||
                persisted.ProviderName != expected.ProviderName || persisted.RequiredInputs.Count != 0 ||
                persisted.CanResume != expected.CanResume || persisted.Sensitivity != expected.Sensitivity ||
                !persisted.AllowedActions.SequenceEqual(expected.AllowedActions))
                throw new UnauthorizedAccessException("The stored permission request differs from the original source-issued Ask.");
            _ = await Read(() => DemandCurrentAsync(record, capture: false)).ConfigureAwait(false);
            currentToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                if (!ReferenceEquals(_ids.GetValueOrDefault(record.Id), record))
                    throw new UnauthorizedAccessException("The original permission request retired during review.");
                var acknowledged = GetAcknowledgedOriginalDecision(record.Id);
                var canRespond = CanRespond(record.Id) && persisted.State is
                    RemediationState.Waiting or RemediationState.InProgress or RemediationState.Suspended;
                var detached = persisted with { AllowedActions = Array.AsReadOnly(persisted.AllowedActions.ToArray()),
                    RequiredInputs = Array.AsReadOnly(persisted.RequiredInputs.ToArray()) };
                return new(detached, record.Original.OriginalOwner, canRespond, acknowledged);
            }
        }
        finally { ExitOriginal(phase); }
    }
}
