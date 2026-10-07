using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Application;
using NineToOne.Web.Services;
using NineToOne.Web.Spaces.Storage;

// SAME production transport/identity-read drivers with controlled exact Tasks and callbacks.
// No JS import is invoked; this is not real browser/authentication or platform acceptance.
[SupportedOSPlatform("browser")]
internal static class BrowserExpectedRefusalControls
{
    private static readonly Guid Account = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly AuthenticatedResourceActor Actor = new("cake-task:" + new string('a', 64) + ":" + Account,
        "cake-account-profile:" + new string('a', 64) + ":" + Account, Account, null, "controlled-current-epoch-not-authority");
    private static JsonElement Args() => JsonSerializer.SerializeToElement(new { taskId = Guid.NewGuid() });
    public static async Task Main()
    {
        await CallerCancelledBeforeAnySourceDoesNotPoisonClose();
        await LawfulNullActorRefusalKeepsOriginalAndAllowsClose();
        await GenuineCancelledActorTaskKeepsStateAndLawfulRelease();
        await FaultedOceAndSiblingStayTrueFaults();
        await CancelledIdentityWithReleaseFailureCannotBecomeExpected();
        await ChangedEpochAfterAcknowledgedWriteStaysFault();
        await LostAppendRemainsUnknownWithoutReplay();
        await RawCancelAndStopFaultsAllRemainOwned();
        await ActualActorSourceCannotAcquireItsOwnEncompassingJoin();
        Console.WriteLine("9 same-driver expected-refusal/cause controls passed; browser/auth remain unverified.");
    }
    private sealed class Actors(Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>> source) : IAuthenticatedResourceActorSource
    {
        public int Calls { get; private set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { Calls++; return source(token); }
    }
    private sealed class Platform
    {
        public int Invokes, Stops, Cancels;
        public Func<Task<string>> Original = () => Task.FromResult("{\"ok\":true,\"committed\":false,\"value\":null}");
        public Action Cancel = () => { };
        public Func<Task> Stop = () => Task.CompletedTask;
        public BrowserTaskExecutionTransport Create() => new("controlled-module-owner-not-identity",
            (_, _, _, _, _) => { Invokes++; return Original(); }, (_, _) => { Cancels++; Cancel(); },
            _ => { Stops++; return Stop(); });
    }
    private static async Task CallerCancelledBeforeAnySourceDoesNotPoisonClose()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel(); var platform = new Platform(); var owner = platform.Create();
        var actors = new Actors(_ => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor));
        var actual = owner.InvokeAuthenticatedControlledAsync(actors, "Get", Args(), stop.Token);
        Require(await Failure(actual) is OperationCanceledException && actual.IsCanceled, "Pre-source driver lost genuine Cancelled state.");
        var observation = owner.ExpectedPreStorageRefusals.Single();
        Require(ReferenceEquals(observation.OriginalTask, actual) && observation.OriginalActorSources.Count == 0 &&
            actors.Calls == 0 && platform.Invokes == 0, "Pre-source cancellation acquired identity/storage or substituted its Task.");
        var close = owner.CloseAndDrainAsync(); Require(ReferenceEquals(close, owner.CloseAndDrainAsync()), "Close changed original Task.");
        await close; Require(platform.Stops == 1, "Actual module close was not joined once.");
    }
    private static async Task LawfulNullActorRefusalKeepsOriginalAndAllowsClose()
    {
        var platform = new Platform(); var owner = platform.Create(); var releases = 0;
        var actors = new Actors(token => new(BrowserTaskIdentityReadOperation.RunAsync<AuthenticatedResourceActor?>(
            () => Task.FromResult("{\"ok\":false}"), () => { }, _ => null, () => { releases++; }, token)));
        var actual = owner.InvokeAuthenticatedControlledAsync(actors, "Get", Args(), CancellationToken.None);
        var error = await Failure(actual); var observation = owner.ExpectedPreStorageRefusals.Single();
        Require(error is BrowserTaskContextUnavailableException && actual.IsFaulted && releases == 1 && platform.Invokes == 0 &&
            observation.OriginalActorSources.Single().IsCompletedSuccessfully && ReferenceEquals(observation.OriginalTask, actual) &&
            ReferenceEquals(observation.OriginalCause, error), "Null actor lost lawful receipt release or original refusal custody.");
        await owner.CloseAndDrainAsync();
    }
    private static async Task GenuineCancelledActorTaskKeepsStateAndLawfulRelease()
    {
        var platform = new Platform(); var owner = platform.Create(); var releases = 0;
        var raw = Task.FromCanceled<string>(new CancellationToken(true));
        var actors = new Actors(token => new(BrowserTaskIdentityReadOperation.RunAsync<AuthenticatedResourceActor?>(
            () => raw, () => { }, _ => Actor, () => { releases++; }, token)));
        var actual = owner.InvokeAuthenticatedControlledAsync(actors, "Get", Args(), CancellationToken.None);
        Require(await Failure(actual) is OperationCanceledException && actual.IsCanceled && raw.IsCanceled && releases == 1 &&
            owner.ExpectedPreStorageRefusals.Single().OriginalActorSources.Single().IsCanceled && platform.Invokes == 0,
            "Genuine canceled actor Task became a sticky fault or skipped lawful release.");
        await owner.CloseAndDrainAsync();
    }
    private static async Task FaultedOceAndSiblingStayTrueFaults()
    {
        var oce = new OperationCanceledException("faulted OCE is not cancellation"); var sibling = new IOException("distinct raw sibling");
        var raw = new TaskCompletionSource<string>(); raw.SetException([oce, sibling]); var releases = 0;
        var actors = new Actors(token => new(BrowserTaskIdentityReadOperation.RunAsync<AuthenticatedResourceActor?>(
            () => raw.Task, () => { }, _ => Actor, () => { releases++; }, token)));
        var platform = new Platform(); var owner = platform.Create(); var actual = owner.InvokeAuthenticatedControlledAsync(actors, "Get", Args(), CancellationToken.None);
        _ = await Failure(actual); var failure = await Failure(owner.CloseAndDrainAsync());
        Require(actual.IsFaulted && raw.Task.IsFaulted && releases == 1 && owner.ExpectedPreStorageRefusals.Count == 0 &&
            Causes(failure).Contains(oce) && Causes(failure).Contains(sibling), "OCE-first true faults/siblings were waived.");
    }
    private static async Task CancelledIdentityWithReleaseFailureCannotBecomeExpected()
    {
        var releaseCause = new IOException("actual identity receipt release fault");
        var raw = Task.FromCanceled<string>(new CancellationToken(true));
        var actors = new Actors(token => new(BrowserTaskIdentityReadOperation.RunAsync<AuthenticatedResourceActor?>(
            () => raw, () => { }, _ => null, () => { throw releaseCause; }, token)));
        var platform = new Platform(); var owner = platform.Create(); var actual = owner.InvokeAuthenticatedControlledAsync(actors, "Get", Args(), CancellationToken.None);
        _ = await Failure(actual); var close = await Failure(owner.CloseAndDrainAsync());
        Require(actual.IsFaulted && owner.ExpectedPreStorageRefusals.Count == 0 && Causes(close).Contains(releaseCause),
            "A canceled source plus release failure was falsely classified clean.");
    }
    private static async Task ChangedEpochAfterAcknowledgedWriteStaysFault()
    {
        var platform = new Platform { Original = () => Task.FromResult("{\"ok\":true,\"committed\":true,\"value\":{\"original\":true}}") };
        var owner = platform.Create(); var count = 0;
        var actors = new Actors(_ => ValueTask.FromResult<AuthenticatedResourceActor?>(++count == 1 ? Actor : Actor with { AuthenticationRevision = "changed-epoch" }));
        var actual = owner.InvokeAuthenticatedControlledAsync(actors, "Upsert", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
        var error = await Failure(actual);
        Require(error is TaskExecutionTransportReplyFailureException retained && retained.OriginalReply.GetProperty("committed").GetBoolean() &&
            owner.ExpectedPreStorageRefusals.Count == 0 && platform.Invokes == 1, "Post-write actor change lost original ack or became clean refusal.");
        _ = await Failure(owner.CloseAndDrainAsync()); Require(platform.Invokes == 1, "Write replayed while closing.");
    }
    private static async Task LostAppendRemainsUnknownWithoutReplay()
    {
        var cause = new IOException("same lost raw append reply"); var platform = new Platform { Original = () => Task.FromException<string>(cause) };
        var owner = platform.Create(); var actors = new Actors(_ => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor));
        var actual = owner.InvokeAuthenticatedControlledAsync(actors, "ExecutionEvents.Append", JsonSerializer.SerializeToElement(new { rows = Array.Empty<object>() }), CancellationToken.None);
        var error = await Failure(actual); var close = await Failure(owner.CloseAndDrainAsync());
        Require(Causes(error).Any(member => member is TaskExecutionCommitOutcomeUnknownException) &&
            Causes(close).Contains(cause) && owner.ExpectedPreStorageRefusals.Count == 0 && platform.Invokes == 1,
            "Lost actual Append was not unknown, lost its cause, or replayed.");
    }
    private static async Task RawCancelAndStopFaultsAllRemainOwned()
    {
        var raw = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelCause = new IOException("actual cancel callback failed"); var stopCause = new IOException("actual module stop failed");
        using var caller = new CancellationTokenSource();
        var platform = new Platform { Original = () => { entered.TrySetResult(); return raw.Task; }, Cancel = () => { throw cancelCause; },
            Stop = () => Task.FromException(stopCause) };
        var owner = platform.Create(); var actors = new Actors(_ => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor));
        var actual = owner.InvokeAuthenticatedControlledAsync(actors, "Upsert", JsonSerializer.SerializeToElement(new { }), caller.Token);
        Task? closed = null; Exception? assertionCause = null, actualCause = null, closeCause = null;
        try { await entered.Task; caller.Cancel(); closed = owner.CloseAndDrainAsync(); Require(!closed.IsCompleted, "Close abandoned held actual raw."); }
        catch (Exception error) { assertionCause = error; }
        finally { raw.TrySetResult("{\"ok\":true,\"committed\":true,\"value\":null}"); }
        // A deliberately detected early-close defect must still join the SAME held raw,
        // driver and encompassing close. Preserve the assertion alongside every sibling.
        try { await actual; } catch (Exception error) { actualCause = actual.IsFaulted ? actual.Exception : error; }
        try { closed ??= owner.CloseAndDrainAsync(); await closed; }
        catch (Exception error) { closeCause = closed?.IsFaulted == true ? closed.Exception : error; }
        try
        {
            Require(actualCause is not null && actual.IsFaulted && closeCause is not null && closed?.IsFaulted == true &&
                Causes(closeCause).Contains(cancelCause) && Causes(closeCause).Contains(stopCause) &&
                owner.ExpectedPreStorageRefusals.Count == 0 && platform.Invokes == 1, "Cancel/stop siblings were lost or broadly waived.");
        }
        catch (Exception error) { assertionCause = assertionCause is null ? error : new AggregateException(assertionCause, error); }
        if (assertionCause is not null) throw new AggregateException("Held-source control failed after all original joins.",
            new[] { assertionCause, actualCause, closeCause }.OfType<Exception>());
    }
    private static async Task ActualActorSourceCannotAcquireItsOwnEncompassingJoin()
    {
        var platform = new Platform(); var owner = platform.Create(); var guardObserved = false, releases = 0;
        var actors = new Actors(token => new(BrowserTaskIdentityReadOperation.RunAsync<AuthenticatedResourceActor?>(() =>
        {
            try { _ = owner.CloseAndDrainAsync(); } catch (InvalidOperationException) { guardObserved = true; }
            Require(guardObserved, "Actual actor source acquired its encompassing close."); return Task.FromResult("null");
        }, () => { }, _ => null, () => { releases++; }, token)));
        _ = await Failure(owner.InvokeAuthenticatedControlledAsync(actors, "Get", Args(), CancellationToken.None));
        await owner.CloseAndDrainAsync(); Require(guardObserved && releases == 1 && platform.Invokes == 0, "Source guard/release original changed.");
    }
    private static IEnumerable<Exception> Causes(Exception value) => value is AggregateException group
        ? group.InnerExceptions.SelectMany(Causes) : new[] { value }.Concat(value.InnerException is { } inner ? Causes(inner) : []);
    private static async Task<Exception> Failure(Task actual)
    { try { await actual; } catch (Exception error) { return error; } throw new InvalidOperationException("Original unexpectedly succeeded."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
