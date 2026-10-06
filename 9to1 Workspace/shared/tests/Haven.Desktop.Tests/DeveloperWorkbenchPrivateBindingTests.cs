using System.Reflection;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>Actual page/control and actual Dev source session with the original synthetic
/// tool/issuer fixture. The test enrolls a private cached snapshot directly to isolate the
/// binding retirement boundary; it certifies no readiness, frame, GUI or Home grant.</summary>
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [AvaloniaFact]
    public async Task Withdrawn_view_binding_refuses_private_source_and_outcome_but_keeps_original_evidence()
    {
        var f = await Fixture.CreateAsync(); var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
        var page = new DeveloperProjectWorkbenchPage(f.Dev, f.Tasks, resolved, f.Current.TaskId, f.Current.ExecutionId,
            f.Current.ContextId, new NoPresentationReadiness(), (_, _, _) => throw new InvalidOperationException("No file selection was requested."), () => { });
        Task? close = null;
        try
        {
            var review = (DeveloperSourceReviewSession)Field("_review").GetValue(page)!;
            var opened = (await review.OpenAsync(f.Reference, f.Context(), f.Document)).Value!;
            Field("_editor").SetValue(page, opened); Field("_active").SetValue(page, true);
            Field("_processOutput").SetValue(page, "retained actual private output observation");
            Assert.True(page.TryGetValue("DraftText", out var before)); Assert.Equal(opened.DraftText, before);
            Assert.True(page.TryGetValue("ProcessOutput", out var output)); Assert.Equal("retained actual private output observation", output);
            page.Deactivate();
            foreach (var name in new[] { "DraftText", "BeforeText", "AfterText", "ProcessOutput", "ProjectSummary" })
            { Assert.False(page.TryGetValue(name, out var value)); Assert.Null(value); }
            Assert.Same(opened, Field("_editor").GetValue(page));
            close = page.CloseAndDrainAsync(); await close;
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(page.TryGetValue("DraftText", out var retired)); Assert.Null(retired);
            Assert.Same(opened, Field("_editor").GetValue(page));
        }
        finally { if (close is null) close = page.CloseAndDrainAsync(); try { await close; } finally { await f.Dev.CloseAndDrainAsync(); } }
    }
    [AvaloniaFact]
    public async Task Borrowed_original_document_source_guard_refuses_page_join_before_acquiring_any_close()
    {
        var f = await Fixture.CreateAsync(); var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
        var cause = new InvalidOperationException("The actual borrowed document source callback is still live."); var live = true;
        var page = new DeveloperProjectWorkbenchPage(f.Dev, f.Tasks, resolved, f.Current.TaskId, f.Current.ExecutionId,
            f.Current.ContextId, new NoPresentationReadiness(), (_, _, _) => throw new InvalidOperationException("No read was requested."),
            () => { if (live) throw cause; });
        Task? close = null;
        try
        {
            Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => { page.CloseAndDrainAsync(); }));
            var work = Field("_work").GetValue(page)!;
            Assert.Null(work.GetType().GetField("_close", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(work));
            live = false; close = page.CloseAndDrainAsync(); await close; Assert.Same(close, page.CloseAndDrainAsync());
        }
        finally { live = false; if (close is null) close = page.CloseAndDrainAsync(); try { await close; } finally { await f.Dev.CloseAndDrainAsync(); } }
    }
    [AvaloniaFact]
    public async Task Files_document_observer_remains_bound_to_the_actual_captured_view_generation()
    {
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null; Task? close = null;
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
            page = new DeveloperProjectWorkbenchPage(f.Dev, f.Tasks, resolved, f.Current.TaskId, f.Current.ExecutionId,
                f.Current.ContextId, new NoPresentationReadiness(), (_, _, _) => throw new InvalidOperationException("No file selection was requested."), () => { });
            var capture = typeof(DeveloperProjectWorkbenchPage).GetMethod("CaptureOriginalDocumentObservationCurrentness", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Field("_active").SetValue(page, true);
            var original = (Func<bool>)capture.Invoke(page, null)!;
            Assert.True(original());
            page.Deactivate(); Assert.False(original());
            Field("_active").SetValue(page, true); // Component generation enrollment only, never a frame/readiness witness.
            Assert.False(original());
            var next = (Func<bool>)capture.Invoke(page, null)!; Assert.True(next());
            close = page.CloseAndDrainAsync(); await close;
            Assert.False(original()); Assert.False(next());
            var refused = Assert.Throws<TargetInvocationException>(() => capture.Invoke(page, null));
            Assert.IsType<ObjectDisposedException>(refused.InnerException);
        }
        finally
        {
            var errors = new List<Exception>();
            if (page is not null) try { close ??= page.CloseAndDrainAsync(); await close; } catch (Exception error) { errors.Add(error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Actual document observer fixture cleanup.", errors);
        }
    }
    [AvaloniaFact]
    public async Task Host_constructor_capture_retains_same_partial_page_before_retirement_refuses_content_publication()
    {
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? acquired = null; Task? close = null;
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
            var constructor = typeof(DeveloperProjectWorkbenchPage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(value => value.GetParameters().Length == 11);
            Action<DeveloperProjectWorkbenchPage> capture = value => { acquired = value; value.RequestRetirement(); };
            Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> resolver =
                (_, _, _) => throw new InvalidOperationException("No file selection was requested.");
            var failure = Assert.Throws<TargetInvocationException>(() => constructor.Invoke([f.Dev, f.Tasks, resolved,
                f.Current.TaskId, f.Current.ExecutionId, f.Current.ContextId, new NoPresentationReadiness(), resolver,
                (Action)(() => { }), CancellationToken.None, capture]));
            Assert.NotNull(acquired);
            Assert.Null(acquired.Content);
            Assert.IsType<ObjectDisposedException>(failure.InnerException);
            close = acquired.CloseAndDrainAsync(); await close;
            Assert.Same(close, acquired.CloseAndDrainAsync());
        }
        finally
        {
            var errors = new List<Exception>();
            if (acquired is not null) try { close ??= acquired.CloseAndDrainAsync(); await close; } catch (Exception error) { errors.Add(error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Actual partial workbench acquisition cleanup.", errors);
        }
    }
    [AvaloniaFact]
    public async Task Genuine_readiness_child_is_captured_after_page_and_same_close_waits_before_host_detach()
    {
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null; Task? close = null;
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = new OriginalReadinessChild(raw.Task);
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
            Func<DeveloperProjectWorkbenchPage, ICuiSceneReadiness> creator = original =>
            { Assert.Same(page, original); Assert.NotNull(Field("_host").GetValue(original)); return child; };
            page = CreateReadinessChildPage(f, resolved, original => page = original, creator);
            Assert.Same(child, Field("_readiness").GetValue(page));
            close = page.CloseAndDrainAsync();
            Assert.Same(close, page.CloseAndDrainAsync()); Assert.True(child.Stopped); Assert.False(close.IsCompleted);
            Assert.Same(Field("_host").GetValue(page), page.Content);
            raw.TrySetResult(); await close.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Same(child.OriginalClose, Field("_readinessChildClose").GetValue(page)); Assert.Null(page.Content);
        }
        finally
        {
            raw.TrySetResult(); var errors = new List<Exception>();
            if (page is not null) try { close ??= page.CloseAndDrainAsync(); await close; } catch (Exception error) { errors.Add(error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
    [AvaloniaFact]
    public async Task Actual_child_join_guard_refuses_before_acquiring_the_existing_parent_close()
    {
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null; Task? close = null;
        var child = new OriginalReadinessChild(Task.CompletedTask) { Live = true };
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
            page = CreateReadinessChildPage(f, resolved, original => page = original, _ => child);
            Assert.Same(child.LiveCause, Assert.Throws<InvalidOperationException>(() => { page.CloseAndDrainAsync(); }));
            var work = Field("_work").GetValue(page)!;
            Assert.Null(work.GetType().GetField("_close", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(work));
            child.Live = false; close = page.CloseAndDrainAsync(); await close; Assert.Same(close, page.CloseAndDrainAsync());
        }
        finally
        {
            child.Live = false; var errors = new List<Exception>();
            if (page is not null) try { close ??= page.CloseAndDrainAsync(); await close; } catch (Exception error) { errors.Add(error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
    [AvaloniaFact]
    public async Task Readiness_predicate_is_exact_host_generation_and_retirement_refusal_only()
    {
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null; Task? close = null;
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
            page = CreateReadinessChildPage(f, resolved, original => page = original, _ => new OriginalReadinessChild(Task.CompletedTask));
            var predicate = typeof(DeveloperProjectWorkbenchPage).GetMethod("IsOriginalReadinessPresentationCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var generation = (long)typeof(DeveloperProjectWorkbenchPage).GetProperty("OriginalReadinessGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var host = Field("_host").GetValue(page)!;
            Assert.False((bool)predicate.Invoke(page, [host, generation])!);
            Field("_active").SetValue(page, true); // Component refusal predicate only; no genuine frame/Ready proof.
            Assert.True((bool)predicate.Invoke(page, [host, generation])!);
            Assert.False((bool)predicate.Invoke(page, [null, generation])!);
            page.Deactivate(); Field("_active").SetValue(page, true);
            Assert.False((bool)predicate.Invoke(page, [host, generation])!);
            close = page.CloseAndDrainAsync(); await close;
            Assert.False((bool)predicate.Invoke(page, [host, generation + 1])!);
        }
        finally
        {
            var errors = new List<Exception>();
            if (page is not null) try { close ??= page.CloseAndDrainAsync(); await close; } catch (Exception error) { errors.Add(error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
    [AvaloniaFact]
    public async Task Actual_final_content_callback_restored_context_refuses_same_parent_join()
    {
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null; Task? close = null;
        var context = ExecutionContext.Capture()!; Task? wrong = null; var refused = 0;
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
            page = CreateReadinessChildPage(f, resolved, original => page = original, _ => new OriginalReadinessChild(Task.CompletedTask));
            ((Avalonia.AvaloniaObject)page).PropertyChanged += (_, args) =>
            {
                if (args.Property != Avalonia.Controls.ContentControl.ContentProperty || page.Content is not null) return;
                ExecutionContext.Run(context, _ => { try { wrong = page.CloseAndDrainAsync(); } catch (InvalidOperationException) { refused++; } }, null);
            };
            close = page.CloseAndDrainAsync(); await close;
            Assert.Null(wrong); Assert.Equal(1, refused); Assert.Same(close, page.CloseAndDrainAsync());
            Assert.True(((Task)Field("_detachOriginalDispatcher").GetValue(page)!).IsCompletedSuccessfully);
        }
        finally
        {
            var errors = new List<Exception>();
            if (page is not null) try { close ??= page.CloseAndDrainAsync(); await close; } catch (Exception error) { errors.Add(error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
    [AvaloniaFact]
    public async Task Actual_final_content_fault_retains_exact_OCE_and_sibling_in_faulted_close()
    {
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null; Task? close = null;
        var first = new OperationCanceledException("Faulted content publication is not canceled dispatcher work.");
        var second = new IOException("Actual content cleanup publication sibling."); var expected = false;
        try
        {
            var resolved = (await f.Dev.ResolveAsync(f.Reference)).Value!;
            page = CreateReadinessChildPage(f, resolved, original => page = original, _ => new OriginalReadinessChild(Task.CompletedTask));
            ((Avalonia.AvaloniaObject)page).PropertyChanged += (_, args) =>
            {
                if (args.Property == Avalonia.Controls.ContentControl.ContentProperty && page.Content is null)
                    throw new AggregateException(first, second);
            };
            expected = true; close = page.CloseAndDrainAsync();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
            Assert.Contains(Causes(error), value => ReferenceEquals(value, first));
            Assert.Contains(Causes(error), value => ReferenceEquals(value, second));
            Assert.True(((Task)Field("_detachOriginalDispatcher").GetValue(page)!).IsFaulted);
        }
        finally
        {
            var errors = new List<Exception>();
            if (page is not null) try { close ??= page.CloseAndDrainAsync(); await close; } catch (Exception error) { if (!expected) errors.Add(error); }
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
    private static IEnumerable<Exception> Causes(Exception actual)
    {
        yield return actual;
        if (actual is AggregateException group)
            foreach (var member in group.InnerExceptions) foreach (var original in Causes(member)) yield return original;
    }
    private static DeveloperProjectWorkbenchPage CreateReadinessChildPage(Fixture f, DeveloperResolvedProject project,
        Action<DeveloperProjectWorkbenchPage> capture, Func<DeveloperProjectWorkbenchPage, ICuiSceneReadiness> creator)
    {
        var constructor = typeof(DeveloperProjectWorkbenchPage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(value => value.GetParameters().Length == 12);
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> resolver =
            (_, _, _) => throw new InvalidOperationException("No file selection requested in readiness child component.");
        return (DeveloperProjectWorkbenchPage)constructor.Invoke([f.Dev, f.Tasks, project, f.Current.TaskId,
            f.Current.ExecutionId, f.Current.ContextId, null, resolver, (Action)(() => { }), CancellationToken.None, capture, creator]);
    }
    private sealed class OriginalReadinessChild(Task originalClose) : ICuiSceneReadiness,
        Haven.Desktop.Services.IDesktopOriginalRetirementParticipant, Haven.Desktop.Services.IDesktopOriginalRetirementJoinGuard
    {
        internal bool Stopped, Live;
        internal InvalidOperationException LiveCause = new("The actual readiness child callback cannot join its parent.");
        internal Task OriginalClose => originalClose;
        public void RequestRetirement() => Stopped = true;
        public void DemandExternalOriginalRetirementJoin() { if (Live) throw LiveCause; }
        public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); RequestRetirement(); return originalClose; }
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable, "TEST_NO_READY", "Original lifetime component, not genuine native readiness."));
    }
    private static FieldInfo Field(string name) => typeof(DeveloperProjectWorkbenchPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private sealed class NoPresentationReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable, "TEST_NO_PRESENTATION", "This focused binding control has no native presentation."));
    }
}
