using System.Runtime.ExceptionServices;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Tests;

public sealed class WorkspaceTabOriginalRetirementTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public Task Current_back_forward_and_abandoned_actual_pages_are_requested_and_joined() => RunCase(async scope =>
    {
        var a = scope.Page(); var b = scope.Page(); var c = scope.Page(); var d = scope.Page();
        var tab = scope.Tab(a);
        tab.NavigateTo("b", "B", b, true, HavenSurface.Home);
        tab.NavigateTo("c", "C", c, true, HavenSurface.Home);
        Assert.True(tab.TryGoBack());
        tab.NavigateTo("d", "D", d, true, HavenSurface.Home);
        Assert.Equal(1, c.Requests);
        var cohort = tab.CaptureOriginalPageCohort();
        foreach (var page in new[] { a, b, c, d }) Assert.Contains(cohort, actual => ReferenceEquals(actual, page));
        var close = scope.Track(tab.CloseAndDrainAsync());
        Assert.Same(close, tab.CloseAndDrainAsync());
        Assert.All(new[] { a, b, c, d }, page => { Assert.Equal(1, page.Requests); Assert.Equal(1, page.Acquisitions); });
        Assert.True(tab.LifetimeToken.IsCancellationRequested);
        a.Close.TrySetResult(); b.Close.TrySetResult(); d.Close.TrySetResult();
        Assert.False(close.IsCompleted);
        c.Close.TrySetResult();
        await close.WaitAsync(Bound);
        Assert.Empty(new[] { tab.CanGoBack, tab.CanGoForward }.Where(value => value));
    });

    [Fact]
    public Task One_child_acquisition_fault_preserves_and_joins_every_other_original() => RunCase(async scope =>
    {
        var first = scope.Page(); var failed = scope.Page(); var last = scope.Page();
        var cause = new IOException("Actual child close source returned no Task.");
        failed.Acquire = () => throw cause;
        var tab = scope.Tab(first);
        tab.NavigateTo("failed", "Failed", failed, true, HavenSurface.Home);
        tab.NavigateTo("last", "Last", last, true, HavenSurface.Home);
        var close = scope.Track(tab.CloseAndDrainAsync());
        Assert.Equal(1, first.Acquisitions); Assert.Equal(1, failed.Acquisitions); Assert.Equal(1, last.Acquisitions);
        Assert.False(close.IsCompleted);
        first.Close.TrySetResult();
        Assert.False(close.IsCompleted);
        last.Close.TrySetResult();
        DemandCauses(await Fault(close), cause);
        scope.Observe(close);
    });

    [Fact]
    public Task Faulted_child_siblings_and_actual_cancellation_callback_are_not_lost() => RunCase(async scope =>
    {
        var first = scope.Page(); var second = scope.Page();
        var a = new IOException("First direct child failure."); var b = new InvalidOperationException("Second direct child failure.");
        var c = new IOException("Actual lifetime cancellation callback.");
        var tab = scope.Tab(first);
        using var registration = tab.LifetimeToken.Register(() => throw c);
        tab.NavigateTo("b", "B", second, true, HavenSurface.Home);
        var close = scope.Track(tab.CloseAndDrainAsync());
        first.Close.TrySetException([a, b]);
        Assert.False(close.IsCompleted);
        second.Close.TrySetResult();
        DemandCauses(await Fault(close), a, b, c);
        Assert.True(close.IsFaulted);
        scope.Observe(close);
    });

    [Fact]
    public Task Entire_cohort_guard_denial_precedes_parent_close_admission() => RunCase(async scope =>
    {
        var first = scope.Page(); var guarded = scope.Page();
        var denial = new InvalidOperationException("Actual child original cannot join itself.");
        guarded.Guard = () => throw denial;
        var tab = scope.Tab(first);
        tab.NavigateTo("g", "G", guarded, true, HavenSurface.Home);
        var actual = Assert.Throws<InvalidOperationException>(() => { _ = tab.CloseAndDrainAsync(); });
        Assert.Same(denial, actual);
        Assert.Equal(0, first.Requests); Assert.Equal(0, first.Acquisitions);
        Assert.Equal(0, guarded.Requests); Assert.Equal(0, guarded.Acquisitions);
        guarded.Guard = null;
        var close = scope.Track(tab.CloseAndDrainAsync());
        first.Close.TrySetResult(); guarded.Close.TrySetResult();
        await close.WaitAsync(Bound);
    });

    [Fact]
    public Task Actual_request_callback_refuses_its_encompassing_join_and_can_return() => RunCase(async scope =>
    {
        var page = scope.Page();
        var tab = scope.Tab(page);
        InvalidOperationException? refusal = null;
        page.Request = () => refusal = Assert.Throws<InvalidOperationException>(() => { _ = tab.CloseAndDrainAsync(); });
        tab.RequestRetirement();
        Assert.NotNull(refusal);
        var close = scope.Track(tab.CloseAndDrainAsync());
        page.Close.TrySetResult();
        await close.WaitAsync(Bound);
    });

    [Fact]
    public Task Direct_faulted_cancellation_from_child_source_stays_faulted() => RunCase(async scope =>
    {
        var page = scope.Page();
        var cause = new OperationCanceledException("Actual faulted child original.");
        var tab = scope.Tab(page);
        var close = scope.Track(tab.CloseAndDrainAsync());
        page.Close.TrySetException(cause);
        DemandCauses(await Fault(close), cause);
        Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
        scope.Observe(close);
    });

    [Fact]
    public Task Ordinary_abandoned_disposal_occurs_once_but_never_certifies_unknown_async_owner() => RunCase(async scope =>
    {
        var a = scope.Page(); var b = new OrdinaryPage(); var c = scope.Page();
        var tab = scope.Tab(a);
        tab.NavigateTo("b", "B", b, true, HavenSurface.Home);
        Assert.True(tab.TryGoBack());
        tab.NavigateTo("c", "C", c, true, HavenSurface.Home);
        Assert.Equal(1, b.Disposals);
        tab.Dispose();
        Assert.Equal(1, b.Disposals);
        var close = scope.Track(tab.CloseAndDrainAsync());
        a.Close.TrySetResult(); c.Close.TrySetResult();
        var error = await Fault(close);
        Assert.Contains(Causes(error), cause => cause is DesktopOriginalRetirementUnavailableException unknown && unknown.ActualOwnerType == typeof(OrdinaryPage));
        Assert.Equal(1, b.Disposals);
        scope.Observe(close);
    });

    [Fact]
    public Task Replacement_retains_actual_old_owner_and_retirement_refuses_new_navigation() => RunCase(async scope =>
    {
        var old = scope.Page(); var next = scope.Page();
        var tab = scope.Tab(old);
        tab.ReplacePage(next);
        Assert.Equal(1, old.Requests);
        Assert.Contains(tab.CaptureOriginalPageCohort(), actual => ReferenceEquals(actual, old));
        var close = scope.Track(tab.CloseAndDrainAsync());
        Assert.Throws<ObjectDisposedException>(() => tab.NavigateTo("late", "Late", new object(), true, HavenSurface.Home));
        Assert.Throws<ObjectDisposedException>(() => { _ = tab.TryGoBack(); });
        Assert.Throws<ObjectDisposedException>(() => tab.ReplacePage(new object()));
        old.Close.TrySetResult(); next.Close.TrySetResult();
        await close.WaitAsync(Bound);
        Assert.Same(next, tab.Page);
    });

    [Fact]
    public Task Navigation_property_reentry_refuses_self_join_and_keeps_actual_destination_until_drained() => RunCase(async scope =>
    {
        var old = scope.Page(); var next = scope.Page(); var tab = scope.Tab(old);
        InvalidOperationException? joinDenial = null;
        tab.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(tab.Key)) return;
            joinDenial = Assert.Throws<InvalidOperationException>(() => { _ = tab.CloseAndDrainAsync(); });
            tab.RequestRetirement();
            Assert.Equal(0, next.Requests); // The still-live navigation body borrows the actual destination.
        };
        var originalRefusal = Assert.Throws<ObjectDisposedException>(() =>
            tab.NavigateTo("next", "Next", next, true, HavenSurface.Home));
        Assert.NotNull(joinDenial); Assert.Same(old, tab.Page);
        Assert.Contains(tab.CaptureOriginalPageCohort(), actual => ReferenceEquals(actual, next));
        var close = scope.Track(tab.CloseAndDrainAsync());
        Assert.False(close.IsCompleted);
        old.Close.TrySetResult(); next.Close.TrySetResult();
        DemandCauses(await Fault(close), originalRefusal);
        Assert.Equal(1, next.Requests); Assert.Equal(1, next.Acquisitions); scope.Observe(close);
    });

    [Fact]
    public Task Replacement_request_reentry_keeps_unpublished_successor_and_waits_original_body() => RunCase(async scope =>
    {
        var old = scope.Page(); var next = scope.Page(); var tab = scope.Tab(old);
        old.Request = () =>
        {
            tab.RequestRetirement();
            Assert.Equal(0, next.Requests); // Request seals first; child resources remain while body runs.
            Assert.Throws<InvalidOperationException>(() => { _ = tab.CloseAndDrainAsync(); });
        };
        var originalRefusal = Assert.Throws<ObjectDisposedException>(() => tab.ReplacePage(next));
        Assert.Same(old, tab.Page);
        Assert.Contains(tab.CaptureOriginalPageCohort(), actual => ReferenceEquals(actual, next));
        var close = scope.Track(tab.CloseAndDrainAsync()); Assert.False(close.IsCompleted);
        old.Close.TrySetResult(); next.Close.TrySetResult();
        DemandCauses(await Fault(close), originalRefusal);
        Assert.Equal(1, old.Requests); Assert.Equal(1, next.Requests); scope.Observe(close);
    });

    private static async Task<Exception> Fault(Task actual)
    {
        try { await actual.WaitAsync(Bound); }
        catch (Exception error) { return error; }
        throw new Xunit.Sdk.XunitException("The actual encompassing original unexpectedly succeeded.");
    }
    private static IEnumerable<Exception> Causes(Exception error)
    {
        yield return error;
        if (error is AggregateException group)
            foreach (var inner in group.InnerExceptions) foreach (var original in Causes(inner)) yield return original;
    }
    private static void DemandCauses(Exception error, params Exception[] expected)
    { foreach (var cause in expected) Assert.Contains(Causes(error), actual => ReferenceEquals(actual, cause)); }

    private static async Task RunCase(Func<Scope, Task> body)
    {
        var scope = new Scope(); var errors = new List<Exception>();
        try { await body(scope); } catch (Exception error) { errors.Add(error); }
        // Unconditional release and actual-owner join even when a primary assertion fails.
        foreach (var page in scope.Pages) page.Close.TrySetResult();
        foreach (var tab in scope.Tabs)
        {
            foreach (var page in scope.Pages) { page.Guard = null; page.Request = null; }
            try { scope.Track(tab.CloseAndDrainAsync()); } catch (Exception error) { errors.Add(error); }
        }
        foreach (var actual in scope.Tasks.Distinct<Task>(ReferenceEqualityComparer.Instance))
            try { await actual.WaitAsync(Bound); }
            catch (Exception error)
            {
                if (scope.Observed.Contains(actual)) continue;
                if (actual.Exception is { InnerExceptions.Count: > 0 } group) errors.AddRange(group.InnerExceptions);
                else errors.Add(error);
            }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 0) throw new AggregateException("Tab owning control and original teardown failed.", errors);
    }
    private sealed class Scope
    {
        internal readonly List<OriginalPage> Pages = [];
        internal readonly List<WorkspaceTabViewModel> Tabs = [];
        internal readonly List<Task> Tasks = [];
        internal readonly HashSet<Task> Observed = new(ReferenceEqualityComparer.Instance);
        internal OriginalPage Page() { var page = new OriginalPage(); Pages.Add(page); return page; }
        internal WorkspaceTabViewModel Tab(object page) { var tab = new WorkspaceTabViewModel("a", "A", page, true, HavenSurface.Home); Tabs.Add(tab); return tab; }
        internal Task Track(Task actual) { Tasks.Add(actual); return actual; }
        internal void Observe(Task actual) => Observed.Add(actual);
    }
    private sealed class OriginalPage : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
    {
        internal readonly TaskCompletionSource Close = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? Request;
        internal Action? Guard;
        internal Func<Task>? Acquire;
        internal int Requests; internal int Acquisitions;
        public void RequestRetirement() { Requests++; Request?.Invoke(); }
        public void DemandExternalOriginalRetirementJoin() => Guard?.Invoke();
        public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); Acquisitions++; return Acquire?.Invoke() ?? Close.Task; }
    }
    private sealed class OrdinaryPage : IDisposable
    { internal int Disposals; public void Dispose() => Disposals++; }
}
