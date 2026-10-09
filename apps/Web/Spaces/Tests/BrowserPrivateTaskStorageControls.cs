using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web;
using NineToOne.Web.Services;
using NineToOne.Web.Spaces.Storage;

// Actual source-linked private storage enrollment/revocation. These controls intentionally
// never acquire an actor/API/JSImport/IDB source; they test the no-effect ownership boundary.
// They cannot replace genuine browser storage, current signed identity or Task/Run acceptance.
[SupportedOSPlatform("browser")]
internal static class BrowserPrivateTaskStorageControls
{
    public static async Task Main()
    {
        await SameRegistryEnrollmentIsUniqueAndRetiredOwnerCannotBecomeCurrent();
        await UninitializedActualOwnerClosesWithoutAcquiringAPrivateSource();
        await InitialAdmissionRefusalCannotCreateAReplacementStorageOwner();
        await FullCanonicalReadByTaskAndContextUsesActualCoordinatorWithoutPublication();
        await WholeManagedReadTailBlocksSameCloseAndRetainsRetiredOriginal();
        await PhysicalAndLogicalSourceCannotJoinOwnClose();
        await GenuineCancelledMappedDriverAndPreCallerRemainExpected();
        await FaultedOceSiblingsRemainFaultsAfterMapping();
        await ExactNullActorMappedRefusalVersusUnassociatedLookalike();
        await CanonicalDecodeFaultAfterTransportSuccessRemainsOwned();
        await RevokedReadBeforeAnyAcquisitionCannotCreateReplacement();
        await OriginalHubPartialFailureBeforeTransportCancelPreservesSiblings();
        await NegativeHeldControlAssertionStillJoinsOriginals();
        await ActualExposedCompletionIsFencedWhilePublicationSourceHeld();
        Console.WriteLine("3 original no-source +11 stored-read owning controls passed; browser/auth/reopen remain unverified.");
    }

    private static async Task SameRegistryEnrollmentIsUniqueAndRetiredOwnerCannotBecomeCurrent()
    {
        var registry = new BrowserSurfaceRegistry();
        BrowserPrivateTaskStorageEnrollment.Register(registry);
        var original = BrowserPrivateTaskStorageEnrollment.GetCurrent(registry);
        Require(registry.AvailableRoutes.Count == 0, "A storage owner fabricated a page route.");
        Require(Thrown(() => BrowserPrivateTaskStorageEnrollment.Register(registry)) is InvalidOperationException,
            "A second same-registry live owner replaced the first.");
        Require(ReferenceEquals(original, BrowserPrivateTaskStorageEnrollment.GetCurrent(registry)), "The actual live owner was replaced.");
        var reset = registry.BeginPrivateContextReset();
        Require(Thrown(() => BrowserPrivateTaskStorageEnrollment.GetCurrent(registry)) is ObjectDisposedException,
            "Weak-table discovery returned the synchronously revoked old owner.");
        Require(Thrown(() => _ = original.Tasks) is ObjectDisposedException &&
            Thrown(() => _ = original.Conversations) is ObjectDisposedException &&
            Thrown(() => _ = original.Contexts) is ObjectDisposedException,
            "A revoked original attempted actor/API/JS source acquisition or published a context.");
        await reset.DrainAsync();
        BrowserPrivateTaskStorageEnrollment.Register(registry);
        var replacement = BrowserPrivateTaskStorageEnrollment.GetCurrent(registry);
        Require(!ReferenceEquals(original, replacement), "Replacement reused a retired storage owner.");
        Require(Thrown(() => _ = original.Contexts) is ObjectDisposedException,
            "The stale native owner adopted the replacement context.");
        await registry.BeginPrivateContextReset().DrainAsync();
    }

    private static async Task UninitializedActualOwnerClosesWithoutAcquiringAPrivateSource()
    {
        var original = new BrowserPrivateTaskStorage();
        original.DemandPrivateContextCurrent();
        var actual = original.DisposeAsync().AsTask();
        Require(ReferenceEquals(actual, original.DisposeAsync().AsTask()), "The actual close Task was replaced.");
        await actual;
        Require(Thrown(original.DemandPrivateContextCurrent) is ObjectDisposedException &&
            Thrown(() => _ = original.Tasks) is ObjectDisposedException,
            "Closed lazy ownership acquired a platform module before refusing the stale caller.");
        await original.DisposeAsync();
    }

    private static async Task InitialAdmissionRefusalCannotCreateAReplacementStorageOwner()
    {
        var registry = new BrowserSurfaceRegistry();
        var pending = new HeldLifetime();
        Require(registry.RegisterPrivateLifetime(pending).Succeeded, "Controlled held owner enrollment failed.");
        var reset = registry.BeginPrivateContextReset();
        var actual = reset.DrainAsync();
        Exception? controlFailure = null;
        try
        {
            await pending.Entered.Task;
            Require(Thrown(() => BrowserPrivateTaskStorageEnrollment.Register(registry)) is InvalidOperationException,
                "Pending original cleanup admitted a new private storage owner.");
            Require(Thrown(() => BrowserPrivateTaskStorageEnrollment.GetCurrent(registry)) is InvalidOperationException,
                "Refused initial admission still published private storage discovery.");
        }
        catch (Exception error) { controlFailure = error; }
        finally { pending.Release.TrySetResult(); }
        await actual;
        BrowserPrivateTaskStorageEnrollment.Register(registry);
        BrowserPrivateTaskStorageEnrollment.GetCurrent(registry).DemandPrivateContextCurrent();
        await registry.BeginPrivateContextReset().DrainAsync();
        if (controlFailure is not null) throw controlFailure;
    }

    // Controlled source observations below exercise the SAME actual coordinator, canonical
    // decoder, transport driver and new read/retirement owners. They verify no signed actor,
    // browser persistence or accepted admission/Run/provider/checkpoint authority.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Account = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly AuthenticatedResourceActor Actor = new("cake-task:" + new string('a', 64) + ":" + Account,
        "cake-account-profile:" + new string('a', 64) + ":" + Account, Account, null, "stored-observation-not-authority");
    private static TaskExecutionSnapshot Snapshot()
    {
        var task = Guid.NewGuid(); var context = Guid.NewGuid(); var execution = Guid.NewGuid(); var action = Guid.NewGuid();
        var attempt = Guid.NewGuid(); var time = DateTimeOffset.Parse("2026-10-07T08:00:00.1234567+00:00");
        return new(task, context, execution, "same original task", TaskExecutionLifecycle.Suspended,
            TaskExecutionDurability.RecoverableCheckpoint, 7,
            [new TaskPlanNode(action, null, "stored accepted work", TaskPlanNodeState.Completed,
                TaskActionInterruptionPolicy.AtomicCommit, 7, RequiredPermissionScopes: ["observed.scope"])
            { Acceptance = new(attempt, "stored-acceptance-observation", time),
              OriginalToolIntent = new("observed-runtime", "observed-tool", null, "observed-call-digest"),
              OriginalOperationOutcome = new(true, false, 17, false, time) }],
            [new(Guid.NewGuid(), task, execution, 9, "original steer", TaskFollowUpInference.Explicit,
                SteerInstructionState.Applied, [action], time)],
            [new(Guid.NewGuid(), task, execution, "original queued work", 11, 0, QueuedFollowUpState.Blocked, null, time, time)],
            ["observed.scope"], action, time, time)
        { PersistenceRevision = 9_007_199_254_740_993, CheckpointId = Guid.NewGuid(),
          OwnerBinding = new(task, context, execution, Actor.ActorId, Actor.ProfileId, Account, null,
              Actor.AuthenticationRevision, "stored-owner-observation"),
          Attempts = [new(attempt, new("observed-route", 3, "observed-provider", "observed-model", "observed-artifact", false, []),
              TaskRunAttemptState.Suspended, "stored-admission-observation", time, time)] };
    }
    private static string Reply(TaskExecutionSnapshot snapshot, bool corrupt = false)
    {
        var json = JsonSerializer.Serialize(snapshot, Json);
        var row = new { taskId = snapshot.TaskId, contextId = snapshot.ContextId, executionId = snapshot.ExecutionId,
            state = (int)snapshot.State, revision = snapshot.PersistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            createdAt = snapshot.CreatedAt.ToString("O"), updatedAt = snapshot.UpdatedAt.ToString("O"), json,
            sha256 = corrupt ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))) };
        return JsonSerializer.Serialize(new { ok = true, committed = false, value = row });
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal int Calls;
        internal Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>> Source = _ => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor);
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { Calls++; return Source(token); }
    }
    private sealed class EventSource : IExecutionEventRepository
    {
        internal int Calls;
        internal Func<Task> Append = () => Task.FromException(new InvalidOperationException("Read-only source unexpectedly published an event."));
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken token) { Calls++; return Append(); }
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid id, CancellationToken token) =>
            throw new InvalidOperationException("This controlled event source is not a history implementation.");
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken token) =>
            throw new InvalidOperationException("This controlled event source is not a history implementation.");
    }
    private sealed class ReadRepository(Func<Guid, bool, CancellationToken, Task<TaskExecutionSnapshot?>> read) : ITaskExecutionRepository
    {
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => read(id, false, token);
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => read(id, true, token);
        public Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token) => throw new InvalidOperationException("Stored-read control must never mutate.");
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => throw new InvalidOperationException("Only the two actual coordinator reads are in scope.");
    }
    private sealed class Rig
    {
        internal readonly TaskExecutionSnapshot Stored = Snapshot();
        internal readonly Actors Actors = new();
        internal readonly EventSource Events = new();
        internal readonly ExecutionEventHub Hub;
        internal readonly BrowserTaskExecutionTransport Transport;
        internal readonly BrowserStoredTaskReadOwner Reads;
        internal readonly TaskCompletionSource Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Invokes, Stops;
        internal Func<Task<string>> Source;
        internal Func<Task> Stop = () => Task.CompletedTask;
        private readonly object _gate = new();
        private Task? _close;
        internal Rig(Func<ITaskExecutionRepository, ITaskExecutionRepository>? decorate = null, Func<Task>? publicationSource = null)
        {
            Source = () => Task.FromResult(Reply(Stored));
            Transport = new("controlled-module-owner-not-identity", (_, _, _, _, _) => { Invokes++; return Source(); }, (_, _) => { },
                _ => { Stops++; Stopped.TrySetResult(); return Stop(); });
            Hub = new(Events);
            var canonical = new IndexedDbTaskExecutionRepository(new ReadWire(this));
            var repository = decorate?.Invoke(canonical) ?? canonical;
            Reads = new(new TaskExecutionCoordinator(repository, Hub, timeProvider: null), Transport, _gate, publicationSource);
        }
        private sealed class ReadWire(Rig owner) : ITaskExecutionBrowserTransport
        {
            public async Task<JsonElement> InvokeAsync(string action, JsonElement args, CancellationToken token)
            {
                var actual = owner.Transport.InvokeAuthenticatedControlledAsync(owner.Actors, action, args, token);
                owner.Reads.RetainAuthenticatedDriver(actual); // SAME capture path as actual sealed ProfileTransport.
                return (await actual).Reply;
            }
        }
        internal Task Close(Exception? partialFailure = null)
        {
            var start = new TaskCompletionSource();
            Task actual;
            lock (_gate)
            {
                Reads.DemandExternalOriginalRetirementJoin(); Transport.DemandExternalOriginalRetirementJoin();
                if (_close is not null) return _close;
                Reads.RevokePrivateContext(); Hub.RequestClose(); Transport.RevokePrivateContext();
                actual = BrowserPrivateTaskStorage.CloseOriginalAsync(start.Task, Reads, Hub, Transport, partialFailure);
                _close = actual;
            }
            start.SetResult(); return actual;
        }
    }
    private static async Task Execute(Rig rig, Func<Task> control, bool expectCloseFault = false)
    {
        Exception? primary = null, cleanup = null;
        try { await control(); } catch (Exception error) { primary = error; }
        var observed = new List<Exception>();
        async Task Observe(Task? actual)
        {
            if (actual is null) return;
            try { await actual; }
            catch (Exception error) { observed.Add(actual.Exception ?? error); }
        }
        Task? close = null;
        try { close = rig.Close(); }
        catch (Exception error) { cleanup = close?.Exception ?? error; }
        // Even an intentionally detected SUT early-close defect cannot make the control
        // abandon its actual hub, transport, outer read, decoder or associated driver.
        Task? hub = null, transport = null, reads = null;
        try { hub = rig.Hub.DisposeAsync().AsTask(); } catch (Exception error) { observed.Add(error); }
        await Observe(hub);
        try { transport = rig.Transport.CloseAndDrainAsync(); } catch (Exception error) { observed.Add(error); }
        foreach (var original in rig.Reads.OriginalReads)
        {
            await Observe(original.Outer); await Observe(original.ManagedDriver); await Observe(original.Publisher);
            await Observe(original.Repository); await Observe(original.PublicationSource);
            foreach (var driver in original.AuthenticatedDrivers) await Observe(driver);
        }
        try { reads = rig.Reads.CloseAndDrainAsync(); } catch (Exception error) { observed.Add(error); }
        await Observe(reads); await Observe(transport);
        if (close is not null)
            try { await close; } catch (Exception error) { cleanup = close.Exception ?? error; }
        if (primary is null && expectCloseFault != (cleanup is not null))
            primary = new InvalidOperationException("Unexpected cleanup result after all SAME original joins.");
        if (primary is not null) throw new AggregateException("Stored-read assertion failed after independent original cleanup joins.",
            new[] { primary, cleanup }.OfType<Exception>().Concat(observed));
    }
    private static async Task FullCanonicalReadByTaskAndContextUsesActualCoordinatorWithoutPublication()
    {
        var rig = new Rig();
        await Execute(rig, async () => {
            var task = await rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None);
            var context = await rig.Reads.GetByContextAsync(rig.Stored.ContextId, CancellationToken.None);
            Require(JsonSerializer.Serialize(task, Json) == JsonSerializer.Serialize(rig.Stored, Json) &&
                JsonSerializer.Serialize(context, Json) == JsonSerializer.Serialize(rig.Stored, Json) && rig.Invokes == 2 &&
                task!.Plan.Single().Acceptance!.AttemptId == task.Attempts.Single().Id && task.CheckpointId == rig.Stored.CheckpointId &&
                task.PersistenceRevision == rig.Stored.PersistenceRevision && rig.Events.Calls == 0,
                "Actual coordinator read dropped canonical Task/Run/accepted/checkpoint/full state or published new work.");
        });
    }
    private static async Task WholeManagedReadTailBlocksSameCloseAndRetainsRetiredOriginal()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = new Rig(canonical => new ReadRepository(async (id, context, token) => {
            var value = context ? await canonical.GetByContextAsync(id, token) : await canonical.GetAsync(id, token);
            entered.TrySetResult(); await release.Task; return value;
        }));
        await Execute(rig, async () => {
            var actual = rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None); Task? close = null;
            try {
                await entered.Task; close = rig.Close(); await rig.Stopped.Task;
                Require(!close.IsCompleted && ReferenceEquals(close, rig.Close()), "Close abandoned or substituted full managed validation tail.");
            } finally { release.TrySetResult(); }
            var error = await Failure(actual); var closeError = await Failure(close!);
            var original = rig.Reads.OriginalReads.Single();
            Require(Causes(error).Any(cause => cause is ObjectDisposedException) && original.Repository is Task<TaskExecutionSnapshot?> raw &&
                raw.IsCompletedSuccessfully && raw.Result!.TaskId == rig.Stored.TaskId && raw.Result.ExecutionId == rig.Stored.ExecutionId &&
                ReferenceEquals(original.Outer, actual) && original.AuthenticatedDrivers.Single().IsCompletedSuccessfully &&
                Causes(closeError).Any(cause => cause is ObjectDisposedException), "Late original result escaped currentness fencing or private custody.");
        }, expectCloseFault: true);
    }
    private static async Task PhysicalAndLogicalSourceCannotJoinOwnClose()
    {
        Rig? rig = null; var physical = false, logical = false;
        rig = new Rig(canonical => new ReadRepository(async (id, context, token) => {
            var value = context ? await canonical.GetByContextAsync(id, token) : await canonical.GetAsync(id, token);
            await Task.Yield(); Exception? refusal = null;
            try { _ = rig!.Close(); } catch (Exception error) { refusal = error; }
            logical = refusal is InvalidOperationException; Require(logical, "Logical original acquired encompassing close."); return value;
        }));
        rig.Source = () => {
            Exception? refusal = null; try { _ = rig.Close(); } catch (Exception error) { refusal = error; }
            physical = refusal is InvalidOperationException; Require(physical, "Physical original acquired encompassing close.");
            return Task.FromResult(Reply(rig.Stored));
        };
        await Execute(rig, async () => {
            var value = await rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None);
            Require(physical && logical && value!.TaskId == rig.Stored.TaskId && rig.Stops == 0, "Physical/logical source own-join mutated retirement or bypassed actual read guard.");
        });
    }
    private static async Task GenuineCancelledMappedDriverAndPreCallerRemainExpected()
    {
        var before = new Rig(); using var caller = new CancellationTokenSource(); caller.Cancel();
        await Execute(before, async () => {
            var actual = before.Reads.GetAsync(before.Stored.TaskId, caller.Token);
            Require(await Failure(actual) is OperationCanceledException && actual.IsCanceled && before.Actors.Calls == 0 && before.Invokes == 0 &&
                before.Reads.OriginalReads.Single().Repository is null, "Pre-source caller cancellation acquired a producer or lost genuine state.");
        });
        var mapped = new Rig(); var source = Task.FromCanceled<AuthenticatedResourceActor?>(new CancellationToken(true));
        mapped.Actors.Source = _ => new(source);
        await Execute(mapped, async () => {
            var actual = mapped.Reads.GetAsync(mapped.Stored.TaskId, CancellationToken.None);
            _ = await Failure(actual); var observation = mapped.Transport.ExpectedPreStorageRefusals.Single();
            Require(actual.IsCanceled && source.IsCanceled && ReferenceEquals(observation.OriginalTask,
                mapped.Reads.OriginalReads.Single().AuthenticatedDrivers.Single()) && mapped.Invokes == 0,
                "Genuine canceled driver lost exact association or became a sticky synthetic fault.");
        });
    }
    private static async Task FaultedOceSiblingsRemainFaultsAfterMapping()
    {
        foreach (var withSibling in new[] { false, true })
        {
            var rig = new Rig(); var oce = new OperationCanceledException("faulted OCE remains a fault"); var sibling = new IOException("actual actor sibling");
            var source = new TaskCompletionSource<AuthenticatedResourceActor?>();
            source.SetException(withSibling ? new Exception[] { oce, sibling } : [oce]); rig.Actors.Source = _ => new(source.Task);
            await Execute(rig, async () => {
                var actual = rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None); _ = await Failure(actual);
                var close = await Failure(rig.Close());
                Require(actual.IsFaulted && source.Task.IsFaulted && rig.Transport.ExpectedPreStorageRefusals.Count == 0 &&
                    Causes(close).Contains(oce) && (!withSibling || Causes(close).Contains(sibling)), "Faulted OCE/siblings were converted to expected cancellation or dropped.");
            }, expectCloseFault: true);
        }
        var releaseRig = new Rig(); var releaseCause = new IOException("same canceled identity receipt release failed");
        var raw = Task.FromCanceled<string>(new CancellationToken(true));
        releaseRig.Actors.Source = token => new(BrowserTaskIdentityReadOperation.RunAsync<AuthenticatedResourceActor?>(
            () => raw, () => { }, _ => Actor, () => { throw releaseCause; }, token));
        await Execute(releaseRig, async () => {
            var actual = releaseRig.Reads.GetAsync(releaseRig.Stored.TaskId, CancellationToken.None); _ = await Failure(actual);
            Require(actual.IsFaulted && raw.IsCanceled && releaseRig.Transport.ExpectedPreStorageRefusals.Count == 0 &&
                Causes(await Failure(releaseRig.Close())).Contains(releaseCause),
                "Same canceled actor plus actual release fault acquired an expected read disposition.");
        }, expectCloseFault: true);
    }
    private static async Task ExactNullActorMappedRefusalVersusUnassociatedLookalike()
    {
        var rig = new Rig(); rig.Actors.Source = _ => ValueTask.FromResult<AuthenticatedResourceActor?>(null);
        await Execute(rig, async () => {
            var actual = rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None); var error = await Failure(actual);
            var expected = rig.Transport.ExpectedPreStorageRefusals.Single();
            Require(actual.IsFaulted && Causes(error).Contains(expected.OriginalCause) &&
                ReferenceEquals(expected.OriginalTask, rig.Reads.OriginalReads.Single().AuthenticatedDrivers.Single()) && rig.Invokes == 0,
                "Same null verified actor lost original cause/driver association or acquired storage.");
        });
        var cause = new BrowserTaskContextUnavailableException("No current verified browser Task account/profile is attached.");
        var lookalike = new Rig(_ => new ReadRepository((_, _, _) => Task.FromException<TaskExecutionSnapshot?>(cause)));
        await Execute(lookalike, async () => {
            var actual = lookalike.Reads.GetAsync(lookalike.Stored.TaskId, CancellationToken.None); _ = await Failure(actual);
            Require(Causes(await Failure(lookalike.Close())).Contains(cause) && lookalike.Reads.OriginalReads.Single().AuthenticatedDrivers.Count == 0,
                "Unassociated similar-looking managed fault received an actor-refusal waiver.");
        }, expectCloseFault: true);
    }
    private static async Task CanonicalDecodeFaultAfterTransportSuccessRemainsOwned()
    {
        var rig = new Rig(); rig.Source = () => Task.FromResult(Reply(rig.Stored, corrupt: true));
        await Execute(rig, async () => {
            var actual = rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None); var error = await Failure(actual);
            Require(Causes(error).Any(cause => cause is InvalidDataException) &&
                rig.Reads.OriginalReads.Single().AuthenticatedDrivers.Single().IsCompletedSuccessfully,
                "Actual canonical hash/decode failure was tested only on a failed transport prefix.");
            Require(Causes(await Failure(rig.Close())).Any(cause => cause is InvalidDataException) && rig.Invokes == 1,
                "Complete managed decode fault escaped owning close or triggered replay.");
        }, expectCloseFault: true);
    }
    private static async Task RevokedReadBeforeAnyAcquisitionCannotCreateReplacement()
    {
        var rig = new Rig(); rig.Reads.RevokePrivateContext();
        await Execute(rig, () => {
            Require(Thrown(() => _ = rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None)) is ObjectDisposedException &&
                Thrown(() => _ = rig.Reads.GetByContextAsync(rig.Stored.ContextId, CancellationToken.None)) is ObjectDisposedException &&
                rig.Actors.Calls == 0 && rig.Invokes == 0 && rig.Reads.OriginalReads.Count == 0,
                "Revoked read facade acquired storage or published a replacement Task."); return Task.CompletedTask;
        });
    }
    private static async Task OriginalHubPartialFailureBeforeTransportCancelPreservesSiblings()
    {
        var rig = new Rig(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var append = new IOException("same genuine collector append fault"); var sibling = new IOException("same collector sibling");
        var partial = new IOException("original partial composition failure"); var stop = new IOException("actual module stop fault");
        rig.Events.Append = () => { entered.TrySetResult(); return held.Task; };
        rig.Stop = () => { Require(rig.Hub.DisposeAsync().AsTask().IsCompleted, "Transport stop preceded genuine collector terminal."); return Task.FromException(stop); };
        await Execute(rig, async () => {
            Require(rig.Hub.TryPublish(new(Guid.NewGuid(), rig.Stored.ExecutionId, Guid.NewGuid(), null, ExecutionOrigin.Mcp,
                ExecutionActionType.UserPrompt, ExecutionActionStatus.Completed, "test-only unexpected original", null, null, null,
                DateTimeOffset.UtcNow)), "Genuine test hub refused original unexpectedly.");
            Task? close = null;
            try { await entered.Task; close = rig.Close(partial); Require(rig.Stops == 0 && !close.IsCompleted, "Partial failure detached acquired genuine collector or started transport early."); }
            finally { held.TrySetException([append, sibling]); }
            var error = await Failure(close!);
            Require(Causes(error).Contains(append) && Causes(error).Contains(sibling) && Causes(error).Contains(partial) &&
                Causes(error).Contains(stop) && rig.Stops == 1 && rig.Hub.OriginalPersistenceFailures.Single().OriginalAppendOrEnqueue == held.Task,
                "Partial/hub/transport sibling custody lost original append or skipped a cleanup.");
        }, expectCloseFault: true);
    }
    private static async Task NegativeHeldControlAssertionStillJoinsOriginals()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = new Rig(canonical => new ReadRepository(async (id, context, token) => {
            var value = context ? await canonical.GetByContextAsync(id, token) : await canonical.GetAsync(id, token);
            entered.TrySetResult(); await held.Task; return value;
        }));
        var assertion = new IOException("deliberate held-read assertion counterexample");
        var error = await Failure(Execute(rig, async () => {
            _ = rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None);
            try { await entered.Task; _ = rig.Close(); throw assertion; }
            finally { held.TrySetResult(); }
        }, expectCloseFault: true));
        var original = rig.Reads.OriginalReads.Single();
        Require(Causes(error).Contains(assertion) && original.Outer.IsCompleted && original.Repository!.IsCompleted &&
            original.AuthenticatedDrivers.All(task => task.IsCompleted) && rig.Close().IsCompleted && rig.Stops == 1,
            "A negative held-read assertion skipped raw/managed/whole close joins or replaced its original cause.");
    }
    private static async Task ActualExposedCompletionIsFencedWhilePublicationSourceHeld()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rig = new Rig(publicationSource: () => { entered.TrySetResult(); return release.Task; });
        await Execute(rig, async () => {
            var exposed = rig.Reads.GetAsync(rig.Stored.TaskId, CancellationToken.None); Task? close = null;
            try
            {
                await entered.Task;
                var held = rig.Reads.OriginalReads.Single();
                Require(ReferenceEquals(held.Outer, exposed) && !exposed.IsCompleted && held.ManagedDriver.IsCompletedSuccessfully &&
                    held.Repository!.IsCompletedSuccessfully && !held.Publisher.IsCompleted && ReferenceEquals(held.PublicationSource, release.Task),
                    "Publication control did not hold SAME exposed Task after actual decode/driver cleanup.");
                close = rig.Close();
                Require(!exposed.IsCompleted && !close.IsCompleted && ReferenceEquals(close, rig.Close()),
                    "Revocation falsely settled or abandoned the actual held publication Task.");
            }
            finally { release.TrySetResult(); }
            var error = await Failure(exposed); var closeError = await Failure(close!);
            var original = rig.Reads.OriginalReads.Single();
            var decoded = (Task<TaskExecutionSnapshot?>)original.ManagedDriver;
            Require(exposed.IsFaulted && !exposed.IsCompletedSuccessfully && original.Publisher.IsFaulted &&
                original.PublicationSource!.IsCompletedSuccessfully && decoded.IsCompletedSuccessfully &&
                JsonSerializer.Serialize(decoded.Result, Json) == JsonSerializer.Serialize(rig.Stored, Json) &&
                Causes(error).Any(cause => cause is ObjectDisposedException) && Causes(closeError).Any(cause => cause is ObjectDisposedException),
                "Actual exposed completion escaped SAME-gate revocation or discarded the full decoded original.");
        }, expectCloseFault: true);

        Rig? faultRig = null; var physicalRefusal = false, logicalRefusal = false;
        var oce = new OperationCanceledException("faulted publication source OCE"); var sibling = new IOException("same publication source sibling");
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task LogicalPublication()
        {
            Exception? assertion = null;
            try
            {
                await Task.Yield(); Exception? refusal = null;
                try { _ = faultRig!.Close(); } catch (Exception error) { refusal = error; }
                logicalRefusal = refusal is InvalidOperationException;
                Require(logicalRefusal, "Logical publisher acquired its own encompassing close.");
            }
            catch (Exception error) { assertion = error; }
            Exception? originalFailure = null;
            try { await source.Task; } catch (Exception error) { originalFailure = source.Task.Exception ?? error; }
            if (assertion is not null) throw new AggregateException("Publisher assertion retained after SAME raw source join.",
                new[] { assertion, originalFailure }.OfType<Exception>());
            if (originalFailure is not null) throw originalFailure;
        }
        Task? sameLogicalSource = null;
        faultRig = new Rig(publicationSource: () => {
            Exception? refusal = null; try { _ = faultRig!.Close(); } catch (Exception error) { refusal = error; }
            physicalRefusal = refusal is InvalidOperationException;
            Require(physicalRefusal, "Physical publisher acquired its own encompassing close.");
            return sameLogicalSource = LogicalPublication();
        });
        await Execute(faultRig, async () => {
            var exposed = faultRig.Reads.GetAsync(faultRig.Stored.TaskId, CancellationToken.None);
            source.TrySetException([oce, sibling]);
            _ = await Failure(source.Task); // Independently observe raw siblings even if physical assertion prevents its wrapper.
            var error = await Failure(exposed); var closeError = await Failure(faultRig.Close());
            var original = faultRig.Reads.OriginalReads.Single();
            Require(physicalRefusal && logicalRefusal && exposed.IsFaulted && original.ManagedDriver.IsCompletedSuccessfully &&
                original.Publisher.IsFaulted && ReferenceEquals(original.PublicationSource, sameLogicalSource) &&
                original.PublicationSource!.IsFaulted && faultRig.Transport.ExpectedPreStorageRefusals.Count == 0 &&
                Causes(error).Contains(oce) && Causes(error).Contains(sibling) && Causes(closeError).Contains(oce) && Causes(closeError).Contains(sibling),
                "Publisher own-join changed retirement or faulted OCE/siblings became expected cancellation or unjoined failure.");
        }, expectCloseFault: true);

        var empty = new AggregateException("same empty synchronous publication fault");
        var emptyRig = new Rig(publicationSource: () => throw empty);
        await Execute(emptyRig, async () => {
            var exposed = emptyRig.Reads.GetAsync(emptyRig.Stored.TaskId, CancellationToken.None); _ = await Failure(exposed);
            Require(exposed.IsFaulted && ReferenceEquals(exposed.Exception!.InnerExceptions.Single(), empty) &&
                emptyRig.Reads.OriginalReads.Single().Publisher.IsFaulted,
                "An empty original fault group prevented completion or became a synthetic clean publication.");
        }, expectCloseFault: true);
        var cancelledSource = Task.FromCanceled(new CancellationToken(true));
        var cancelledRig = new Rig(publicationSource: () => cancelledSource);
        await Execute(cancelledRig, async () => {
            var exposed = cancelledRig.Reads.GetAsync(cancelledRig.Stored.TaskId, CancellationToken.None); _ = await Failure(exposed);
            var original = cancelledRig.Reads.OriginalReads.Single();
            Require(exposed.IsFaulted && original.ManagedDriver.IsCompletedSuccessfully && original.Publisher.IsFaulted &&
                ReferenceEquals(original.PublicationSource, cancelledSource) && cancelledSource.IsCanceled &&
                cancelledRig.Transport.ExpectedPreStorageRefusals.Count == 0,
                "Post-storage publication cancellation received a genuine prestorage expected disposition.");
        }, expectCloseFault: true);
    }
    private static IEnumerable<Exception> Causes(Exception value) => value is AggregateException group
        ? group.InnerExceptions.SelectMany(Causes) : new[] { value }.Concat(value.InnerException is { } inner ? Causes(inner) : []);
    private static async Task<Exception> Failure(Task actual)
    { try { await actual; } catch (Exception error) { return error; } throw new InvalidOperationException("The actual original unexpectedly succeeded."); }

    private sealed class HeldLifetime : IBrowserPrivateContextParticipant
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _actual;
        private bool _revoked;
        public void RevokePrivateContext() => _revoked = true;
        public ValueTask DisposeAsync() => new(_actual ??= Drain());
        private async Task Drain()
        {
            Entered.TrySetResult(); // Exact entry settles even if the actual fence assertion fails.
            Require(_revoked, "Held original was not revoked before its real drain.");
            await Release.Task;
        }
    }
    private static Exception Thrown(Action action)
    { try { action(); } catch (Exception error) { return error; } throw new InvalidOperationException("The original unexpectedly succeeded."); }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
