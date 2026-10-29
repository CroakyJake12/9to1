using System.Runtime.ExceptionServices;
using Haven.Core;
using Haven.Browser;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseChrome
{
    private readonly HashSet<ContentTransition> _contentTransitions = [];
    private readonly List<BrowseCloseRefusal> _closeRefusals = [];

    /// <summary>Why the most recent close attempt was declined, in the chrome's own words.</summary>
    public IReadOnlyList<BrowseCloseRefusal> CloseRefusals
    { get { lock (_nativeSourceGate) return _closeRefusals.ToArray(); } }

    private BrowseCloseVetoException Veto(string site, string detail)
    {
        ReportCloseRefusal("veto@" + site, detail);
        return new BrowseCloseVetoException();
    }

    private void ReportCloseRefusal(string kind, string detail)
    {
        lock (_nativeSourceGate)
        {
            // Bounded: a refused close must not grow an unbounded ledger. The first causes are the
            // ones that explain the abort; later repeats from the same attempt add nothing.
            if (_closeRefusals.Count < 16) _closeRefusals.Add(new BrowseCloseRefusal(kind, detail));
        }
    }

    private readonly AsyncLocal<ContentTransition?> _executingContentTransition = new();
    private readonly AsyncLocal<ClosePreparation?> _executingWindowPreparation = new();
    private readonly AsyncLocal<ClosePreparation?> _executingWindowCommit = new();
    private ClosePreparation? _windowPreparation;
    private readonly List<Task> _originalContentTransitions = [];
    public IReadOnlyList<Task> OriginalContentTransitions
    { get { lock (_nativeSourceGate) return _originalContentTransitions.ToArray(); } }
    private void RetainContentSource(Task actual, ContentTransition? fence = null)
    {
        lock (_nativeSourceGate)
        {
            (fence ?? _executingContentTransition.Value)?.Retained.Add(actual);
            _originalContentTransitions.RemoveAll(task => task.IsCompletedSuccessfully);
            PruneAdmissionRefusalsLocked(_originalContentTransitions);
            _originalContentTransitions.Add(actual);
        }
    }

    // These private, exact live admissions fence topology changes, not a timing
    // counter or a substitute completion receipt for an app/engine source.
    private sealed class ContentTransition(BrowseChrome owner) : IDisposable
    {
        /// <summary>The enclosing live admission, so a witnessed admission can prove which
        /// tasks are its own propagation chain rather than any later task that happens to
        /// carry the same exception.</summary>
        public ContentTransition? Parent;

        /// <summary>The exact tasks this fence retained while it was live.</summary>
        public readonly List<Task> Retained = [];

        public void Dispose() { lock (owner._nativeSourceGate) owner._contentTransitions.Remove(this); }
    }
    private ContentTransition AdmitContentTransition()
    {
        lock (_nativeSourceGate)
        {
            ThrowIfCannotAccept();
            if (_windowPreparation is not null)
                throw new InvalidOperationException("The window has a reversible close preparation. Abort it before changing content.");
            // A refused native admission acquired nothing, so it must not consume
            // unresolved transition capacity forever. It moves to the chrome's
            // inspectable admission-refusal ledger instead.
            PruneAdmissionRefusalsLocked(_originalContentTransitions);
            if (_originalContentTransitions.Count(task => !task.IsCompletedSuccessfully) >= 128)
                throw new InvalidOperationException("Retire this host before admitting more unresolved content transitions.");
            var actual = new ContentTransition(this) { Parent = _executingContentTransition.Value };
            _contentTransitions.Add(actual);
            return actual;
        }
    }
    private Task<T> RetainContentTransition<T>(Func<Task<T>> operation, CancellationToken token = default, bool blocksRetirement = false)
    {
        token.ThrowIfCancellationRequested();
        var admission = AdmitContentTransition();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = RunAsync();
        RetainContentSource(original, admission);
        if (blocksRetirement) RetainAppSource(original, admission);
        start.SetResult(); return original;
        async Task<T> RunAsync()
        {
            using (admission)
            {
                await start.Task.ConfigureAwait(false);
                var previous = _executingContentTransition.Value;
                _executingContentTransition.Value = admission;
                try
                {
                    var actual = operation() ?? throw new InvalidOperationException("The content transition supplied no original task.");
                    RetainContentSource(actual, admission);
                    if (blocksRetirement) RetainAppSource(actual, admission);
                    return await actual.ConfigureAwait(false);
                }
                finally { _executingContentTransition.Value = previous; }
            }
        }
    }

    private sealed class ContentCloseAttempt(TabRuntime tab, AppContentOwner? owner)
    {
        internal readonly TabRuntime Tab = tab;
        internal readonly AppContentOwner? Owner = owner;
        internal readonly AppContentAddress Address = tab.Address;
        internal readonly long Generation = tab.NavigationGeneration;
        internal IBrowseAppContentClosePreparation? Lease;
        internal Task? Abort;
        internal bool Accepted, Beginning;
    }
    private bool IsCurrent(ContentCloseAttempt attempt)
    {
        // Evaluate the subordinate observation first, then revalidate our own exact
        // identities in case its getter reentered a canonical owner notification.
        var leaseCurrent = attempt.Lease?.IsCurrent ?? true;
        return leaseCurrent && _tabs.Contains(attempt.Tab) && ReferenceEquals(attempt.Tab.AppOwner, attempt.Owner) &&
            attempt.Tab.Address == attempt.Address && attempt.Tab.NavigationGeneration == attempt.Generation &&
            (attempt.Owner is null || attempt.Owner.Close is not null || ReferenceEquals(attempt.Owner.Preparation, attempt));
    }
    private string DescribeContentTransitions()
    {
        lock (_nativeSourceGate)
        {
            var index = 0;
            return string.Join(" | ", _contentTransitions.Select(fence =>
            {
                var held = fence.Retained;
                var pending = held.Count(task => !task.IsCompleted);
                var faulted = held.FirstOrDefault(task => task.IsFaulted);
                var cause = faulted?.Exception?.InnerExceptions?.FirstOrDefault();
                return "#" + (++index) + " held=" + held.Count + " unfinished=" + pending +
                    (cause is null ? string.Empty : " fault=" + cause.GetType().Name + ":" +
                        (cause.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault() ?? string.Empty).Substring(0, Math.Min(90,
                            (cause.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                                .FirstOrDefault() ?? string.Empty).Length)));
            }));
        }
    }

    private static string DescribeStaleAttempt(ContentCloseAttempt attempt)
    {
        var owner = attempt.Owner;
        var causes = new List<string>();
        if (attempt.Lease?.IsCurrent != true) causes.Add("the preparation lease is no longer current");
        if (!attempt.Tab.Address.Equals(attempt.Address)) causes.Add("the tab address changed from " +
            attempt.Address + " to " + attempt.Tab.Address);
        if (attempt.Tab.NavigationGeneration != attempt.Generation)
            causes.Add("the tab navigation generation moved " + attempt.Generation + " -> " +
                attempt.Tab.NavigationGeneration);
        if (owner is not null && owner.Close is null && !ReferenceEquals(owner.Preparation, attempt))
            causes.Add("the tab owner's preparation is no longer this attempt");
        return causes.Count == 0 ? "an unlisted IsCurrent condition failed" : string.Join("; ", causes);
    }

    private sealed class ContentAdmissionCanceledException(CancellationToken token) : OperationCanceledException(token);
    private static void DemandContentAdmission(CancellationToken token)
    { if (token.IsCancellationRequested) throw new ContentAdmissionCanceledException(token); }

    private async Task<ContentCloseAttempt?> AcquireContentCloseAsync(TabRuntime tab, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var attempt = new ContentCloseAttempt(tab, tab.AppOwner);
        if (attempt.Owner is not { Close: null } owner) return attempt;
        lock (_nativeSourceGate)
        {
            if (owner.Preparation is not null)
            {
                // Measured on 29 October: this is the only close path that returns null without the
                // app session ever being consulted, and both reproduced window-close aborts were
                // silent here. The tab's owner already carries an earlier attempt, normally one a
                // navigation or a tab close acquired and never released, so the whole window cohort
                // is voided and this tab is never asked. Observation only; the decision is unchanged.
                var stale = owner.Preparation;
                ReportCloseRefusal("owner-already-prepared", "tab " + tab.Address + " still carries an earlier close preparation" +
                    " (lease=" + (stale.Lease is null ? "none" : "held") + ", accepted=" + stale.Accepted +
                    ", beginning=" + stale.Beginning + ", addressThen=" + stale.Address + ", addressNow=" + tab.Address +
                    ", generationThen=" + stale.Generation + ", generationNow=" + tab.NavigationGeneration + ")");
                return null;
            }
            owner.Preparation = attempt;
        }
        try
        {
            if (owner.Session is IBrowseAppContentClosePreparationParticipant { SupportsReversibleClosePreparation: true } participant)
            {
                var actual = participant.PrepareCloseAsync(token) ?? throw new InvalidOperationException("No original reversible preparation task was supplied.");
                RetainAppSource(actual);
                try { attempt.Lease = await actual.ConfigureAwait(false); }
                catch (OperationCanceledException error) when (actual.IsFaulted)
                { throw new AggregateException("The actual preparation faulted with cancellation causes.", actual.Exception?.InnerExceptions ?? [error]); }
                if (attempt.Lease is null) { ClearAttempt(attempt); return null; }
                if (!ReferenceEquals(attempt.Lease.Session, owner.Session))
                    throw new InvalidOperationException("The preparation belongs to a different app session.");
            }
            else
            {
                var actual = owner.Session.TryPrepareToCloseAsync(token) ?? throw new InvalidOperationException("No original close preparation task was supplied.");
                RetainAppSource(actual);
                if (!await actual.ConfigureAwait(false))
                {
                    // A one-shot owner answering false voids the ENTIRE window close, and until now
                    // nothing named it: measured on 29 October against a copy of the owner's saved
                    // session, 10 of 11 Boards tabs reached their own preparation and the eleventh was
                    // never asked, which is exactly the shape of an earlier one-shot refusal followed
                    // by a cohort abort. The address and session type are the only way to see which
                    // tab refused from outside this file. Diagnostic only; the decision is unchanged.
                    ReportCloseRefusal("one-shot-refused", "one-shot close preparation REFUSED by " +
                        owner.Session.GetType().Name + "@" + tab.Address + "; the whole cohort is void");
                    ClearAttempt(attempt); return null;
                }
            }
            token.ThrowIfCancellationRequested();
            lock (_nativeSourceGate)
            {
                if (!IsCurrent(attempt))
                {
                    // The 8-tab control run on 29 October aborted exactly here with the generic
                    // "The native app has unsaved or pending work" message, which names no tab and
                    // no cause. The sub-conditions below are the same ones IsCurrent already reads.
                    throw Veto("acquire-not-current", "tab " + tab.Address + " - " + DescribeStaleAttempt(attempt));
                }
            }
            return attempt;
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            await AbortContentCloseAsync(attempt, failures).ConfigureAwait(false);
            ThrowCloseCauses(failures);
            throw;
        }
    }
    private void ClearAttempt(ContentCloseAttempt attempt)
    {
        lock (_nativeSourceGate)
            if (attempt.Owner is { } owner && ReferenceEquals(owner.Preparation, attempt)) owner.Preparation = null;
    }
    private async Task AbortContentCloseAsync(ContentCloseAttempt attempt, List<Exception> failures)
    {
        try
        {
            if (!attempt.Accepted && attempt.Lease is { } lease)
            {
                Task actual;
                lock (_nativeSourceGate)
                    actual = attempt.Abort ??= lease.AbortAsync() ?? throw new InvalidOperationException("No original preparation abort task was supplied.");
                RetainAppSource(actual);
                try { await actual.ConfigureAwait(false); }
                catch (Exception error) { AddCloseCauses(failures, actual, error); }
            }
        }
        catch (Exception error) { failures.Add(error); }
        finally { ClearAttempt(attempt); }
    }
    private void BeginContentClose(ContentCloseAttempt attempt)
    {
        AppContentOwner owner;
        lock (_nativeSourceGate)
        {
            if (!IsCurrent(attempt)) throw Veto("begin-not-current", "tab " + attempt.Tab.Address + " - " + DescribeStaleAttempt(attempt));
            if (attempt.Owner is not { } actualOwner) { attempt.Accepted = true; return; }
            owner = actualOwner;
            if (owner.Close is not null) { attempt.Accepted = true; return; }
            if (attempt.Beginning) throw Veto("begin-twice", "tab " + attempt.Tab.Address + " was already begun by this preparation");
            attempt.Beginning = true;
        }
        // The exact Preparation still fences this owner against replacement. Native
        // admission may marshal to its UI thread and read Chrome's publication state;
        // never hold the Chrome gate across that finite external callback.
        try
        {
            var failures = new List<Exception>();
            try
            {
                owner.Close = attempt.Lease is { } lease ? lease.BeginClose() : owner.Session.CloseAndDrainAsync();
                if (owner.Close is null) throw new InvalidOperationException("The app supplied no original close task.");
                attempt.Accepted = true;
                RetainAppSource(owner.Close);
            }
            catch (Exception failure) { failures.Add(failure); }
            // Observe the reported original even when Begin or a later event
            // accessor fails. A throwing callback may already have acquired work.
            try
            {
                owner.ReportedClose = owner.Session.OriginalClose;
                if (owner.ReportedClose is { } reported)
                {
                    RetainAppSource(reported);
                    if (owner.Close is null) { owner.Close = reported; attempt.Accepted = true; }
                }
            }
            catch (Exception observation) { failures.Add(observation); }
            if (attempt.Accepted)
            {
                if (!ReferenceEquals(owner.Close, owner.ReportedClose))
                {
                    PinContractSource(owner.Close); PinContractSource(owner.ReportedClose);
                    failures.Add(new InvalidOperationException("The app did not retain its original close task."));
                }
                owner.Published = false;
                try { if (owner.Changed is not null) owner.Session.StateChanged -= owner.Changed; }
                catch (Exception cleanup) { failures.Add(cleanup); }
                finally { ClearAttempt(attempt); }
                foreach (var failure in failures) RecordCloseFailure(owner, failure);
            }
            ThrowCloseCauses(failures);
        }
        finally { lock (_nativeSourceGate) attempt.Beginning = false; }
    }
    private static void RecordCloseFailure(AppContentOwner owner, Exception failure)
    {
        if (ReferenceEquals(owner.CloseContractFailure, failure)) return;
        owner.CloseContractFailure = owner.CloseContractFailure is { } prior
            ? new AggregateException("Original owner close/cleanup causes remain retained.", prior, failure) : failure;
    }
    private async Task FinishContentAttemptAsync(ContentCloseAttempt attempt, Exception? primary = null)
    {
        var failures = new List<Exception>();
        if (primary is not null) failures.Add(primary);
        await AbortContentCloseAsync(attempt, failures).ConfigureAwait(false);
        await JoinAcceptedContentCloseAsync(attempt, failures).ConfigureAwait(false);
        ThrowCloseCauses(failures);
    }
    private async Task JoinAcceptedContentCloseAsync(ContentCloseAttempt attempt, List<Exception> failures)
    {
        if (attempt.Accepted && attempt.Owner is { } owner)
        {
            foreach (var actual in new[] { owner.Close, owner.ReportedClose }.OfType<Task>().Distinct())
                try { await actual.ConfigureAwait(false); } catch (Exception error) { AddCloseCauses(failures, actual, error); }
            if (owner.CloseContractFailure is { } contract && !failures.Contains(contract)) failures.Add(contract);
        }
    }
    private static void AddCloseCauses(List<Exception> failures, Task actual, Exception caught)
    {
        foreach (var cause in actual.Exception is { } group ? group.InnerExceptions : [caught])
            if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
    }
    private static void ThrowCloseCauses(List<Exception> failures)
    {
        if (failures.Count == 0) return;
        if (failures.Count > 1 || failures[0] is OperationCanceledException)
            throw new AggregateException("Original content preparation/abort causes remain retained.", failures);
        ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }

    /// <summary>Exact reversible window cohort. The external caller must AbortAsync
    /// on every path that does not accept BeginClose. No App/window wiring is implied.</summary>
    private sealed class WindowCloseState(BrowseChrome owner)
    {
        internal readonly BrowseChrome Owner = owner;
        internal readonly TabRuntime[] Tabs = owner._tabs.ToArray();
        internal readonly List<ContentCloseAttempt> Entries = [];
        internal Task<ClosePreparation?>? Preparation;
        internal Task? Abort, Close, ActualClose;
        internal bool Ready, Aborted, Committing;
        internal readonly List<Exception> CommitFailures = [];
    }
    public sealed class ClosePreparation
    {
        // No public constructor or writable token metadata. The private state type
        // and exact registered token reference remain the issuing Chrome's custody.
        internal object State { get; }
        internal ClosePreparation(object state) => State = state;
        public bool IsCurrent
        {
            get
            {
                var actual = StateOf(this);
                lock (actual.Owner._nativeSourceGate) return actual.Ready && !actual.Aborted && !actual.Committing &&
                    ReferenceEquals(actual.Owner._windowPreparation, this) &&
                    actual.Tabs.SequenceEqual(actual.Owner._tabs, ReferenceEqualityComparer.Instance) &&
                    actual.Entries.All(actual.Owner.IsCurrent);
            }
        }
        public Task AbortAsync() => StateOf(this).Owner.AbortWindowClose(this);
        public Task BeginClose() => StateOf(this).Owner.BeginWindowClose(this);
    }
    private static WindowCloseState StateOf(ClosePreparation actual) => (WindowCloseState)actual.State;
    private Task AbortWindowClose(ClosePreparation cohort)
    {
        var actual = StateOf(cohort);
        lock (_nativeSourceGate)
        {
            if (actual.Abort is not null) return actual.Abort;
            if (actual.Close is not null) return actual.Abort = actual.Close;
            if (actual.Committing)
                throw new InvalidOperationException("The window is acquiring its exact prepared close; it cannot be aborted during acceptance.");
            actual.Aborted = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.Abort = AbortWindowPreparationAsync(start.Task, cohort);
            RetainAppSource(actual.Abort); start.SetResult(); return actual.Abort;
        }
    }
    private Task BeginWindowClose(ClosePreparation cohort, Task? externalIdentity = null)
    {
        DemandExternalOriginalRetirementJoin();
        var state = StateOf(cohort);
        lock (_nativeSourceGate)
        {
            if (state.Close is not null) return state.Close;
            if (!ReferenceEquals(state.Owner, this) || !cohort.IsCurrent || _contentTransitions.Count != 0)
                throw Veto("commit-window", "host current=" + ReferenceEquals(state.Owner, this) +
                    ", cohort current=" + cohort.IsCurrent + ", live fences=" + _contentTransitions.Count +
                    " [" + DescribeContentTransitions() + "]");
            // Keep the reversible host publication alive while synchronously
            // acquiring exact participant originals. Publishing Chrome retirement
            // first could invalidate a perfectly current subordinate host lease.
            state.Committing = true;
        }
        // Keep the exact cohort/topology fence, but release the observation gate
        // while native owners synchronously acquire UI-affine close originals.
        var previous = _executingWindowCommit.Value; _executingWindowCommit.Value = cohort;
        try
        {
            foreach (var entry in state.Entries)
            {
                try { BeginContentClose(entry); }
                catch (Exception failure) { state.CommitFailures.Add(failure); break; }
            }
        }
        finally { _executingWindowCommit.Value = previous; }
        lock (_nativeSourceGate)
        {
            if (state.CommitFailures.Count != 0 && !state.Entries.Any(entry => entry.Accepted && entry.Owner?.Close is not null))
            {
                state.Committing = false;
                var abort = AbortWindowClose(cohort); // Caller must join this same retained cleanup on refusal.
                RetainAppSource(abort);
                ThrowCloseCauses(state.CommitFailures);
            }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _nativeRetiring = true;
            state.ActualClose = ClosePreparedWindowAsync(start.Task, cohort);
            state.Close = _originalChromeClose = externalIdentity ?? state.ActualClose;
            start.SetResult(); return state.Close;
        }
    }
    private async Task PrepareStrongExternalCloseAsync(Task start)
    {
        await start.ConfigureAwait(false);
        ClosePreparation? cohort = null;
        var failures = new List<Exception>();
        try
        {
            cohort = await PrepareCloseAsync().ConfigureAwait(false);
            if (cohort is null) throw Veto("cohort-null", "the chrome produced no reversible preparation for this window");
            var acceptedOriginal = BeginWindowClose(cohort, _pendingExternalClose);
            if (!ReferenceEquals(acceptedOriginal, OriginalClose)) throw new InvalidOperationException("The window did not retain its exact original close.");
            await StateOf(cohort).ActualClose!.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            if (cohort is not null && StateOf(cohort).ActualClose is { } actual) AddCloseCauses(failures, actual, failure);
            else failures.Add(failure);
        }
        finally
        {
            if (cohort is not null && StateOf(cohort).Close is null)
            {
                var abort = cohort.AbortAsync();
                try { await abort.ConfigureAwait(false); } catch (Exception failure) { AddCloseCauses(failures, abort, failure); }
            }
            lock (_nativeSourceGate) if (_originalChromeClose is null) _pendingExternalClose = null;
        }
        ThrowCloseCauses(failures);
    }
    public Task<ClosePreparation?> PrepareCloseAsync(CancellationToken token = default)
    {
        DemandExternalOriginalRetirementJoin(); token.ThrowIfCancellationRequested();
        lock (_nativeSourceGate)
        {
            ThrowIfCannotAccept();
            if (_windowPreparation is { } existing) return StateOf(existing).Preparation!;
            _closeRefusals.Clear(); // this attempt reports its own causes, not an older attempt's
            if (_contentTransitions.Count != 0)
            {
                // Measured on 29 October: with the owner's restored 15-tab session the close aborted
                // WITHOUT asking any app session (only 2 of 11 Boards tabs had even been entered,
                // and those were navigation asks). This gate is the only close path that can void the
                // whole cohort before a single participant is consulted, so it must say which fences
                // are live and what they are still holding. Observation only.
                ReportCloseRefusal("content-transition-fence", _contentTransitions.Count +
                    " live content transition fence(s) blocked the close: " + DescribeContentTransitions());
                return Task.FromResult<ClosePreparation?>(null);
            }
            var cohort = _windowPreparation = new ClosePreparation(new WindowCloseState(this));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = PrepareWindowAsync(start.Task, cohort, token);
            StateOf(cohort).Preparation = actual;
            RetainAppSource(actual); start.SetResult(); return actual;
        }
    }
    private async Task<ClosePreparation?> PrepareWindowAsync(Task start, ClosePreparation cohort, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        Exception? failure = null;
        var previous = _executingWindowPreparation.Value; _executingWindowPreparation.Value = cohort;
        try
        {
            foreach (var tab in StateOf(cohort).Tabs)
            {
                var acquired = await AcquireContentCloseAsync(tab, token).ConfigureAwait(false);
                if (acquired is null) return null;
                StateOf(cohort).Entries.Add(acquired);
            }
            token.ThrowIfCancellationRequested();
            lock (_nativeSourceGate)
            {
                if (StateOf(cohort).Aborted || !StateOf(cohort).Tabs.SequenceEqual(_tabs, ReferenceEqualityComparer.Instance) || !StateOf(cohort).Entries.All(IsCurrent)) return null;
                StateOf(cohort).Ready = true; return cohort;
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            _executingWindowPreparation.Value = previous;
            if (!StateOf(cohort).Ready)
            {
                var failures = new List<Exception>();
                if (failure is not null) failures.Add(failure);
                foreach (var entry in StateOf(cohort).Entries) await AbortContentCloseAsync(entry, failures).ConfigureAwait(false);
                lock (_nativeSourceGate) if (ReferenceEquals(_windowPreparation, cohort)) _windowPreparation = null;
                ThrowCloseCauses(failures);
            }
        }
    }
    private async Task AbortWindowPreparationAsync(Task start, ClosePreparation cohort)
    {
        await start.ConfigureAwait(false);
        // A public cohort is returned only after preparation settled; no self join.
        var failures = new List<Exception>();
        foreach (var entry in StateOf(cohort).Entries) await AbortContentCloseAsync(entry, failures).ConfigureAwait(false);
        lock (_nativeSourceGate) if (ReferenceEquals(_windowPreparation, cohort)) _windowPreparation = null;
        ThrowCloseCauses(failures);
    }
    private async Task ClosePreparedWindowAsync(Task start, ClosePreparation cohort)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>(StateOf(cohort).CommitFailures);
        if (failures.Count != 0)
        {
            foreach (var entry in StateOf(cohort).Entries)
            {
                await AbortContentCloseAsync(entry, failures).ConfigureAwait(false);
                if (entry.Accepted && entry.Owner is { } owner)
                    foreach (var close in new[] { owner.Close, owner.ReportedClose }.OfType<Task>().Distinct())
                        try { await close.ConfigureAwait(false); } catch (Exception error) { AddCloseCauses(failures, close, error); }
            }
            ThrowCloseCauses(failures);
        }
        await CloseOriginalChromeAsync(Task.CompletedTask).ConfigureAwait(false);
    }
    private async Task ReplaceAppContentAsync(TabRuntime tab, AppContentAddress address, CancellationToken token)
    {
        var attempt = await AcquireContentCloseAsync(tab, token).ConfigureAwait(false);
        if (attempt is null) { tab.Status = "Finish or save the app's pending work before navigating."; return; }
        var previousOwners = tab.AppOwners.ToHashSet();
        AppContentOwner? replacement = null;
        IBrowseEngineTab? engine = null;
        var published = false;
        Exception? failure = null;
        try
        {
            DemandContentAdmission(token);
            lock (_nativeSourceGate) if (!IsCurrent(attempt)) throw Veto("navigation-not-current",
                "tab " + attempt.Tab.Address + " - " + DescribeStaleAttempt(attempt));
            if (address.IsWeb)
            {
                var assessment = BrowserNativeRequestPolicy.AssessTopLevel(address.WebUri!);
                if (!assessment.IsAllowed) throw new InvalidOperationException(assessment.Reason);
                var choice = _enginePolicy.Resolve(address.WebUri!, tab.Id);
                if (IsEngineAvailable(choice))
                {
                    var actual = CreateEngineHostAsync(tab, address.WebUri!, choice, token);
                    RetainAppSource(actual);
                    engine = await actual.ConfigureAwait(false);
                    tab.PendingHosts.Add(engine); // Retain before any validation/getter can fail.
                }
                DemandContentAdmission(token);
                BeginContentClose(attempt); // Revalidates exact owner/route/generation after acquisition.
                await ReleaseContentAsync(tab, preservePendingHosts: true).ConfigureAwait(false);
                tab.NativePage = BrowserNativePage.None; tab.Address = address;
                tab.Title = address.Authority; tab.IconKey = "web"; tab.Engine = choice;
                if (engine is not null) { AttachAcquiredHost(tab, engine); tab.PendingHosts.Remove(engine); }
                else { tab.EngineState = BrowseEngineState.Unsupported; tab.Status = EngineUnavailableReason(choice); }
            }
            else
            {
                if (!BrowserNativePages.TryResolve(address.Value, out var page))
                    replacement = await InitializeAppOwnerAsync(tab, address, token).ConfigureAwait(false);
                DemandContentAdmission(token);
                BeginContentClose(attempt); // No irreversible old-owner release before this exact revalidation.
                await ReleaseContentAsync(tab, replacement).ConfigureAwait(false);
                if (BrowserNativePages.TryResolve(address.Value, out page)) ApplyNativePage(tab, page);
                else ApplyAppContent(tab, address, replacement);
            }
            published = true;
            await SaveTabsAsync(token).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        var failures = new List<Exception>();
        if (failure is not null) failures.Add(failure);
        try
        {
        if (!published)
        {
            foreach (var acquired in tab.AppOwners.Where(owner => !previousOwners.Contains(owner)).ToArray())
                try { await CloseUnpublishedAppOwnerAsync(tab, acquired, failures).ConfigureAwait(false); }
                catch (Exception unexpected) { failures.Add(unexpected); }
            if (engine is not null && !ReferenceEquals(tab.Host, engine))
            {
                try
                {
                    if (!tab.PendingHostCloses.TryGetValue(engine, out var close))
                    { close = engine.DisposeAsync().AsTask(); tab.PendingHostCloses.Add(engine, close); RetainAppSource(close); }
                    try { await close.ConfigureAwait(false); } catch (Exception error) { AddCloseCauses(failures, close, error); }
                    if (close.IsCompletedSuccessfully) { tab.PendingHosts.Remove(engine); tab.PendingHostCloses.Remove(engine); }
                }
                catch (Exception error) { failures.Add(error); }
            }
        }
        }
        catch (Exception cleanup) { failures.Add(cleanup); }
        finally
        {
            // Candidate cleanup never owns the right to skip restoring the old
            // unaccepted owner, even when a getter/event accessor itself throws.
            await AbortContentCloseAsync(attempt, failures).ConfigureAwait(false);
            await JoinAcceptedContentCloseAsync(attempt, failures).ConfigureAwait(false);
        }
        // Only this privately issued post-source admission check can acknowledge
        // normal navigation retirement/supersession. Faulted raw OCEs are unrelated.
        if (failures.Count == 1 && failure is ContentAdmissionCanceledException && token.IsCancellationRequested)
            ExceptionDispatchInfo.Capture(failure).Throw();
        ThrowCloseCauses(failures);
    }
    private async Task CloseUnpublishedAppOwnerAsync(TabRuntime tab, AppContentOwner owner, List<Exception> failures)
    {
        // Only this unpublished acquisition, never ReleaseContent(tab), which would
        // also retire the still-live original app/renderer on acquisition failure.
        try
        {
            if (owner.Close is null)
            {
                owner.Close = owner.Session.CloseAndDrainAsync() ?? throw new InvalidOperationException("No acquired owner's original close task.");
                RetainAppSource(owner.Close);
            }
            owner.ReportedClose = owner.Session.OriginalClose;
            if (owner.ReportedClose is not null) RetainAppSource(owner.ReportedClose);
            if (!ReferenceEquals(owner.Close, owner.ReportedClose))
                owner.CloseContractFailure = new InvalidOperationException("The acquired owner did not retain the same original close task.");
        }
        catch (Exception error)
        {
            failures.Add(error);
            try
            {
                if (owner.Session.OriginalClose is { } issued)
                { owner.ReportedClose = issued; RetainAppSource(issued); }
            }
            catch (Exception observation) { failures.Add(observation); }
        }
        foreach (var actual in new[] { owner.Close, owner.ReportedClose, owner.Initialization, owner.ReportedInitialization }.OfType<Task>().Distinct())
            try { await actual.ConfigureAwait(false); } catch (Exception error) { AddCloseCauses(failures, actual, error); }
        if (owner.CloseContractFailure is { } contractFailure) failures.Add(contractFailure);
        if (owner.Close is { IsCompletedSuccessfully: true } && owner.CloseContractFailure is null)
        {
            var canRetire = false;
            try { canRetire = owner.Session.CanRetireOriginalView; }
            catch (Exception observation) { failures.Add(observation); RecordCloseFailure(owner, observation); }
            if (canRetire)
            {
                try
                {
                    if (owner.Changed is not null) owner.Session.StateChanged -= owner.Changed;
                    ArchiveDrainedStartupFailure(owner);
                    tab.AppOwners.Remove(owner);
                }
                catch (Exception cleanup) { failures.Add(cleanup); RecordCloseFailure(owner, cleanup); }
            }
        }
    }


/// <summary>Read-only observation seam for the app host's bounded close diagnostic. The chrome does
/// not own that log; this only forwards what the close decision already knows. It never changes a
/// decision, and a missing host observer is ignored.</summary>
/// <summary>A close attempt that the chrome itself declined, in the same inspectable style as
/// BrowseDrainedStartupFailure: the chrome never writes the app host's log, it only records what its
/// own decision was and why. The host reads this list into its bounded close diagnostic. Until now a
/// declined close produced no named cause anywhere, which is why the reported window-close failure
/// could not be attributed (measured 29 October).</summary>
public sealed record BrowseCloseRefusal(string Kind, string Detail);


}
