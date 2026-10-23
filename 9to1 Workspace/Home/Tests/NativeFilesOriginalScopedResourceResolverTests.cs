using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// Actual local Home registration, original Files ownership and durable provider. These
// observations do not grant an app session, write approval, or physical-path ancestry.
public sealed class NativeFilesOriginalScopedResourceResolverTests
{
    [Fact]
    public async Task Registered_authority_resolves_real_Canvas_folder_and_artifact_under_restored_context()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            rig.Principal.RequireScope = true;
            rig.RestoreContext = true;
            var folder = await rig.Workspace.Provider.GetAsync(rig.Workspace.Configuration.AppFolders["canvas"], rig.Token);
            var create = new ResourceScope("files.item", folder.Value!.Id.ToString(), folder.Value.CurrentRevisionId!.Value.ToString(), ResourceAccess.Write);
            var actual = rig.Keep(rig.Domain.Resources.AuthorizeForActorWithinOriginalSourceAsync(rig.Actor,
                "canvas.file.create", [create], rig.Scope, rig.Retain, rig.Token));
            Assert.Equal(rig.Actor, await actual);
            Assert.True((await rig.Read()).Allowed);
            Assert.NotEmpty(rig.Originals());
            Assert.Contains(rig.Originals(), task => task is Task<DurableDriveProvider.State>);
            Assert.Same(rig.Resolver, rig.Domain.Services.GetService(typeof(FilesArtifactResourceResolver)));
        });
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("actor")]
    [InlineData("owner-app")]
    [InlineData("artifact-type")]
    [InlineData("owner")]
    [InlineData("store")]
    public async Task Real_policy_rejects_changed_revision_actor_artifact_owner_and_type(string changed)
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var actor = changed == "actor" ? rig.Actor with { AuthenticationRevision = "different-authentication" } : rig.Actor;
            var scope = changed == "revision" ? rig.ArtifactScope with { Revision = Guid.NewGuid().ToString("D") } : rig.ArtifactScope;
            if (changed is "owner-app" or "artifact-type" or "owner")
                await rig.Store.UpdateAsync(state => state with
                {
                    Artifacts = state.Artifacts.Select(artifact => artifact.FileId != rig.Artifact.FileId ? artifact :
                        changed == "owner-app" ? artifact with { OwnerAppId = "write" } :
                        changed == "artifact-type" ? artifact with { ArtifactType = nameof(FilesArtifactType.WriteDocument) } : artifact).ToArray(),
                    Items = state.Items.Select(item => changed == "owner" && item.Metadata.Id == rig.Artifact.FileId
                        ? item with { Metadata = item.Metadata with { OwnerPrincipalId = "foreign-principal" } } : item).ToArray()
                }, rig.Token);
            if (changed == "store") await rig.Store.UpdateAsync(state => state with { StoreId = Guid.NewGuid() }, rig.Token);
            Task<ResourceAccessDecision> actual = rig.Read(actor: actor, resource: scope);
            try { Assert.False((await actual).Allowed); }
            catch (Exception error) when ((changed is "owner" or "store") && Leaves(error).All(cause => cause is UnauthorizedAccessException))
            { rig.Expect(error); }
        });
    }

    [Fact]
    public async Task Delegate_only_resolver_refuses_without_invoking_unscoped_provider_or_folder_factories()
    {
        var calls = 0;
        var resolver = new FilesArtifactResourceResolver((_, _) => { calls++; throw new InvalidOperationException("unscoped provider"); },
            (_, _, _) => { calls++; throw new InvalidOperationException("unscoped folder"); });
        var actor = new AuthenticatedResourceActor("actor", "profile", null, null, "authentication");
        await Assert.ThrowsAsync<NotSupportedException>(() => resolver.EvaluateWithinOriginalSourceAsync(actor,
            "canvas.file.open", new("files.item", Guid.NewGuid().ToString("D"), "uncommitted", ResourceAccess.Read),
            callback => callback(), _ => { }, CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Configuration_change_after_metadata_acquisition_refuses_same_cached_workspace()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var changed = 0;
            rig.OnRetain = raw =>
            {
                if (raw is Task<FilesResult<HostedItemMetadata>> && Interlocked.Exchange(ref changed, 1) == 0)
                    ((Dictionary<string, HostedItemId>)rig.Workspace.Configuration.AppFolders)["canvas"] = HostedItemId.New();
            };
            Assert.False((await rig.Read()).Allowed);
            Assert.Equal(1, changed);
        });
    }

    [Fact]
    public async Task Configuration_is_rechecked_inside_final_publication_callback()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var callbacks = 0;
            Assert.True((await rig.Read(callback => { callbacks++; rig.Scope(callback); })).Allowed);
            var observed = 0; var changed = false;
            var actual = rig.Read(callback =>
            {
                if (++observed == callbacks)
                {
                    ((Dictionary<string, HostedItemId>)rig.Workspace.Configuration.AppFolders)["canvas"] = HostedItemId.New();
                    changed = true;
                }
                rig.Scope(callback);
            });
            Assert.False((await actual).Allowed);
            Assert.True(changed); Assert.Equal(callbacks, observed);
        });
    }

    [Fact]
    public async Task Artifact_producer_preserves_original_store_and_revision_checks()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var wrongRevision = rig.Keep(rig.Workspace.Provider.GetArtifactForOriginalStoreAsync(rig.Workspace.Configuration.StoreId,
                rig.Artifact.FileId, new FilesRevisionId(Guid.NewGuid()), rig.Token, rig.Scope, rig.Retain));
            var result = await wrongRevision;
            Assert.False(result.IsSuccess); Assert.Equal(FilesErrorCode.RevisionConflict, result.Error!.Code);
            var wrongStore = rig.Keep(rig.Workspace.Provider.GetArtifactForOriginalStoreAsync(Guid.NewGuid(),
                rig.Artifact.FileId, rig.Revision, rig.Token, rig.Scope, rig.Retain));
            var failure = await Assert.ThrowsAsync<AggregateException>(() => wrongStore);
            Assert.All(Leaves(failure), cause => Assert.IsType<FilesOriginalStoreReadChangedException>(cause));
            rig.Expect(failure);
        });
    }

    [Fact]
    public async Task Held_principal_is_retained_before_scope_fault_and_independently_joined()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var cause = new IOException("actual resolver parent failure after raw principal acquisition");
            rig.Expect(cause); rig.Principal.Arm();
            var once = 0;
            var actual = rig.Read(callback =>
            {
                rig.Scope(callback);
                if (rig.Originals().Contains(rig.Principal.Raw.Task, ReferenceEqualityComparer.Instance) &&
                    Interlocked.Exchange(ref once, 1) == 0) throw cause;
            });
            await rig.Principal.Entered.Task.WaitAsync(rig.Token);
            Assert.False(actual.IsCompleted);
            Assert.Contains(rig.Principal.Raw.Task, rig.Originals());
            rig.Principal.Release();
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(cause, Leaves(error));
            Assert.True(rig.Principal.Raw.Task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task Faulted_principal_OCE_and_independent_IO_fault_stay_faults()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var cancellation = new OperationCanceledException("faulted raw resolver principal");
            var sibling = new IOException("independent original principal fault");
            rig.Expect(cancellation); rig.Expect(sibling); rig.Principal.Arm();
            rig.Principal.Raw.SetException([cancellation, sibling]);
            var actual = rig.Read();
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(cancellation, Leaves(error)); Assert.Contains(sibling, Leaves(error));
            Assert.Contains(rig.Principal.Raw.Task, rig.Originals());
        });
    }

    [Fact]
    public async Task Actually_canceled_principal_preserves_the_returned_Task_cancellation()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            rig.Principal.Arm(); rig.Principal.Raw.SetCanceled(new CancellationToken(true));
            var actual = rig.Read();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            rig.AllowActualCancellation = true;
            Assert.True(actual.IsCanceled); Assert.True(rig.Principal.Raw.Task.IsCanceled);
            Assert.Contains(rig.Principal.Raw.Task, rig.Originals());
        });
    }

    [Fact]
    public async Task Artifact_raw_repository_Task_is_retained_and_joined_when_parent_swallows_enrollment_failure()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var locked = rig.Keep(rig.Store.UpdateAsync(state => state, async _ =>
            { entered.TrySetResult(); await release.Task.ConfigureAwait(false); }, rig.Token));
            await entered.Task.WaitAsync(rig.Token);
            var cause = new IOException("actual raw repository enrollment refusal"); rig.Expect(cause);
            Task? raw = null;
            try
            {
                var actual = rig.Keep(rig.Workspace.Provider.GetArtifactForOriginalStoreAsync(rig.Workspace.Configuration.StoreId,
                    rig.Artifact.FileId, rig.Revision, rig.Token, callback =>
                    { try { rig.Scope(callback); } catch (IOException error) when (ReferenceEquals(error, cause)) { } }, task =>
                    {
                        rig.Retain(task);
                        if (task is Task<DurableDriveProvider.State>) { raw = task; throw cause; }
                    }));
                Assert.NotNull(raw); Assert.False(actual.IsCompleted); Assert.False(raw!.IsCompleted);
                release.TrySetResult(); await locked;
                var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
                Assert.Contains(cause, Leaves(error)); Assert.True(raw.IsCompletedSuccessfully);
                Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            }
            finally { release.TrySetResult(); }
        });
    }

    [Fact]
    public async Task Actual_repository_cancellation_keeps_the_raw_and_producer_Tasks_canceled()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var locked = rig.Keep(rig.Store.UpdateAsync(state => state, async _ =>
            { entered.TrySetResult(); await release.Task.ConfigureAwait(false); }, rig.Token));
            await entered.Task.WaitAsync(rig.Token);
            using var cancellation = new CancellationTokenSource(); Task? raw = null;
            try
            {
                var actual = rig.Keep(rig.Workspace.Provider.GetArtifactForOriginalStoreAsync(rig.Workspace.Configuration.StoreId,
                    rig.Artifact.FileId, rig.Revision, cancellation.Token, rig.Scope, task =>
                    { rig.Retain(task); if (task is Task<DurableDriveProvider.State>) raw = task; }));
                Assert.NotNull(raw); Assert.False(actual.IsCompleted);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
                rig.AllowActualCancellation = true;
                Assert.True(raw!.IsCanceled); Assert.True(actual.IsCanceled);
            }
            finally { release.TrySetResult(); await locked; }
        });
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("repeated")]
    [InlineData("cross-thread")]
    [InlineData("post-callback")]
    public async Task Direct_artifact_producer_rejects_finite_callback_protocol_failures(string failure)
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            Action? late = null; Exception? refused = null; var first = 0;
            void Scope(Action callback)
            {
                if (Interlocked.Exchange(ref first, 1) != 0) { rig.Scope(callback); return; }
                if (failure == "post-callback") { late = callback; return; }
                if (failure == "omitted") return;
                if (failure == "cross-thread")
                {
                    var thread = new Thread(() => { try { rig.Scope(callback); } catch (Exception error) { refused = error; } });
                    thread.Start(); thread.Join(); return;
                }
                rig.Scope(callback);
                try { rig.Scope(callback); } catch (Exception error) { refused = error; }
            }
            var actual = rig.Keep(rig.Workspace.Provider.GetArtifactForOriginalStoreAsync(rig.Workspace.Configuration.StoreId,
                rig.Artifact.FileId, rig.Revision, rig.Token, Scope, rig.Retain));
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            rig.Expect(error); Assert.True(actual.IsFaulted);
            if (late is not null) Assert.Throws<InvalidOperationException>(() => rig.Scope(late));
            if (failure is "repeated" or "cross-thread") Assert.IsType<InvalidOperationException>(refused);
        });
    }

    private sealed class Principal : ITrustedHostPrincipalSource
    {
        internal readonly TaskCompletionSource<string?> Raw = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool RequireScope;
        private string? _actual;
        private int _armed;
        internal async Task Initialize() => _actual = await new OperatingSystemPrincipalSource().GetPrincipalAsync(CancellationToken.None);
        internal void Arm() => Volatile.Write(ref _armed, 1);
        internal void Release() => Raw.TrySetResult(_actual);
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            if (RequireScope && Rig.PhysicalDepth == 0) throw new InvalidOperationException("Actual principal escaped its physical parent scope.");
            if (Volatile.Read(ref _armed) == 0) return new OperatingSystemPrincipalSource().GetPrincipalAsync(token);
            Entered.TrySetResult(); return new(Raw.Task);
        }
    }

    private sealed class Rig
    {
        [ThreadStatic] internal static int PhysicalDepth;
        private readonly ExecutionContext _context = ExecutionContext.Capture()!;
        private readonly List<Task> _originals = [];
        internal readonly HashSet<Exception> Expected = new(ReferenceEqualityComparer.Instance);
        internal readonly Principal Principal = new();
        internal readonly CancellationTokenSource Deadline = new(TimeSpan.FromSeconds(30));
        internal CancellationToken Token => Deadline.Token;
        internal bool RestoreContext;
        internal bool AllowActualCancellation;
        internal Action<Task>? OnRetain;
        internal HomeLocalDomainComposition Domain = null!;
        internal NativeFilesWorkspaceService Workspaces = null!;
        internal NativeFilesWorkspaceAuthority Authority = null!;
        internal FilesArtifactResourceResolver Resolver = null!;
        internal NativeFilesWorkspace Workspace = null!;
        internal FilesArtifactReference Artifact = null!;
        internal FilesRevisionId Revision;
        internal VersionedJsonStateStore<DurableDriveProvider.State> Store = null!;
        internal AuthenticatedResourceActor Actor => Domain.GetOriginalStartedActor();
        internal ResourceScope ArtifactScope => new("files.item", Artifact.FileId.ToString(), Revision.ToString(), ResourceAccess.Read);
        internal void Scope(Action callback)
        {
            void Invoke() { PhysicalDepth++; try { callback(); } finally { PhysicalDepth--; } }
            if (RestoreContext) ExecutionContext.Run(_context.CreateCopy(), _ => Invoke(), null); else Invoke();
        }
        internal void Retain(Task task)
        {
            if (PhysicalDepth == 0) throw new InvalidOperationException("Raw Task enrollment escaped the physical parent scope.");
            lock (_originals) if (!_originals.Contains(task, ReferenceEqualityComparer.Instance)) _originals.Add(task);
            OnRetain?.Invoke(task);
        }
        internal Task<T> Keep<T>(Task<T> task) { lock (_originals) _originals.Add(task); return task; }
        internal Task Keep(Task task) { lock (_originals) _originals.Add(task); return task; }
        internal Task[] Originals() { lock (_originals) return _originals.ToArray(); }
        internal void Expect(Exception error) { foreach (var cause in Leaves(error)) Expected.Add(cause); }
        internal Task<ResourceAccessDecision> Read(Action<Action>? scope = null, AuthenticatedResourceActor? actor = null, ResourceScope? resource = null)
            => Keep(Resolver.EvaluateWithinOriginalSourceAsync(actor ?? Actor, "canvas.file.open", resource ?? ArtifactScope, scope ?? Scope, Retain, Token));
        internal async Task<Exception[]> Close()
        {
            Principal.Release(); List<Exception> errors = [];
            foreach (var raw in Originals().Distinct<Task>(ReferenceEqualityComparer.Instance)) await Join(raw);
            if (Domain is not null) await Join(Domain.CloseAndDrainAsync());
            Deadline.Dispose(); return errors.ToArray();
            async Task Join(Task raw)
            {
                try { await raw.ConfigureAwait(false); }
                catch (Exception error)
                {
                    if (AllowActualCancellation && raw.IsCanceled && Originals().Contains(raw, ReferenceEqualityComparer.Instance)) return;
                    foreach (var cause in Leaves(raw.Exception ?? error))
                        if (!Expected.Contains(cause) && !errors.Contains(cause, ReferenceEqualityComparer.Instance)) errors.Add(cause);
                }
            }
        }
    }

    private static async Task WithRig(Func<Rig, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "home-files-scoped-resolver-" + Guid.NewGuid().ToString("N"));
        var rig = new Rig(); Task? actualBody = null; List<Exception> failures = [];
        try
        {
            Directory.CreateDirectory(root);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await rig.Principal.Initialize();
            rig.Domain = new(new(Path.Combine(root, "home-state.json")), rig.Principal,
                configureOriginalStores: identity =>
                {
                    rig.Workspaces = new(identity.StateStore, identity.Profiles);
                    return new([rig.Workspaces], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = rig.Workspaces });
                }, configureOriginalResolvers: owners =>
                {
                    rig.Authority = new(rig.Workspaces, owners.Identity.Profiles, owners.Ownership);
                    rig.Resolver = new(rig.Authority);
                    return new([rig.Resolver], new Dictionary<Type, object>
                    { [typeof(NativeFilesWorkspaceAuthority)] = rig.Authority, [typeof(FilesArtifactResourceResolver)] = rig.Resolver });
                });
            await rig.Keep(rig.Domain.StartOriginalAsync());
            var files = Path.Combine(root, "files"); Directory.CreateDirectory(files);
            await rig.Workspaces.ConfigureNewAsync(files, rig.Domain.LocalStoreOwnership, rig.Token);
            rig.Workspace = await rig.Authority.GetCurrentAsync(rig.Token) ?? throw new InvalidOperationException("Actual configured Files workspace is missing.");
            rig.Artifact = new("canvas", Guid.NewGuid().ToString("N"), HostedItemId.New(),
                rig.Workspace.Configuration.AppFolders["canvas"], nameof(FilesArtifactType.Canvas), "Revision notebook");
            Assert.True((await rig.Workspace.Provider.RegisterArtifactAsync(rig.Artifact, rig.Actor.ActorId, rig.Token)).IsSuccess);
            var committed = await rig.Workspace.Provider.CommitDurableRevisionAsync(new(rig.Artifact.FileId, "canvas", "first-owning-revision",
                rig.Actor.ActorId, DateTimeOffset.UtcNow, 1, new string('a', 64), "content/notebook.9to1c", null), rig.Token);
            Assert.True(committed.IsSuccess); rig.Revision = committed.Value!.Id;
            rig.Store = new(Path.Combine(files, ".9to1-files", "drive.json"), 1,
                () => throw new InvalidOperationException("Existing source must never create a replacement store."));
            actualBody = body(rig); await actualBody;
        }
        catch (Exception error) { failures.AddRange(Leaves(actualBody?.Exception ?? error)); }
        finally
        {
            try { failures.AddRange(await rig.Close()); } catch (Exception error) { failures.AddRange(Leaves(error)); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { failures.AddRange(Leaves(error)); }
        }
        if (failures.Count != 0) throw new AggregateException("Actual Files resolver body and independent original cleanup failed.", failures);
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group
        ? group.InnerExceptions.SelectMany(Leaves) : [error];
}
