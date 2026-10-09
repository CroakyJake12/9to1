#if !ANDROID
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Controls;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
using OriginalPermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task A_saved_Home_callback_after_genuine_healthy_claim_pruning_blocks_the_next_save_and_owner_close() =>
        RunActualHomeCallbackCustodyControl(afterHealthyPrune: true);

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task A_late_Home_callback_inside_an_admitted_scope_refuses_its_productive_body_and_retains_the_owner_cause() =>
        RunActualHomeCallbackCustodyControl(afterHealthyPrune: false);

    [Fact]
    public void Home_callback_protocol_preserves_only_exact_bounded_nonempty_body_wrappers()
    {
        var bodyCause = new InvalidOperationException("Actual synchronous body occurrence");
        var foreignCause = new IOException("Independent caller occurrence");
        ObserveProtocolWrapper(bodyCause, new AggregateException(new AggregateException(bodyCause)), unexpected: false);
        ObserveProtocolWrapper(bodyCause, new AggregateException(bodyCause, foreignCause), unexpected: true);
        ObserveProtocolWrapper(bodyCause, new AggregateException(bodyCause, new AggregateException()), unexpected: true);
        var opaque = new HomeCallbackUnknownAggregate(bodyCause);
        Assert.Same(opaque, Assert.Single(HomeCallbackLeaves(opaque)));
        Assert.DoesNotContain(HomeCallbackLeaves(opaque), cause => ReferenceEquals(cause, bodyCause));
        ObserveProtocolWrapper(bodyCause, opaque, unexpected: true);
        ObserveProtocolWrapper(bodyCause, new AggregateException(Enumerable.Repeat<Exception>(bodyCause, 4097)), unexpected: true);
    }
    private sealed class HomeCallbackUnknownAggregate(Exception sameBody) : AggregateException(sameBody);
    private static void ObserveProtocolWrapper(Exception sameBody, Exception sameWrapper, bool unexpected)
    {
        var causes = new List<Exception>();
        void Caller(Action body)
        {
            try { body(); }
            catch (Exception caught) { Assert.Same(sameBody, caught); throw sameWrapper; }
        }
        var helper = typeof(HomeCanonicalGeneratedUiInteractionWriteSource).Assembly.GetType(
            "HavenOS.Home.Core.HomeOwnershipOriginalSourceCallbacks", throwOnError: true)!;
        var constructor = Assert.Single(helper.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        var protocol = constructor.Invoke([new Action<Action>(Caller), new Action<Task>(_ => { }), new Action<Exception>(causes.Add)]);
        var invoke = Assert.Single(helper.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic), method => method.Name == "Invoke");
        var failure = Assert.Throws<TargetInvocationException>(() =>
        {
            _ = invoke.MakeGenericMethod(typeof(int)).Invoke(protocol, [new Func<int>(() => throw sameBody)]);
        });
        Assert.NotNull(failure.InnerException);
        var actualErrors = Assert.IsType<Exception[]>(helper.GetProperty("Errors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(protocol));
        Assert.Contains(actualErrors, cause => ReferenceEquals(cause, sameBody));
        if (unexpected) Assert.Contains(causes, cause => ReferenceEquals(cause, sameWrapper));
        else Assert.Empty(causes);
    }

    private static async Task RunActualHomeCallbackCustodyControl(bool afterHealthyPrune)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var rig = new Rig(originalAdditionalPolicy: new HomeCanonicalGeneratedUiInteractionActionPolicySource());
            ProtectedGeneratedGraph? graph = null;
            var roots = new List<object>(); var observed = new List<Exception>();
            Exception? expectedLate = null, controlFailure = null;
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                var binding = await rig.CreateAsync(new() { Name = "Fictional Home callback custody", Memory = new(false) });
                await AddProtectedGeneratedMessage(rig, binding);
                var actual = graph = new ProtectedGeneratedGraph(rig); roots.Add(actual);
                var view = WithProtectedGeneratedView(rig, actual, async (window, surface, controller, host) =>
                {
                    await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                    await AwaitOriginalGeneratedTree(window, surface, () => host.OriginalInteractionObservations.Count != 0 ||
                        window.GetVisualDescendants().OfType<Haven.Desktop.Controls.GenerativeUiSurface>().Any());
                    await ClickProtectedGeneratedControl(window, host, "Review generated interaction 1");
                    var observation = Assert.Single(host.OriginalInteractionObservations);
                    var intent = await actual.Writer.PrepareOriginalSaveWithinSourceAsync(observation, Guid.NewGuid(), Scope, actual.Retain, Token);
                    var first = actual.Writer.CommitOriginalSaveWithinSourceAsync(intent, Scope, actual.Retain, Token);
                    actual.Retain(first); roots.Add(first);
                    var review = await ObserveHomeCallbackReview(rig, first, actual);
                    var resource = Assert.Single(review.Impact.ResourceBinding!.Scopes);
                    var actor = Assert.IsType<AuthenticatedResourceActor>(await rig.Profiles.GetCurrentWithinOriginalSourceAsync(Scope, actual.Retain, Token));
                    Action? oldCallback = null;
                    void CaptureActualHomeCallback(Action body) { oldCallback ??= body; body(); }
                    var evaluated = actual.Home.EvaluateWithinOriginalSourceAsync(actor, HomeCanonicalGeneratedUiInteractionWriteSource.SaveAction,
                        resource, CaptureActualHomeCallback, actual.Retain, Token);
                    actual.Retain(evaluated); roots.Add(evaluated);
                    Assert.True((await evaluated.WaitAsync(TimeSpan.FromSeconds(15), Token)).Allowed);
                    Assert.NotNull(oldCallback);
                    Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> affected = first;
                    if (afterHealthyPrune)
                    {
                        Assert.True((await rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
                        await first.WaitAsync(TimeSpan.FromSeconds(15), Token);
                        Assert.True(first.IsCompletedSuccessfully);
                        var firstClaim = await ObserveActualHealthyHomeClaim(actual);
                        var originalClose = Assert.IsAssignableFrom<Task>(firstClaim.OriginalClose);
                        await originalClose.WaitAsync(TimeSpan.FromSeconds(15), Token);
                        Assert.Same(originalClose, firstClaim.CloseAndDrainOriginalAsync());
                        // Observe the maintained owner's real close observer, rather than
                        // waiting a tick or changing its healthy-prune flag.
                        var closeObserver = Assert.IsAssignableFrom<Task>(ReadHomeCallbackField(firstClaim, "_closeObservation"));
                        actual.Retain(closeObserver); await closeObserver.WaitAsync(TimeSpan.FromSeconds(15), Token);
                        Assert.Contains(ReadActualHomeClaims(actual.Home), item => ReferenceEquals(item, firstClaim));
                        await ClickProtectedGeneratedControl(window, host, "Review generated interaction 1");
                        var fresh = Assert.Single(host.OriginalInteractionObservations);
                        var nextIntent = await actual.Writer.PrepareOriginalSaveWithinSourceAsync(fresh, Guid.NewGuid(), Scope, actual.Retain, Token);
                        affected = actual.Writer.CommitOriginalSaveWithinSourceAsync(nextIntent, Scope, actual.Retain, Token);
                        actual.Retain(affected); roots.Add(affected);
                        await ObserveHomeCallbackReview(rig, affected, actual);
                        Assert.DoesNotContain(ReadActualHomeClaims(actual.Home), item => ReferenceEquals(item, firstClaim));
                        expectedLate = Assert.Throws<InvalidOperationException>(() => oldCallback!());
                    }
                    else
                    {
                        var bodyCalls = 0;
                        void LateBeforeActualBody(Action body)
                        {
                            expectedLate ??= Assert.Throws<InvalidOperationException>(() => oldCallback!());
                            body(); bodyCalls++;
                        }
                        var admitted = actual.Home.EvaluateWithinOriginalSourceAsync(actor, HomeCanonicalGeneratedUiInteractionWriteSource.SaveAction,
                            resource, LateBeforeActualBody, actual.Retain, Token);
                        actual.Retain(admitted); roots.Add(admitted);
                        Assert.NotNull(await Record.ExceptionAsync(() => admitted.WaitAsync(TimeSpan.FromSeconds(15), Token)));
                        Assert.True(admitted.IsFaulted); Assert.Equal(0, bodyCalls);
                        ObserveOnlyHomeCallbackCause(admitted.Exception!, expectedLate!, observed);
                    }
                    Assert.NotNull(expectedLate);
                    Assert.Null(expectedLate!.InnerException);
                    Assert.Equal("The original ownership callback is inactive, foreign-thread or consumed.", expectedLate.Message);
                    observed.Add(expectedLate);
                    Assert.NotNull(await Record.ExceptionAsync(() => affected.WaitAsync(TimeSpan.FromSeconds(15), Token)));
                    Assert.True(affected.IsFaulted); Assert.False(affected.IsCanceled);
                    ObserveOnlyHomeCallbackCause(affected.Exception!, expectedLate!, observed);
                    Assert.Equal(afterHealthyPrune ? 1L : 0L, await CountProtectedGeneratedRows(rig, "genui_apps"));
                    Assert.Equal(afterHealthyPrune ? 1L : 0L, await CountProtectedGeneratedOperations(rig));
                    var futureCalls = 0;
                    Assert.NotNull(Record.Exception((Action)(() =>
                    {
                        var unexpectedlyReturned = actual.Home.EvaluateWithinOriginalSourceAsync(actor,
                            HomeCanonicalGeneratedUiInteractionWriteSource.SaveAction, resource, body => { futureCalls++; body(); }, actual.Retain, Token);
                        actual.Retain(unexpectedlyReturned); roots.Add(unexpectedlyReturned);
                    })));
                    Assert.Equal(0, futureCalls);
                    Assert.Null(rig.Store.OriginalClose); Assert.Null(actual.Origins.OriginalClose);
                });
                roots.Add(view);
                var viewError = await Record.ExceptionAsync(() => view.WaitAsync(TimeSpan.FromSeconds(30), Token));
                if (viewError is not null)
                {
                    Assert.NotNull(expectedLate);
                    ObserveOnlyHomeCallbackCause(viewError, expectedLate!, observed);
                }
                Assert.NotNull(expectedLate);
                // Acquire and independently join both process closes. The writer's
                // unknown outcome does not skip this separately owned Home close.
                Task? writerClose = null, homeClose = null; var closeErrors = new List<Exception>();
                try { writerClose = actual.Writer.CloseAndDrainOriginalAsync(); actual.Retain(writerClose); roots.Add(writerClose); }
                catch (Exception cause) { closeErrors.Add(cause); }
                try { homeClose = actual.Home.CloseAndDrainOriginalAsync(); actual.Retain(homeClose); roots.Add(homeClose); }
                catch (Exception cause) { closeErrors.Add(cause); }
                foreach (var raw in new[] { writerClose, homeClose })
                    if (raw is not null)
                    {
                        try { await raw.WaitAsync(TimeSpan.FromSeconds(15), Token); }
                        catch (Exception cause) { closeErrors.Add(raw.Exception ?? cause); }
                    }
                Assert.NotNull(writerClose); Assert.NotNull(homeClose);
                Assert.True(writerClose!.IsFaulted); Assert.True(homeClose!.IsFaulted);
                Assert.Same(homeClose, actual.Home.CloseAndDrainOriginalAsync());
                foreach (var cause in closeErrors) ObserveOnlyHomeCallbackCause(cause, expectedLate!, observed);
                Assert.Contains(HomeCallbackLeaves(homeClose.Exception!), cause => ReferenceEquals(cause, expectedLate));
                Assert.Null(rig.Store.OriginalClose); Assert.Null(actual.Origins.OriginalClose);
                return true;
            }
            catch (Exception cause) { controlFailure = cause; observed.Add(cause); throw; }
            finally
            {
                var cleanup = new List<Exception>();
                if (graph is { } same)
                {
                    Task? withdrawal = null, writerClose = null;
                    try
                    {
                        same.Home.RequestOriginalPendingReviewWithdrawals();
                        withdrawal = same.Home.OriginalPendingReviewWithdrawalTask;
                        same.Writer.RequestOriginalPendingReviewWithdrawals();
                        if (withdrawal is not null) { same.Retain(withdrawal); roots.Add(withdrawal); }
                    }
                    catch (Exception cause) { cleanup.Add(cause); }
                    if (withdrawal is not null)
                        try { await withdrawal.WaitAsync(TimeSpan.FromSeconds(15), Token); }
                        catch (Exception cause) { cleanup.Add(withdrawal.Exception ?? cause); }
                    try { writerClose = same.Writer.CloseAndDrainOriginalAsync(); same.Retain(writerClose); roots.Add(writerClose); }
                    catch (Exception cause) { cleanup.Add(cause); }
                    if (writerClose is not null)
                        try { await writerClose.WaitAsync(TimeSpan.FromSeconds(15), Token); }
                        catch (Exception cause) { cleanup.Add(writerClose.Exception ?? cause); }
                    // A terminal writer permits this separate issuer-only close probe;
                    // an unresolved predecessor retains Home without acquiring it.
                    if (writerClose?.IsCompleted == true)
                    {
                        Task? homeClose = null;
                        try { homeClose = same.Home.CloseAndDrainOriginalAsync(); same.Retain(homeClose); roots.Add(homeClose); }
                        catch (Exception cause) { cleanup.Add(cause); }
                        if (homeClose is not null)
                            try { await homeClose.WaitAsync(TimeSpan.FromSeconds(15), Token); }
                            catch (Exception cause) { cleanup.Add(homeClose.Exception ?? cause); }
                    }
                    Task[] raw; lock (same.Originals) raw = same.Originals.Distinct().ToArray();
                    foreach (var actualRaw in raw)
                        try { await actualRaw.WaitAsync(TimeSpan.FromSeconds(15), Token); }
                        catch (Exception cause) { cleanup.Add(actualRaw.Exception ?? cause); }
                }
                observed.AddRange(cleanup);
                // Every unknown/control occurrence keeps the SAME objects retained.
                // Store/Den/origin cleanup is barred by the failed writer/Home closes.
                lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, graph!, roots, observed]);
                if (controlFailure is null && expectedLate is not null)
                    foreach (var cause in cleanup) ObserveOnlyHomeCallbackCause(cause, expectedLate, observed);
                else if (cleanup.Count != 0)
                    throw new AggregateException("Actual control and independent cleanup causes remain retained.",
                        controlFailure is null ? cleanup : new[] { controlFailure }.Concat(cleanup));
            }
        }, Token));
    }

    private static async Task<OriginalPermissionRequest> ObserveHomeCallbackReview(Rig rig, Task sameDriver, ProtectedGeneratedGraph graph)
    {
        for (var attempt = 0; attempt < 1500; attempt++)
        {
            var snapshot = rig.Permissions.GetSnapshotAsync(cancellationToken: Token); graph.Retain(snapshot); var current = await snapshot;
            var pending = current.PendingRequests.Where(request => request.Scope.ActionName == HomeCanonicalGeneratedUiInteractionWriteSource.SaveAction &&
                request.State == HomePermissionRequestState.PendingApproval).ToArray();
            if (pending.Length != 0) return Assert.Single(pending);
            if (sameDriver.IsCompleted) { await sameDriver; throw new InvalidOperationException("The real save ended before its original manual review."); }
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The original Home save did not publish its actual individual review.");
    }
    private static async Task<ICanonicalGeneratedUiOriginalHomeWriteClaim> ObserveActualHealthyHomeClaim(ProtectedGeneratedGraph graph)
    {
        Task[] captured; lock (graph.Originals) captured = graph.Originals.ToArray();
        foreach (var candidate in captured.OfType<Task<ICanonicalGeneratedUiOriginalHomeWriteClaim>>())
        {
            var claim = await candidate;
            if (ReferenceEquals(candidate, claim.OriginalAcquisition)) return claim;
        }
        throw new InvalidOperationException("The real writer did not retain the actual source-issued Home acquisition.");
    }
    private static object? ReadHomeCallbackField(object same, string field) => same.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(same);
    private static object[] ReadActualHomeClaims(HomeCanonicalGeneratedUiInteractionWriteSource same)
    {
        var actualGate = Assert.IsType<object>(ReadHomeCallbackField(same, "_gate"));
        lock (actualGate)
            return Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReadHomeCallbackField(same, "_active")).Cast<object>().ToArray();
    }
    private static IEnumerable<Exception> HomeCallbackLeaves(Exception cause)
    {
        var pending = new Stack<Exception>(); var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var enqueued = 1; pending.Push(cause);
        while (pending.TryPop(out var actual))
        {
            if (!seen.Add(actual)) continue;
            if (seen.Count > 4096) throw new InvalidOperationException("The original failure graph exceeds its bounded inspection.");
            if (actual.GetType() == typeof(AggregateException) && actual is AggregateException { InnerExceptions.Count: > 0 } group)
                foreach (var child in group.InnerExceptions)
                {
                    if (++enqueued > 4096) throw new InvalidOperationException("The original failure graph exceeds its bounded occurrence inspection.");
                    pending.Push(child);
                }
            else yield return actual; // Empty aggregates and opaque subclasses remain unknown leaves.
        }
    }
    private static void ObserveOnlyHomeCallbackCause(Exception actual, Exception expected, List<Exception> observations)
    {
        var leaves = HomeCallbackLeaves(actual).ToArray(); Assert.NotEmpty(leaves);
        Assert.All(leaves, leaf => Assert.Same(expected, leaf)); observations.Add(actual);
    }
}
#endif
