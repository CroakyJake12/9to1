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
    private sealed record PublishedBuilderRoute(SpaceBuilderPage Page, AppContentAddress Address);
    private PublishedBuilderRoute? _publishedBuilderRoute;
    private readonly List<SpaceBuilderPage> _ownedBuilders = [];
    private readonly Dictionary<Guid, SpaceBuilderPage> _buildersBySpace = [];
    private readonly Dictionary<SpaceBuilderPage, Task> _builderCloses = [];

    internal async Task<bool> TryRestoreRequestedBuilderAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var segments = _request.Address.ResourceSegments;
        if (segments.Count == 0 || segments[0] != "builder") return false;
        if (segments.Count != 2 || !Guid.TryParseExact(segments[1], "D", out var id) || id == Guid.Empty)
            throw new FormatException("The explicit Space builder address is invalid.");
        await OpenBuilderOnUiAsync(id, token);
        return true;
    }

    private async Task OpenBuilderOnUiAsync(Guid id, CancellationToken token)
    {
        var registry = _services.GetRequiredService<SpaceRegistry>();
        var space = await registry.ReadExistingAsync(id, token)
            ?? throw new InvalidOperationException("The requested Space builder is unavailable.");
        if (space.Id != id || space.IsArchived || space.IsBuiltIn || space.Origin != SpaceOrigin.UserCreated)
            throw new InvalidOperationException("Only an active user-created Space has an editable builder.");
        if (_publishedBuilderRoute?.Page is { } current && current.State.SpaceId == id) return;
        await WithCurrentEditorPreparationAsync(async () =>
        {
            SpaceBuilderPage? page = null;
            var acquired = false;
            var published = false;
            Exception? failure = null;
            try
            {
                if (!_buildersBySpace.TryGetValue(id, out page) || page.OriginalClose is not null)
                {
                    page = new SpaceBuilderPage(registry, id, SpacesHomePage.ImmediateReadinessInstance,
                        settlement => QueueHomeReturnForBuilder(page!, settlement));
                    lock (_gate) _ownedBuilders.Add(page);
                    acquired = true;
                    await page.ActivateAsync(token);
                }
                if (page.Show is not { IsCompletedSuccessfully: true } shown ||
                    shown.Result.State != CuiSceneAvailabilityState.Ready || page.State.SpaceId != id ||
                    !page.State.IsLoaded || page.State.IsReadOnly || page.State.IsBusy)
                    throw new InvalidOperationException("The real Space builder page did not complete admission.");
                var currentSpace = await registry.ReadExistingAsync(id, token);
                if (currentSpace is null || currentSpace.IsArchived || currentSpace.IsBuiltIn || currentSpace.Origin != SpaceOrigin.UserCreated)
                    throw new InvalidOperationException("The Space builder target changed during admission.");
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested(); DemandAlive();
                    if (_activeDetailsPreparation is not null) throw new InvalidOperationException("Abort the session close preparation before navigating.");
                    SetRootContent(page);
                    _publishedBuilderRoute = new(page, AppContentAddress.Parse($"9to1://spaces/builder/{id:D}"));
                    _publishedChatRoute = null;
                    _address = _publishedBuilderRoute.Address;
                    _routeEpoch++;
                    _buildersBySpace[id] = page;
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
                    lock (_gate) _builderCloses[page] = close;
                    try { await close; } catch (Exception cause) { if (cause is not null) failures.Add(cause); }
                    if (close.IsCompletedSuccessfully)
                        lock (_gate) { _ownedBuilders.Remove(page); _builderCloses.Remove(page); }
                }
                catch (Exception cleanup) { failures.Add(cleanup); }
                if (failures.Count > 0)
                    ThrowActualCauses(failures, "Space builder admission and cleanup failed.");
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }, token);
    }

    private bool QueueHomeReturnForBuilder(SpaceBuilderPage page, Task settlement)
    {
        lock (_gate)
        {
            if (_closing || !_published || !ReferenceEquals(_publishedBuilderRoute?.Page, page)) return false;
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

    private async Task WithCurrentEditorPreparationAsync(Func<Task> transition, CancellationToken token)
    {
        SpaceBuilderPage? builderCurrent; SpaceDetailsPage? detailsCurrent;
        lock (_gate) { builderCurrent = _publishedBuilderRoute?.Page; detailsCurrent = _publishedDetailsRoute?.Page; }
        SpaceBuilderPage.ClosePreparation? builderLease = null;
        SpaceDetailsPage.ClosePreparation? detailsLease = null;
        Exception? failure = null;
        try
        {
            if (builderCurrent is not null)
            {
                var actual = builderCurrent.PrepareCloseAsync(token); lock (_gate) RetainPreparation(actual);
                builderLease = await actual;
                if (builderLease is null) return;
                await builderLease.Settlement;
                if (!builderLease.IsCurrent) throw new InvalidOperationException("The visible builder preparation changed before navigation.");
            }
            else if (detailsCurrent is not null)
            {
                var actual = detailsCurrent.PrepareCloseAsync(token); lock (_gate) RetainPreparation(actual);
                detailsLease = await actual;
                if (detailsLease is null) return;
                await detailsLease.Settlement;
                if (!detailsLease.IsCurrent) throw new InvalidOperationException("The visible details preparation changed before navigation.");
            }
            await transition();
        }
        catch (Exception cause) { failure = cause; }
        finally
        {
            if (builderLease is not null)
            {
                try
                {
                    var abort = builderCurrent!.AbortCloseAsync(builderLease); lock (_gate) RetainPreparation(abort);
                    if (!await abort) throw new InvalidOperationException("The retained builder preparation could not be restored.");
                }
                catch (Exception abortFailure)
                { failure = failure is null ? abortFailure : new AggregateException(failure, abortFailure); }
            }
            else if (detailsLease is not null)
            {
                try
                {
                    var abort = detailsCurrent!.AbortCloseAsync(detailsLease); lock (_gate) RetainPreparation(abort);
                    if (!await abort) throw new InvalidOperationException("The retained details preparation could not be restored.");
                }
                catch (Exception abortFailure)
                { failure = failure is null ? abortFailure : new AggregateException(failure, abortFailure); }
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void BeginBuilderRetirement(List<Exception> failures)
    {
        foreach (var page in _ownedBuilders.Where(p => p.OriginalClose is null).ToArray())
        {
            try { page.DemandExternalOriginalRetirementJoin(); }
            catch (Exception error) { failures.Add(error); }
        }
    }

    private async Task JoinBuilderRetirementAsync(List<Exception> failures)
    {
        SpaceBuilderPage[] pages; Dictionary<SpaceBuilderPage, Task> closes;
        lock (_gate) { pages = _ownedBuilders.Where(p => p.OriginalClose is null).ToArray(); closes = new(_builderCloses); }
        foreach (var page in pages)
        {
            try
            {
                var close = page.CloseAndDrainAsync();
                lock (_gate) _builderCloses[page] = close;
                await close;
                if (close.IsCompletedSuccessfully)
                    lock (_gate) { _ownedBuilders.Remove(page); _builderCloses.Remove(page); }
            }
            catch (Exception error) { failures.Add(error); }
        }
    }
}
