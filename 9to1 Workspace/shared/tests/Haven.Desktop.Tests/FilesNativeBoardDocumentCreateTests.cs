using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Application.Compatibility;
using Haven.Desktop.Services;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class FilesNativeBoardDocumentCreateTests
{
    private sealed class LinuxFactAttribute : FactAttribute
    { public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires the actual Linux create-only Files publisher."; } }

    [LinuxFact]
    public async Task Reviewed_new_board_publishes_and_reopens_the_exact_current_files_revision()
    {
        await using var rig = await Rig.CreateAsync();
        var source = await rig.OpenBoardFolderAsync();
        var before = await rig.ReadBytesAsync();
        var creator = rig.Creator = rig.Browser.CreateOriginalBoardDocumentCreateOwner(rig.Physical);
        Assert.True(await rig.Keep(creator.CanCreateInFolderWithinSourceAsync(source.Page, source.Folder,
            rig.Workspace.Actor, () => true, body => body(), rig.Retain, rig.Token)));
        var unrelatedFolder = Assert.Single(source.Page.Items,
            item => item.Id == rig.Workspace.Configuration.AppFolders["picture"]);
        Assert.False(await rig.Keep(creator.CanCreateInFolderWithinSourceAsync(source.Page, unrelatedFolder,
            rig.Workspace.Actor, () => true, body => body(), rig.Retain, rig.Token)));
        var bytes = BoardBytes(Guid.NewGuid(), "Created and reopened");
        var approvals = new List<string>();
        async Task Review(HomePermissionAuthorization request, CancellationToken token)
        {
            approvals.Add(request.RequestId);
            Assert.True((await rig.Trust.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
        }

        var receipt = await rig.Keep(creator.CreateBoardDocumentWithinSourceAsync(source.Page, source.Folder,
            rig.Workspace.Actor, "Created board", bytes, () => true, Review, "home-board-create-test",
            body => body(), rig.Retain, rig.Token));

        Assert.Single(approvals);
        Assert.True(creator.IsIssuedCreateReceipt(receipt));
        Assert.Equal(rig.Workspace.Configuration.StoreId, receipt.FilesStoreId);
        Assert.Equal(rig.Workspace.Configuration.AppFolders["boards"].Value, receipt.ParentFolderId);
        Assert.NotEqual(Guid.Empty, receipt.FileId);
        Assert.Equal(receipt.FileRevisionId, receipt.FilesCasRevisionId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), receipt.ContentSha256);
        Assert.Equal(bytes.LongLength, receipt.SizeBytes);
        await rig.Keep(creator.RevalidateCreatedBoardWithinSourceAsync(receipt, body => body(), rig.Retain, rig.Token));

        var page = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, receipt.ParentFolderId,
            token: rig.Token, expectedStoreId: receipt.FilesStoreId));
        var row = Assert.Single(page.Items, item => item.Id.Value == receipt.FileId);
        Assert.Equal(HostedItemKind.File, row.Kind);
        Assert.Equal(receipt.DisplayName, row.Name);
        Assert.Equal(receipt.FilesCasRevisionId, row.CurrentRevisionId!.Value.Value);
        var reopened = await rig.Keep(rig.Browser.ReadOriginalBoardDocumentWithinSourceAsync(page, row,
            rig.Workspace.Actor, () => true, body => body(), rig.Retain, rig.Token));
        Assert.Equal(receipt.FileId, reopened.FileId);
        Assert.Equal(receipt.FileRevisionId, reopened.RevisionId);
        Assert.Equal(receipt.DocumentId, HavenBoardDocumentCodec.Deserialize(reopened.Bytes).DocumentId);
        Assert.Equal(bytes, reopened.Bytes);
        var after = await rig.ReadBytesAsync();
        Assert.NotEqual(before.Drive, after.Drive);
        Assert.Contains("FILES_BOARD_CREATED", Encoding.UTF8.GetString(after.Home), StringComparison.Ordinal);
    }

    [LinuxFact]
    public async Task Stale_actor_and_folder_selection_are_refused_without_a_new_board_file()
    {
        await using var rig = await Rig.CreateAsync();
        var source = await rig.OpenBoardFolderAsync();
        var creator = rig.Creator = rig.Browser.CreateOriginalBoardDocumentCreateOwner(rig.Physical);
        var bytes = BoardBytes(Guid.NewGuid(), "Must not be created");
        var before = await rig.ReadBytesAsync();
        var staleActor = rig.Workspace.Actor with { AuthenticationRevision = rig.Workspace.Actor.AuthenticationRevision + ":stale" };
        var wrongActor = Record.Exception(() => _ = creator.CreateBoardDocumentWithinSourceAsync(
            source.Page, source.Folder, staleActor, "Stale actor", bytes, () => true,
            (_, _) => Task.CompletedTask, "stale-actor", body => body(), rig.Retain, rig.Token));
        Assert.NotNull(wrongActor);
        AssertUnauthorized(wrongActor!);

        var retired = false;
        async Task RevokeAfterReview(HomePermissionAuthorization request, CancellationToken token)
        {
            Assert.True((await rig.Trust.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
            retired = true;
        }
        var retiredTask = rig.Keep(creator.CreateBoardDocumentWithinSourceAsync(
            source.Page, source.Folder, rig.Workspace.Actor, "Retired source", bytes, () => !retired,
            RevokeAfterReview, "retired-source", body => body(), rig.Retain, rig.Token));
        AssertUnauthorized(await Assert.ThrowsAnyAsync<Exception>(() => retiredTask));

        var page = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor,
            rig.Workspace.Configuration.AppFolders["boards"].Value, token: rig.Token,
            expectedStoreId: rig.Workspace.Configuration.StoreId));
        Assert.Empty(page.Items);
        Assert.Equal(before.Drive, (await rig.ReadBytesAsync()).Drive);
    }

    [LinuxFact]
    public async Task Files_page_advanced_during_Home_review_cannot_publish_a_board_into_a_stale_folder_selection()
    {
        await using var rig = await Rig.CreateAsync();
        var source = await rig.OpenBoardFolderAsync();
        var creator = rig.Creator = rig.Browser.CreateOriginalBoardDocumentCreateOwner(rig.Physical);
        var bytes = BoardBytes(Guid.NewGuid(), "Stale page must not publish");
        async Task Review(HomePermissionAuthorization request, CancellationToken token)
        {
            await rig.SeedFileAsync(source.Folder.Id, "unrelated-during-review.txt");
            Assert.True((await rig.Trust.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
        }
        var operation = rig.Keep(creator.CreateBoardDocumentWithinSourceAsync(source.Page, source.Folder,
            rig.Workspace.Actor, "Stale page", bytes, () => true, Review, "stale-files-page",
            body => body(), rig.Retain, rig.Token));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => operation);
        Assert.Contains("folder changed", failure.ToString(), StringComparison.OrdinalIgnoreCase);
        var refreshed = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor,
            rig.Workspace.Configuration.AppFolders["boards"].Value, token: rig.Token,
            expectedStoreId: rig.Workspace.Configuration.StoreId));
        Assert.Contains(refreshed.Items, item => item.Name == "unrelated-during-review.txt");
        Assert.DoesNotContain(refreshed.Items, item => item.Name == "Stale page.9to1board");
    }

    [LinuxFact]
    public async Task Failed_create_audit_is_retried_by_exact_attempt_without_publishing_a_second_file()
    {
        await using var rig = await Rig.CreateAsync();
        var source = await rig.OpenBoardFolderAsync();
        var creator = rig.Creator = rig.Browser.CreateOriginalBoardDocumentCreateOwner(rig.Physical);
        var bytes = BoardBytes(Guid.NewGuid(), "Audit retry");
        var approvals = 0;
        async Task Review(HomePermissionAuthorization request, CancellationToken token)
        {
            approvals++;
            Assert.True((await rig.Trust.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: token)).Succeeded);
        }
        rig.FailingHomeStore.FailNextBoardCreateAudit = true;

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => rig.Keep(
            creator.CreateBoardDocumentWithinSourceAsync(source.Page, source.Folder, rig.Workspace.Actor,
                "Audit retry", bytes, () => true, Review, "home-board-create-audit-retry",
                body => body(), rig.Retain, rig.Token)));
        var pending = FindSettlement(failure);

        Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.HomeAuditPending, pending.State);
        var exactReceipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(pending.Receipt);
        var current = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, exactReceipt.ParentFolderId,
            token: rig.Token, expectedStoreId: exactReceipt.FilesStoreId));
        Assert.Single(current.Items, item => item.Id.Value == exactReceipt.FileId);

        var settled = await rig.Keep(creator.RetryCreateSettlementWithinSourceAsync(pending.Attempt,
            body => body(), rig.Retain, rig.Token));

        Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.Committed, settled.State);
        Assert.Same(exactReceipt, settled.Receipt);
        Assert.True(creator.IsIssuedCreateReceipt(exactReceipt));
        Assert.Equal(1, approvals);
        Assert.Equal(1, rig.FailingHomeStore.BoardCreateAuditFailureCount);
        await rig.Keep(creator.RevalidateCreatedBoardWithinSourceAsync(exactReceipt,
            body => body(), rig.Retain, rig.Token));
        var final = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, exactReceipt.ParentFolderId,
            token: rig.Token, expectedStoreId: exactReceipt.FilesStoreId));
        Assert.Single(final.Items, item => item.Id.Value == exactReceipt.FileId);
        Assert.Single(final.Items, item => item.Name == exactReceipt.DisplayName);
    }

    private static byte[] BoardBytes(Guid id, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return JsonSerializer.SerializeToUtf8Bytes(new HavenBoardDocument(HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion, id, now, now, null, HavenRichNotes.Create(title)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void AssertUnauthorized(Exception cause)
    {
        if (cause is AggregateException aggregate)
        {
            Assert.NotEmpty(aggregate.InnerExceptions);
            foreach (var inner in aggregate.InnerExceptions) AssertUnauthorized(inner);
            return;
        }
        Assert.IsType<UnauthorizedAccessException>(cause);
    }

    private static FilesOriginalBoardDocumentCreateSettlementException FindSettlement(Exception cause)
    {
        if (cause is FilesOriginalBoardDocumentCreateSettlementException settlement) return settlement;
        if (cause is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                try { return FindSettlement(inner); } catch (InvalidOperationException) { }
        throw new InvalidOperationException("The exact typed Files board-create settlement outcome was not preserved.", cause);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "files-board-create-" + Guid.NewGuid().ToString("N"));
        private readonly string _homePath;
        private readonly string _chosen;
        private readonly string _drivePath;
        private readonly object _gate = new();
        private readonly HashSet<Task> _retained = new(ReferenceEqualityComparer.Instance);
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(30));
        private ServiceProvider? _graph;
        private string _boardsDirectory = string.Empty;
        private Rig()
        {
            _homePath = Path.Combine(_root, "home.json"); _chosen = Path.Combine(_root, "chosen");
            _drivePath = Path.Combine(_chosen, ".9to1-files", "drive.json");
        }
        internal CancellationToken Token => _deadline.Token;
        internal NativeFilesWorkspace Workspace { get; private set; } = null!;
        internal FilesNativeBrowserService Browser { get; private set; } = null!;
        internal FilesOriginalBoardDocumentPhysicalOwner Physical { get; } = new();
        internal HomePermissionTrustService Trust { get; private set; } = null!;
        internal FailBoardCreateAuditStore FailingHomeStore { get; private set; } = null!;
        internal void Retain(Task actual) { lock (_gate) _retained.Add(actual); }
        internal Task<T> Keep<T>(Task<T> actual) { Retain(actual); return actual; }
        internal async Task<(byte[] Home, byte[] Drive)> ReadBytesAsync() =>
            (await File.ReadAllBytesAsync(_homePath, Token), await File.ReadAllBytesAsync(_drivePath, Token));

        internal async Task SeedFileAsync(HostedItemId parent, string name)
        {
            var bytes = Encoding.UTF8.GetBytes("seed");
            await File.WriteAllBytesAsync(Path.Combine(_boardsDirectory, name), bytes, Token);
            var currentParent = (await Keep(Workspace.Provider.GetForOriginalStoreAsync(
                Workspace.Configuration.StoreId, parent, Token))).Value!;
            var content = new FilesUploadedContent(HostedItemId.New(), parent, name, "text/plain",
                new(Guid.NewGuid()), null, Workspace.Actor.ActorId, DateTimeOffset.UtcNow, bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)), name);
            var authority = await Keep(_graph!.GetRequiredService<NativeFilesWorkspaceAuthority>()
                .CaptureCommitAuthorityAsync(Workspace.Actor, Workspace.Provider, () => true, Token).AsTask());
            var result = await Keep(Workspace.Provider.CommitUploadedContentAsync(content,
                [new(parent, currentParent.CurrentRevisionId)], Workspace.Configuration.StoreId, authority, Token));
            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        internal async Task<(FilesNativeBrowserPage Page, HostedItemMetadata Folder)> OpenBoardFolderAsync()
        {
            // Root app folders are ordinary canonical rows in the same Files page issuer.
            var page = await Keep(Browser.ListAsync(Workspace.Actor, token: Token,
                expectedStoreId: Workspace.Configuration.StoreId));
            var folder = Assert.Single(page.Items, row => row.Id == Workspace.Configuration.AppFolders["boards"]);
            return (page, folder);
        }

        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                Directory.CreateDirectory(rig._chosen);
                var home = new FileHomeCoreStateStore(rig._homePath);
                rig.FailingHomeStore = new FailBoardCreateAuditStore(home);
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var services = new ServiceCollection();
                services.AddSingleton<IHomeCoreStateStore>(rig.FailingHomeStore); services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
                var policy = new FilesNativeBrowserActionPolicySource();
                rig.Trust = new HomePermissionTrustService(rig.FailingHomeStore, policy.TryGet);
                services.AddSingleton(rig.Trust); services.AddSingleton<IHomeActionPolicySource>(policy);
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
                services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
                services.AddSingleton<ResourceAuthorizationService>(); services.AddSingleton<HomeResourceOperationBroker>();
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
                var configured = await rig.Keep(rig._graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(
                    rig._chosen, rig._graph.GetRequiredService<HomeLocalStoreOwnership>(), rig.Token));
                var authority = rig._graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
                rig.Workspace = await rig.Keep(authority.GetCurrentAsync(configured.Configuration.StoreId, rig.Token))
                    ?? throw new InvalidOperationException("The original Files workspace did not remain current.");
                rig.Browser = rig._graph.GetRequiredService<FilesNativeBrowserService>();
                rig._boardsDirectory = (await rig.Keep(rig.Workspace.Directories.ResolveProfileAsync(
                    Guid.Parse(rig.Workspace.Actor.ProfileId), "boards", rig.Token))).Value!.DirectoryPath;
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            Task[] tasks; lock (_gate) tasks = _retained.ToArray();
            foreach (var task in tasks)
                try { await task; } catch { /* Assertions consume expected operation failures. */ }
            try { await Physical.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count == 0 && _graph is not null)
                try { await _graph.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            _deadline.Dispose();
            if (errors.Count != 0) throw new AggregateException("The board-create Files source did not drain cleanly.", errors);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        internal FilesNativeBrowserService.BoardDocumentCreateOwner? Creator { get; set; }
    }

    private sealed class FailBoardCreateAuditStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        private int _failNextBoardCreateAudit;
        private int _boardCreateAuditFailureCount;
        internal bool FailNextBoardCreateAudit
        {
            set => Interlocked.Exchange(ref _failNextBoardCreateAudit, value ? 1 : 0);
        }
        internal int BoardCreateAuditFailureCount => Volatile.Read(ref _boardCreateAuditFailureCount);
        public Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default) => inner.ReadAsync(cancellationToken);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expectedRecordRevision,
            AuthenticatedResourceActor expectedActor, IHomeStateCommitActorGuard guard,
            CancellationToken cancellationToken = default)
            => inner.WriteGuardedAsync(record, expectedRecordRevision, expectedActor, guard, cancellationToken);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision,
            CancellationToken cancellationToken = default)
        {
            if (record.RecordType == "home.permissions-trust" &&
                record.Payload.GetRawText().Contains("FILES_BOARD_CREATED", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _failNextBoardCreateAudit, 0) != 0)
            {
                Interlocked.Increment(ref _boardCreateAuditFailureCount);
                return Task.FromResult(HomeStateWriteResult.Failed(new(HomeCoreErrorCode.HomeServiceUnavailable,
                    "Controlled one-shot board create audit failure.", record.RecordId, true)));
            }
            return inner.WriteAsync(record, expectedRecordRevision, cancellationToken);
        }
    }
}
