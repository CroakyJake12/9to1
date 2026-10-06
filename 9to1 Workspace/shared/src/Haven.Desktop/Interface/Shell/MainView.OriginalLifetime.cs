using System.Runtime.ExceptionServices;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private readonly DesktopOriginalWorkLifetime _originalShellWork;
    private readonly List<CancellationTokenSource> _originalAutomationSources = [];
    private WorkspaceTabViewModel[]? _originalShellTabs;
    private object[]? _originalShellChildren;
    private readonly List<Exception> _originalShellStopFailures = [];
    [ThreadStatic] private static List<MainView>? _synchronousShellSources;

    public void RequestRetirement()
    {
        // Snapshot/save is the native host's preceding step. This seals only this
        // view's owned producers; it does not cancel canonical Task/Run services.
        if (_originalShellTabs is null)
        {
            _originalShellTabs = (_secondaryTab is { } secondary ? OpenTabs.Append(secondary) : OpenTabs)
                .Distinct<WorkspaceTabViewModel>(ReferenceEqualityComparer.Instance).ToArray();
            _originalShellChildren = CaptureCurrentOriginalShellChildren();
        }
        IsDisposed = true;
        _originalShellWork.RequestRetirement();
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_synchronousShellSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual shell callback must return before its encompassing external join.");
        _originalShellWork.DemandExternalClose();
        DemandOriginalShellChildJoins(_originalShellChildren, CaptureCurrentOriginalShellChildren());
        DemandOriginalShellTabJoins(_originalShellTabs, OpenTabs, _secondaryTab);
    }

    // Pure preflight. A factory can return an actual product after the shell's seal;
    // its private late cohort must participate BEFORE any existing close is returned.
    // This neither retires the child nor certifies that an unknown child has drained.
    internal static void DemandOriginalShellChildJoins(object[]? sealedChildren, object[] currentChildren)
    {
        foreach (var child in (sealedChildren ?? []).Concat(currentChildren).Distinct(ReferenceEqualityComparer.Instance))
            if (child is IDesktopOriginalRetirementJoinGuard guard) guard.DemandExternalOriginalRetirementJoin();
    }

    internal static void DemandOriginalShellTabJoins(WorkspaceTabViewModel[]? sealedTabs,
        IEnumerable<WorkspaceTabViewModel> currentTabs, WorkspaceTabViewModel? actualSecondary)
    {
        foreach (var tab in (sealedTabs ?? []).Concat(currentTabs)
            .Concat(actualSecondary is { } secondary ? [secondary] : [])
            .Distinct<WorkspaceTabViewModel>(ReferenceEqualityComparer.Instance))
            tab.DemandExternalOriginalRetirementJoin();
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        RequestRetirement();
        return _originalShellWork.CloseAndDrainAsync();
    }

    private object[] CaptureCurrentOriginalShellChildren() => new object?[]
        { _homePage, _newDashboardPage, _goPage, _newChatPage, _planPage, _terminalPage,
          _mailPage, _playPage, _currentPage, _currentChat, _activeProjectPage, _previousContent,
          _studyAssignmentsSidebar, _nativeChatSidebar, _nativeProjectsPage, _spacesPage, _companionDockVm }
        .OfType<object>().Concat(_chats.Values).Concat(_projectChats.Values).Concat(_projectPages.Values)
        .Concat(_groupChats.Values).Concat(_groupPages.Values).Concat(_modeWorkspaces.Values)
        .Concat(_canonicalSpaceTaskPages)
        .Distinct(ReferenceEqualityComparer.Instance).ToArray();

    private T AcquireOriginalShellSynchronous<T>(DesktopOriginalWorkLifetime.Original original, Func<T> callback)
    {
        (_synchronousShellSources ??= []).Add(this);
        try { return callback(); }
        catch (Exception error)
        {
            original.Retain(error);
            if (error is OperationCanceledException)
                throw new AggregateException("The actual synchronous shell callback returned no canceled original Task.", error);
            throw;
        }
        finally { _synchronousShellSources.RemoveAt(_synchronousShellSources.Count - 1); }
    }

    private void DemandOriginalShellProducerJoin()
    {
        if (_synchronousShellSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("The actual parent Shell source must return before its borrowed Go original joins.");
        _originalShellWork.DemandExternalClose();
    }

    private void StartOriginalShellEvent(Func<DesktopOriginalWorkLifetime.Original, Task> actualBody)
    {
        if (IsDisposed) return; // A late event does not admit new work after the permanent view seal.
        _ = _originalShellWork.RunAsync(actualBody); // SAME actual full body retained before event callbacks.
    }

    private Task StopOriginalShellProducersAsync()
    {
        var failures = _originalShellStopFailures;
        void Stop(Action actual)
        {
            try { _originalShellWork.RunCloseCallback(actual); }
            catch (Exception error) { AddShellFailure(failures, error); }
        }
        Stop(_originalSidebarSearch.RequestRetirement);
        Stop(() => _reminderTimer.Stop());
        Stop(StopAutomationScheduler);
        CancellationTokenSource[] suggestions;
        lock (_goSuggestionRefreshes) suggestions = _goSuggestionRefreshes.Values.Distinct().ToArray();
        foreach (var original in suggestions) Stop(original.Cancel);
        // Child resources still belong to parent originals admitted before the
        // seal. Their retirement starts in cleanup AFTER those originals settle.
        return Task.CompletedTask; // Failures retained below; not an all-drained receipt.
    }

    private async Task CloseOriginalShellChildrenAsync()
    {
        var failures = _originalShellStopFailures.ToList();
        var actualCloses = new List<Task>();
        // Include genuine late factory products before destruction. Keep the
        // sealed old cohort as well; current fields alone cannot prove removed
        // tabs/pages are no longer owned.
        var tabs = (_originalShellTabs ?? []).Concat(OpenTabs)
            .Concat(_secondaryTab is { } secondary ? [secondary] : [])
            .Distinct<WorkspaceTabViewModel>(ReferenceEqualityComparer.Instance).ToArray();
        var children = (_originalShellChildren ?? []).Concat(CaptureCurrentOriginalShellChildren())
            .Distinct(ReferenceEqualityComparer.Instance).ToArray();
        var participants = children.OfType<IDesktopOriginalRetirementParticipant>()
            .Concat(tabs).Distinct<IDesktopOriginalRetirementParticipant>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var participant in participants)
            if (participant is IDesktopOriginalRetirementJoinGuard guard) guard.DemandExternalOriginalRetirementJoin();
        foreach (var participant in participants)
            try { _originalShellWork.RunCloseCallback(participant.RequestRetirement); }
            catch (Exception error) { AddShellFailure(failures, error); }
        foreach (var participant in children.OfType<IDesktopOriginalRetirementParticipant>()
            .Concat(tabs).Distinct<IDesktopOriginalRetirementParticipant>(ReferenceEqualityComparer.Instance))
            try
            {
                Task? actual = null;
                _originalShellWork.RunCloseCallback(() => actual = participant.CloseAndDrainAsync());
                actualCloses.Add(actual ?? throw new InvalidOperationException("The actual shell child supplied no close Task."));
            }
            catch (Exception error) { AddShellFailure(failures, error); }
        try { actualCloses.Add(CloseOriginalFilesAndDrainAsync()); }
        catch (Exception error) { AddShellFailure(failures, error); }
        foreach (var actual in actualCloses.Distinct<Task>(ReferenceEqualityComparer.Instance))
            try { await actual; }
            catch (Exception observed)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var original in group.InnerExceptions) AddShellFailure(failures, original);
                else AddShellFailure(failures, observed);
            }
        // All admitted automation/suggestion stages have settled before their sources are released.
        foreach (var original in _originalAutomationSources)
            try { original.Dispose(); } catch (Exception error) { AddShellFailure(failures, error); }
        foreach (var child in children)
            if (child is not IDesktopOriginalRetirementParticipant || child is not IDesktopOriginalRetirementJoinGuard)
                AddShellFailure(failures, new DesktopOriginalRetirementUnavailableException(child.GetType()));
        ThrowShellFailures(failures);
        // Destroy owned controls only after every actual child has a successful owning join.
        _callCoordinator.StateChanged -= OnCallStateChanged;
        _homePage?.Deactivate();
        _newDashboardPage?.Deactivate();
        _studyAssignmentsSidebar?.Dispose();
        _nativeChatSidebar?.Dispose();
        RemoveSplitView();
        OpenTabs.Clear();
        TopRail.Dispose();
    }

    private static void AddShellFailure(List<Exception> failures, Exception error)
    { if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error); }
    private static void ThrowShellFailures(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual shell originals or unclaimed child ownership remain unresolved.", failures);
    }
}
