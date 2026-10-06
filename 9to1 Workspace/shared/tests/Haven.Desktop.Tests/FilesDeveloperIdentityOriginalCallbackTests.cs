using System.Security.Cryptography;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Real Home/profile/Files/Dev stores and privately issued browser selection.
/// The trusted fixture seeds canonical mappings; it does not certify the new import producer,
/// native Home approval, a frame, account role, or a physical content permission.</summary>
public sealed partial class FilesDeveloperIdentityOriginalCallbackTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Held_actual_project_read_and_restored_context_callback_cannot_return_the_encompassing_close()
    {
        await using var rig = await Rig.CreateAsync();
        var beforeOriginal = ExecutionContext.Capture()!;
        Task? incorrectlyReturnedClose = null;
        var guardedCalls = 0;
        var testCallback = false;
        bool Lifetime()
        {
            if (testCallback) ExecutionContext.Run(beforeOriginal, _ =>
            {
                try { incorrectlyReturnedClose = rig.Browser.CloseOriginalDeveloperReadsAsync(); }
                catch (InvalidOperationException) { guardedCalls++; }
            }, null);
            return true;
        }
        var actual = rig.Resolve(Lifetime);
        await rig.Projects.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        var close = rig.Browser.CloseOriginalDeveloperReadsAsync();
        Assert.Same(close, rig.Browser.CloseOriginalDeveloperReadsAsync());
        Assert.False(close.IsCompleted);
        testCallback = true;
        rig.Projects.Release.TrySetResult();
        var result = await actual.WaitAsync(Bound, TestContext.Current.CancellationToken);
        await close.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal(rig.Row.Id.Value, result.Value!.FileId);
        Assert.Null(incorrectlyReturnedClose);
        Assert.True(guardedCalls > 1); // Direct Require and the lower Paired callback after awaited reads.
    }

    [Fact]
    public async Task Lower_pairing_cannot_swallow_the_exact_direct_callback_cancellation_cause()
    {
        await using var rig = await Rig.CreateAsync();
        var original = new OperationCanceledException("Actual direct lifetime callback; no canceled I/O Task.");
        var fail = false;
        var afterReleaseCalls = 0;
        var actual = rig.Resolve(() =>
        {
            // First visit is owning Require; second is the lower original Paired callback.
            if (fail && ++afterReleaseCalls == 2) throw original;
            return true;
        });
        await rig.Projects.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        rig.ExpectedReadFault = true;
        fail = true;
        rig.Projects.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => actual);
        var close = rig.Browser.CloseOriginalDeveloperReadsAsync();
        var fault = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.True(close.IsFaulted);
        Assert.Contains(AllCauses(fault), value => ReferenceEquals(value, original));
    }

    [Fact]
    public async Task Lower_pairing_retains_the_same_compound_callback_and_both_original_members()
    {
        await using var rig = await Rig.CreateAsync();
        var first = new IOException("Actual original callback one.");
        var second = new InvalidOperationException("Actual original callback two.");
        var original = new AggregateException("Actual compound callback.", first, second);
        var fail = false;
        var afterReleaseCalls = 0;
        var actual = rig.Resolve(() =>
        {
            // First visit is owning Require; second is the lower original Paired callback.
            if (fail && ++afterReleaseCalls == 2) throw original;
            return true;
        });
        await rig.Projects.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        rig.ExpectedReadFault = true;
        fail = true;
        rig.Projects.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => actual);
        var fault = await Assert.ThrowsAnyAsync<Exception>(() => rig.Browser.CloseOriginalDeveloperReadsAsync());
        var causes = AllCauses(fault).ToArray();
        Assert.Contains(causes, value => ReferenceEquals(value, original));
        Assert.Contains(causes, value => ReferenceEquals(value, first));
        Assert.Contains(causes, value => ReferenceEquals(value, second));
    }

    private static IEnumerable<Exception> AllCauses(Exception original)
    {
        yield return original;
        if (original is AggregateException group)
            foreach (var member in group.InnerExceptions)
                foreach (var cause in AllCauses(member)) yield return cause;
    }

    private sealed class HeldProjects(FileDeveloperWorkspaceStore actual) : IDeveloperWorkspaceStore
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _held;
        public Task<DeveloperOperationResult<DeveloperWorkspace>> CreateAsync(DeveloperWorkspace value, CancellationToken ct = default) => actual.CreateAsync(value, ct);
        public Task<DeveloperOperationResult<DeveloperWorkspace>> SaveAsync(DeveloperWorkspace value, long revision, CancellationToken ct = default) => actual.SaveAsync(value, revision, ct);
        public async Task<DeveloperOperationResult<DeveloperWorkspace>> GetAsync(Guid id, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _held) == 1)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return await actual.GetAsync(id, ct);
        }
    }
    private sealed class Rig : IAsyncDisposable
    {
        internal string Directory = null!;
        internal ServiceProvider Graph = null!;
        internal FilesNativeBrowserService Browser = null!;
        internal HeldProjects Projects = null!;
        internal AuthenticatedResourceActor Actor = null!;
        internal FilesNativeBrowserPage Page = null!;
        internal HostedItemMetadata Row = null!;
        internal FilesWorkspaceDirectoryBinding Root = null!;
        internal DeveloperResolvedProject Project = null!;
        internal bool ExpectedReadFault;
        internal readonly List<Task> _actualReads = [];
        internal Task<DeveloperOperationResult<DeveloperCodeDocument>> Resolve(Func<bool> lifetime)
        {
            var actual = Browser.ResolveOriginalDeveloperSelectionAsync(Page, Row, Actor, Project, Root, "main.cs", lifetime);
            _actualReads.Add(actual); return actual;
        }
        internal static async Task<Rig> CreateAsync(Func<HomeLocalProfileIdentity, IAuthenticatedResourceActorSource>? originalActorProbe = null)
        {
            var rig = new Rig { Directory = Path.Combine(Path.GetTempPath(), "astra-dev-existing-identity-" + Guid.NewGuid().ToString("N")) };
            System.IO.Directory.CreateDirectory(rig.Directory);
            try
            {
                var home = new FileHomeCoreStateStore(Path.Combine(rig.Directory, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var services = new ServiceCollection();
                services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
                services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
                services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
                services.AddSingleton<ResourceAuthorizationService>(); services.AddFilesNativeHost();
                rig.Graph = services.BuildServiceProvider();
                rig.Actor = (await profiles.GetCurrentAsync())!;
                var chosen = Path.Combine(rig.Directory, "chosen"); System.IO.Directory.CreateDirectory(chosen);
                await rig.Graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen, rig.Graph.GetRequiredService<HomeLocalStoreOwnership>());
                var workspace = (await rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync())!;
                var folderId = new HostedItemId(Guid.NewGuid()); var now = DateTimeOffset.UtcNow;
                var operation = new FilesOperation(new(Guid.NewGuid()), rig.Actor.ActorId, folderId, null, null,
                    "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
                Assert.True((await workspace.Provider.MutateAsync(operation, "actual-dev-project", default)).IsSuccess);
                var path = Path.Combine(chosen, "actual-dev-project"); System.IO.Directory.CreateDirectory(path);
                var rootId = Guid.NewGuid(); var projectId = Guid.NewGuid();
                rig.Root = (await workspace.Directories.RegisterProfileAsync(Guid.Parse(rig.Actor.ProfileId), folderId,
                    "dev.project." + projectId.ToString("N"), path)).Value!;
                var bytes = System.Text.Encoding.UTF8.GetBytes("class ActualOriginal { }\n");
                var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                await File.WriteAllBytesAsync(Path.Combine(path, "main.cs"), bytes);
                var fileId = new HostedItemId(Guid.NewGuid()); var revision = new FilesRevisionId(Guid.NewGuid());
                Assert.True((await workspace.Provider.CommitUploadedContentAsync(new(fileId, folderId, "main.cs", "text/plain",
                    revision, null, rig.Actor.ActorId, now, bytes.Length, hash, "fixtures/actual-source"))).IsSuccess);
                await workspace.Materializations.RegisterValidatedAsync(Path.Combine(path, "main.cs"), new(fileId, revision, hash, bytes.Length, now), SyncAvailability.AvailableOffline);
                var descriptor = new DeveloperProject(projectId, "source", "Actual", [rootId], "csharp", null, "none", [], [], [], [], null);
                var root = new DeveloperWorkspaceRoot(rootId, path);
                var saved = DeveloperWorkspace.Create([root]) with { Projects = [descriptor],
                    OpenEditors = [new DeveloperOpenEditor(fileId.Value, projectId, "files:" + fileId, "main", 0, false, false)] };
                var store = new FileDeveloperWorkspaceStore(Path.Combine(rig.Directory, "dev"));
                saved = (await store.CreateAsync(saved)).Value!; rig.Projects = new(store);
                rig.Project = new(new(saved.WorkspaceId, saved.Revision, projectId, descriptor.Revision, rootId), saved, descriptor, root, null);
                rig.Browser = new(rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>(), originalActorProbe?.Invoke(profiles) ?? profiles,
                    rig.Graph.GetRequiredService<ResourceAuthorizationService>(), rig.Graph.GetRequiredService<ICompatibilityPackageContentSource>(),
                    rig.Graph.GetRequiredService<FilesOriginalChildFolderReadSource>(), rig.Projects);
                rig.Page = await rig.Browser.ListAsync(rig.Actor, folderId.Value);
                rig.Row = Assert.Single(rig.Page.Items);
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Projects?.Release.TrySetResult();
            var errors = new List<Exception>();
            foreach (var actual in _actualReads)
                try { await actual; } catch (Exception error) { if (!ExpectedReadFault) errors.Add(error); }
            if (Browser is not null)
                try { await Browser.CloseOriginalDeveloperReadsAsync(); } catch (Exception error) { if (!ExpectedReadFault) errors.Add(error); }
            if (Graph is not null)
                try { await Graph.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { System.IO.Directory.Delete(Directory, true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Original Files Dev fixture cleanup.", errors);
        }
    }
}
