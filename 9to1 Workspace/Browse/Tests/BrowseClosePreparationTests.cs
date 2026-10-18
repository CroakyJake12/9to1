using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    private static readonly TimeSpan PreparationBound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Strong_bool_preflight_aborts_its_lease_and_real_tab_close_uses_same_original()
    {
        var apps = new PreparedFactory();
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a");
        var owner = Assert.Single(apps.Sessions);
        Assert.True(await chrome.TryPrepareToCloseAsync().WaitAsync(PreparationBound));
        Assert.False(owner.Frozen); Assert.Equal(1, owner.Aborts); Assert.Null(owner.OriginalClose);
        Assert.True(owner.Edit());
        await chrome.NewTabAsync(false); var closing = chrome.State.Tabs.Single(t => t.Address.Value == "9to1://write/a");
        await chrome.CloseTabAsync(closing.Id).WaitAsync(PreparationBound);
        Assert.Equal(1, owner.Closes); Assert.True(owner.OriginalClose!.IsCompletedSuccessfully);
        Assert.DoesNotContain(chrome.State.Tabs, t => t.Id == closing.Id);
    }

    [Theory]
    [InlineData("veto")]
    [InlineData("fault")]
    [InlineData("cancel")]
    public async Task Strong_window_sibling_failure_aborts_prior_leases_without_caching_close(string kind)
    {
        var apps = new PreparedFactory();
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a");
        await chrome.OpenContentAsync(AppContentAddress.Parse("9to1://write/b"));
        var first = apps.Sessions[0]; var second = apps.Sessions[1];
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("Actual second participant preparation failed");
        second.Preparing = () =>
        {
            if (kind == "cancel") cancellation.Cancel();
            if (kind == "fault") throw failure;
            return kind != "veto";
        };
        try
        {
            var prepare = chrome.PrepareCloseAsync(cancellation.Token);
            if (kind == "veto") Assert.Null(await prepare.WaitAsync(PreparationBound));
            else
            {
                var actual = await Assert.ThrowsAnyAsync<Exception>(() => prepare.WaitAsync(PreparationBound));
                if (kind == "fault") Assert.True(HasExact(actual, failure));
            }
            Assert.False(first.Frozen); Assert.False(second.Frozen);
            Assert.True(first.Edit()); Assert.Null(chrome.OriginalClose); Assert.Equal(0, first.Closes);
            Assert.Equal(1, first.Aborts);
        }
        finally
        {
            second.Preparing = null;
            // A real preparation fault remains retained, even after a later retry.
            try { await chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound); }
            catch when ((kind is "fault" or "cancel") && chrome.OriginalClose is { IsFaulted: true }) { }
        }
    }

    [Fact]
    public async Task Strong_abort_fault_cannot_skip_remaining_acquired_sibling_abort()
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); await chrome.OpenContentAsync(AppContentAddress.Parse("9to1://write/b"));
        await chrome.OpenContentAsync(AppContentAddress.Parse("9to1://write/c"));
        var failure = new IOException("First actual abort failed after releasing its admission");
        apps.Sessions[0].AbortFailure = failure; apps.Sessions[2].Preparing = () => false;
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => chrome.PrepareCloseAsync().WaitAsync(PreparationBound));
            Assert.True(HasExact(error, failure)); Assert.Equal(1, apps.Sessions[1].Aborts);
            Assert.All(apps.Sessions, owner => Assert.False(owner.Frozen)); Assert.Null(chrome.OriginalClose);
        }
        finally
        {
            apps.Sessions[0].AbortFailure = null; apps.Sessions[2].Preparing = null;
            await Assert.ThrowsAnyAsync<Exception>(() => chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound));
        }
    }

    [Fact]
    public async Task Strong_origin_and_loader_sources_settle_before_cohort_and_abort_restores_all_admissions()
    {
        var apps = new PreparedFactory(); await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a");
        var owner = apps.Sessions[0];
        var origin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Origin = origin.Task; owner.Loader = loader.Task;
        Task<BrowseChrome.ClosePreparation?>? preparation = null;
        try
        {
            preparation = chrome.PrepareCloseAsync();
            await owner.PreparationEntered.Task.WaitAsync(PreparationBound);
            Assert.False(preparation.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.NewTabAsync(false));
            origin.SetResult(); Assert.False(preparation.IsCompleted);
            loader.SetResult();
            var cohort = Assert.IsType<BrowseChrome.ClosePreparation>(await preparation.WaitAsync(PreparationBound));
            Assert.True(cohort.IsCurrent); Assert.True(owner.Frozen); Assert.Null(chrome.OriginalClose);
            await cohort.AbortAsync().WaitAsync(PreparationBound);
            Assert.False(owner.Frozen); Assert.False(cohort.IsCurrent); Assert.True(owner.Edit());
            Assert.Throws<BrowseCloseVetoException>(() => { _ = cohort.BeginClose(); }); Assert.Null(chrome.OriginalClose);
            await chrome.NewTabAsync(false);
        }
        finally
        {
            origin.TrySetResult(); loader.TrySetResult();
            if (preparation is not null && await preparation.WaitAsync(PreparationBound) is { } held) await held.AbortAsync();
        }
    }

    [Fact]
    public async Task Strong_partial_begin_retains_accepted_original_and_aborts_every_unaccepted_sibling()
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); await chrome.OpenContentAsync(AppContentAddress.Parse("9to1://write/b"));
        await chrome.OpenContentAsync(AppContentAddress.Parse("9to1://write/c"));
        var failure = new IOException("Second BeginClose refused before acquiring close"); apps.Sessions[1].BeginFailure = failure;
        var acceptedFailure = new IOException("First accepted original failed independently"); apps.Sessions[0].CloseFailure = acceptedFailure;
        var cohort = Assert.IsType<BrowseChrome.ClosePreparation>(await chrome.PrepareCloseAsync().WaitAsync(PreparationBound));
        var close = cohort.BeginClose();
        var actual = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(PreparationBound));
        Assert.True(HasExact(actual, failure)); Assert.True(HasExact(actual, acceptedFailure)); Assert.Same(close, chrome.OriginalClose);
        Assert.Equal(1, apps.Sessions[0].Closes); Assert.NotNull(apps.Sessions[0].OriginalClose);
        Assert.Null(apps.Sessions[1].OriginalClose); Assert.Null(apps.Sessions[2].OriginalClose);
        Assert.Equal(1, apps.Sessions[1].Aborts); Assert.Equal(1, apps.Sessions[2].Aborts);
        Assert.False(apps.Sessions[1].Frozen); Assert.False(apps.Sessions[2].Frozen);
        Assert.Same(close, chrome.DisposeAsync().AsTask());
    }

    [Theory]
    [InlineData("9to1://write/new")]
    [InlineData("https://replacement.example/")]
    public async Task Strong_failed_replacement_acquisition_keeps_exact_original_live_and_releases_lease(string route)
    {
        var apps = new PreparedFactory(); var engines = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), engines, appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); var before = chrome.State.SelectedTab;
        var owner = apps.Sessions[0]; apps.FactoryFailure = new IOException("Actual replacement app factory failed"); engines.FailEngine = BrowseEngineKind.Gecko;
        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() => chrome.NavigateAsync(route).WaitAsync(PreparationBound));
            Assert.Same(owner, chrome.ObserveOriginalAppContent(before.Id)); Assert.Equal(before, chrome.State.SelectedTab);
            Assert.False(owner.Frozen); Assert.True(owner.Edit()); Assert.Null(owner.OriginalClose);
            Assert.Null(chrome.OriginalClose);
        }
        finally
        {
            apps.FactoryFailure = null; engines.FailEngine = null;
            await Assert.ThrowsAnyAsync<Exception>(() => chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound));
        }
    }

    [Fact]
    public async Task Strong_window_dispose_returns_exact_cached_original_and_rejects_late_reopen_and_engine_mutation()
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NewTabAsync(false); var closed = chrome.State.SelectedTabId; await chrome.CloseTabAsync(closed);
        await chrome.NavigateAsync("9to1://write/a");
        var cohort = Assert.IsType<BrowseChrome.ClosePreparation>(await chrome.PrepareCloseAsync().WaitAsync(PreparationBound));
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.ReopenClosedTabAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium));
        await cohort.AbortAsync();
        var close = chrome.DisposeAsync().AsTask(); await close.WaitAsync(PreparationBound);
        Assert.Same(close, chrome.OriginalClose); Assert.Same(close, chrome.DisposeAsync().AsTask());
        Assert.Equal(1, apps.Sessions[0].Closes);
    }

    [Fact]
    public async Task Strong_held_replacement_revalidates_actual_owner_route_before_release_and_cleans_only_candidate()
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); var original = apps.Sessions[0];
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        apps.NextInitialization = held.Task;
        var navigation = chrome.NavigateAsync("9to1://write/new");
        try
        {
            var candidate = await apps.NextCreated.Task.WaitAsync(PreparationBound);
            await candidate.InitializationEntered.Task.WaitAsync(PreparationBound);
            Assert.True(original.Frozen); Assert.False(navigation.IsCompleted);
            original.ChangeAddress(AppContentAddress.Parse("9to1://write/canonical-owner-correction"));
            held.SetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => navigation.WaitAsync(PreparationBound));
            Assert.Same(original, chrome.ObserveOriginalAppContent(chrome.State.SelectedTabId));
            Assert.Equal(original.Address, chrome.State.SelectedTab.Address);
            Assert.Null(original.OriginalClose); Assert.False(original.Frozen); Assert.True(original.Edit());
            Assert.True(candidate.OriginalClose!.IsCompletedSuccessfully); Assert.Equal(1, candidate.Closes);
            Assert.Null(chrome.OriginalClose);
        }
        finally
        {
            held.TrySetResult();
            try { await Assert.ThrowsAnyAsync<Exception>(() => navigation.WaitAsync(PreparationBound)); }
            finally { await Assert.ThrowsAnyAsync<Exception>(() => chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound)); }
        }
    }

    [Fact]
    public async Task Strong_faulted_preparation_with_default_token_OCE_is_not_swallowed_as_navigation_supersession()
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); var owner = apps.Sessions[0];
        var exact = new OperationCanceledException("Unrelated actual fault, not navigation cancellation");
        var actual = Task.FromException<IBrowseAppContentClosePreparation?>(exact);
        owner.PreparationOverride = actual;
        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => chrome.NavigateAsync("browse://history").WaitAsync(PreparationBound));
            Assert.True(actual.IsFaulted); Assert.True(HasExact(failure, exact));
            Assert.Null(owner.OriginalClose); Assert.Same(owner, chrome.ObserveOriginalAppContent(chrome.State.SelectedTabId));
            Assert.Contains(actual, chrome.OriginalNativeSources);
        }
        finally
        {
            owner.PreparationOverride = null;
            await Assert.ThrowsAnyAsync<Exception>(() => chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound));
        }
    }

    [Fact]
    public async Task Strong_window_preparation_vetoes_actual_held_new_tab_admission_before_acquiring_any_lease()
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); var first = apps.Sessions[0];
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        apps.NextInitialization = held.Task;
        var admission = chrome.OpenContentAsync(AppContentAddress.Parse("9to1://write/pending"));
        try
        {
            var candidate = await apps.NextCreated.Task.WaitAsync(PreparationBound);
            await candidate.InitializationEntered.Task.WaitAsync(PreparationBound);
            Assert.Null(await chrome.PrepareCloseAsync().WaitAsync(PreparationBound));
            Assert.False(first.Frozen); Assert.Null(chrome.OriginalClose); Assert.False(admission.IsCompleted);
            held.SetResult(); await admission.WaitAsync(PreparationBound);
            var cohort = Assert.IsType<BrowseChrome.ClosePreparation>(await chrome.PrepareCloseAsync().WaitAsync(PreparationBound));
            Assert.All(apps.Sessions, owner => Assert.True(owner.Frozen)); await cohort.AbortAsync();
        }
        finally
        {
            held.TrySetResult();
            try { await admission.WaitAsync(PreparationBound); }
            finally { await chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Strong_stale_or_foreign_participant_preparation_refuses_before_cached_close(bool foreign)
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); var owner = apps.Sessions[0];
        if (foreign) owner.ClaimedSession = new PreparedSession(AppContentAddress.Parse("9to1://write/other"));
        else owner.ForceStale = true;
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => chrome.PrepareCloseAsync().WaitAsync(PreparationBound));
            Assert.Null(owner.OriginalClose); Assert.Null(chrome.OriginalClose);
            Assert.Equal(1, owner.Aborts); Assert.False(owner.Frozen); Assert.True(owner.Edit());
        }
        finally
        {
            owner.ForceStale = false; owner.ClaimedSession = null;
            await Assert.ThrowsAnyAsync<Exception>(() => chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound));
        }
    }

    [Fact]
    public async Task Strong_mismatched_begin_tasks_are_both_joined_before_failed_tab_close_returns()
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); var owner = apps.Sessions[0]; owner.MismatchClose = true;
        var close = chrome.CloseTabAsync(chrome.State.SelectedTabId);
        try
        {
            await owner.CloseEntered.Task.WaitAsync(PreparationBound);
            owner.ReturnedCloseGate.SetResult(); Assert.False(close.IsCompleted);
            owner.ReportedCloseGate.SetResult();
            await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(PreparationBound));
            Assert.Contains(owner.ReturnedClose!, chrome.OriginalNativeSources);
            Assert.Contains(owner.OriginalClose!, chrome.OriginalNativeSources);
            Assert.Same(owner, chrome.ObserveOriginalAppContent(chrome.State.SelectedTabId));
            Assert.Null(chrome.OriginalClose); Assert.Equal(1, owner.Closes);
        }
        finally
        {
            owner.ReturnedCloseGate.TrySetResult(); owner.ReportedCloseGate.TrySetResult();
            try { await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(PreparationBound)); }
            finally { await Assert.ThrowsAnyAsync<Exception>(() => chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound)); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Strong_failed_candidate_cleanup_observation_cannot_skip_old_lease_abort(bool unsubscribe)
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); var original = apps.Sessions[0];
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        apps.NextInitialization = held.Task;
        var navigation = chrome.NavigateAsync("9to1://write/new");
        var cleanupCause = new IOException("Exact acquired candidate cleanup observation failed");
        Exception? assertionFailure = null;
        try
        {
            var candidate = await apps.NextCreated.Task.WaitAsync(PreparationBound);
            await candidate.InitializationEntered.Task.WaitAsync(PreparationBound);
            if (unsubscribe) candidate.RemoveFailure = cleanupCause;
            else candidate.CanRetireFailure = cleanupCause;
            original.ChangeAddress(AppContentAddress.Parse("9to1://write/owner-correction"));
            held.SetResult();
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => navigation.WaitAsync(PreparationBound));
            Assert.True(HasExact(failure, cleanupCause));
            Assert.True(HasType<BrowseCloseVetoException>(failure)); // Keep the actual primary and cleanup sibling.
            Assert.True(candidate.OriginalClose!.IsCompletedSuccessfully);
            Assert.False(original.Frozen); Assert.True(original.Edit());
            Assert.Equal(1, original.Aborts); Assert.Null(original.OriginalClose);
            Assert.Same(original, chrome.ObserveOriginalAppContent(chrome.State.SelectedTabId));
        }
        catch (Exception error) { assertionFailure = error; throw; }
        finally
        {
            held.TrySetResult();
            Exception? cleanup = null;
            try { await navigation.WaitAsync(PreparationBound); } catch (Exception error) { cleanup = error; }
            try { await chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound); }
            catch (Exception error)
            {
                if (assertionFailure is not null)
                    throw new AggregateException("Regression assertion and actual attempted cleanup remain visible.", assertionFailure, error);
                Assert.True(HasExact(error, cleanupCause));
            }
            Assert.NotNull(cleanup);
        }
    }

    [Fact]
    public async Task Strong_throwing_begin_unsubscribe_still_observes_and_joins_reported_original_close()
    {
        var apps = new PreparedFactory(); var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), appContentFactory: apps);
        await chrome.NavigateAsync("9to1://write/a"); var owner = apps.Sessions[0];
        var cleanupCause = new IOException("Exact BeginClose event removal failed");
        owner.MismatchClose = true; owner.RemoveFailure = cleanupCause;
        var close = chrome.CloseTabAsync(chrome.State.SelectedTabId);
        Exception? assertionFailure = null;
        try
        {
            await owner.CloseEntered.Task.WaitAsync(PreparationBound);
            await owner.RemovalEntered.Task.WaitAsync(PreparationBound);
            // This is the actual reported-task getter, not a silence delay. Old
            // finally-unsubscribe throws before this source is observed at all.
            await owner.CloseObserved.Task.WaitAsync(PreparationBound);
            owner.ReturnedCloseGate.TrySetResult();
            Assert.False(close.IsCompleted);
            owner.ReportedCloseGate.TrySetResult();
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(PreparationBound));
            Assert.True(HasExact(failure, cleanupCause));
            Assert.True(owner.ReturnedClose!.IsCompletedSuccessfully);
            Assert.True(owner.OriginalClose!.IsCompletedSuccessfully);
            Assert.Contains(owner.ReturnedClose, chrome.OriginalNativeSources);
            Assert.Contains(owner.OriginalClose, chrome.OriginalNativeSources);
        }
        catch (Exception error) { assertionFailure = error; throw; }
        finally
        {
            owner.ReturnedCloseGate.TrySetResult(); owner.ReportedCloseGate.TrySetResult();
            try { await close.WaitAsync(PreparationBound); } catch (Exception) { }
            try { await chrome.DisposeAsync().AsTask().WaitAsync(PreparationBound); }
            catch (Exception error)
            {
                if (assertionFailure is not null) throw new AggregateException("Regression and cleanup remain visible.", assertionFailure, error);
                Assert.True(HasExact(error, cleanupCause));
            }
        }
    }

    private static bool HasType<T>(Exception actual) where T : Exception => actual is T ||
        actual is AggregateException group && group.InnerExceptions.Any(HasType<T>) ||
        actual.InnerException is { } inner && HasType<T>(inner);

    private static bool HasExact(Exception actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException aggregate && aggregate.InnerExceptions.Any(error => HasExact(error, expected)) ||
        actual.InnerException is { } inner && HasExact(inner, expected);

    // Instrumented app boundary only: actual production Chrome owns all routing,
    // admission, cohort/abort ordering, original-task identity and resource release.
    private sealed class PreparedFactory : IBrowseAppContentFactory
    {
        internal readonly List<PreparedSession> Sessions = [];
        internal IOException? FactoryFailure;
        internal Task? NextInitialization;
        internal TaskCompletionSource<PreparedSession> NextCreated { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        {
            if (FactoryFailure is { } failure) throw failure;
            var owner = new PreparedSession(request.Address) { Initialization = NextInitialization ?? Task.CompletedTask };
            if (NextInitialization is not null) { NextInitialization = null; NextCreated.TrySetResult(owner); }
            Sessions.Add(owner); return owner;
        }
    }
    private sealed class PreparedSession(AppContentAddress address) : IBrowseAppContentSession, IBrowseAppContentClosePreparationParticipant
    {
        private PreparedLease? _lease;
        internal bool Frozen;
        internal int Aborts, Closes;
        internal Func<bool>? Preparing;
        internal Exception? AbortFailure, BeginFailure, CloseFailure, CanRetireFailure, RemoveFailure;
        internal bool ForceStale, MismatchClose;
        internal IBrowseAppContentSession? ClaimedSession;
        internal Task? ReturnedClose;
        internal TaskCompletionSource CloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReturnedCloseGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReportedCloseGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Origin = Task.CompletedTask, Loader = Task.CompletedTask, Initialization = Task.CompletedTask;
        internal Task<IBrowseAppContentClosePreparation?>? PreparationOverride;
        internal TaskCompletionSource InitializationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PreparationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AppContentAddress Address { get; private set; } = address;
        internal void ChangeAddress(AppContentAddress next) { Address = next; _stateChanged?.Invoke(this, EventArgs.Empty); }
        public string Title => "Instrumented participant";
        public string IconKey => "test";
        private EventHandler? _stateChanged;
        public event EventHandler? StateChanged
        {
            add => _stateChanged += value;
            remove
            {
                _stateChanged -= value;
                if (RemoveFailure is { } failure)
                { RemoveFailure = null; RemovalEntered.TrySetResult(); throw failure; }
            }
        }
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView
        {
            get
            {
                if (CanRetireFailure is { } failure) { CanRetireFailure = null; throw failure; }
                return OriginalClose is { IsCompletedSuccessfully: true };
            }
        }
        public Task? OriginalInitialization { get; private set; }
        private Task? _originalClose;
        internal TaskCompletionSource CloseObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource RemovalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? OriginalClose
        {
            get { if (_originalClose is not null) CloseObserved.TrySetResult(); return _originalClose; }
            private set => _originalClose = value;
        }
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        { InitializationEntered.TrySetResult(); return OriginalInitialization ??= Initialization; }
        public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Strong participant must use its actual reversible preparation.");
        public bool SupportsReversibleClosePreparation => true;
        public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default) =>
            PreparationOverride ?? PrepareActualAsync(cancellationToken);
        private async Task<IBrowseAppContentClosePreparation?> PrepareActualAsync(CancellationToken cancellationToken)
        {
            PreparationEntered.TrySetResult();
            await Origin.WaitAsync(cancellationToken); await Loader.WaitAsync(cancellationToken);
            if (Preparing?.Invoke() == false) return null;
            cancellationToken.ThrowIfCancellationRequested();
            Frozen = true; return _lease = new(this);
        }
        public Task CloseAndDrainAsync()
        {
            if (ReturnedClose is not null) return ReturnedClose;
            Closes++; Frozen = false;
            ReturnedClose = MismatchClose ? ReturnedCloseGate.Task : CloseFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
            OriginalClose = MismatchClose ? ReportedCloseGate.Task : ReturnedClose;
            CloseEntered.TrySetResult(); return ReturnedClose;
        }
        internal bool Edit() => !Frozen && OriginalClose is null;
        private sealed class PreparedLease(PreparedSession owner) : IBrowseAppContentClosePreparation
        {
            private Task? _abort;
            public IBrowseAppContentSession Session => owner.ClaimedSession ?? owner;
            public bool IsCurrent => ReferenceEquals(owner._lease, this) && owner.Frozen && !owner.ForceStale && owner.OriginalClose is null;
            public Task AbortAsync() => _abort ??= Abort();
            private Task Abort()
            {
                owner.Aborts++; owner.Frozen = false;
                return owner.AbortFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
            }
            public Task BeginClose()
            {
                if (!IsCurrent) throw new InvalidOperationException("Fixture's exact lease is stale.");
                if (owner.BeginFailure is { } failure) throw failure;
                return owner.CloseAndDrainAsync();
            }
        }
    }
}
