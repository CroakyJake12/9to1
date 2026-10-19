using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Spaces;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class SpacesBrowseAppContentSession
{
    private sealed record PublishedDetailsRoute(SpaceDetailsPage Page, AppContentAddress Address);
    private PublishedDetailsRoute? _publishedDetailsRoute; // Same _gate as the Chat projection.
    // Canonical return-chat identity preserved across management/builder entry so
    // the prior chat can be restored on return rather than being lost.
    private Guid? _returnChatConversationId;
    private readonly List<SpaceDetailsPage> _ownedDetails = [];
    private readonly Dictionary<Guid, SpaceDetailsPage> _detailsBySpace = [];
    private readonly Dictionary<SpaceDetailsPage, Task> _detailsCloses = [];
    private readonly List<Task> _detailsPreparations = [];
    private readonly List<Exception> _preparedCloseFailures = [];
    private Task? _lastQueuedNavigation;
    private long _routeEpoch;

    internal Task? OriginalQueuedNavigation { get { lock (_gate) return _lastQueuedNavigation; } }

    private bool QueueSpaceAction(SpacesAction action, Task actualOriginAndLoaderSettlement)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(actualOriginAndLoaderSettlement);
        lock (_gate)
        {
            if (_closing || !_published || _activeDetailsPreparation is not null ||
                action.Kind is not (SpacesActionKind.Open or SpacesActionKind.EditDetails)) return false;
            var selection = _spacesPage?.Controller.State.Selection;
            if (selection is null || selection.Id != action.Context.Scope.RegisteredSpaceId) return false;
            var epoch = _routeEpoch;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var execution = new CommandExecution(this);
            var original = RunQueuedSpaceActionAsync(start.Task, action, actualOriginAndLoaderSettlement, selection, epoch, execution);
            execution.Original = original;
            _commands.RemoveAll(task => task.IsCompletedSuccessfully);
            _commands.Add(actualOriginAndLoaderSettlement);
            _commands.Add(original);
            _lastQueuedNavigation = original;
            start.SetResult();
            ObserveQueuedSpaceAction(original);
            return true;
        }
    }

    private static async void ObserveQueuedSpaceAction(Task original)
    {
        try { await original.ConfigureAwait(false); }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Spaces retained queued action failure: {0}", failure); }
    }

    private async Task RunQueuedSpaceActionAsync(Task start, SpacesAction action, Task settlement,
        SpacesHomeSelection selection, long epoch, CommandExecution execution)
    {
        await start.ConfigureAwait(false);
        Task actual = settlement;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_token);
        var serialEntered = false;
        try
        {
            // Never hold the serial or retire the originating page while its actual command
            // or CUI loader action is still running.
            await actual.ConfigureAwait(false);
            actual = _commandSerial.WaitAsync(linked.Token);
            await actual.ConfigureAwait(false);
            serialEntered = true;
            actual = OnUiAsync(async () =>
            {
                var prior = ExecutingCommand.Value;
                ExecutingCommand.Value = execution; Volatile.Write(ref execution.Active, true);
                try
                {
                    linked.Token.ThrowIfCancellationRequested(); DemandAlive();
                    if (_routeEpoch != epoch || !ReferenceEquals(_spacesPage?.Controller.State.Selection, selection))
                        throw new InvalidOperationException("The admitted Spaces selection or route changed before navigation.");
                    await ExecuteActionOnUiAsync(action, linked.Token);
                }
                finally { ExecutingCommand.Value = prior; }
            });
            await actual.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            Volatile.Write(ref execution.Active, true);
            var failures = new List<Exception>(); AddActualCauses(failures, actual, failure);
            try
            {
                await OnUiAsync(() =>
                {
                    var prior = ExecutingCommand.Value; ExecutingCommand.Value = execution;
                    try
                    {
                        SpacesHomePage? currentHome = null;
                        // Report the queued failure even when a previous chat route is already
                        // published, so a failed new selection cannot silently leave the old
                        // actionable chat under the advanced rail selection. The home's own
                        // ReportQueuedActionFailure is responsible for coherent rollback or
                        // explicit unavailable presentation tied to the requested Space.
                        lock (_gate)
                            if (!_closing && _published && _routeEpoch == epoch && _publishedDetailsRoute is null &&
                                _spacesPage is { } home)
                                currentHome = home;
                        currentHome?.Controller.ReportQueuedActionFailure(selection, action, failure);
                        return Task.CompletedTask;
                    }
                    finally { ExecutingCommand.Value = prior; }
                }).ConfigureAwait(false);
            }
            catch (Exception observation) { failures.Add(observation); }
            if (failures.Count == 1 && actual.IsCanceled && _token.IsCancellationRequested &&
                failure is OperationCanceledException canceled && canceled.CancellationToken == linked.Token)
                ExceptionDispatchInfo.Capture(failure).Throw(); // This exact issued canceled source only.
            ThrowActualCauses(failures, "The original queued Spaces action failed.");
        }
        finally { Volatile.Write(ref execution.Active, false); if (serialEntered) _commandSerial.Release(); }
    }

    private async Task ExecuteActionOnUiAsync(SpacesAction action, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        switch (action.Kind)
        {
            case SpacesActionKind.Open:
                await WithCurrentEditorPreparationAsync(() => ExecuteChatOnUiAsync(action, token), token);
                break;
            case SpacesActionKind.EditDetails:
                if (action.Context.Scope.RegisteredSpaceId is not { } id ||
                    action.Context.Scope != SpaceScope.ForCustom(id) || action.Context.Definition.IsBuiltIn)
                    throw new InvalidOperationException("Retain the canonical user-created Details target.");
                await OpenBuilderOnUiAsync(id, token);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown Spaces action.");
        }
    }

    private static Guid? ReadDetailsTarget(AppContentAddress address)
    {
        var segments = address.ResourceSegments;
        if (segments.Count == 0 || segments[0] != "details") return null;
        if (segments.Count == 2 && Guid.TryParseExact(segments[1], "D", out var id) && id != Guid.Empty) return id;
        throw new FormatException("The explicit Space details address is invalid.");
    }

    internal async Task<bool> TryRestoreRequestedDetailsAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ReadDetailsTarget(_request.Address) is not { } id) return false;
        await OpenDetailsOnUiAsync(id, token);
        return true;
    }

    private async Task DemandDetailsHomeAdmissionAsync(CancellationToken token)
    {
        if (_request.Privacy != BrowserTabPrivacy.Standard)
            throw new UnauthorizedAccessException("Space details require the authenticated standard profile.");
        var home = _services.GetRequiredService<HomeNativeWindowsComposition>();
        if (home.OriginalStartTask is { } started) await started;
        var actor = await home.Profiles.GetCurrentAsync(token)
            ?? throw new UnauthorizedAccessException("The authenticated Home profile is unavailable.");
        if (!string.Equals(actor.ProfileId, _request.ProfileId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The Space details target belongs to a different authenticated profile.");
        foreach (var required in new[] { "home.core", "home.state", "permissions.trust" })
        {
            var service = home.Runtime.Current.Services.SingleOrDefault(item => item.ServiceId == required);
            if (service is null || !service.IsAvailable || service.State != HomeServiceLifecycleState.Ready)
                throw new UnauthorizedAccessException("A required Home service is unavailable for Space details.");
        }
        token.ThrowIfCancellationRequested(); DemandAlive();
    }

    private async Task OpenDetailsOnUiAsync(Guid id, CancellationToken token)
    {
        await DemandDetailsHomeAdmissionAsync(token);
        var registry = _services.GetRequiredService<SpaceRegistry>();
        var space = await registry.ReadExistingAsync(id, token)
            ?? throw new InvalidOperationException("The requested Space details are unavailable.");
        if (space.Id != id || space.IsArchived || space.IsBuiltIn || space.Origin != SpaceOrigin.UserCreated)
            throw new InvalidOperationException("Only an active user-created Space has editable details.");
        if (_publishedDetailsRoute?.Page is { } current && current.State.SpaceId == id) return;
        await WithCurrentEditorPreparationAsync(async () =>
        {
            SpaceDetailsPage? page = null;
            var acquired = false;
            var published = false;
            Exception? failure = null;
            try
            {
                if (!_detailsBySpace.TryGetValue(id, out page) || page.OriginalClose is not null)
                {
                    page = new SpaceDetailsPage(registry, id, SpacesHomePage.ImmediateReadinessInstance,
                        settlement => QueueHomeReturn(page!, settlement));
                    lock (_gate) _ownedDetails.Add(page); // Custody before activation or another currentness check.
                    acquired = true;
                    await page.ActivateAsync(token);
                }
                if (page.EditorShow is not { IsCompletedSuccessfully: true } shown ||
                    shown.Result.State != CuiSceneAvailabilityState.Ready || page.State.SpaceId != id ||
                    !page.State.IsLoaded || page.State.IsReadOnly || page.State.IsBusy)
                    throw new InvalidOperationException("The real Space details page did not complete admission.");
                await DemandDetailsHomeAdmissionAsync(token);
                var currentSpace = await registry.ReadExistingAsync(id, token);
                if (currentSpace is null || currentSpace.IsArchived || currentSpace.IsBuiltIn || currentSpace.Origin != SpaceOrigin.UserCreated)
                    throw new InvalidOperationException("The Space details target changed during admission.");
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested(); DemandAlive();
                    if (_activeDetailsPreparation is not null) throw new InvalidOperationException("Abort the session close preparation before navigating.");
                    SetRootContent(page); // Only successful native publication changes the immutable projection.
                    if (_publishedChatRoute is { } priorChat) _returnChatConversationId = priorChat.Page.CurrentConversation.Id;
                    _publishedDetailsRoute = new(page, AppContentAddress.Parse($"9to1://spaces/details/{id:D}"));
                    _publishedChatRoute = null;
                    _publishedBuilderRoute = null;
                    _address = _publishedDetailsRoute.Address;
                    _routeEpoch++;
                    _detailsBySpace[id] = page;
                    published = true;
                }
                _spacesPage?.Deactivate();
                PublishStateChanged();
            }
            catch (Exception cause) { failure = cause; }
            if (!published && acquired && page is not null)
            {
                var failures = new List<Exception>(); if (failure is not null) failures.Add(failure);
                try
                {
                    var close = page.CloseAndDrainAsync();
                    lock (_gate) _detailsCloses[page] = close;
                    try { await close; } catch (Exception cause) { AddActualCauses(failures, close, cause); }
                    if (close.IsCompletedSuccessfully)
                        lock (_gate) { _ownedDetails.Remove(page); _detailsCloses.Remove(page); }
                }
                catch (Exception cleanup) { failures.Add(cleanup); }
                ThrowActualCauses(failures, "Space details admission and acquired-page cleanup failed.");
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }, token);
    }

    private async Task WithCurrentDetailsPreparationAsync(Func<Task> transition, CancellationToken token)
    {
        SpaceDetailsPage? current; lock (_gate) current = _publishedDetailsRoute?.Page;
        SpaceDetailsPage.ClosePreparation? lease = null;
        Exception? failure = null;
        try
        {
            if (current is not null)
            {
                var actual = current.PrepareCloseAsync(token); lock (_gate) RetainPreparation(actual);
                lease = await actual;
                if (lease is null) return; // Actual Save/Discard/Cancel refusal leaves the same view and draft live.
                await lease.Settlement;
                if (!lease.IsCurrent) throw new InvalidOperationException("The visible details preparation changed before navigation.");
            }
            await transition();
        }
        catch (Exception cause) { failure = cause; }
        finally
        {
            if (lease is not null)
            {
                try
                {
                    // The old editor remains an owned (possibly hidden) view. Re-enable it only
                    // after the successful UI swap; failures restore its still-visible ownership.
                    var abort = current!.AbortCloseAsync(lease); lock (_gate) RetainPreparation(abort);
                    if (!await abort) throw new InvalidOperationException("The retained editor preparation could not be restored.");
                }
                catch (Exception abortFailure)
                { failure = failure is null ? abortFailure : new AggregateException(failure, abortFailure); }
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private bool QueueHomeReturn(SpaceDetailsPage page, Task settlement)
    {
        lock (_gate)
        {
            if (_closing || !_published || _activeDetailsPreparation is not null || !ReferenceEquals(_publishedDetailsRoute?.Page, page)) return false;
            var epoch = _routeEpoch;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var execution = new CommandExecution(this);
            var original = ReturnHomeAsync(start.Task, settlement, epoch, execution);
            execution.Original = original;
            _commands.RemoveAll(task => task.IsCompletedSuccessfully);
            _commands.Add(settlement); _commands.Add(original); _lastQueuedNavigation = original;
            start.SetResult(); ObserveQueuedSpaceAction(original);
            return true;
        }
    }

    private async Task ReturnHomeAsync(Task start, Task settlement, long epoch, CommandExecution execution)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_token);
        var entered = false;
        Task actual = settlement;
        try
        {
            await actual.ConfigureAwait(false);
            actual = _commandSerial.WaitAsync(linked.Token); await actual.ConfigureAwait(false); entered = true;
            actual = OnUiAsync(async () =>
            {
                var previous = ExecutingCommand.Value; ExecutingCommand.Value = execution; Volatile.Write(ref execution.Active, true);
                try
                {
                    DemandAlive(); linked.Token.ThrowIfCancellationRequested();
                    if (_routeEpoch != epoch) throw new InvalidOperationException("The original Details route changed before returning.");
                    await WithCurrentEditorPreparationAsync(async () =>
                    {
                        var home = _spacesPage ?? throw new InvalidOperationException("The original Spaces Home is unavailable.");
                        await home.ActivateAsync(linked.Token);
                        lock (_gate)
                        {
                            DemandAlive(); linked.Token.ThrowIfCancellationRequested();
                            SetRootContent(home);
                            _publishedDetailsRoute = null; _publishedChatRoute = null; _publishedBuilderRoute = null;
                            _routeEpoch++; UpdateAddress(home.Controller.State.Selection?.Id);
                        }
                        PublishStateChanged();
                    }, linked.Token);
                }
                finally { ExecutingCommand.Value = previous; }
            });
            await actual.ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            if (actual.IsCanceled && _token.IsCancellationRequested && cause is OperationCanceledException canceled && canceled.CancellationToken == linked.Token)
                ExceptionDispatchInfo.Capture(cause).Throw();
            var failures = new List<Exception>(); AddActualCauses(failures, actual, cause);
            ThrowActualCauses(failures, "The actual return to Spaces failed.");
        }
        finally { Volatile.Write(ref execution.Active, false); if (entered) _commandSerial.Release(); }
    }

    private static void AddActualCauses(List<Exception> failures, Task actual, Exception caught)
    {
        foreach (var cause in actual.Exception is { } group ? group.InnerExceptions : [caught])
            if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
    }
    private static void ThrowActualCauses(List<Exception> failures, string message)
    {
        if (failures.Count == 0) return;
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException(message, failures);
    }
}
