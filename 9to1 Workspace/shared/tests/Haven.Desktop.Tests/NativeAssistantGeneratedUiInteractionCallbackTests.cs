#if !ANDROID
using System.Collections;
using System.Reflection;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Saved_actual_READ_callback_after_successful_source_pruning_refuses_new_admission_and_faults_cached_close() =>
        RunProtectedGeneratedCallbackControl(alreadyAdmitted: false);

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Saved_actual_READ_callback_refuses_an_already_admitted_body_but_independently_closes_owned_sources() =>
        RunProtectedGeneratedCallbackControl(alreadyAdmitted: true);

    private static object ProtectedCallbackField(object actual, string field) => actual.GetType().GetField(field,
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)!.GetValue(actual)!;
    private static object ProtectedCallbackOriginal(CanonicalGeneratedUiInteractionOriginalOwner writer, Task same) =>
        Assert.Single(((IEnumerable)ProtectedCallbackField(writer, "_originals")).Cast<object>(),
            original => ReferenceEquals(ProtectedCallbackField(original, "Raw"), same));
    private static ICanonicalGeneratedUiOriginalSelection ProtectedCallbackSelection(OriginalAssistantGeneratedUiHost host) =>
        Assert.IsAssignableFrom<ICanonicalGeneratedUiOriginalSelection>(ProtectedCallbackField(
            Assert.Single(((IEnumerable)ProtectedCallbackField(host, "_interactions")).Cast<object>()), "Selection"));
    private static void AssertProtectedCallbackGraph(Exception actual, Exception sameCause)
    {
        if (ReferenceEquals(actual, sameCause)) return;
        var combined = Assert.IsType<AggregateException>(actual); Assert.NotEmpty(combined.InnerExceptions);
        foreach (var child in combined.InnerExceptions) AssertProtectedCallbackGraph(child, sameCause);
    }
    private static async Task RunProtectedGeneratedCallbackControl(bool alreadyAdmitted)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var rig = new Rig(originalAdditionalPolicy: new HomeCanonicalGeneratedUiInteractionActionPolicySource());
            ProtectedGeneratedGraph? graph = null;
            using var release = new ManualResetEventSlim(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var roots = new List<object>(); var raws = new List<Task>(); var expectedFaults = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            var errors = new List<Exception>(); Exception? actualLateFailure = null;
            Task<ICanonicalGeneratedUiOriginalObservation>? admittedBody = null;
            Task<ICanonicalGeneratedUiOriginalObservation>? actualAdmittedBody = null;
            void Capture(Task actual) { raws.Add(actual); roots.Add(actual); graph!.Retain(actual); }
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                var binding = await rig.CreateAsync(new() { Name = "Fictional late callback custody", Memory = new(false) });
                await AddProtectedGeneratedMessage(rig, binding); var actualGraph = graph = new(rig); roots.Add(actualGraph);
                await WithProtectedGeneratedView(rig, actualGraph, async (window, surface, controller, host) =>
                {
                    await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                    await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                    var review = ClickProtectedGeneratedControl(window, host, "Review generated interaction 1"); Capture(review); await review;
                    var selection = ProtectedCallbackSelection(host); Action? saved = null;
                    void SaveActualCallback(Action actual) { saved ??= actual; actual(); }
                    var first = actualGraph.Writer.ReadOriginalInteractionWithinSourceAsync(selection, SaveActualCallback, actualGraph.Retain, Token);
                    Capture(first); var firstObservation = await first;
                    Assert.True(actualGraph.Writer.IsIssuedOriginalObservation(firstObservation));
                    var firstOriginal = ProtectedCallbackOriginal(actualGraph.Writer, first);
                    var firstSource = ProtectedCallbackField(firstOriginal, "Source"); roots.Add(firstSource);
                    var observed = Assert.IsAssignableFrom<Task>(ProtectedCallbackField(firstOriginal, "Observation")); Capture(observed); await observed;
                    var prune = actualGraph.Writer.RevalidateOriginalObservationWithinSourceAsync(firstObservation, Scope, actualGraph.Retain, Token);
                    Capture(prune); await prune;
                    Assert.DoesNotContain(((IEnumerable)ProtectedCallbackField(actualGraph.Writer, "_originals")).Cast<object>(),
                        original => ReferenceEquals(ProtectedCallbackField(original, "Source"), firstSource));
                    Assert.NotNull(saved);
                    if (alreadyAdmitted)
                    {
                        var firstEntry = 0;
                        void HoldActualCallback(Action actual)
                        {
                            if (Interlocked.Exchange(ref firstEntry, 1) == 0)
                            {
                                entered.TrySetResult();
                                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("The genuine admitted callback was not released.");
                            }
                            actual();
                        }
                        // Controlled scheduling only: the real current issuer/READ/source
                        // body is used unchanged. No substitute origin, actor or SQLite lease.
                        admittedBody = Task.Run(() =>
                        {
                            actualAdmittedBody = actualGraph.Writer.ReadOriginalInteractionWithinSourceAsync(selection,
                                HoldActualCallback, actualGraph.Retain, CancellationToken.None);
                            actualGraph.Retain(actualAdmittedBody); return actualAdmittedBody;
                        });
                        Capture(admittedBody); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                    }
                    actualLateFailure = Assert.Throws<InvalidOperationException>(() => saved!()); roots.Add(actualLateFailure);
                    Assert.Equal("The original SQLite callback is inactive, foreign-thread or already consumed.", actualLateFailure.Message);
                    Assert.Null(actualLateFailure.InnerException);
                    release.Set();
                    if (admittedBody is not null)
                    {
                        expectedFaults.Add(admittedBody);
                        Assert.NotNull(await Record.ExceptionAsync(() => admittedBody.WaitAsync(TimeSpan.FromSeconds(15), Token)));
                        Assert.True(admittedBody.IsFaulted); Assert.False(admittedBody.IsCanceled);
                        AssertProtectedCallbackGraph(admittedBody.Exception!, actualLateFailure);
                        var sameBody = Assert.IsAssignableFrom<Task<ICanonicalGeneratedUiOriginalObservation>>(actualAdmittedBody);
                        Capture(sameBody); expectedFaults.Add(sameBody);
                        var bodyOriginal = ProtectedCallbackOriginal(actualGraph.Writer, sameBody);
                        var bodySource = ProtectedCallbackField(bodyOriginal, "Source"); roots.Add(bodySource);
                        // Sticky failure is checked inside the admitted actual callback,
                        // before its first origin getter/async productive READ factory.
                        Assert.Empty(((IEnumerable)ProtectedCallbackField(bodySource, "_raw")).Cast<object>());
                        Assert.Empty(((IEnumerable)ProtectedCallbackField(bodySource, "_resources")).Cast<object>());
                    }
                    Task<ICanonicalGeneratedUiOriginalObservation>? unexpected = null;
                    var refusal = Record.Exception(() =>
                    { unexpected = actualGraph.Writer.ReadOriginalInteractionWithinSourceAsync(selection, Scope, actualGraph.Retain, Token); Capture(unexpected); });
                    Assert.NotNull(refusal); Assert.Null(unexpected); AssertProtectedCallbackGraph(refusal!, actualLateFailure);
                    Assert.Equal(0L, await CountProtectedGeneratedRows(rig, "genui_apps"));
                    Assert.Equal(0L, await CountProtectedGeneratedOperations(rig));
                    Assert.Null(actualGraph.Origins.OriginalClose); Assert.Null(actualGraph.Home.OriginalClose); Assert.Null(rig.Store.OriginalClose);
                });
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                release.Set();
                // Every accepted original is independently joined even after an early
                // assertion/observer failure. Failed writer keeps all process dependencies.
                if (graph is not null)
                {
                    if (actualAdmittedBody is not null && !raws.Any(raw => ReferenceEquals(raw, actualAdmittedBody)))
                    { Capture(actualAdmittedBody); if (actualLateFailure is not null) expectedFaults.Add(actualAdmittedBody); }
                    Task? close = null;
                    try { close = graph.CloseAsync(); Capture(close); if (actualLateFailure is not null) expectedFaults.Add(close); }
                    catch (Exception cause) { errors.Add(cause); }
                    foreach (var raw in raws.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray())
                    {
                        try { await raw.WaitAsync(TimeSpan.FromSeconds(15)); raw.GetAwaiter().GetResult(); }
                        catch (Exception cause)
                        {
                            var complete = raw.Exception ?? cause; roots.Add(complete);
                            if (actualLateFailure is null || !expectedFaults.Contains(raw) || !raw.IsFaulted || raw.IsCanceled) errors.Add(complete);
                            else try { AssertProtectedCallbackGraph(complete, actualLateFailure); } catch (Exception mismatch) { errors.Add(complete); errors.Add(mismatch); }
                        }
                    }
                    if (actualLateFailure is not null && close is not null)
                    {
                        try
                        {
                            Assert.True(close.IsFaulted); Assert.False(close.IsCanceled); Assert.Same(close, graph.CloseAsync());
                            Assert.Null(graph.Origins.OriginalClose); Assert.Null(graph.Home.OriginalClose); Assert.Null(rig.Store.OriginalClose);
                        }
                        catch (Exception cause) { errors.Add(cause); }
                    }
                }
                lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, graph!, roots, raws, errors]);
            }
            if (errors.Count != 0) throw new AggregateException("Genuine original callback control retains every body/raw/cleanup failure.", errors);
            return true;
        }, Token));
    }

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Saved_actual_origin_callback_fences_admitted_and_fresh_snapshot_bodies_and_retains_close_cause()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var rig = new Rig(originalAdditionalPolicy: new HomeCanonicalGeneratedUiInteractionActionPolicySource());
            ProtectedGeneratedGraph? graph = null;
            var roots = new List<object>(); var raws = new List<Task>(); var errors = new List<Exception>();
            var expected = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            Exception? sameLate = null; Haven.Core.GenUiDocument? sameDocument = null;
            void Capture(Task actual) { raws.Add(actual); roots.Add(actual); graph!.Retain(actual); }
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                var binding = await rig.CreateAsync(new() { Name = "Fictional origin callback custody", Memory = new(false) });
                await AddProtectedGeneratedMessage(rig, binding);
                var actualGraph = graph = new(rig); roots.Add(actualGraph);
                var view = WithProtectedGeneratedView(rig, actualGraph, async (window, surface, controller, host) =>
                {
                    await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                    await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                    var selection = ProtectedCallbackSelection(host); Action? saved = null;
                    void SaveActualOriginCallback(Action body) { saved ??= body; body(); }
                    var first = actualGraph.Origins.ObserveOriginalSnapshotWithinSourceAsync(selection,
                        SaveActualOriginCallback, actualGraph.Retain, Token);
                    Capture(first); var snapshot = await first; roots.Add(snapshot);
                    sameDocument = snapshot.OriginalRegistration.Document; roots.Add(sameDocument);
                    Assert.True(first.IsCompletedSuccessfully);
                    Assert.True(actualGraph.Origins.IsIssuedOriginalSnapshot(snapshot)); Assert.NotNull(saved);
                    var productiveBodies = 0; var callbackCalls = 0;
                    void LateBeforeAdmittedOriginBody(Action body)
                    {
                        if (Interlocked.Exchange(ref callbackCalls, 1) == 0)
                        {
                            sameLate = Assert.Throws<InvalidOperationException>(() => saved!()); roots.Add(sameLate);
                            Assert.Null(sameLate.InnerException);
                            Assert.Equal("The original origin callback is inactive, repeated or foreign-thread.", sameLate.Message);
                        }
                        body(); productiveBodies++;
                    }
                    // Actual source admission succeeds first. The borrowed finite
                    // caller then invokes the previously consumed real callback;
                    // the inner productive getter must be fenced before it runs.
                    var admitted = actualGraph.Origins.ObserveOriginalSnapshotWithinSourceAsync(selection,
                        LateBeforeAdmittedOriginBody, actualGraph.Retain, Token);
                    Capture(admitted); expected.Add(admitted);
                    Assert.NotNull(await Record.ExceptionAsync(() => admitted.WaitAsync(TimeSpan.FromSeconds(15), Token)));
                    Assert.NotNull(sameLate); Assert.True(admitted.IsFaulted); Assert.False(admitted.IsCanceled);
                    AssertProtectedCallbackGraph(admitted.Exception!, sameLate!);
                    Assert.Equal(0, productiveBodies); Assert.Equal(1, callbackCalls);
                    var priorCohorts = ProtectedOriginCohorts(actualGraph.Origins);
                    var futureCallbacks = 0;
                    var future = actualGraph.Origins.ObserveOriginalSnapshotWithinSourceAsync(selection,
                        body => { futureCallbacks++; body(); }, actualGraph.Retain, Token);
                    Capture(future); expected.Add(future);
                    Assert.NotNull(await Record.ExceptionAsync(() => future.WaitAsync(TimeSpan.FromSeconds(15), Token)));
                    Assert.True(future.IsFaulted); Assert.False(future.IsCanceled);
                    AssertProtectedCallbackGraph(future.Exception!, sameLate!); Assert.Equal(0, futureCallbacks);
                    var remaining = ProtectedOriginCohorts(actualGraph.Origins);
                    Assert.Equal(priorCohorts.Length, remaining.Length);
                    Assert.All(priorCohorts, same => Assert.Contains(remaining, actual => ReferenceEquals(same, actual)));
                    Assert.Equal(0L, await CountProtectedGeneratedRows(rig, "genui_apps"));
                    Assert.Equal(0L, await CountProtectedGeneratedOperations(rig));
                    Assert.Null(actualGraph.Origins.OriginalClose); Assert.Null(actualGraph.Home.OriginalClose); Assert.Null(rig.Store.OriginalClose);
                });
                Capture(view); await view;
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                if (graph is { } actual)
                {
                    Task? close = null;
                    try { close = actual.CloseAsync(); Capture(close); if (sameLate is not null) expected.Add(close); }
                    catch (Exception cause) { errors.Add(cause); }
                    if (close is not null)
                        try { await close.WaitAsync(TimeSpan.FromSeconds(15)); }
                        catch (Exception cause)
                        {
                            if (sameLate is null) errors.Add(close.Exception ?? cause);
                            else try { AssertProtectedCallbackGraph(close.Exception ?? cause, sameLate); }
                                catch (Exception mismatch) { errors.Add(close.Exception ?? cause); errors.Add(mismatch); }
                        }
                    // The actual healthy writer barrier precedes origin retirement.
                    // The origin's independently captured cached failure keeps Home,
                    // Den/store and runtime documents rooted rather than detaching.
                    if (actual.Origins.OriginalClose is { } originClose)
                    { Capture(originClose); if (sameLate is not null) expected.Add(originClose); }
                    Task[] accepted; lock (actual.Originals) accepted = actual.Originals.Concat(raws).Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
                    foreach (var raw in accepted)
                    {
                        try { await raw.WaitAsync(TimeSpan.FromSeconds(15)); raw.GetAwaiter().GetResult(); }
                        catch (Exception cause)
                        {
                            var complete = raw.Exception ?? cause; roots.Add(complete);
                            if (sameLate is null || !expected.Contains(raw) || !raw.IsFaulted || raw.IsCanceled) errors.Add(complete);
                            else try { AssertProtectedCallbackGraph(complete, sameLate); }
                                catch (Exception mismatch) { errors.Add(complete); errors.Add(mismatch); }
                        }
                    }
                    if (sameLate is not null)
                        try
                        {
                            Assert.NotNull(close); Assert.True(close!.IsFaulted); Assert.Same(close, actual.CloseAsync());
                            Assert.True(actual.Writer.OriginalClose?.IsCompletedSuccessfully == true);
                            var cachedOriginClose = Assert.IsAssignableFrom<Task>(actual.Origins.OriginalClose);
                            Assert.True(cachedOriginClose.IsFaulted); Assert.False(cachedOriginClose.IsCanceled);
                            Assert.Same(cachedOriginClose, actual.Origins.CloseAndDrainOriginalAsync());
                            AssertProtectedCallbackGraph(cachedOriginClose.Exception!, sameLate);
                            var retainedDocument = Assert.IsType<Haven.Core.GenUiDocument>(sameDocument);
                            Assert.Same(retainedDocument, actual.Instances.TryGet(retainedDocument.Origin.InstanceId));
                            Assert.Null(actual.Home.OriginalClose); Assert.Null(rig.Store.OriginalClose);
                        }
                        catch (Exception cause) { errors.Add(cause); }
                }
                lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, graph!, roots, raws, errors]);
            }
            if (errors.Count != 0) throw new AggregateException("Actual origin callback and independent original cleanup causes remain retained.", errors);
            return true;
        }, Token));
    }
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Saved_actual_origin_callback_after_healthy_close_refuses_repeated_success_publication()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var rig = new Rig(originalAdditionalPolicy: new HomeCanonicalGeneratedUiInteractionActionPolicySource());
            ProtectedGeneratedGraph? graph = null;
            var roots = new List<object>(); var raws = new List<Task>(); var errors = new List<Exception>();
            Task? firstOriginClose = null; Action? saved = null;
            void Capture(Task actual) { raws.Add(actual); roots.Add(actual); graph!.Retain(actual); }
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                var binding = await rig.CreateAsync(new() { Name = "Fictional closed origin callback", Memory = new(false) });
                await AddProtectedGeneratedMessage(rig, binding);
                var actualGraph = graph = new(rig); roots.Add(actualGraph);
                object? exactSource = null;
                var view = WithProtectedGeneratedView(rig, actualGraph, async (window, surface, controller, host) =>
                {
                    await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                    await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                    var before = ProtectedOriginCohorts(actualGraph.Origins);
                    var selection = ProtectedCallbackSelection(host);
                    void SaveActualCallback(Action body) { saved ??= body; body(); }
                    var first = actualGraph.Origins.ObserveOriginalSnapshotWithinSourceAsync(selection, SaveActualCallback, actualGraph.Retain, Token);
                    Capture(first); var observed = await first; roots.Add(observed);
                    Assert.True(actualGraph.Origins.IsIssuedOriginalSnapshot(observed)); Assert.NotNull(saved);
                    exactSource = Assert.Single(ProtectedOriginCohorts(actualGraph.Origins), source =>
                        !before.Any(prior => ReferenceEquals(prior, source))); roots.Add(exactSource);
                });
                Capture(view); await view;
                var firstGraphClose = actualGraph.CloseAsync(); Capture(firstGraphClose); await firstGraphClose;
                Assert.True(firstGraphClose.IsCompletedSuccessfully);
                firstOriginClose = Assert.IsAssignableFrom<Task>(actualGraph.Origins.OriginalClose); Capture(firstOriginClose);
                await firstOriginClose; firstOriginClose.GetAwaiter().GetResult(); Assert.True(firstOriginClose.IsCompletedSuccessfully);
                Assert.Same(firstOriginClose, actualGraph.Origins.CloseAndDrainOriginalAsync());
                var late = Assert.Throws<InvalidOperationException>(() => saved!()); roots.Add(late);
                Assert.Equal("The original origin callback is inactive, repeated or foreign-thread.", late.Message);
                Assert.Null(late.InnerException);
                var ledger = Assert.IsType<CloudflareOriginalTaskLedger>(ProtectedCallbackField(exactSource!, "Errors"));
                Assert.Contains(ledger.OriginalErrors, cause => ReferenceEquals(cause, late));
                Task? incorrectlyRepublished = null;
                var refused = Record.Exception(() => { incorrectlyRepublished = actualGraph.Origins.CloseAndDrainOriginalAsync(); });
                Assert.NotNull(refused); roots.Add(refused!); Assert.Null(incorrectlyRepublished);
                AssertProtectedCallbackGraph(refused!, late);
                // Preserve the exact historical receipt; the new failed observation
                // cannot turn that completed original into another Task or an effect.
                Assert.Same(firstOriginClose, actualGraph.Origins.OriginalClose); Assert.True(firstOriginClose.IsCompletedSuccessfully);
                Assert.True(actualGraph.Writer.OriginalClose?.IsCompletedSuccessfully == true);
                Assert.True(actualGraph.Home.OriginalClose?.IsCompletedSuccessfully == true);
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                // No accepted raw is skipped by an assertion or late refusal.
                // Historical cached tasks are independently joined, and the exact
                // process/source/error graph remains held after the callback fault.
                var healthyGraphJoined = graph is null;
                if (graph is { } actual)
                {
                    Task? close = null;
                    try { close = actual.CloseAsync(); Capture(close); }
                    catch (Exception cause) { errors.Add(cause); }
                    Task[] accepted; lock (actual.Originals) accepted = actual.Originals.Concat(raws).Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
                    foreach (var raw in accepted)
                        try
                        {
                            await raw.WaitAsync(TimeSpan.FromSeconds(15)); raw.GetAwaiter().GetResult();
                            if (ReferenceEquals(raw, close)) healthyGraphJoined = true;
                        }
                        catch (Exception cause) { errors.Add(raw.Exception ?? cause); roots.Add(raw.Exception ?? cause); }
                }
                if (healthyGraphJoined && errors.Count == 0)
                {
                    Task? rigClose = null;
                    try { rigClose = rig.CloseAsync(); rig.Retain(rigClose); roots.Add(rigClose); await rigClose.WaitAsync(TimeSpan.FromSeconds(15)); }
                    catch (Exception cause) { errors.Add(rigClose?.Exception ?? cause); }
                }
                lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, graph!, roots, raws, errors]);
            }
            if (errors.Count != 0) throw new AggregateException("Actual closed origin callback and independent cached original cleanup remain retained.", errors);
            return true;
        }, Token));
    }
    private static object[] ProtectedOriginCohorts(OriginalAssistantGeneratedUiOriginOwner actual)
    {
        var gate = ProtectedCallbackField(actual, "_gate");
        lock (gate) return ((IEnumerable)ProtectedCallbackField(actual, "_sources")).Cast<object>().ToArray();
    }
}
#endif
