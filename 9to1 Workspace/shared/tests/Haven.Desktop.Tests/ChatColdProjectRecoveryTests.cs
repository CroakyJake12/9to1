using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

// The maintained personal-store fixture supplies real journal/key/kernel/Task actor
// originals. Its schema1 input cannot manufacture a schema2 project receipt. A genuine
// Home resource producer and invoked/model boundary positive remain separate acceptance.
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public Task Cold_genuine_schema1_claim_and_context_cannot_issue_project_material() =>
        WithColdProjectControlOriginals(async (fixture, originals) =>
        {
            var basis = await AcquireColdProjectControlBasis(fixture, originals);
            var callbacks = 0; var retained = new List<Task>();
            var actual = originals.Own(fixture.Journal.GetOriginalProjectMaterialWithinSourceAsync(
                basis.Claim, basis.Context, basis.Before, callback => { callbacks++; callback(); },
                task => { lock (retained) retained.Add(task); }, TestContext.Current.CancellationToken));
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            var refusal = Assert.Single(Leaves(failure).Where(value => value is InvalidOperationException
                && value.Message == "This project capsule has no exact source-captured, bounded resource material."));
            originals.Expect(refusal);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled); Assert.True(callbacks > 0);
            lock (retained) Assert.NotEmpty(retained);
            Assert.Equal(1, basis.Entry.Capsule.SchemaVersion);
            Assert.Null(basis.Entry.Capsule.OriginalProjectIdentity);
            Assert.Equal(JsonSerializer.Serialize(basis.Before), JsonSerializer.Serialize(await originals.Own(
                fixture.Rows.GetAsync(basis.Before.TaskId, TestContext.Current.CancellationToken))));
            Assert.Equal(1L, await originals.Own(fixture.ScalarAsync(
                "SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken)));
            Assert.Equal(0, fixture.Client.Dispatches);
        });

    [Fact]
    public Task Cold_copied_claim_is_denied_before_project_scope_or_resource_callbacks() =>
        WithColdProjectControlOriginals(async (fixture, originals) =>
        {
            var basis = await AcquireColdProjectControlBasis(fixture, originals);
            var copied = new PublicColdClaim(basis.Entry, basis.Before);
            var callbacks = 0; var retained = new List<Task>();
            var actual = originals.Own(fixture.Journal.GetOriginalProjectMaterialWithinSourceAsync(
                copied, basis.Context, basis.Before, callback => { callbacks++; callback(); },
                task => { lock (retained) retained.Add(task); }, TestContext.Current.CancellationToken));
            var failure = await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => actual);
            originals.Expect(failure);
            Assert.True(actual.IsFaulted); Assert.Equal(0, callbacks); Assert.Empty(retained);
            Assert.NotSame(basis.Claim, copied);
            Assert.Same(basis.Claim, basis.Context.OriginalClaim);
            Assert.Equal(basis.Before.TaskId, basis.Entry.Capsule.AcknowledgedTask.TaskId);
            Assert.Equal(basis.Before.ExecutionId, basis.Entry.Capsule.AcknowledgedTask.ExecutionId);
            Assert.Equal(JsonSerializer.Serialize(basis.Before), JsonSerializer.Serialize(await originals.Own(
                fixture.Rows.GetAsync(basis.Before.TaskId, TestContext.Current.CancellationToken))));
            Assert.Equal(1L, await originals.Own(fixture.ScalarAsync(
                "SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken)));
            Assert.Equal(0, fixture.Client.Dispatches);
        });

    [Fact]
    public Task Cold_public_material_is_refused_before_its_getters_or_caller_factory() =>
        WithColdProjectControlOriginals(async (fixture, originals) =>
        {
            var basis = await AcquireColdProjectControlBasis(fixture, originals);
            var publicMaterial = new UnreadPublicColdProjectMaterial();
            var callbacks = 0; var retained = new List<Task>(); Task? actual = null;
            Assert.False(fixture.Journal.IsIssuedOriginalProjectMaterial(publicMaterial,
                basis.Claim, basis.Context, basis.Before));
            var refusal = Assert.Throws<UnauthorizedAccessException>((Action)(() =>
            {
                actual = fixture.Journal.ValidateOriginalProjectMaterialWithinSourceAsync(publicMaterial,
                    callback => { callbacks++; callback(); }, retained.Add, TestContext.Current.CancellationToken);
            }));
            Assert.NotNull(refusal); Assert.Null(actual); Assert.Equal(0, publicMaterial.Reads);
            Assert.Equal(0, callbacks); Assert.Empty(retained);
            Assert.Same(basis.Claim, basis.Context.OriginalClaim);
            Assert.Equal(JsonSerializer.Serialize(basis.Before), JsonSerializer.Serialize(await originals.Own(
                fixture.Rows.GetAsync(basis.Before.TaskId, TestContext.Current.CancellationToken))));
            Assert.Equal(1L, await originals.Own(fixture.ScalarAsync(
                "SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken)));
            Assert.Equal(0, fixture.Client.Dispatches);
        });

    private static async Task<(ITaskRunColdJournalEntry Entry, ITaskRunColdJournalClaim Claim,
        ITaskRunColdContextLease Context, TaskExecutionSnapshot Before)> AcquireColdProjectControlBasis(
        ColdPersonalFixture fixture, ColdProjectControlOriginals originals)
    {
        var lease = await originals.Own(fixture.StartAsync(TestContext.Current.CancellationToken));
        await originals.Own(InitialHostedProducer(lease));
        await originals.Own(fixture.CaptureDriver(lease));
        await originals.Own(lease.DetachAndDrainAsync());
        var before = await originals.Own(fixture.Rows.GetByContextAsync(fixture.Conversation.Id,
            TestContext.Current.CancellationToken)) ?? throw new InvalidOperationException("No actual initial Task row was acknowledged.");
        var entry = await originals.Own(fixture.Journal.ReadOriginalAsync(before.TaskId, before.ExecutionId,
            ColdCaptureSourceScope(fixture, lease), TestContext.Current.CancellationToken))
            ?? throw new InvalidOperationException("No source-authenticated original capsule exists.");
        var claim = await originals.Own(fixture.Journal.ClaimOriginalAsync(entry, before,
            TestContext.Current.CancellationToken));
        originals.KeepResource(claim);
        var context = await originals.Own(fixture.Journal.AuthorizeOriginalAsync(claim, before,
            TestContext.Current.CancellationToken).AsTask());
        originals.KeepResource(context);
        return (entry, claim, context, before);
    }

    private static async Task WithColdProjectControlOriginals(Func<ColdPersonalFixture,
        ColdProjectControlOriginals, Task> body)
    {
        if (!OperatingSystem.IsLinux()) return;
        ColdPersonalFixture? fixture = null; Exception? primary = null;
        var originals = new ColdProjectControlOriginals(); var cleanup = new List<Exception>();
        try
        {
            // Retain the SAME maintained fixture before its first initialization await,
            // so a database/mode failure still reaches its actual owner cleanup.
            fixture = new ColdPersonalFixture();
            await originals.Own(fixture.Database.InitializeAsync(TestContext.Current.CancellationToken));
            File.SetUnixFileMode(fixture.Paths.DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await body(fixture, originals);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            // Acquire every actual resource close independently BEFORE joining any one.
            // A synchronous close refusal cannot skip the other Context/Claim/owner close.
            foreach (var resource in originals.Resources.AsEnumerable().Reverse())
                try { _ = originals.Own(resource.DisposeAsync().AsTask()); }
                catch (Exception cause) { cleanup.Add(cause); }
            if (fixture is not null)
                try { _ = originals.Own(fixture.DisposeAsync().AsTask()); }
                catch (Exception cause) { cleanup.Add(cause); }
            try { await JoinColdBoundaryControlSources(primary, originals.Tasks, originals.Expected); }
            catch (Exception cause) { cleanup.Add(cause); }
        }
        if (cleanup.Count != 0)
            throw new AggregateException("Project source control primary and unknown independent cleanup remain visible.",
                (primary is null ? Enumerable.Empty<Exception>() : new[] { primary }).Concat(cleanup));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private sealed class ColdProjectControlOriginals
    {
        internal readonly List<Task> Tasks = new();
        internal readonly List<Exception> Expected = new();
        internal readonly List<IAsyncDisposable> Resources = new();
        internal Task<T> Own<T>(Task<T> sameTask) { Tasks.Add(sameTask); return sameTask; }
        internal Task Own(Task sameTask) { Tasks.Add(sameTask); return sameTask; }
        internal void Expect(Exception sameCause) => Expected.Add(sameCause);
        internal void KeepResource(IAsyncDisposable sameResource) => Resources.Add(sameResource);
    }

    private sealed class UnreadPublicColdProjectMaterial : ITaskRunColdOriginalProjectMaterial
    {
        internal int Reads;
        private T Deny<T>() { Reads++; throw new InvalidOperationException("A public material getter is not an issuer."); }
        public ITaskRunColdJournalClaim OriginalClaim => Deny<ITaskRunColdJournalClaim>();
        public ITaskRunColdContextLease OriginalContext => Deny<ITaskRunColdContextLease>();
        public TaskExecutionSnapshot OriginalExpected => Deny<TaskExecutionSnapshot>();
        public TaskRunColdChatInput OriginalInput => Deny<TaskRunColdChatInput>();
        public TaskRunColdProjectIdentity OriginalProjectIdentity => Deny<TaskRunColdProjectIdentity>();
    }
    [Fact]
    public Task Cold_project_context_refuses_early_source_callback_join_before_project_source_is_published() =>
        WithColdProjectControlOriginals(async (fixture, originals) =>
        {
            var basis = await AcquireColdProjectControlBasis(fixture, originals);
            MarkColdProjectControlContext(basis.Context, basis.Claim);
            var callback = ColdProjectContextControlScope(basis.Context);
            callback(() =>
            {
                var refusal = Assert.Throws<InvalidOperationException>((Action)(() => { _ = basis.Context.DisposeAsync(); }));
                Assert.Contains("Context callback", refusal.Message);
                Assert.Null(ColdProjectContextControlField(basis.Context, "Close"));
            });
            Assert.Null(ColdProjectContextControlField(basis.Context, "ProjectSource"));
            var close = originals.Own(basis.Context.DisposeAsync().AsTask()); await close;
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(ColdProjectContextControlStoreClosed(basis.Context));
            Assert.Equal(1, basis.Entry.Capsule.SchemaVersion);
            Assert.Equal(0, fixture.Client.Dispatches);
        });

    [Fact]
    public Task Cold_project_context_joins_same_held_child_and_refuses_restored_callback_even_for_existing_close() =>
        WithColdProjectControlOriginals(async (fixture, originals) =>
        {
            var basis = await AcquireColdProjectControlBasis(fixture, originals);
            var prior = ExecutionContext.Capture()!;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var child = new ColdProjectControlChild();
            var source = InjectColdProjectControlChild(fixture, basis.Context, basis.Claim, basis.Before, child, owned: true);
            var callback = ColdProjectContextControlScope(basis.Context); Exception? primary = null;
            child.Body = async () =>
            {
                entered.TrySetResult(); await release.Task;
                ExecutionContext.Run(prior.CreateCopy(), _ => callback(() =>
                {
                    var refusal = Assert.Throws<InvalidOperationException>((Action)(() => { _ = basis.Context.DisposeAsync(); }));
                    Assert.Contains("Context callback", refusal.Message);
                }), null);
            };
            Task? close = null;
            try
            {
                close = originals.Own(basis.Context.DisposeAsync().AsTask());
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                Assert.False(close.IsCompleted);
                Assert.Same(close, ColdProjectContextControlField(basis.Context, "Close"));
                Assert.Same(child.Close, ColdProjectContextControlField(basis.Context, "ProjectClose"));
            }
            catch (Exception error) { primary = error; }
            finally
            {
                release.TrySetResult();
                try { if (close is not null) await close; }
                catch (Exception error) { throw new AggregateException("Primary and SAME project close failure remain visible.",
                    (primary is null ? Enumerable.Empty<Exception>() : new[] { primary }).Concat(close?.Exception?.InnerExceptions.AsEnumerable() ?? new[] { error })); }
            }
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
            Assert.NotNull(close); Assert.True(close.IsCompletedSuccessfully);
            Assert.Equal(1, child.CloseCalls); Assert.Equal(1, child.Requests);
            Assert.True(source.OwnershipReads > 0); Assert.False(source.IsIssuedOriginalProjectRestoration(child, child.Material!));
            Assert.True(ColdProjectContextControlStoreClosed(basis.Context));
            Assert.Equal(1L, await originals.Own(fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken)));
            Assert.Equal(0, fixture.Client.Dispatches);
        });

    [Fact]
    public Task Cold_project_context_refused_parent_still_closes_recognized_child_and_actual_native_store() =>
        WithColdProjectControlOriginals(async (fixture, originals) =>
        {
            var basis = await AcquireColdProjectControlBasis(fixture, originals);
            var child = new ColdProjectControlChild();
            var source = InjectColdProjectControlChild(fixture, basis.Context, basis.Claim, basis.Before, child, owned: true);
            var refusal = new IOException("exact controlled project parent refusal before fixed owned cleanup");
            var refusing = false; Task? close = null;
            void Scope(Action callback) { if (Volatile.Read(ref refusing)) throw refusal; callback(); }
            var actual = originals.Own(ColdProjectContextControlWithin(fixture, basis.Context, Scope,
                task => { _ = originals.Own(task); }, () =>
                {
                    Volatile.Write(ref refusing, true);
                    close = originals.Own(basis.Context.DisposeAsync().AsTask()); return close;
                }));
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual); originals.Expect(refusal);
            Assert.Same(refusal, Assert.Single(Leaves(actual.Exception ?? failure).Distinct<Exception>(ReferenceEqualityComparer.Instance)));
            Assert.NotNull(close); Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
            Assert.NotNull(child.Close); Assert.True(child.Close.IsCompletedSuccessfully);
            Assert.Same(child.Close, ColdProjectContextControlField(basis.Context, "ProjectClose"));
            Assert.Equal(1, child.CloseCalls); Assert.Equal(1, child.Requests); Assert.True(source.OwnershipReads > 0);
            Assert.True(ColdProjectContextControlStoreClosed(basis.Context));
            Assert.False((bool)ColdProjectContextControlField(basis.Context, "Closed")!);
            Assert.Equal(1, basis.Entry.Capsule.SchemaVersion); Assert.Equal(0, fixture.Client.Dispatches);
        });

    [Fact]
    public Task Cold_project_context_does_not_dispose_unknown_late_child_but_joins_actual_store_cleanup() =>
        WithColdProjectControlOriginals(async (fixture, originals) =>
        {
            var basis = await AcquireColdProjectControlBasis(fixture, originals);
            var child = new ColdProjectControlChild();
            var source = InjectColdProjectControlChild(fixture, basis.Context, basis.Claim, basis.Before, child, owned: false);
            var close = originals.Own(basis.Context.DisposeAsync().AsTask());
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => close);
            var refusal = Assert.Single(Leaves(close.Exception ?? failure).OfType<UnauthorizedAccessException>());
            originals.Expect(refusal);
            Assert.True(close.IsFaulted); Assert.Equal(1, source.OwnershipReads);
            Assert.Equal(0, child.Requests); Assert.Equal(0, child.CloseCalls); Assert.Null(child.Close);
            Assert.Null(ColdProjectContextControlField(basis.Context, "ProjectClose"));
            Assert.True(ColdProjectContextControlStoreClosed(basis.Context));
            Assert.False((bool)ColdProjectContextControlField(basis.Context, "Closed")!);
            Assert.Equal(1L, await originals.Own(fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken)));
            Assert.Equal(0, fixture.Client.Dispatches);
        });

    // Component fault injection only: SAME real schema1 Context/Claim/native Store, plus
    // controlled child/source metadata. These controls never create schema2, execute an
    // AUTH validator/CAS/model call or establish a genuine Home/project resource grant.
    private static object? ColdProjectContextControlField(object context, string name) =>
        context.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(context);
    private static void SetColdProjectContextControlField(object context, string name, object? value) =>
        context.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(context, value);
    private static void MarkColdProjectControlContext(ITaskRunColdContextLease context, ITaskRunColdJournalClaim claim) =>
        SetColdProjectContextControlField(claim, "ProjectContext", context);
    private static bool ColdProjectContextControlStoreClosed(ITaskRunColdContextLease context)
    {
        var store = ColdProjectContextControlField(context, "Store")!;
        return (bool)store.GetType().GetField("_disposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
    }
    private static TaskRunColdOriginalSourceScope ColdProjectContextControlSources(ITaskRunColdContextLease context)
    {
        var claim = ColdProjectContextControlField(context, "Claim")!;
        var entry = ColdProjectContextControlField(claim, "Entry")!;
        return (TaskRunColdOriginalSourceScope)ColdProjectContextControlField(entry, "Sources")!;
    }
    private static Action<Action> ColdProjectContextControlScope(ITaskRunColdContextLease context)
    {
        var owner = ColdProjectContextControlField(context, "Owner")!;
        return (Action<Action>)owner.GetType().GetMethod("ProjectCaller", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, new object[] { ColdProjectContextControlSources(context), context })!;
    }
    private static Task ColdProjectContextControlWithin(ColdPersonalFixture fixture, ITaskRunColdContextLease context,
        Action<Action> scope, Action<Task> retain, Func<Task> body) => (Task)fixture.Journal.GetType()
            .GetMethod("WithinOriginalSourceAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(fixture.Journal, new object[] { ColdProjectContextControlSources(context), scope, retain, body })!;
    private static ColdProjectControlSource InjectColdProjectControlChild(ColdPersonalFixture fixture,
        ITaskRunColdContextLease context, ITaskRunColdJournalClaim claim, TaskExecutionSnapshot expected,
        ColdProjectControlChild child, bool owned)
    {
        MarkColdProjectControlContext(context, claim);
        var type = context.GetType().GetField("ProjectMaterial", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.FieldType;
        var entry = ColdProjectContextControlField(claim, "Entry")!;
        var payload = (string)ColdProjectContextControlField(entry, "Payload")!;
        child.Material = (ITaskRunColdOriginalProjectMaterial)type.GetConstructors(System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Single()
            .Invoke(new object[] { fixture.Journal, claim, context, expected, payload });
        var source = new ColdProjectControlSource(child, owned);
        SetColdProjectContextControlField(context, "ProjectMaterial", child.Material);
        SetColdProjectContextControlField(context, "ProjectSource", source);
        SetColdProjectContextControlField(context, "ProjectLease", child);
        fixture.Journal.GetType().GetField("_projectResources", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(fixture.Journal, source);
        return source;
    }
    private sealed class ColdProjectControlChild : ITaskRunColdProjectRestorationLease
    {
        internal ITaskRunColdOriginalProjectMaterial? Material; internal Func<Task> Body = () => Task.CompletedTask;
        internal Task? Close; internal int Requests, CloseCalls;
        public ITaskRunColdOriginalProjectMaterial OriginalMaterial => Material!;
        public TaskRunColdProjectIdentity OriginalIdentity => throw new NotSupportedException("No component resource grant.");
        public AuthenticatedResourceActor CurrentHomeResourceActor => throw new NotSupportedException("No component actor grant.");
        public Task OriginalPreparation => throw new NotSupportedException("No component prepared authority.");
        public void DemandOriginalCommit() => throw new NotSupportedException("No component commit pin.");
        public void RequestOriginalRetirement() => Requests++;
        public void DemandExternalOriginalJoin() { }
        public Task CloseAndDrainOriginalAsync() { CloseCalls++; return Close ??= Body(); }
    }
    private sealed class ColdProjectControlSource(ColdProjectControlChild child, bool owned) : ITaskRunColdProjectResourceSource
    {
        internal int OwnershipReads;
        public bool HasOriginalColdProjectComposition(ITaskRunColdRecoveryJournal journal, IAuthenticatedResourceActorSource actors) => false;
        public bool IsOwnedOriginalProjectRestoration(ITaskRunColdProjectRestorationLease lease, ITaskRunColdOriginalProjectMaterial material)
        { OwnershipReads++; return owned && ReferenceEquals(lease, child) && ReferenceEquals(material, child.Material); }
        public bool IsIssuedOriginalProjectRestoration(ITaskRunColdProjectRestorationLease lease, ITaskRunColdOriginalProjectMaterial material) => false;
        public bool IsIssuedOriginalProjectInput(ITaskRunColdOriginalProjectInput input) => false;
        public bool IsOwnedOriginalProjectInput(ITaskRunColdOriginalProjectInput input) => false;
        public Task<ITaskRunColdOriginalProjectInput> PrepareOriginalProjectInputWithinSourceAsync(Conversation conversation,
            ContainerDefinition container, string exact, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateOriginalProjectInputWithinSourceAsync(ITaskRunColdOriginalProjectInput input, TaskRunColdChatInput exact,
            Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task<TaskRunColdProjectIdentity> CaptureOriginalClosedProjectIdentityWithinSourceAsync(ITaskRunColdOriginalProjectInput input,
            TaskExecutionSnapshot terminal, TaskRunColdChatInput exact, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task<ITaskRunColdProjectRestorationLease> PrepareOriginalProjectRestorationWithinSourceAsync(ITaskRunColdOriginalProjectMaterial material,
            Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateOriginalProjectRestorationWithinSourceAsync(ITaskRunColdProjectRestorationLease lease, ITaskRunColdOriginalProjectMaterial material,
            Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateOriginalClosedProjectRestorationWithinSourceAsync(ITaskRunColdProjectRestorationLease lease, ITaskRunColdOriginalProjectMaterial material,
            ITaskRunColdJournalAcknowledgment ack, TaskExecutionSnapshot current, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
    }
}
