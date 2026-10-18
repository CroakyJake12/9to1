using Haven.Desktop.Views.Pages.Spaces;
using Avalonia.Controls;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;

namespace Haven.Desktop.Services;

internal sealed partial class SpacesBrowseAppContentSession
{
    private SessionPreparation? _activeDetailsPreparation;
    private Task<IBrowseAppContentClosePreparation?>? _cohortPreparation;
    private Task? _spacesClose;
    private void RetainPreparation(Task actual)
    {
        // Active leases retain their exact sources. Healthy completed historical drivers
        // need no extra owner root; pending and failed originals remain in close custody.
        _detailsPreparations.RemoveAll(task => task.IsCompletedSuccessfully);
        _detailsPreparations.Add(actual);
    }

    public bool SupportsReversibleClosePreparation => true;

    private sealed class SessionPreparation(SpacesBrowseAppContentSession owner,
        SpaceDetailsPage[] pages, SpacesHomeViewState? homeState, long epoch) : IBrowseAppContentClosePreparation
    {
        internal readonly SpacesBrowseAppContentSession Owner = owner;
        internal readonly SpaceDetailsPage[] Pages = pages;
        internal SpacesHomeViewState? HomeState = homeState;
        internal SpacesHomePage.HomeClosePreparation? HomeLease;
        internal long Epoch = epoch;
        internal readonly List<(SpaceDetailsPage Page, SpaceDetailsPage.ClosePreparation Lease)> Leases = [];
        internal bool Ready, Aborted, Accepted;
        internal Task? Abort, Close;
        public IBrowseAppContentSession Session => Owner;
        public bool IsCurrent => Owner.IsCurrentPreparation(this);
        public Task AbortAsync() => Owner.AbortPreparation(this);
        public Task BeginClose() => Owner.BeginPreparedClose(this);
    }

    public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default)
    {
        DemandExternalJoin();
        lock (_gate)
        {
            DemandAlive();
            if (!_published || _page is null || _initialization is { IsCompleted: false })
                return Task.FromResult<IBrowseAppContentClosePreparation?>(null);
            if (_activeDetailsPreparation is not null) return _cohortPreparation!;
            // A live navigation owns its own original+loader settlement. Closing must not
            // queue a decision behind, or join, the originating command from that command.
            if (_commands.Any(task => !task.IsCompleted))
                return Task.FromResult<IBrowseAppContentClosePreparation?>(null);
            var candidate = new SessionPreparation(this,
                _ownedDetails.Where(page => page.OriginalClose is null).ToArray(),
                _spacesPage?.Controller.State, _routeEpoch);
            _activeDetailsPreparation = candidate;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _cohortPreparation = PrepareCohortAsync(start.Task, candidate, cancellationToken);
            RetainPreparation(_cohortPreparation);
            start.SetResult();
            return _cohortPreparation;
        }
    }

    private async Task<IBrowseAppContentClosePreparation?> PrepareCohortAsync(Task start,
        SessionPreparation candidate, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var failures = new List<Exception>();
        var accepted = false;
        async Task<bool> AcquireAsync()
        {
            linked.Token.ThrowIfCancellationRequested();
            Task[] earlier;
            lock (_gate) earlier = _commands.Where(task => !task.IsCompletedSuccessfully).ToArray();
            foreach (var original in earlier)
                try { await original.ConfigureAwait(false); }
                catch (Exception cause) { AddActualCauses(failures, original, cause); }
            ThrowActualCauses(failures, "An earlier original Spaces command failed.");
            if (_spacesPage is { } home)
            {
                var original = home.PrepareHomeCloseAsync(linked.Token);
                lock (_gate) RetainPreparation(original);
                var lease = await original.ConfigureAwait(false);
                if (lease is null) return false;
                lock (_gate) { candidate.HomeLease = lease; candidate.HomeState = lease.State; }
            }
            foreach (var page in candidate.Pages)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!await PrepareEditorAsync(candidate, page, linked.Token).ConfigureAwait(false)) return false;
            }
            if (candidate.HomeLease is { } currentHome && !currentHome.IsCurrent) return false;
            lock (_gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                candidate.Ready = true;
                if (!IsOwnCurrentPreparation(candidate)) { candidate.Ready = false; return false; }
                return true;
            }
        }
        try { accepted = await AcquireAsync().ConfigureAwait(false); }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            if (!accepted)
            {
                try { await AbortPreparation(candidate).ConfigureAwait(false); }
                catch (Exception cause) { AddActualCauses(failures, candidate.Abort!, cause); }
            }
        }
        ThrowActualCauses(failures, "The actual Spaces close preparation failed.");
        return accepted ? candidate : null;
    }

    private async Task<bool> PrepareEditorAsync(SessionPreparation candidate, SpaceDetailsPage page, CancellationToken token)
    {
        Control? priorView = null;
        PublishedDetailsRoute? priorDetails = null;
        PublishedChatRoute? priorChat = null;
        Haven.Core.AppContentAddress? priorAddress = null;
        var switched = false;
        var failures = new List<Exception>();
        var accepted = false;
        Task? actual = null;
        try
        {
            actual = OnUiAsync(() =>
            {
                lock (_gate)
                {
                    DemandAlive(); token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(_activeDetailsPreparation, candidate)) throw new InvalidOperationException("The editor cohort changed.");
                    if ((page.State.HasChanges || page.State.HasConflict) && !ReferenceEquals(_publishedDetailsRoute?.Page, page))
                    {
                        priorView = _root.Content as Control;
                        priorDetails = _publishedDetailsRoute; priorChat = _publishedChatRoute; priorAddress = _address;
                        SetRootContent(page);
                        _publishedDetailsRoute = new(page, Haven.Core.AppContentAddress.Parse($"9to1://spaces/details/{page.State.SpaceId:D}"));
                        _publishedChatRoute = null; _address = _publishedDetailsRoute.Address;
                        candidate.Epoch = ++_routeEpoch;
                        switched = true;
                    }
                }
                if (switched) PublishStateChanged();
                return Task.CompletedTask;
            });
            await actual.ConfigureAwait(false);
            var original = page.PrepareCloseAsync(token);
            actual = original; lock (_gate) RetainPreparation(original);
            var lease = await original.ConfigureAwait(false);
            if (lease is not null)
            {
                lock (_gate) candidate.Leases.Add((page, lease));
                actual = lease.Settlement;
                lock (_gate) RetainPreparation(actual);
                await actual.ConfigureAwait(false);
                accepted = lease.IsCurrent;
            }
        }
        catch (Exception cause)
        { if (actual is null) failures.Add(cause); else AddActualCauses(failures, actual, cause); }
        finally
        {
            if (switched)
                try
                {
                    actual = OnUiAsync(() =>
                    {
                        lock (_gate)
                        {
                            if (_closing) return Task.CompletedTask; // Never republish a retiring view.
                            if (!ReferenceEquals(_activeDetailsPreparation, candidate)) throw new InvalidOperationException("The editor decision lost its owner.");
                            // A real already-admitted Chat transition may have completed while
                            // its view was hidden for this decision. Derive from that SAME page
                            // on its UI owner; the saved projection is not a fresh identity proof.
                            if (priorChat is not null && !ReferenceEquals(priorView, priorChat.Page))
                                throw new InvalidOperationException("The retained Chat view and route owner disagree.");
                            var restoredChat = priorChat is null ? null : new PublishedChatRoute(priorChat.Page, ConversationAddress(priorChat.Page.CurrentConversation));
                            var restoredAddress = restoredChat?.Address ?? priorAddress!;
                            SetRootContent(priorView);
                            _publishedDetailsRoute = priorDetails; _publishedChatRoute = restoredChat; _address = restoredAddress;
                            candidate.Epoch = ++_routeEpoch;
                        }
                        PublishStateChanged();
                        return Task.CompletedTask;
                    });
                    await actual.ConfigureAwait(false);
                }
                catch (Exception cause)
                { if (actual is null) failures.Add(cause); else AddActualCauses(failures, actual, cause); }
        }
        ThrowActualCauses(failures, "The actual editor decision and prior-view restoration failed.");
        return accepted;
    }

    private bool IsCurrentPreparation(SessionPreparation preparation)
    {
        // Home mutations publish into this session while holding their Home admission
        // gate. Never invert that order by acquiring Home's gate under the session gate.
        if (preparation.HomeLease is { } home && !home.IsCurrent) return false;
        lock (_gate) return IsOwnCurrentPreparation(preparation);
    }

    private bool IsOwnCurrentPreparation(SessionPreparation preparation) =>
        ReferenceEquals(preparation.Owner, this) && ReferenceEquals(_activeDetailsPreparation, preparation) &&
        preparation.Ready && !preparation.Aborted && !preparation.Accepted && !_closing &&
        !_token.IsCancellationRequested && _routeEpoch == preparation.Epoch &&
        (_spacesPage is null || preparation.HomeLease is not null) &&
        !_commands.Any(task => !task.IsCompletedSuccessfully) &&
        _ownedDetails.Where(page => page.OriginalClose is null).SequenceEqual(preparation.Pages) &&
        preparation.Leases.Count == preparation.Pages.Length &&
        preparation.Leases.All(item => item.Lease.IsCurrent && item.Lease.Settlement.IsCompletedSuccessfully);

    private Task AbortPreparation(SessionPreparation preparation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(preparation.Owner, this)) throw new InvalidOperationException("Foreign Spaces preparation.");
            if (preparation.Accepted) return preparation.Close!;
            if (preparation.Abort is not null) return preparation.Abort;
            preparation.Aborted = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            preparation.Abort = AbortCohortAsync(start.Task, preparation);
            RetainPreparation(preparation.Abort);
            start.SetResult();
            return preparation.Abort;
        }
    }

    private async Task AbortCohortAsync(Task start, SessionPreparation preparation)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        foreach (var (page, lease) in preparation.Leases.ToArray())
        {
            Task<bool>? actual = null;
            try
            {
                actual = page.AbortCloseAsync(lease);
                if (!await actual.ConfigureAwait(false) && !_closing)
                    throw new InvalidOperationException("An acquired Space details preparation could not be restored.");
            }
            catch (Exception cause)
            {
                if (actual is null) failures.Add(cause); else AddActualCauses(failures, actual, cause);
            }
        }
        if (preparation.HomeLease is { } homeLease)
        {
            Task? actual = null;
            try { actual = homeLease.Owner.AbortHomeCloseAsync(homeLease); await actual.ConfigureAwait(false); }
            catch (Exception cause)
            { if (actual is null) failures.Add(cause); else AddActualCauses(failures, actual, cause); }
        }
        lock (_gate)
            if (ReferenceEquals(_activeDetailsPreparation, preparation)) _activeDetailsPreparation = null;
        ThrowActualCauses(failures, "Spaces could not restore all acquired editor preparations.");
    }

    private Task BeginPreparedClose(SessionPreparation preparation)
    {
        DemandExternalJoin();
        if (preparation.HomeLease is { } home)
            return home.Owner.AcceptHomeClosePreparation(home, () => BeginPreparedCloseUnderHomeGate(preparation));
        return BeginPreparedCloseUnderHomeGate(preparation);
    }

    private Task BeginPreparedCloseUnderHomeGate(SessionPreparation preparation)
    {
        lock (_gate)
        {
            if (!IsOwnCurrentPreparation(preparation))
                throw new InvalidOperationException("This exact Spaces preparation is no longer current.");
            var acquired = false;
            if (preparation.HomeLease is { } homeLease)
            {
                try { _spacesClose = homeLease.Owner.BeginHomeClosePrepared(homeLease); acquired = true; }
                catch (Exception cause)
                {
                    if (homeLease.Owner.OriginalClose is not { } original) throw;
                    _spacesClose = original; acquired = true; _preparedCloseFailures.Add(cause);
                }
            }
            foreach (var (page, lease) in preparation.Leases)
            {
                try { RecordDetailsClose(page, page.BeginClosePrepared(lease)); acquired = true; }
                catch (Exception cause)
                {
                    if (page.OriginalClose is { } original) { RecordDetailsClose(page, original); acquired = true; }
                    if (!acquired) throw; // No irreversible acceptance; external owner can still abort.
                    _preparedCloseFailures.Add(cause); // Partial acceptance must drain every sibling and retain its failure.
                }
            }
            preparation.Accepted = true;
            return preparation.Close = CloseAndDrainAsync();
        }
    }

    private void RecordDetailsClose(SpaceDetailsPage page, Task original)
    {
        if (_detailsCloses.TryGetValue(page, out var prior) && !ReferenceEquals(prior, original))
            throw new InvalidOperationException("The details page replaced its published original close.");
        _detailsCloses[page] = original;
    }

    private void BeginDetailsRetirement(List<Exception> failures)
    {
        SpaceDetailsPage[] pages;
        lock (_gate) pages = _ownedDetails.ToArray();
        foreach (var page in pages)
            try { lock (_gate) RecordDetailsClose(page, page.CloseAndDrainAsync()); }
            catch (Exception cause)
            {
                failures.Add(cause);
                if (page.OriginalClose is { } original) lock (_gate) RecordDetailsClose(page, original);
            }
        // Home owns the actual originating command/CUI loader. Withdraw and cancel that
        // owner before waiting for a queued driver that may still be joining those originals.
        if (_spacesPage is { } home)
            try { _spacesClose ??= home.CloseAndDrainAsync(); }
            catch (Exception cause) { failures.Add(cause); _spacesClose ??= home.OriginalClose; }
    }

    private async Task JoinDetailsRetirementAsync(List<Exception> failures)
    {
        KeyValuePair<SpaceDetailsPage, Task>[] originals;
        lock (_gate) { originals = _detailsCloses.ToArray(); failures.AddRange(_preparedCloseFailures); }
        foreach (var (page, original) in originals)
        {
            try { await original.ConfigureAwait(false); }
            catch (Exception cause) { AddActualCauses(failures, original, cause); }
            if (original.IsCompletedSuccessfully)
                lock (_gate)
                {
                    _ownedDetails.Remove(page); _detailsCloses.Remove(page);
                    foreach (var key in _detailsBySpace.Where(pair => ReferenceEquals(pair.Value, page)).Select(pair => pair.Key).ToArray())
                        _detailsBySpace.Remove(key);
                }
        }
        if (_spacesClose is { } homeClose)
            try { await homeClose.ConfigureAwait(false); }
            catch (Exception cause) { AddActualCauses(failures, homeClose, cause); }
    }
}
