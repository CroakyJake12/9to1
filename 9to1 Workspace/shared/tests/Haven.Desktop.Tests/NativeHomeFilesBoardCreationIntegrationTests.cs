using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Application.Compatibility;
using Haven.Desktop.Services;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.CompilerServices;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Exercises the actual Home Library source over the registered Linux Files owner.
/// These tests stop before Browse host publication; addresses/leases remain owner-issued.</summary>
public sealed class NativeHomeFilesBoardCreationIntegrationTests
{
    private sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
            : base(sourceFilePath, sourceLineNumber)
        { if (!OperatingSystem.IsLinux()) Skip = "Requires the actual Linux Home/Files board owners."; }
    }

    [LinuxFact]
    public async Task Declined_create_leaves_no_board_and_successful_receipt_reopens_the_same_revision_after_read_refusal()
    {
        await using var rig = await Rig.CreateAsync();
        var reads = 0;
        var phase = 0;
        async Task Review(HomePermissionAuthorization authorization, CancellationToken token)
        {
            var choice = phase == 0 || Interlocked.Increment(ref reads) == 2
                ? HomeApprovalChoice.Decline : HomeApprovalChoice.Accept;
            Assert.True((await rig.Trust.DecideAsync(authorization.RequestId, choice, cancellationToken: token)).Succeeded);
        }
        await rig.InitializeLibraryAsync(Review);
        var selected = await rig.SelectBoardsFolderAsync();
        var bytes = BoardBytes(Guid.NewGuid(), "Declined before create");

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => rig.Keep(
            rig.Library.CreateInSelectedFolderAsync(selected, "Refused board", bytes, rig.Token)));
        Assert.DoesNotContain("FILES_BOARD_CREATED", refused.ToString(), StringComparison.Ordinal);
        Assert.Empty(await rig.ListBoardsAsync());

        // A new action is reviewed independently. The first review accepts create and the
        // next review declines only the separate files.board.read needed to open its receipt.
        phase = 1;
        reads = 0;
        selected = await rig.SelectBoardsFolderAsync();
        var created = await rig.Keep(rig.Library.CreateInSelectedFolderAsync(
            selected, "Created board", BoardBytes(Guid.NewGuid(), "Created then read-refused"), rig.Token));

        Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.Committed, created.State);
        var receipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(created.Receipt);
        Assert.Null(created.PreparedAddress);
        Assert.Equal("CreatedOpenFailed", created.OpenFailureCode);
        Assert.Equal(2, reads);
        var rows = await rig.ListBoardsAsync();
        var row = Assert.Single(rows);
        Assert.Equal(receipt.FileId, row.Id.Value);
        Assert.Equal(receipt.FilesCasRevisionId, row.CurrentRevisionId!.Value.Value);
        Assert.Equal(receipt.DisplayName, row.Name);

        // Opening the exact retained receipt performs a new currentness check and fresh READ
        // review. It prepares the same existing file rather than issuing another create.
        var address = await rig.Keep(rig.Library.PrepareCreatedBoardOpenAsync(receipt, rig.Token));
        Assert.Equal(3, reads);
        var parsed = BoardsBrowseAppContentAddress.Parse(address);
        Assert.True(parsed.IsFilesDocument);
        Assert.True(parsed.Writable);
        Assert.Equal(receipt.FilesStoreId, parsed.StoreId);
        Assert.Equal(receipt.ParentFolderId, parsed.ParentFolderId);
        Assert.Equal(receipt.FileId, parsed.FileId);
        Assert.Equal(receipt.FilesCasRevisionId, parsed.RevisionId);
        var lease = rig.Writes.TakePrepared(parsed.ReadToken!.Value, parsed.StoreId!.Value,
            parsed.ParentFolderId!.Value, parsed.FileId!.Value, parsed.RevisionId!.Value);
        await using (lease)
        {
            Assert.Equal(receipt.DocumentId, lease.DocumentId);
            Assert.Equal(receipt.FileId, lease.FileId);
            Assert.Equal(receipt.FilesCasRevisionId, lease.RevisionId);
            var document = HavenBoardDocumentCodec.Deserialize(lease.Bytes);
            Assert.Equal(receipt.DocumentId, document.DocumentId);
            Assert.Equal("Created then read-refused", document.RichNotes!.Title);
            await rig.Keep(lease.DemandCurrentAsync(rig.Token));

            var originalRevision = receipt.FilesCasRevisionId;
            var originalLeaves = Directory.GetFiles(rig.BoardsDirectory, ".9to1-board-*.content");
            var originalLeaf = Assert.Single(originalLeaves);
            var originalPhysicalBytes = await File.ReadAllBytesAsync(originalLeaf, rig.Token);
            var editedBytes = BoardBytes(receipt.DocumentId, "Saved through Home reviewed write");
            var saved = await rig.Keep(lease.SaveAsync(lease.SourceId, originalRevision.ToString("D"),
                editedBytes, rig.Token).AsTask());
            Assert.True(saved.Committed, saved.Message);
            Assert.NotNull(saved.NewRevision);
            var savedRevision = Guid.Parse(saved.NewRevision!);
            Assert.NotEqual(originalRevision, savedRevision);
            Assert.Equal(savedRevision, lease.RevisionId);
            Assert.Equal(originalPhysicalBytes, await File.ReadAllBytesAsync(originalLeaf, rig.Token));
            Assert.Equal(2, Directory.GetFiles(rig.BoardsDirectory, ".9to1-board-*.content").Length);
            Assert.Equal(4, reads); // create, declined READ, accepted READ, reviewed WRITE.
        }
        var currentReference = Assert.Single(await rig.Keep(rig.Library.SearchAsync(
            new HomeLibraryQuery(OwnerApp: "files", ArtifactType: "9to1 board document"), rig.Token)));
        var readToken = await rig.Keep(rig.Library.PrepareOriginalBoardReadAsync(currentReference.OpenLink, rig.Token))
            ?? throw new InvalidOperationException("The current Home Library board row did not prepare a read.");
        var currentRow = Assert.Single(await rig.ListBoardsAsync());
        var reopened = rig.Read.TakePrepared(readToken, receipt.FilesStoreId, receipt.ParentFolderId,
            receipt.FileId, currentRow.CurrentRevisionId!.Value.Value);
        await using (reopened)
        {
            Assert.Equal(receipt.FileId, reopened.FileId);
            Assert.Equal(currentRow.CurrentRevisionId!.Value.Value, reopened.RevisionId);
            var updatedDocument = HavenBoardDocumentCodec.Deserialize(reopened.Bytes);
            Assert.Equal(receipt.DocumentId, updatedDocument.DocumentId);
            Assert.Equal("Saved through Home reviewed write", updatedDocument.RichNotes!.Title);
            await rig.Keep(reopened.DemandCurrentAsync(rig.Token));
        }
    }

    [LinuxFact]
    public async Task Home_audit_retry_uses_the_same_attempt_and_receipt_and_opens_only_after_terminal_settlement()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Home.FailNextBoardCreateAudits(2); // Initial settlement plus the source's one bounded retry.
        var approvals = 0;
        async Task Review(HomePermissionAuthorization authorization, CancellationToken token)
        {
            approvals++;
            Assert.True((await rig.Trust.DecideAsync(authorization.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
        }
        await rig.InitializeLibraryAsync(Review);
        var selected = await rig.SelectBoardsFolderAsync();
        var created = await rig.Keep(rig.Library.CreateInSelectedFolderAsync(
            selected, "Audit retry board", BoardBytes(Guid.NewGuid(), "Audit retry"), rig.Token));

        Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.HomeAuditPending, created.State);
        var attempt = Assert.IsType<FilesOriginalBoardDocumentCreateAttempt>(created.SettlementAttempt);
        var receipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(created.Receipt);
        Assert.Null(created.PreparedAddress);
        Assert.Single(await rig.ListBoardsAsync());

        var homeBeforeObserve = await File.ReadAllBytesAsync(rig.HomePath, rig.Token);
        var driveBeforeObserve = await File.ReadAllBytesAsync(rig.DrivePath, rig.Token);
        var observed = await rig.Keep(rig.Library.ObserveCreateSettlementAsync(attempt, rig.Token));
        Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.HomeAuditPending, observed.State);
        Assert.Same(receipt, observed.Receipt);
        Assert.Null(observed.PreparedAddress);
        Assert.Equal(homeBeforeObserve, await File.ReadAllBytesAsync(rig.HomePath, rig.Token));
        Assert.Equal(driveBeforeObserve, await File.ReadAllBytesAsync(rig.DrivePath, rig.Token));
        Assert.Single(await rig.ListBoardsAsync());

        var settled = await rig.Keep(rig.Library.RetryCreateSettlementAndPrepareAsync(attempt, rig.Token));
        Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.Committed, settled.State);
        Assert.Same(receipt, settled.Receipt);
        Assert.NotNull(settled.PreparedAddress);
        Assert.Equal(2, rig.Home.BoardCreateAuditFailureCount);
        Assert.Equal(2, approvals); // One create review and one fresh read review; retries did not create again.
        Assert.Single(await rig.ListBoardsAsync());
        var parsed = BoardsBrowseAppContentAddress.Parse(settled.PreparedAddress!);
        Assert.Equal(receipt.FileId, parsed.FileId);
        Assert.Equal(receipt.FilesCasRevisionId, parsed.RevisionId);
        var lease = rig.Writes.TakePrepared(parsed.ReadToken!.Value, parsed.StoreId!.Value,
            parsed.ParentFolderId!.Value, parsed.FileId!.Value, parsed.RevisionId!.Value);
        await using (lease)
        {
            Assert.Equal(receipt.DocumentId, lease.DocumentId);
            await rig.Keep(lease.DemandCurrentAsync(rig.Token));
        }
    }

    private static byte[] BoardBytes(Guid documentId, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return JsonSerializer.SerializeToUtf8Bytes(new HavenBoardDocument(HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion, documentId, now, now, null, HavenRichNotes.Create(title)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-files-board-create-" + Guid.NewGuid().ToString("N"));
        private readonly string _chosen;
        private readonly object _gate = new();
        private readonly HashSet<Task> _retained = new(ReferenceEqualityComparer.Instance);
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(45));
        private ServiceProvider? _graph;
        private FilesOriginalBoardDocumentPhysicalOwner? _physical;
        private FilesNativeBrowserService.BoardDocumentCreateOwner? _creator;
        private FilesNativeBrowserService.BoardDocumentWriteOwner? _writer;
        private NativeHomeFilesBoardReadSource? _read;
        private NativeHomeFilesBoardWriteSource? _writes;
        private NativeHomeFilesLibrarySource? _library;

        private Rig()
        { _chosen = Path.Combine(_root, "chosen"); HomePath = Path.Combine(_root, "home.json"); DrivePath = Path.Combine(_chosen, ".9to1-files", "drive.json"); }

        internal string HomePath { get; }
        internal string DrivePath { get; }
        internal string BoardsDirectory { get; private set; } = string.Empty;
        internal CancellationToken Token => _deadline.Token;
        internal NativeFilesWorkspace Workspace { get; private set; } = null!;
        internal FilesNativeBrowserService Browser { get; private set; } = null!;
        internal FilesOriginalBoardDocumentPhysicalOwner Physical => _physical ?? throw new InvalidOperationException();
        internal HomePermissionTrustService Trust { get; private set; } = null!;
        internal FailBoardCreateAuditStore Home { get; private set; } = null!;
        internal NativeHomeFilesBoardWriteSource Writes => _writes ?? throw new InvalidOperationException();
        internal NativeHomeFilesBoardReadSource Read => _read ?? throw new InvalidOperationException();
        internal NativeHomeFilesLibrarySource Library => _library ?? throw new InvalidOperationException();

        internal void Retain(Task task) { lock (_gate) _retained.Add(task); }
        internal Task<T> Keep<T>(Task<T> task) { Retain(task); return task; }
        internal Task Keep(Task task) { Retain(task); return task; }

        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                Directory.CreateDirectory(rig._chosen);
                var homeStore = new FileHomeCoreStateStore(rig.HomePath);
                rig.Home = new FailBoardCreateAuditStore(homeStore);
                var profiles = new HomeLocalProfileIdentity(rig.Home, new OperatingSystemPrincipalSource());
                var services = new ServiceCollection();
                services.AddSingleton<IHomeCoreStateStore>(rig.Home);
                services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
                var policy = new FilesNativeBrowserActionPolicySource();
                rig.Trust = new HomePermissionTrustService(rig.Home, policy.TryGet);
                services.AddSingleton(rig.Trust);
                services.AddSingleton<IHomeActionPolicySource>(policy);
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
                services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
                services.AddSingleton<ResourceAuthorizationService>();
                services.AddSingleton<HomeResourceOperationBroker>();
                services.AddFilesNativeHost();
                services.RemoveAll<FilesNativeBrowserService>();
                services.AddSingleton(provider => new FilesNativeBrowserService(
                    provider.GetRequiredService<NativeFilesWorkspaceAuthority>(),
                    provider.GetRequiredService<IAuthenticatedResourceActorSource>(),
                    provider.GetRequiredService<ResourceAuthorizationService>(),
                    provider.GetRequiredService<ICompatibilityPackageContentSource>(),
                    provider.GetRequiredService<FilesOriginalChildFolderReadSource>(),
                    provider.GetRequiredService<HomeResourceOperationBroker>()));
                rig._graph = services.BuildServiceProvider();
                var configured = await rig.Keep(rig._graph.GetRequiredService<NativeFilesWorkspaceService>()
                    .ConfigureNewAsync(rig._chosen, rig._graph.GetRequiredService<HomeLocalStoreOwnership>(), rig.Token));
                var authority = rig._graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
                rig.Workspace = await rig.Keep(authority.GetCurrentAsync(configured.Configuration.StoreId, rig.Token))
                    ?? throw new InvalidOperationException("The configured original Files workspace is unavailable.");
                Assert.Equal(configured.Actor, rig.Workspace.Actor);
                Assert.Equal(configured.Configuration.StoreId, rig.Workspace.Configuration.StoreId);
                rig.Browser = rig._graph.GetRequiredService<FilesNativeBrowserService>();
                rig._physical = new FilesOriginalBoardDocumentPhysicalOwner();
                rig._creator = rig.Browser.CreateOriginalBoardDocumentCreateOwner(rig.Physical);
                rig._writer = rig.Browser.CreateOriginalBoardDocumentWriteOwner(rig.Physical);
                rig._read = new NativeHomeFilesBoardReadSource(rig.Browser, rig.Workspace.Actor);
                rig._writes = new NativeHomeFilesBoardWriteSource(rig.Browser, rig._writer, rig.Workspace.Actor);
                rig.BoardsDirectory = (await rig.Keep(rig.Workspace.Directories.ResolveProfileAsync(
                    Guid.Parse(rig.Workspace.Actor.ProfileId), "boards", rig.Token))).Value!.DirectoryPath;
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }

        internal async Task InitializeLibraryAsync(
            Func<HomePermissionAuthorization, CancellationToken, Task> presentReview)
        {
            var graph = _graph ?? throw new InvalidOperationException("The Home/Files test graph has not been created.");
            var profiles = graph.GetRequiredService<IAuthenticatedResourceActorSource>();
            async Task<bool> Current(CancellationToken token) => await profiles.GetCurrentAsync(token) == Workspace.Actor;
            _library = new NativeHomeFilesLibrarySource(Browser, Workspace.Actor,
                graph.GetRequiredService<IHomeCoreStateStore>(), Current, body => body(), () => true,
                _read, _writes, presentReview, body => body(), Retain, _creator);
        }

        internal async Task<HomeDeepLink> SelectBoardsFolderAsync()
        {
            var rows = await Keep(Library.SearchAsync(new HomeLibraryQuery(OwnerApp: "files", ArtifactType: "Files folder"), Token));
            var id = Workspace.Configuration.StoreId.ToString("D") + "/" +
                Workspace.Configuration.AppFolders["boards"].Value.ToString("D");
            var selected = Assert.Single(rows, row => row.ArtifactId == id);
            Assert.Equal("files.folder", selected.OpenLink.TargetRouteId);
            Assert.Same(selected.OpenLink, await Keep(Library.SelectBoardCreateFolderAsync(selected.OpenLink, Token)));
            return selected.OpenLink;
        }

        internal async Task<IReadOnlyList<HostedItemMetadata>> ListBoardsAsync()
        {
            var page = await Keep(Browser.ListAsync(Workspace.Actor, Workspace.Configuration.AppFolders["boards"].Value,
                token: Token, expectedStoreId: Workspace.Configuration.StoreId));
            return page.Items.Where(FilesNativeBrowserService.IsBoardDocumentCandidate).ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            try { if (_library is not null) await _library.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            try { if (_read is not null) await _read.DrainPreparedAsync(); } catch (Exception error) { failures.Add(error); }
            try { if (_writes is not null) await _writes.DrainPreparedAsync(); } catch (Exception error) { failures.Add(error); }
            try { if (_writer is not null) await _writer.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            try { if (_creator is not null) await _creator.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            try { if (_physical is not null) await _physical.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            Task[] retained; lock (_gate) retained = _retained.ToArray();
            foreach (var task in retained) try { await task; } catch { /* Expected refusal tasks are asserted by their cases. */ }
            if (_graph is not null) try { await _graph.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            _deadline.Dispose();
            if (failures.Count != 0) throw new AggregateException("The original Home Files board creation owner did not drain cleanly.", failures);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FailBoardCreateAuditStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        private int _remainingFailures;
        private int _failureCount;
        internal int BoardCreateAuditFailureCount => Volatile.Read(ref _failureCount);
        internal void FailNextBoardCreateAudits(int count) => Interlocked.Exchange(ref _remainingFailures, count);
        public Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default) => inner.ReadAsync(cancellationToken);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expectedRecordRevision,
            AuthenticatedResourceActor expectedActor, IHomeStateCommitActorGuard guard, CancellationToken cancellationToken = default) =>
            inner.WriteGuardedAsync(record, expectedRecordRevision, expectedActor, guard, cancellationToken);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision,
            CancellationToken cancellationToken = default)
        {
            if (record.RecordType == "home.permissions-trust" &&
                record.Payload.GetRawText().Contains("FILES_BOARD_CREATED", StringComparison.Ordinal))
            {
                while (true)
                {
                    var remaining = Volatile.Read(ref _remainingFailures);
                    if (remaining == 0) break;
                    if (Interlocked.CompareExchange(ref _remainingFailures, remaining - 1, remaining) != remaining) continue;
                    Interlocked.Increment(ref _failureCount);
                    return Task.FromResult(HomeStateWriteResult.Failed(new(HomeCoreErrorCode.HomeServiceUnavailable,
                        "Controlled Home board-create audit failure.", record.RecordId, true)));
                }
            }
            return inner.WriteAsync(record, expectedRecordRevision, cancellationToken);
        }
    }
}
