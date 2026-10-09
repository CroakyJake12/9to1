using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

// Genuine configured FileHome/OS profile/Files metadata and content. Seed writes
// use the maintained upload owner before the actual read under test; this is not
// a native Browser download, manual Browser/Files WRITE or installed-peer proof.
public sealed class FilesOriginalRegisteredDownloadSelectionTests
{
    [Fact]
    public Task Exact_registered_revision_beyond_first_hundred_uses_the_same_issued_page_and_row_without_writes()
        => Run(async rig =>
        {
            for (var index = 0; index < 101; index++) await rig.UploadAsync($"earlier-{index:D3}.txt");
            var target = await rig.UploadAsync("z-original-registered-download.txt");
            var first = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, rig.Folder.Value,
                token: rig.Token, expectedStoreId: rig.Workspace.Configuration.StoreId));
            Assert.Equal(100, first.Items.Count); Assert.NotNull(first.Next);
            Assert.DoesNotContain(first.Items, item => item.Id == target.FileId);
            var before = await rig.BytesAsync();
            var selected = await rig.Keep(rig.Browser.ReadOriginalRegisteredDownloadSelectionWithinSourceAsync(
                rig.Workspace.Actor, rig.Workspace.Configuration.StoreId, rig.Folder,
                target.FileId, target.RevisionId, () => true, body => body(), rig.Retain, rig.Token));
            Assert.Equal(target.FileId, selected.Row.Id); Assert.Equal(target.RevisionId, selected.Row.CurrentRevisionId);
            Assert.Equal(rig.Folder, selected.Row.ParentId);
            Assert.Same(selected.Row, Assert.Single(selected.Page.Items, item => item.Id == target.FileId));
            Assert.DoesNotContain(first.Items, item => ReferenceEquals(item, selected.Row));
            // The ordinary issuer validates this exact issued page. A copied page
            // carrying the same row cannot become an original navigation observation.
            await rig.Keep(rig.Browser.RevalidateAsync(selected.Page, rig.Workspace.Actor, rig.Token));
            var copied = rig.Keep(rig.Browser.CaptureOriginalPageReadCheckAsync(selected.Page with { },
                rig.Workspace.Actor, () => true, rig.Token).AsTask());
            await rig.ObserveExpectedFailureAsync(copied, typeof(UnauthorizedAccessException),
                "Retain the SAME privately issued original Files page.");
            Assert.Equal(before.Home, (await rig.BytesAsync()).Home);
            Assert.Equal(before.Drive, (await rig.BytesAsync()).Drive);
        });

    [Fact]
    public Task Changed_registered_revision_or_actual_actor_and_retired_observation_refuse_without_metadata_effects()
        => Run(async rig =>
        {
            var target = await rig.UploadAsync("original-registered-download.txt");
            var before = await rig.BytesAsync();
            var changed = rig.Keep(rig.Browser.ReadOriginalRegisteredDownloadSelectionWithinSourceAsync(
                rig.Workspace.Actor, rig.Workspace.Configuration.StoreId, rig.Folder,
                target.FileId, new FilesRevisionId(Guid.NewGuid()), () => true, body => body(), rig.Retain, rig.Token));
            await rig.ObserveExpectedFailureAsync(changed, typeof(InvalidOperationException),
                "The original registered Files item now has another parent, kind or revision.");
            var foreignActor = rig.Workspace.Actor with { AuthenticationRevision = rig.Workspace.Actor.AuthenticationRevision + ":stale" };
            var stale = rig.Keep(rig.Browser.ReadOriginalRegisteredDownloadSelectionWithinSourceAsync(
                foreignActor, rig.Workspace.Configuration.StoreId, rig.Folder,
                target.FileId, target.RevisionId, () => true, body => body(), rig.Retain, rig.Token));
            await rig.ObserveExpectedFailureAsync(stale, typeof(UnauthorizedAccessException),
                "The original Files session changed.");
            var sourcesBefore = rig.RawCount;
            var retired = rig.Browser.ReadOriginalRegisteredDownloadSelectionWithinSourceAsync(
                rig.Workspace.Actor, rig.Workspace.Configuration.StoreId, rig.Folder,
                target.FileId, target.RevisionId, () => false, body => body(), rig.Retain, rig.Token);
            Assert.Equal(sourcesBefore, rig.RawCount); // Refuse before another metadata source is acquired.
            await rig.ObserveExpectedFailureAsync(rig.Keep(retired), typeof(UnauthorizedAccessException),
                "The original registered Files observation retired.");
            var current = await rig.Keep(rig.Browser.ReadOriginalRegisteredDownloadSelectionWithinSourceAsync(
                rig.Workspace.Actor, rig.Workspace.Configuration.StoreId, rig.Folder,
                target.FileId, target.RevisionId, () => true, body => body(), rig.Retain, rig.Token));
            Assert.Equal(target.RevisionId, current.Row.CurrentRevisionId);
            Assert.Equal(before.Home, (await rig.BytesAsync()).Home);
            Assert.Equal(before.Drive, (await rig.BytesAsync()).Drive);
        });

    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "astra-files-registered-selection-" + Guid.NewGuid().ToString("N"));
        private string Chosen => Path.Combine(Root, "chosen");
        private string HomePath => Path.Combine(Root, "home.json");
        private string DrivePath => Path.Combine(Chosen, ".9to1-files", "drive.json");
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal ServiceProvider? Graph;
        internal NativeFilesWorkspace Workspace = null!;
        internal NativeFilesWorkspaceAuthority Authority = null!;
        internal FilesNativeBrowserService Browser = null!;
        internal HostedItemId Folder;
        private string _directory = "";
        private readonly object _gate = new();
        private readonly HashSet<Task> _raw = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Exception> _observed = new(ReferenceEqualityComparer.Instance);
        internal int RawCount { get { lock (_gate) return _raw.Count; } }
        internal void Retain(Task actual) { lock (_gate) _raw.Add(actual); }
        internal Task<T> Keep<T>(Task<T> actual) { Retain(actual); return actual; }
        internal Task Keep(Task actual) { Retain(actual); return actual; }
        internal async Task InitializeAsync()
        {
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Chosen);
            var home = new FileHomeCoreStateStore(HomePath);
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
            services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddSingleton(new HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService(home, (_, _) => null));
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            services.AddSingleton<ResourceAuthorizationService>(); services.AddFilesNativeHost();
            Graph = services.BuildServiceProvider();
            var configured = await Keep(Graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(Chosen,
                Graph.GetRequiredService<HomeLocalStoreOwnership>(), Token));
            Authority = Graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            // Setup returns its creation workspace. Content commits must borrow the actual current cached workspace.
            Workspace = await Keep(Authority.GetCurrentAsync(configured.Configuration.StoreId, Token))
                ?? throw new InvalidOperationException("The actual configured Files workspace is unavailable.");
            Assert.Equal(configured.Actor, Workspace.Actor);
            Assert.Equal(configured.Configuration.StoreId, Workspace.Configuration.StoreId);
            var current = await Keep(Authority.GetCurrentAsync(configured.Configuration.StoreId, Token));
            Assert.NotNull(current);
            Assert.Same(Workspace.Provider, current.Provider);
            Assert.Same(Workspace.Directories, current.Directories);
            Assert.Same(Workspace.Materializations, current.Materializations);
            Browser = Graph.GetRequiredService<FilesNativeBrowserService>(); Folder = Workspace.Configuration.AppFolders["picture"];
            _directory = (await Keep(Workspace.Directories.ResolveProfileAsync(Guid.Parse(Workspace.Actor.ProfileId), "picture", Token))).Value!.DirectoryPath;
        }
        internal async Task<FilesUploadedContent> UploadAsync(string name)
        {
            byte[] bytes = [1, 2, 3, 4];
            await Keep(File.WriteAllBytesAsync(Path.Combine(_directory, name), bytes, Token));
            var parent = (await Keep(Workspace.Provider.GetAsync(Folder, Token))).Value!;
            var upload = new FilesUploadedContent(HostedItemId.New(), Folder, name, "text/plain", new(Guid.NewGuid()),
                null, Workspace.Actor.ActorId, DateTimeOffset.UtcNow, bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)), name);
            var authority = await Keep(Authority.CaptureCommitAuthorityAsync(Workspace.Actor, Workspace.Provider, () => true, Token).AsTask());
            var committed = await Keep(Workspace.Provider.CommitUploadedContentAsync(upload,
                [new(Folder, parent.CurrentRevisionId)], authority, Token)); Assert.True(committed.IsSuccess); return upload;
        }
        internal async Task<(byte[] Home, byte[] Drive)> BytesAsync() =>
            (await Keep(File.ReadAllBytesAsync(HomePath, Token)), await Keep(File.ReadAllBytesAsync(DrivePath, Token)));
        internal async Task ObserveExpectedFailureAsync(Task actual, Type expectedType, string expectedMessage)
        {
            Exception? observed = null;
            try { await actual; } catch (Exception error) { observed = actual.Exception ?? error; }
            Assert.NotNull(observed); Assert.True(actual.IsFaulted);
            Verify(observed!); // Unknown IO/opaque empty groups are never accepted as an expected refusal.
            lock (_gate) Remember(observed!);
            void Verify(Exception error)
            {
                if (error is AggregateException group)
                {
                    Assert.NotEmpty(group.InnerExceptions);
                    foreach (var direct in group.InnerExceptions) Verify(direct);
                }
                else { Assert.Equal(expectedType, error.GetType()); Assert.Equal(expectedMessage, error.Message); }
            }
            void Remember(Exception error)
            {
                if (!_observed.Add(error)) return;
                if (error is AggregateException group) foreach (var direct in group.InnerExceptions) Remember(direct);
            }
        }
        internal async Task DrainAsync(List<Exception> errors)
        {
            Task[] originals; lock (_gate) originals = _raw.ToArray();
            foreach (var actual in originals)
                try { await actual; }
                catch (Exception error) { if (!IsObserved(actual.Exception ?? error)) errors.Add(actual.Exception ?? error); }
            if (errors.Count == 0 && Graph is not null)
                try { await Graph.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            bool IsObserved(Exception error)
            {
                lock (_gate) if (_observed.Contains(error)) return true;
                return error is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(IsObserved);
            }
        }
    }
    private static readonly List<(Rig Actual, Exception? Failure)> Retained = [];
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = new Rig(); var errors = new List<Exception>();
        try { await rig.InitializeAsync(); await body(rig); } catch (Exception error) { errors.Add(error); }
        await rig.DrainAsync(errors);
        lock (Retained) Retained.Add((rig, errors.Count == 0 ? null : new AggregateException(errors)));
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual Files registered-selection sources retained.", errors);
        // Keep the exclusive fixture and all failed originals; no deletion/reclamation.
    }
}
