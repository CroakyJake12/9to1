using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

// Custody controls exercise the maintained real service with injected held/faulted
// source stages; these adapters issue no Files/Home/native permission authority.
public sealed class BrowserNativeDownloadOriginalCustodyTests
{
    [Fact]
    public async Task CloseRetainsTheWholeAdmittedApprovalBeforeItsApprovedStoreStageReturns()
    {
        var store = new Store { HoldApproved = true }; var execution = new Execution();
        var service = new BrowserNativeDownloadAutomationService(new Inner(), new Policy(), store);
        var id = Guid.NewGuid(); await service.RequestNativeDownloadAsync(new(id, new("https://example.test/file"), "file.bin", false), execution, CancellationToken.None);
        var approve = service.ApproveAsync(id, CancellationToken.None); Task? close = null; Exception? assertion = null;
        try
        {
            if (!ReferenceEquals(await Task.WhenAny(store.ApprovedEntered.Task, approve), store.ApprovedEntered.Task)) await approve;
            await store.ApprovedEntered.Task; close = service.DisposeAsync().AsTask();
            await execution.CancelEntered.Task;
            Assert.False(close.IsCompleted); Assert.Equal(1, execution.CancelCount); Assert.Equal(0, execution.ExecuteCount);
        }
        catch (Exception error) { assertion = error; }
        finally { store.ReleaseApproved.TrySetResult(); }
        var actualFailure = await Failure(approve); close ??= service.DisposeAsync().AsTask(); var closeFailure = await Failure(close);
        var actual = Leaves(actualFailure).OfType<ObjectDisposedException>().Distinct<Exception>(ReferenceEqualityComparer.Instance).Single();
        Assert.True(Contains(closeFailure, actual)); Assert.Equal(0, execution.ExecuteCount);
        Assert.Equal(1, store.UpdateCount); Assert.Equal(0, store.DownloadCount);
        if (assertion is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(assertion).Throw();
    }
    [Fact]
    public async Task UnapprovedOriginalCancellationFaultPreservesAllDirectRawCausesAndCachedClose()
    {
        var empty = new AggregateException("Foreign empty actual Cancel cause."); var io = new IOException("Independent original Cancel sibling.");
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); failed.SetException([empty, io]);
        var store = new Store(); var execution = new Execution { Cancel = failed.Task };
        var service = new BrowserNativeDownloadAutomationService(new Inner(), new Policy(), store);
        await service.RequestNativeDownloadAsync(new(Guid.NewGuid(), new("https://example.test/file"), "file.bin", false), execution, CancellationToken.None);
        var close = service.DisposeAsync().AsTask(); var failure = await Failure(close);
        Assert.Equal(1, execution.CancelCount); Assert.True(Contains(failure, empty)); Assert.True(Contains(failure, io));
        Assert.True(Leaves(failure).All(error => ReferenceEquals(error, empty) || ReferenceEquals(error, io)));
        Assert.Same(close, service.DisposeAsync().AsTask()); Assert.Equal(1, execution.CancelCount);
    }
    [Fact]
    public async Task RestoredContextOriginalStoreCallbackCannotSynchronouslyJoinItsOwnService()
    {
        var neutral = ExecutionContext.Capture()!; var store = new Store(); var refused = 0;
        var service = new BrowserNativeDownloadAutomationService(new Inner(), new Policy(), store);
        store.BeforeApproved = () => ExecutionContext.Run(neutral, ignored =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = service.DisposeAsync(); }); refused++;
        }, null);
        var id = Guid.NewGuid(); var execution = new Execution { ActionId = id };
        await service.RequestNativeDownloadAsync(new(id, new("https://example.test/file"), "file.bin", false), execution, CancellationToken.None);
        var result = await service.ApproveAsync(id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Executed, result.State); Assert.Equal(1, refused); Assert.Equal(1, execution.ExecuteCount);
        await service.DisposeAsync();
    }
    private sealed class Policy : IBrowserNavigationPolicy
    {
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken token) =>
            Task.FromResult(new BrowserNavigationAssessment(address, true, "Custody fixture only.", ["203.0.113.10"]));
    }
    private sealed class Execution : IBrowserNativeDownloadExecution
    {
        internal Task Cancel = Task.CompletedTask; internal int CancelCount, ExecuteCount; internal Guid ActionId;
        internal readonly TaskCompletionSource CancelEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task CancelAsync(CancellationToken token) { Interlocked.Increment(ref CancelCount); CancelEntered.TrySetResult(); return Cancel; }
        public Task<BrowserDownloadRecord> ExecuteAsync(CancellationToken token)
        {
            Interlocked.Increment(ref ExecuteCount);
            return Task.FromResult(new BrowserDownloadRecord(Guid.NewGuid(), ActionId, "https://example.test/file", "file.bin",
                "fixture-only", 1, new string('a', 64), null, DateTimeOffset.UtcNow));
        }
    }
    private sealed class Inner : IBrowserAutomationService
    {
        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> NavigateAsync(string address, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickReferenceAsync(string reference, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FillReferenceAsync(string reference, string value, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserPendingAction> RequestDownloadAsync(string address, string? name, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> ApproveAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> RejectAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>([]);
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int maximum, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>([]);
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int maximum, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);
    }
    private sealed class Store : IBrowserAutomationStore
    {
        private readonly Dictionary<Guid, BrowserPendingAction> _actions = [];
        internal bool HoldApproved; internal Action? BeforeApproved;
        internal readonly TaskCompletionSource ApprovedEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseApproved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int UpdateCount, DownloadCount;
        public Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken token)
        { _actions.Add(action.Id, action); return Task.FromResult(action); }
        public Task<BrowserPendingAction?> GetActionAsync(Guid id, CancellationToken token) => Task.FromResult(_actions.GetValueOrDefault(id));
        public async Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken token)
        {
            if (action.State == BrowserActionState.Approved)
            {
                BeforeApproved?.Invoke(); ApprovedEntered.TrySetResult(); if (HoldApproved) await ReleaseApproved.Task;
            }
            Interlocked.Increment(ref UpdateCount); _actions[action.Id] = action; return action;
        }
        public Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken token) { Interlocked.Increment(ref DownloadCount); return Task.CompletedTask; }
        public Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>(_actions.Values.ToArray());
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>([]);
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);
    }
    private static async Task<Exception> Failure(Task actual)
    { try { await actual; } catch (Exception error) { return actual.Exception ?? error; } throw new InvalidOperationException("The exact original Task was expected to fail."); }
    private static IEnumerable<Exception> Leaves(Exception actual) => actual is AggregateException { InnerExceptions.Count: > 0 } group
        ? group.InnerExceptions.SelectMany(Leaves) : [actual];
    private static bool Contains(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(error => Contains(error, expected));
}
