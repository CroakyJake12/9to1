using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

// Genuine configured Home/OS profile/Files provider and registered materialization.
// Maintained upload/registration arrange existing state before the tested metadata READ.
// These controls grant no native file content, worker/runtime, Data operation or installed authority.
public sealed class FilesOriginalDataSelectionTests
{
    [Fact]
    public Task Same_current_page_row_and_registered_revision_issue_only_metadata_without_writing()
        => Run(async rig =>
        {
            var upload = await rig.UploadAsync("current-workbook.ods");
            var (page, row) = await rig.SelectRowAsync(upload); var before = await rig.BytesAsync();
            var selected = await rig.SelectAsync(page, row);
            Assert.True(rig.Browser.IsIssuedOriginalDataFileSelection(selected));
            Assert.Equal(rig.Workspace.Actor, selected.OriginalActor);
            Assert.Equal(upload.FileId.Value, selected.OriginalFileId);
            Assert.Equal(upload.RevisionId.Value, selected.OriginalRevisionId);
            Assert.Equal(row.Name, selected.OriginalName); Assert.Equal(row.SizeBytes, selected.OriginalSizeBytes);
            Assert.Equal(row.ContentHash, selected.OriginalContentSha256);
            Assert.Equal(page.StoreID, selected.OriginalStoreIdentity.StoreId);
            Assert.NotNull(selected.OriginalStoreOwnership.Receipt);
            Assert.Equal(ResourceAccess.Read, selected.OriginalScope.Access);
            await rig.Keep(rig.Browser.RevalidateOriginalDataFileSelectionWithinSourceAsync(selected,
                body => body(), rig.Retain, rig.Token));
            await rig.AssertUnchangedAsync(before);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Copied_page_or_row_refuses_before_another_metadata_source_and_keeps_all_bytes(bool copyPage)
        => Run(async rig =>
        {
            var upload = await rig.UploadAsync("current-workbook.ods");
            var (page, row) = await rig.SelectRowAsync(upload); var before = await rig.BytesAsync();
            var retainedBefore = rig.RawCount;
            var actual = rig.SelectAsync(copyPage ? page with { } : page, copyPage ? row : row with { });
            await rig.ObserveExpectedFailureAsync(actual, typeof(UnauthorizedAccessException),
                "Select the current original Files row for Data.");
            Assert.Equal(retainedBefore + 1, rig.RawCount); // Only this actual refusal task; no child metadata/worker source.
            await rig.AssertUnchangedAsync(before);
        });

    [Fact]
    public Task Unrelated_store_change_refuses_old_full_window_without_replacing_selected_identity()
        => Run(async rig =>
        {
            var upload = await rig.UploadAsync("current-workbook.ods");
            var (page, row) = await rig.SelectRowAsync(upload);
            await rig.UploadAsync("later-unrelated-file.txt"); var before = await rig.BytesAsync();
            var actual = rig.SelectAsync(page, row);
            await rig.ObserveExpectedFailureAsync(actual, typeof(UnauthorizedAccessException),
                "The selected Files page changed. Refresh it before opening Data.");
            var current = await rig.Keep(rig.Workspace.Provider.GetAsync(upload.FileId, rig.Token));
            Assert.Equal(upload.RevisionId, current.Value!.CurrentRevisionId);
            Assert.Equal(upload.FileId, current.Value.Id); await rig.AssertUnchangedAsync(before);
        });

    [Fact]
    public Task Late_callback_after_independently_joined_and_pruned_read_blocks_admission_and_preserves_failed_close()
        => Run(async rig =>
        {
            var upload = await rig.UploadAsync("current-workbook.ods");
            var (page, row) = await rig.SelectRowAsync(upload); Action? saved = null;
            var actual = rig.SelectAsync(page, row, body => { saved ??= body; body(); });
            var selected = await actual; await rig.JoinActualReadObserver(actual);
            await rig.Keep(rig.Browser.RevalidateOriginalDataFileSelectionWithinSourceAsync(selected,
                body => body(), rig.Retain, rig.Token)); // Actual successful read prunes the prior independently joined cohort.
            Assert.NotNull(saved); var before = await rig.BytesAsync();
            Exception? protocol = null; try { saved!(); } catch (Exception cause) { protocol = cause; }
            Assert.IsType<InvalidOperationException>(protocol);
            Assert.Equal("The original Data selection callback is finite, once-only and same-thread.", protocol!.Message);
            Assert.Null(protocol.InnerException);
            rig.RememberKnown(protocol);
            var count = rig.RawCount; Exception? refused = null;
            try
            {
                var unexpected = rig.Browser.RevalidateOriginalDataFileSelectionWithinSourceAsync(selected,
                    body => body(), rig.Retain, rig.Token);
                await rig.Keep(unexpected); // Retain and independently join any unexpectedly admitted raw before failing.
            }
            catch (Exception cause) { refused = cause; }
            Assert.NotNull(refused); rig.VerifyOnlyObserved(refused!); Assert.Equal(count, rig.RawCount);
            var close = rig.Keep(rig.Browser.CloseOriginalDataSelectionsAndDrainAsync());
            Exception? failure = null; try { await close; } catch (Exception cause) { failure = close.Exception ?? cause; }
            Assert.NotNull(failure); rig.VerifyOnlyObserved(failure!);
            Assert.Same(close, rig.Browser.CloseOriginalDataSelectionsAndDrainAsync());
            await rig.AssertUnchangedAsync(before);
        });

    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "astra-files-data-selection-" + Guid.NewGuid().ToString("N"));
        private string Chosen => Path.Combine(Root, "chosen");
        private string HomePath => Path.Combine(Root, "home.json");
        private string MaterializationsPath => Path.Combine(Chosen, ".9to1-files", "materializations.json");
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
                "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), name);
            var authority = await Keep(Authority.CaptureCommitAuthorityAsync(Workspace.Actor, Workspace.Provider, () => true, Token).AsTask());
            var committed = await Keep(Workspace.Provider.CommitUploadedContentAsync(upload,
                [new(Folder, parent.CurrentRevisionId)], authority, Token)); Assert.True(committed.IsSuccess);
            await Keep(Workspace.Materializations.RegisterValidatedAsync(Path.Combine(_directory, name),
                new(upload.FileId, upload.RevisionId, upload.ContentHash, bytes.Length, upload.CommittedAt), SyncAvailability.AvailableOffline, Token));
            return upload;
        }
        internal async Task<(byte[] Home, byte[] Drive, byte[] Materializations)> BytesAsync() =>
            (await Keep(File.ReadAllBytesAsync(HomePath, Token)), await Keep(File.ReadAllBytesAsync(DrivePath, Token)),
                await Keep(File.ReadAllBytesAsync(MaterializationsPath, Token)));
        internal async Task<(FilesNativeBrowserPage Page, HostedItemMetadata Row)> SelectRowAsync(FilesUploadedContent upload)
        {
            var page = await Keep(Browser.ListAsync(Workspace.Actor, Folder.Value, token: Token,
                expectedStoreId: Workspace.Configuration.StoreId));
            return (page, Assert.Single(page.Items, row => row.Id == upload.FileId));
        }
        internal Task<ICanonicalOriginalDataFileSelection> SelectAsync(FilesNativeBrowserPage page, HostedItemMetadata row,
            Action<Action>? scope = null) => Keep(Browser.SelectOriginalDataFileWithinSourceAsync(page, row,
                Workspace.Actor, scope ?? (body => body()), Retain, Token));
        internal async Task AssertUnchangedAsync((byte[] Home, byte[] Drive, byte[] Materializations) before)
        {
            var after = await BytesAsync(); Assert.Equal(before.Home, after.Home);
            Assert.Equal(before.Drive, after.Drive); Assert.Equal(before.Materializations, after.Materializations);
        }
        internal void RememberKnown(Exception cause)
        {
            lock (_gate) Remember(cause);
            void Remember(Exception current)
            {
                if (!_observed.Add(current)) return;
                if (current.GetType() == typeof(AggregateException) && current is AggregateException { InnerExceptions.Count: > 0 } group)
                    foreach (var direct in group.InnerExceptions) Remember(direct);
            }
        }
        internal void VerifyOnlyObserved(Exception cause)
        {
            lock (_gate) if (_observed.Contains(cause)) return;
            if (cause.GetType() == typeof(AggregateException) && cause is AggregateException { InnerExceptions.Count: > 0 } group)
            { foreach (var direct in group.InnerExceptions) VerifyOnlyObserved(direct); return; }
            throw new InvalidOperationException("An unexpected Data selection or cleanup occurrence remains retained.", cause);
        }
        internal async Task JoinActualReadObserver(Task actual)
        {
            var records = (System.Collections.IEnumerable)typeof(FilesNativeBrowserService)
                .GetField("_dataSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Browser)!;
            object? retained = null;
            foreach (var record in records)
                if (ReferenceEquals(record!.GetType().GetField("Driver", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(record), actual))
                    retained = record;
            Assert.NotNull(retained);
            var observer = Assert.IsAssignableFrom<Task>(retained!.GetType().GetField("Observer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(retained));
            await Keep(observer); // Independently observe the SAME owner-issued completion, never a substitute.
        }
        internal async Task ObserveExpectedFailureAsync(Task actual, Type expectedType, string expectedMessage)
        {
            Exception? observed = null;
            try { await actual; } catch (Exception error) { observed = actual.Exception ?? error; }
            Assert.NotNull(observed); Assert.True(actual.IsFaulted);
            Verify(observed!); // Unknown IO/opaque empty groups are never accepted as an expected refusal.
            lock (_gate) Remember(observed!);
            void Verify(Exception error)
            {
                if (error.GetType() == typeof(AggregateException) && error is AggregateException group)
                {
                    Assert.NotEmpty(group.InnerExceptions);
                    foreach (var direct in group.InnerExceptions) Verify(direct);
                }
                else { Assert.Equal(expectedType, error.GetType()); Assert.Equal(expectedMessage, error.Message); Assert.Null(error.InnerException); }
            }
            void Remember(Exception error)
            {
                if (!_observed.Add(error)) return;
                if (error.GetType() == typeof(AggregateException) && error is AggregateException group)
                    foreach (var direct in group.InnerExceptions) Remember(direct);
            }
        }
        internal async Task DrainAsync(List<Exception> errors)
        {
            Task? selectionClose = null, providerClose = null, mappingClose = null;
            // Acquire each actual independent close even after a body/assertion fault.
            try { if (Browser is not null) selectionClose = Keep(Browser.CloseOriginalDataSelectionsAndDrainAsync()); }
            catch (Exception cause) { Collect(cause); }
            if (selectionClose is not null) await Join(selectionClose);
            try { if (Workspace is not null) providerClose = Keep(Workspace.Provider.CloseOriginalDataReadsAndDrainAsync()); }
            catch (Exception cause) { Collect(cause); }
            try { if (Workspace is not null) mappingClose = Keep(Workspace.Materializations.CloseOriginalDataReadsAndDrainAsync()); }
            catch (Exception cause) { Collect(cause); }
            if (providerClose is not null) await Join(providerClose);
            if (mappingClose is not null) await Join(mappingClose);
            Task[] originals; lock (_gate) originals = _raw.ToArray();
            foreach (var actual in originals) await Join(actual);
            if (errors.Count == 0 && Graph is not null)
                try { await Graph.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            async Task Join(Task actual)
            { try { await actual; } catch (Exception cause) { Collect(actual.Exception ?? cause); } }
            void Collect(Exception cause)
            { try { VerifyOnlyObserved(cause); } catch { errors.Add(cause); } }
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
        if (errors.Count != 0) throw new AggregateException("Actual Files Data-selection sources retained.", errors);
        // Keep the exclusive fixture and all failed originals; no deletion/reclamation.
    }
}
