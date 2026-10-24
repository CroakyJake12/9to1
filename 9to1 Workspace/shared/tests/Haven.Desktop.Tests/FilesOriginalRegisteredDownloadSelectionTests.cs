using System.Security.Cryptography;
using System.Text;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Home;
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

    [Fact]
    public Task Home_Library_publishes_actual_Files_folder_references_and_rechecks_the_exact_original_target_without_writes()
        => Run(async rig =>
        {
            var before = await rig.BytesAsync();
            var profiles = rig.Graph!.GetRequiredService<IAuthenticatedResourceActorSource>();
            async Task<bool> Current(CancellationToken token) =>
                await profiles.GetCurrentAsync(token) == rig.Workspace.Actor;
            var source = new NativeHomeFilesLibrarySource(rig.Browser, rig.Workspace.Actor,
                rig.Graph.GetRequiredService<IHomeCoreStateStore>(), Current, body => body(), () => true);
            try
            {
                var rows = await rig.Keep(source.SearchAsync(new HomeLibraryQuery(), rig.Token));
                var id = rig.Workspace.Configuration.StoreId.ToString("D") + "/" + rig.Folder.Value.ToString("D");
                var actual = Assert.Single(rows, item => item.ArtifactId == id);
                Assert.Equal("files", actual.OwnerApp); Assert.Equal("Files folder", actual.ArtifactType);
                var canonical = (await rig.Keep(rig.Workspace.Provider.GetAsync(rig.Folder, rig.Token))).Value!;
                Assert.Equal(canonical.Name, actual.DisplayName); Assert.Equal(canonical.ModifiedAt, actual.LastModified);
                Assert.Equal(canonical.OwnerPrincipalId, actual.Owner); Assert.Equal(canonical.IsShared, actual.IsShared);
                Assert.Same(actual, await rig.Keep(source.GetByIdAsync("files", id, rig.Token)));
                Assert.True(await rig.Keep(source.CanOpenAsync(actual.OpenLink, rig.Token)));
                Assert.False(await rig.Keep(source.CanOpenAsync(actual.OpenLink with { }, rig.Token)));
                Assert.False(await rig.Keep(source.CanReadAsync("boards", id, rig.Token)));
                var after = await rig.BytesAsync();
                Assert.Equal(before.Home, after.Home); Assert.Equal(before.Drive, after.Drive);
            }
            finally { await rig.Keep(source.DisposeAsync().AsTask()); }
        });

    [Fact]
    public Task Home_Library_bridge_requires_its_actual_issued_page_and_full_actor_and_reports_retirement_before_reading()
        => Run(async rig =>
        {
            var before = await rig.BytesAsync();
            var available = await rig.Keep(rig.Browser.ObserveOriginalHomeLibraryAvailabilityWithinSourceAsync(
                rig.Workspace.Actor, () => true, body => body(), rig.Retain, rig.Token));
            Assert.True(available.IsAvailable); Assert.Equal(rig.Workspace.Configuration.StoreId, available.StoreId);
            Assert.True(rig.Browser.IsIssuedOriginalHomeLibraryAvailability(available, rig.Workspace.Actor));
            var foreign = rig.Workspace.Actor with { AuthenticationRevision = rig.Workspace.Actor.AuthenticationRevision + ":retired" };
            Assert.False(rig.Browser.IsIssuedOriginalHomeLibraryAvailability(available, foreign));
            var changed = await rig.Keep(rig.Browser.ObserveOriginalHomeLibraryAvailabilityWithinSourceAsync(
                foreign, () => true, body => body(), rig.Retain, rig.Token));
            Assert.False(changed.IsAvailable); Assert.Equal("LibraryProfileChanged", changed.Code);
            var page = await rig.Keep(rig.Browser.ListOriginalHomeLibraryPageWithinSourceAsync(rig.Workspace.Actor,
                available.StoreId!.Value, "", null, () => true, body => body(), rig.Retain, rig.Token));
            Assert.Contains(page.Items, row => row.Id == rig.Folder && row.Kind == HostedItemKind.Folder);
            await rig.Keep(rig.Browser.RevalidateOriginalHomeLibraryPageWithinSourceAsync(page, rig.Workspace.Actor,
                () => true, body => body(), rig.Retain, rig.Token));
            var copied = rig.Keep(rig.Browser.RevalidateOriginalHomeLibraryPageWithinSourceAsync(page with { }, rig.Workspace.Actor,
                () => true, body => body(), rig.Retain, rig.Token));
            await rig.ObserveExpectedFailureAsync(copied, typeof(UnauthorizedAccessException),
                "Retain the SAME privately issued original Files Library page.");
            var rawBefore = rig.RawCount;
            var retired = rig.Browser.ObserveOriginalHomeLibraryAvailabilityWithinSourceAsync(
                rig.Workspace.Actor, () => false, body => body(), rig.Retain, rig.Token);
            Assert.Equal(rawBefore, rig.RawCount);
            var unavailable = await rig.Keep(retired);
            Assert.False(unavailable.IsAvailable); Assert.Null(unavailable.StoreId); Assert.Equal("LibraryRetired", unavailable.Code);
            var after = await rig.BytesAsync(); Assert.Equal(before.Home, after.Home); Assert.Equal(before.Drive, after.Drive);
        });

    [Fact]
    public Task Original_text_preview_reads_the_actual_committed_revision_with_explicit_character_limit_and_no_writes()
        => Run(async rig =>
        {
            var text = "// revision notes\n" + new string('a', 70000) + "\n<script>plain text only</script>";
            var uploaded = await rig.UploadAsync("revision-notes.cs", Encoding.UTF8.GetBytes(text));
            var page = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, rig.Folder.Value,
                token: rig.Token, expectedStoreId: rig.Workspace.Configuration.StoreId));
            var row = Assert.Single(page.Items, item => item.Id == uploaded.FileId);
            var before = await rig.BytesAsync();
            var preview = await rig.Keep(rig.Browser.ReadOriginalTextPreviewWithinSourceAsync(page, row,
                rig.Workspace.Actor, () => true, body => body(), rig.Retain, rig.Token));
            Assert.True(preview.Succeeded); Assert.Equal("PreviewReady", preview.Code); Assert.True(preview.IsTruncated);
            Assert.Equal(text[..FilesNativeBrowserService.MaximumTextPreviewCharacters], preview.Text);
            Assert.Equal(uploaded.FileId.Value, preview.FileId); Assert.Equal(uploaded.RevisionId.ToString(), preview.Revision);
            Assert.Equal(Encoding.UTF8.GetByteCount(text), preview.TotalBytes);
            Assert.Contains("Read-only", preview.Message, StringComparison.Ordinal);
            Assert.Equal(before.Home, (await rig.BytesAsync()).Home); Assert.Equal(before.Drive, (await rig.BytesAsync()).Drive);
        });

    [Fact]
    public Task Unsupported_binary_and_oversize_preview_are_truthful_nonfaulting_results_and_a_later_text_read_remains_healthy()
        => Run(async rig =>
        {
            var binary = await rig.UploadAsync("binary.txt", [0, 1, 2, 3]);
            var large = await rig.UploadAsync("too-large.txt", new byte[FilesNativeBrowserService.MaximumTextPreviewBytes + 1]);
            var valid = await rig.UploadAsync("healthy.txt", Encoding.UTF8.GetBytes("still healthy"));
            var page = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, rig.Folder.Value,
                token: rig.Token, expectedStoreId: rig.Workspace.Configuration.StoreId));
            var before = await rig.BytesAsync();
            var binaryResult = await rig.Keep(rig.Browser.ReadOriginalTextPreviewWithinSourceAsync(page,
                Assert.Single(page.Items, row => row.Id == binary.FileId), rig.Workspace.Actor,
                () => true, body => body(), rig.Retain, rig.Token));
            Assert.False(binaryResult.Succeeded); Assert.Equal("UnsupportedBinary", binaryResult.Code); Assert.Empty(binaryResult.Text);
            var largeResult = await rig.Keep(rig.Browser.ReadOriginalTextPreviewWithinSourceAsync(page,
                Assert.Single(page.Items, row => row.Id == large.FileId), rig.Workspace.Actor,
                () => true, body => body(), rig.Retain, rig.Token));
            Assert.False(largeResult.Succeeded); Assert.Equal("PreviewTooLarge", largeResult.Code); Assert.Empty(largeResult.Text);
            var current = await rig.Keep(rig.Browser.ReadOriginalTextPreviewWithinSourceAsync(page,
                Assert.Single(page.Items, row => row.Id == valid.FileId), rig.Workspace.Actor,
                () => true, body => body(), rig.Retain, rig.Token));
            Assert.True(current.Succeeded); Assert.Equal("still healthy", current.Text); Assert.False(current.IsTruncated);
            Assert.Equal(before.Home, (await rig.BytesAsync()).Home); Assert.Equal(before.Drive, (await rig.BytesAsync()).Drive);
        });

    [Fact]
    public Task Text_preview_rejects_copied_pages_rows_and_changed_actor_and_preserves_a_real_post_callback_IO_failure()
        => Run(async rig =>
        {
            var uploaded = await rig.UploadAsync("original.txt", Encoding.UTF8.GetBytes("unchanged"));
            var page = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, rig.Folder.Value,
                token: rig.Token, expectedStoreId: rig.Workspace.Configuration.StoreId));
            var row = Assert.Single(page.Items, item => item.Id == uploaded.FileId);
            const string refusal = "Select the SAME current original Files row for preview.";
            foreach (var pair in new[] { (Page: page with { }, Row: row), (Page: page, Row: row with { }) })
                await rig.ObserveExpectedFailureAsync(rig.Keep(rig.Browser.ReadOriginalTextPreviewWithinSourceAsync(
                    pair.Page, pair.Row, rig.Workspace.Actor, () => true, body => body(), rig.Retain, rig.Token)),
                    typeof(UnauthorizedAccessException), refusal);
            var stale = rig.Workspace.Actor with { AuthenticationRevision = rig.Workspace.Actor.AuthenticationRevision + ":stale" };
            await rig.ObserveExpectedFailureAsync(rig.Keep(rig.Browser.ReadOriginalTextPreviewWithinSourceAsync(page, row,
                stale, () => true, body => body(), rig.Retain, rig.Token)), typeof(UnauthorizedAccessException), refusal);
            var originalIo = new UnauthorizedAccessException("genuine preview source IO failed");
            var failed = rig.Keep(rig.Browser.ReadOriginalTextPreviewWithinSourceAsync(page, row,
                rig.Workspace.Actor, () => true, body => { body(); throw originalIo; }, rig.Retain, rig.Token));
            await rig.ObserveExpectedFailureAsync(failed, typeof(UnauthorizedAccessException), originalIo.Message);
            Assert.True(ContainsOriginal(failed.Exception!, originalIo));
            static bool ContainsOriginal(Exception cause, Exception original) => ReferenceEquals(cause, original) ||
                cause is AggregateException group && group.InnerExceptions.Any(child => ContainsOriginal(child, original));
            // Preview failures are operation-owned; they do not poison this shared metadata browser.
            var healthy = await rig.Keep(rig.Browser.ReadOriginalTextPreviewWithinSourceAsync(page, row,
                rig.Workspace.Actor, () => true, body => body(), rig.Retain, rig.Token));
            Assert.True(healthy.Succeeded); Assert.Equal("unchanged", healthy.Text);
        });

    [Fact]
    public Task Home_Library_finds_nested_text_by_shared_search_and_resolves_stable_identity_to_fresh_original_preview_target()
        => Run(async rig =>
        {
            var uploaded = await rig.UploadAsync("revision-notes.md", Encoding.UTF8.GetBytes("# Revision\nActual original text."));
            var before = await rig.BytesAsync();
            var profiles = rig.Graph!.GetRequiredService<IAuthenticatedResourceActorSource>();
            async Task<bool> Current(CancellationToken token) => await profiles.GetCurrentAsync(token) == rig.Workspace.Actor;
            var source = new NativeHomeFilesLibrarySource(rig.Browser, rig.Workspace.Actor,
                rig.Graph.GetRequiredService<IHomeCoreStateStore>(), Current, body => body(), () => true);
            try
            {
                var library = source.CreateService(new UnusedLibraryRouter());
                var page = await rig.Keep(library.SearchAsync(new HomeLibraryQuery(Text: "revision-notes"), rig.Token));
                Assert.True(page.Succeeded);
                var row = Assert.Single(page.Items);
                var stableId = rig.Workspace.Configuration.StoreId.ToString("D") + "/" + uploaded.FileId.Value.ToString("D");
                Assert.Equal(stableId, row.ArtifactId); Assert.Equal("files", row.OwnerApp); Assert.Equal("files.preview", row.OpenLink.TargetRouteId);
                Assert.Equal("revision-notes.md", row.DisplayName); Assert.Equal("Text/code file", row.ArtifactType);
                Assert.True(await rig.Keep(source.CanOpenAsync(row.OpenLink, rig.Token)));
                var target = await rig.Keep(source.ResolveOriginalOpenTargetAsync(row.OpenLink, rig.Token));
                Assert.NotNull(target); Assert.Equal(rig.Workspace.Configuration.StoreId, target.StoreId);
                Assert.Equal(rig.Folder.Value, target.FolderId); Assert.Equal(uploaded.FileId.Value, target.FileId);
                Assert.Equal(uploaded.RevisionId.Value, target.RevisionId);
                Assert.Null(await rig.Keep(source.ResolveOriginalOpenTargetAsync(row.OpenLink with { }, rig.Token)));
                var selected = await rig.Keep(rig.Browser.ReadOriginalTextPreviewSelectionWithinSourceAsync(rig.Workspace.Actor,
                    target.StoreId, new(target.FolderId), new(target.FileId!.Value), new(target.RevisionId!.Value),
                    () => true, body => body(), rig.Retain, rig.Token));
                Assert.Equal(uploaded.FileId, selected.Row.Id); Assert.Equal(uploaded.RevisionId, selected.Row.CurrentRevisionId);
                Assert.Same(selected.Row, Assert.Single(selected.Page.Items, item => item.Id == uploaded.FileId));
                Assert.Equal(before.Home, (await rig.BytesAsync()).Home); Assert.Equal(before.Drive, (await rig.BytesAsync()).Drive);
            }
            finally { await rig.Keep(source.DisposeAsync().AsTask()); }
        });
    private sealed class UnusedLibraryRouter : IHomeDeepLinkRouter
    {
        public Task<HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken token = default) =>
            throw new InvalidOperationException("This metadata/search case must not simulate native navigation success.");
    }

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
        internal async Task<FilesUploadedContent> UploadAsync(string name, byte[]? suppliedBytes = null, string contentType = "text/plain")
        {
            byte[] bytes = suppliedBytes ?? [1, 2, 3, 4];
            await Keep(File.WriteAllBytesAsync(Path.Combine(_directory, name), bytes, Token));
            var parent = (await Keep(Workspace.Provider.GetAsync(Folder, Token))).Value!;
            var upload = new FilesUploadedContent(HostedItemId.New(), Folder, name, contentType, new(Guid.NewGuid()),
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
