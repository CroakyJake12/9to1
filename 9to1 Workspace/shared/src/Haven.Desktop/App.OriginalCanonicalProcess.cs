#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private bool _canonicalProcessCaptureEntered;
    private CanonicalProcessOriginalRetirementAdapter? _actualCanonicalProcessRetirement;
    private readonly List<CanonicalProcessOriginalRetirementAdapter> _actualCanonicalProcessBorrowers = [];
    private readonly List<Task> _actualCanonicalProcessCloses = [];
    private bool _canonicalProcessBorrowersTransferred;
    private Task? _actualUntransferredCanonicalProcessDrain;

    private void CaptureOriginalCanonicalProcessOwner(IServiceProvider actualProvider)
    {
        _originalAppWork.RunSynchronous(original =>
        {
            original.DemandPublication();
            lock (_actualStartupAcquisitionGate)
            {
                if (_canonicalProcessCaptureEntered)
                    throw new InvalidOperationException("The original App canonical process capture was already entered.");
                _canonicalProcessCaptureEntered = true;
            }
            var cohort = AcquireOriginalAppSynchronous(original, () =>
            {
                var acquired = actualProvider.GetRequiredService<TaskRunCanonicalProcessRetirementOwner>();
                // Retain the actual returned owner BEFORE another resolver, freshness
                // check or callback can fail or seal this startup original.
                var adapter = new CanonicalProcessOriginalRetirementAdapter(acquired);
                lock (_actualStartupAcquisitionGate)
                {
                    _actualCanonicalProcessRetirement = adapter;
                    _actualCanonicalProcessBorrowers.Add(adapter);
                }
                return acquired;
            });
            original.DemandPublication();
            var coordinator = AcquireOriginalAppSynchronous(original, actualProvider.GetRequiredService<TaskExecutionCoordinator>);
            original.DemandPublication();
            var agents = AcquireOriginalAppSynchronous(original, actualProvider.GetRequiredService<AgentTaskRuntimeService>);
            original.DemandPublication();
            var authority = AcquireOriginalAppSynchronous(original, actualProvider.GetRequiredService<TaskRunPermissionAuthority>);
            original.DemandPublication();
            var frames = AcquireOriginalAppSynchronous(original, actualProvider.GetRequiredService<TaskRunOriginalFrameOwner>);
            original.DemandPublication();
            if (!cohort.HasOriginalComposition(coordinator, agents, authority, frames))
                throw new InvalidOperationException("The original App must capture the SAME configured canonical process composition.");
            original.DemandPublication();
        });
    }

    private CanonicalProcessOriginalRetirementAdapter CaptureOriginalCanonicalProcessBorrower()
    {
        lock (_actualStartupAcquisitionGate)
            return _actualCanonicalProcessRetirement
                ?? throw new InvalidOperationException("The original App canonical process owner was not captured.");
    }

    private Task JoinOriginalUntransferredCanonicalProcessBorrowersAsync()
    {
        Task actual;
        TaskCompletionSource<bool>? start = null;
        CanonicalProcessOriginalRetirementAdapter[] actuals = [];
        var failures = new List<Exception>();
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualUntransferredCanonicalProcessDrain is null)
            {
                actuals = _canonicalProcessBorrowersTransferred ? [] : _actualCanonicalProcessBorrowers.ToArray();
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _actualUntransferredCanonicalProcessDrain = DrainOriginalUntransferredCanonicalProcessBorrowersAsync(start.Task, actuals, failures);
            }
            actual = _actualUntransferredCanonicalProcessDrain;
        }
        if (start is not null)
        {
            // This exact cleanup driver is already published. Complete pure preflight
            // before synchronous process seals; other failed-startup child stop callbacks
            // cannot run first or initiate new canonical work during an unsealed interval.
            foreach (var owner in actuals)
                try { _originalAppWork.RunCloseCallback(owner.DemandExternalOriginalRetirementJoin); }
                catch (Exception cause) { AddAppCause(failures, cause); }
            var preflightPassed = failures.Count == 0;
            if (preflightPassed)
                foreach (var owner in actuals)
                    try { _originalAppWork.RunCloseCallback(owner.RequestRetirement); }
                    catch (Exception cause) { AddAppCause(failures, cause); }
            start.SetResult(preflightPassed);
        }
        return actual;
    }

    private async Task DrainOriginalUntransferredCanonicalProcessBorrowersAsync(Task<bool> start,
        CanonicalProcessOriginalRetirementAdapter[] actuals, List<Exception> failures)
    {
        var preflightPassed = await start;
        // A pure preflight failure denies this whole captured cohort's requests and
        // joins; earlier request failures still require every actual close acquisition.
        if (!preflightPassed)
        {
            ThrowAppCauses(failures);
            return;
        }
        var originals = new List<Task>();
        foreach (var actual in actuals)
            try
            {
                _originalAppWork.RunCloseCallback(() =>
                {
                    var close = actual.CloseAndDrainAsync();
                    lock (_actualStartupAcquisitionGate) _actualCanonicalProcessCloses.Add(close);
                    originals.Add(close);
                });
            }
            catch (Exception cause) { AddAppCause(failures, cause); }
        foreach (var original in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(original, failures);
        ThrowAppCauses(failures);
    }
}
#endif
