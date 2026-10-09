using System.Collections.Immutable;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real Home JSON CAS/profile/OS principal with a synthetic capture issuer. These
/// controls prove journal preparation/admission custody, not physical source-read permission,
/// Home import review, native effects, recovery inspection or project registration completion.</summary>
public sealed partial class HomeDeveloperProjectSetupJournalTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Reopen_reuses_all_once_persisted_ids_without_any_second_write_or_resource_effect()
    {
        await using var rig = await Rig.Create();
        var first = await rig.Prepare();
        var originalBody = JsonSerializer.Serialize(first.Intent);
        var writes = rig.Store.SetupWrites;
        var reopened = rig.OtherOwner();
        var second = await rig.Prepare(reopened);
        Assert.Equal(originalBody, JsonSerializer.Serialize(second.Intent));
        Assert.Equal(writes, rig.Store.SetupWrites);
        Assert.Null(first.Intent.Validate());
        Assert.NotEmpty(first.Intent.Files);
        Assert.Equal(rig.SetupId, first.Intent.SetupId);
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Genuine_pending_step_is_persisted_before_effect_and_cannot_be_replayed_or_reminted()
    {
        await using var rig = await Rig.Create();
        var prepared = await rig.Prepare();
        var step = prepared.Intent.Steps[0];
        var admission = await rig.Owner.AdmitOriginalStepAsync(prepared, step);
        Assert.Same(step, admission.Step);
        var saved = await rig.Owner.ReadCheckpointAsync(rig.SetupId, rig.Actor);
        Assert.Equal(DeveloperProjectSetupStepState.Admitted, saved!.Observations[0].State);
        Assert.Equal(prepared.Intent.ProjectId, saved.Intent.ProjectId);
        Assert.Equal(prepared.Intent.Files[0].FileId, saved.Intent.Files[0].FileId);
        var writes = rig.Store.SetupWrites;
        rig.ExpectFault = true;
        await AssertOriginalCause<InvalidOperationException>(() => rig.Owner.AdmitOriginalStepAsync(prepared, step));
        await AssertOriginalCause<InvalidOperationException>(() => rig.Prepare(rig.OtherOwner()));
        Assert.Equal(writes, rig.Store.SetupWrites);
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Copied_capture_and_value_equal_copied_step_are_not_original_admission()
    {
        await using var rig = await Rig.Create();
        rig.ExpectFault = true;
        await AssertOriginalCause<UnauthorizedAccessException>(() => rig.Prepare(source: new Capture()));
        Assert.Equal(0, rig.Store.SetupWrites);
        // A denied copied capture did not reserve the genuine original setup preparation.
        var prepared = await rig.Prepare();
        var writes = rig.Store.SetupWrites;
        await AssertOriginalCause<UnauthorizedAccessException>(() => rig.Owner.AdmitOriginalStepAsync(prepared, prepared.Intent.Steps[0] with { }));
        Assert.Equal(writes, rig.Store.SetupWrites);
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Changed_actual_actor_refuses_preparation_without_a_setup_record()
    {
        await using var rig = await Rig.Create();
        rig.ExpectFault = true;
        await AssertOriginalCause<UnauthorizedAccessException>(() => rig.Prepare(actor: rig.Actor with { AuthenticationRevision = "withdrawn" }));
        Assert.Equal(0, rig.Store.SetupWrites);
        Assert.DoesNotContain((await rig.Store.Inner.ReadAsync()).State!.Records, row => row.RecordType == "home.dev-project-setup");
    }

    [Fact]
    public async Task Held_actual_home_write_keeps_same_close_pending_and_retains_direct_siblings()
    {
        await using var rig = await Rig.Create();
        var first = new IOException("Actual original setup persistence one.");
        var second = new OperationCanceledException("Faulted original setup persistence, not canceled work.");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<HomeStateWriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Releases.Add(() => raw.TrySetException([first, second]));
        rig.Store.BeforeSetupWrite = (_, _, _, _, _) => { entered.TrySetResult(); return raw.Task; };
        var preparation = rig.Prepare();
        await entered.Task.WaitAsync(Bound); // This owning Home test project uses xUnit v2.
        rig.ExpectFault = true;
        var close = rig.Owner.CloseAndDrainAsync();
        Assert.Same(close, rig.Owner.CloseAndDrainAsync());
        Assert.False(close.IsCompleted);
        raw.TrySetException([first, second]);
        await Assert.ThrowsAnyAsync<Exception>(() => preparation);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.True(close.IsFaulted);
        Assert.Contains(AllCauses(error), value => ReferenceEquals(value, first));
        Assert.Contains(AllCauses(error), value => ReferenceEquals(value, second));
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Persist_then_throw_preserves_original_ids_and_refuses_a_second_preparation_write()
    {
        await using var rig = await Rig.Create();
        var cause = new IOException("Actual wrapper failed after original Home persistence.");
        rig.Store.AfterSetupWrite = _ => throw cause;
        rig.ExpectFault = true;
        var preparation = rig.Prepare();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => preparation);
        Assert.Contains(AllCauses(error), value => ReferenceEquals(value, cause));
        var physical = Assert.Single((await rig.Store.Inner.ReadAsync()).State!.Records, value => value.RecordType == "home.dev-project-setup");
        var retained = physical.Payload.Deserialize<DeveloperProjectSetupCheckpoint>()!;
        var writes = rig.Store.SetupWrites;
        await AssertOriginalCause<InvalidOperationException>(() => rig.Prepare());
        Assert.Equal(writes, rig.Store.SetupWrites);
        var inspected = await rig.Owner.ReadCheckpointAsync(rig.SetupId, rig.Actor);
        Assert.Equal(JsonSerializer.Serialize(retained), JsonSerializer.Serialize(inspected));
        Assert.All(inspected!.Observations, value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
        Assert.Equal(0, rig.Captures.ResourceEffects); // This read is metadata, never an import success receipt.
    }

    [Fact]
    public async Task Existing_registration_preserves_physical_project_and_once_created_ids_without_copy_steps()
    {
        await using var rig = await Rig.Create();
        var source = new ExistingCapture(rig.Directory); rig.Captures = new(source);
        var owner = rig.OtherOwner();
        var prepared = await rig.PrepareExisting(owner, source);
        Assert.Equal(DeveloperProjectSetupMode.RegisterExisting, prepared.Intent.Mode);
        Assert.Equal(rig.Directory, prepared.Intent.OriginalExistingProjectRoot);
        Assert.Null(prepared.Intent.Validate());
        Assert.Contains(prepared.Intent.Steps, value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata);
        Assert.DoesNotContain(prepared.Intent.Steps, value => value.Kind is DeveloperProjectSetupStepKind.PublishImmutableSource or
            DeveloperProjectSetupStepKind.PublishMaterializedFile or DeveloperProjectSetupStepKind.CreateProjectDirectory or DeveloperProjectSetupStepKind.CreateChildDirectory);
        var body = JsonSerializer.Serialize(prepared.Intent); var writes = rig.Store.SetupWrites;
        var reopened = await rig.PrepareExisting(rig.OtherOwner(), source);
        Assert.Equal(body, JsonSerializer.Serialize(reopened.Intent)); Assert.Equal(writes, rig.Store.SetupWrites);
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }
    [Fact]
    public async Task Previously_prepared_copy_mode_cannot_be_adopted_as_existing_registration()
    {
        await using var rig = await Rig.Create(); var source = new ExistingCapture(rig.Directory);
        rig.Captures = new(source); var owner = rig.OtherOwner();
        var prepared = await rig.Prepare(owner, source); var writes = rig.Store.SetupWrites;
        Assert.Equal(DeveloperProjectSetupMode.CopyIntoFiles, prepared.Intent.Mode);
        rig.ExpectFault = true;
        await AssertOriginalCause<InvalidOperationException>(() => rig.PrepareExisting(rig.OtherOwner(), source));
        Assert.Equal(writes, rig.Store.SetupWrites); Assert.Equal(0, rig.Captures.ResourceEffects);
    }
    [Fact]
    public async Task Existing_original_root_replacement_refuses_reopen_without_a_second_identity_plan()
    {
        await using var rig = await Rig.Create(); var source = new ExistingCapture(rig.Directory);
        rig.Captures = new(source); var prepared = await rig.PrepareExisting(rig.OtherOwner(), source);
        var ids = prepared.Intent.Files.Select(value => value.FileId).ToArray(); var writes = rig.Store.SetupWrites;
        source.Root = Path.Combine(rig.Directory, "rebound"); rig.ExpectFault = true;
        await AssertOriginalCause<InvalidOperationException>(() => rig.PrepareExisting(rig.OtherOwner(), source));
        Assert.Equal(writes, rig.Store.SetupWrites);
        Assert.Equal(ids, (await rig.Owner.ReadCheckpointAsync(rig.SetupId, rig.Actor))!.Intent.Files.Select(value => value.FileId));
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    private static IEnumerable<Exception> AllCauses(Exception value)
    {
        yield return value;
        if (value is AggregateException group)
            foreach (var inner in group.InnerExceptions)
                foreach (var original in AllCauses(inner)) yield return original;
    }
    private static async Task AssertOriginalCause<T>(Func<Task> actual) where T : Exception
    {
        var observed = await Assert.ThrowsAnyAsync<Exception>(actual);
        Assert.Contains(AllCauses(observed), value => value is T);
    }

    private class Capture : IDeveloperProjectOriginalSourceCapture
    {
        public string OriginalCaptureReference => "actual-synthetic-capture-reference";
        public string OriginalCaptureDigest => new('a', 64);
        public ImmutableArray<string> OriginalFolderPaths => [];
        public ImmutableArray<DeveloperProjectCapturedSourceFile> OriginalFiles =>
            [new("main.cs", 12, new string('b', 64), "actual-source-file-reference")];
    }
    private sealed class ExistingCapture(string root) : Capture, IDeveloperProjectOriginalExistingSourceCapture
    {
        internal string Root = root;
        public string OriginalExistingProjectRoot => Root;
    }
    private sealed class CaptureAuthority(Capture actual) : IDeveloperProjectOriginalCaptureAuthority
    {
        internal int ResourceEffects = 0; // No resource-effect producer exists in this journal fixture.
        public bool IsIssuedOriginal(IDeveloperProjectOriginalSourceCapture source) => ReferenceEquals(source, actual);
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalSourceCapture source, AuthenticatedResourceActor actor, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (!IsIssuedOriginal(source)) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public void DemandExternalOriginalCaptureJoin() { }
    }
    private sealed class Store(FileHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        internal FileHomeCoreStateStore Inner => inner;
        internal int SetupWrites;
        internal Func<HomeCoreStateRecord, long, AuthenticatedResourceActor, IHomeStateCommitActorGuard, CancellationToken, Task<HomeStateWriteResult>>? BeforeSetupWrite;
        internal Action<HomeStateWriteResult>? AfterSetupWrite;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord row, long expected, CancellationToken ct = default) => inner.WriteAsync(row, expected, ct);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord row, long expected, AuthenticatedResourceActor actor,
            IHomeStateCommitActorGuard guard, CancellationToken ct = default)
        {
            if (row.RecordType != "home.dev-project-setup") return inner.WriteGuardedAsync(row, expected, actor, guard, ct);
            SetupWrites++;
            return BeforeSetupWrite is { } before ? before(row, expected, actor, guard, ct) : WriteOriginal(row, expected, actor, guard, ct);
        }
        private async Task<HomeStateWriteResult> WriteOriginal(HomeCoreStateRecord row, long expected, AuthenticatedResourceActor actor,
            IHomeStateCommitActorGuard guard, CancellationToken ct)
        {
            var actual = inner.WriteGuardedAsync(row, expected, actor, guard, ct);
            HomeStateWriteResult result;
            try { result = await actual; } catch when (actual.IsFaulted) { throw actual.Exception!; }
            try { AfterSetupWrite?.Invoke(result); }
            catch (OperationCanceledException original) { throw new AggregateException("Direct fixture observation fault after Home write.", original); }
            return result;
        }
    }
    private sealed class Rig : IAsyncDisposable
    {
        internal string Directory = null!;
        internal Store Store = null!;
        internal HomeLocalProfileIdentity Profiles = null!;
        internal AuthenticatedResourceActor Actor = null!;
        internal readonly Capture Source = new();
        internal CaptureAuthority Captures = null!;
        internal HomeDeveloperProjectSetupJournal Owner = null!;
        internal readonly List<HomeDeveloperProjectSetupJournal> Owners = [];
        internal readonly List<Action> Releases = [];
        internal readonly Guid SetupId = Guid.NewGuid();
        internal readonly Guid FilesStore = Guid.NewGuid();
        internal readonly Guid Destination = Guid.NewGuid();
        internal readonly Guid DestinationRevision = Guid.NewGuid();
        internal readonly Guid Workspace = Guid.NewGuid();
        internal bool ExpectFault;
        internal static async Task<Rig> Create()
        {
            var rig = new Rig { Directory = Path.Combine(Path.GetTempPath(), "astra-dev-setup-journal-" + Guid.NewGuid().ToString("N")) };
            System.IO.Directory.CreateDirectory(rig.Directory);
            try
            {
                rig.Store = new(new FileHomeCoreStateStore(Path.Combine(rig.Directory, "home.json")));
                rig.Profiles = new(rig.Store, new OperatingSystemPrincipalSource());
                rig.Actor = (await rig.Profiles.GetCurrentAsync(CancellationToken.None))!;
                rig.Captures = new(rig.Source);
                rig.Owner = rig.OtherOwner(); return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal HomeDeveloperProjectSetupJournal OtherOwner()
        { var actual = new HomeDeveloperProjectSetupJournal(Store, Profiles, Captures); Owners.Add(actual); return actual; }
        internal Task<HomeDeveloperProjectSetupJournal.Prepared> Prepare(HomeDeveloperProjectSetupJournal? owner = null,
            IDeveloperProjectOriginalSourceCapture? source = null, AuthenticatedResourceActor? actor = null) =>
            (owner ?? Owner).PrepareOriginalAsync(SetupId, actor ?? Actor, FilesStore, new string('c', 64), Destination,
                DestinationRevision.ToString("D"), source ?? Source, Workspace, 0, "ActualProject");
        internal Task<HomeDeveloperProjectSetupJournal.Prepared> PrepareExisting(HomeDeveloperProjectSetupJournal owner,
            IDeveloperProjectOriginalExistingSourceCapture source) => owner.PrepareExistingOriginalAsync(SetupId,
                Actor, FilesStore, new string('c', 64), Destination, DestinationRevision.ToString("D"), source, Workspace, 0, "ActualProject");
        public async ValueTask DisposeAsync()
        {
            foreach (var release in Releases) release();
            var failures = new List<Exception>();
            foreach (var owner in Owners)
                try { await owner.CloseAndDrainAsync(); } catch (Exception error) { if (!ExpectFault) failures.Add(error); }
            try { System.IO.Directory.Delete(Directory, true); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Actual Home setup-journal fixture teardown.", failures);
        }
    }
}
