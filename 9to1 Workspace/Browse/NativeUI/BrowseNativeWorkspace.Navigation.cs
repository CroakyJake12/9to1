using Avalonia.Threading;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseNativeWorkspace
{
    /// <summary>Accepts an immutable intent and the originating loader's actual
    /// action-settlement source. The action must return immediately after calling
    /// this method; the separately owned driver waits outside that action.</summary>
    public bool QueueNavigation(BrowseNavigationIntent intent, Task originalSourceSettlement)
    {
        Dispatcher.UIThread.VerifyAccess(); ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(originalSourceSettlement);
        if (!IsReady || _busy || !_originalActionAvailability("9to1.Browse.Navigate") ||
            !_snapshot.Tabs.Any(tab => tab.Id == intent.SourceTabId)) return false;
        lock (_sourceGate) { CheckAdmission(_originalCommands); Retain(_originalOperations, originalSourceSettlement); }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _busy = true;
        var prior = LogicalDriver.Value;
        var invocation = new CommandInvocation(this, prior);
        var driver = RunQueuedNavigationAsync(start.Task, intent, originalSourceSettlement, invocation);
        invocation.Self = driver;
        _originalCommand = driver;
        lock (_sourceGate) Retain(_originalCommands, driver);
        try { _status = "Opening…"; Changed(); start.SetResult(); }
        catch (Exception failure) { start.SetException(failure); throw; }
        ObserveQueuedNavigation(driver);
        return true;
    }

    private async void ObserveQueuedNavigation(Task original)
    {
        try { await original; }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse retained queued native navigation failure: {0}", failure); }
    }

    private async Task RunQueuedNavigationAsync(Task start, BrowseNavigationIntent intent, Task originalSourceSettlement,
        CommandInvocation invocation)
    {
        var prior = LogicalDriver.Value; LogicalDriver.Value = invocation;
        var failures = new List<Exception>();
        try
        {
            await start;
            await originalSourceSettlement;
            if (!IsReady) throw new ObjectDisposedException(nameof(BrowseNativeWorkspace));
            await Operation(() => _chrome.OpenContentAsync(intent.Address, intent.OpenInNewTab, intent.SourceTabId));
            if (IsOriginalPublicationCurrent) { ApplySnapshot(_chrome.State); _status = _snapshot.Status; }
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            try { if (IsOriginalPublicationCurrent) ApplySnapshot(_chrome.State); } catch (Exception observation) { failures.Add(observation); }
            _status = "The original app navigation failed: " + failure.Message;
        }
        finally
        {
            _busy = false;
            try { Changed(); } catch (Exception notification) { failures.Add(notification); }
            invocation.Active = false; LogicalDriver.Value = prior;
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Browse retained original navigation and observer failures.", failures);
    }
}
