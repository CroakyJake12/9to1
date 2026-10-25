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
    public async Task Declined_create_leaves_no_board_and_successful_receipt_saves_and_reopens_the_same_file()
    {
        await WithRigAsync(async rig =>
        {
            var reviews = 0;
            var declineCreate = true;
            async Task Review(HomePermissionAuthorization authorization, CancellationToken token)
            {
                Interlocked.Increment(ref reviews);
                var choice = declineCreate ? HomeApprovalChoice.Decline : HomeApprovalChoice.Accept;
                Assert.True((await rig.Trust.DecideAsync(authorization.RequestId, choice, cancellationToken: token)).Succeeded);
            }
            await rig.InitializeLibraryAsync(Review);
            var selected = await rig.SelectBoardsFolderAsync();
            var bytes = BoardBytes(Guid.NewGuid(), "Declined before create");

            var refused = await Assert.ThrowsAnyAsync<Exception>(() => rig.Keep(
                rig.Library.CreateInSelectedFolderAsync(selected, "Refused board", bytes, rig.Token)));
            Assert.DoesNotContain("FILES_BOARD_CREATED", refused.ToString(), StringComparison.Ordinal);
            Assert.True(rig.Library.IsExpectedRefusal(refused));
            Assert.False(rig.Library.IsExpectedRefusal(new UnauthorizedAccessException(refused.Message)));
            Assert.Empty(await rig.ListBoardsAsync());

            // A new create is reviewed independently. Fresh READ authorization uses the
            // actual Files resource owner; it does not invoke a write-review callback.
            declineCreate = false;
            reviews = 0;
            selected = await rig.SelectBoardsFolderAsync();
            var created = await rig.Keep(rig.Library.CreateInSelectedFolderAsync(
                selected, "Created board", BoardBytes(Guid.NewGuid(), "Created after review decline"), rig.Token));

            Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.Committed, created.State);
            var receipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(created.Receipt);
            Assert.NotNull(created.PreparedAddress);
            Assert.Null(created.OpenFailureCode);
            Assert.Equal(1, reviews);
            var rows = await rig.ListBoardsAsync();
            var row = Assert.Single(rows);
            Assert.Equal(receipt.FileId, row.Id.Value);
            Assert.Equal(receipt.FilesCasRevisionId, row.CurrentRevisionId!.Value.Value);
            Assert.Equal(receipt.DisplayName, row.Name);

            // Open only the exact address prepared by the actual current Files READ owner.
            var address = created.PreparedAddress!;
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
                Assert.Equal("Created after review decline", document.RichNotes!.Title);
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
                Assert.Equal(2, reviews); // One create review and one explicit Save WRITE review.
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
        });
    }

    [LinuxFact]
    public async Task Lost_create_audit_acknowledgement_reuses_the_same_receipt_without_a_second_create()
    {
        await WithRigAsync(async rig =>
        {
            rig.FailNextBoardCreateAudits(1); // The actual Home record is persisted before its acknowledgement is lost.
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

            Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.Committed, created.State);
            var receipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(created.Receipt);
            Assert.NotNull(created.PreparedAddress);
            Assert.Equal(1, rig.BoardCreateAuditFailureCount);
            Assert.Equal(1, approvals); // READ uses fresh ResourceAuthorization, without an invented write-review callback.
            Assert.Single(await rig.ListBoardsAsync());
            var parsed = BoardsBrowseAppContentAddress.Parse(created.PreparedAddress!);
            Assert.Equal(receipt.FileId, parsed.FileId);
            Assert.Equal(receipt.FilesCasRevisionId, parsed.RevisionId);
            var lease = rig.Writes.TakePrepared(parsed.ReadToken!.Value, parsed.StoreId!.Value,
                parsed.ParentFolderId!.Value, parsed.FileId!.Value, parsed.RevisionId!.Value);
            await using (lease)
            {
                Assert.Equal(receipt.DocumentId, lease.DocumentId);
                await rig.Keep(lease.DemandCurrentAsync(rig.Token));
            }
        });
    }

    private static async Task WithRigAsync(Func<Rig, Task> body, Action<Exception>? assertCleanupFailure = null)
    {
        var rig = await Rig.CreateAsync();
        var failures = new List<Exception>();
        try { await body(rig); } catch (Exception error) { failures.Add(error); }
        Exception? cleanupFailure = null;
        try { await rig.DisposeAsync(); } catch (Exception error) { cleanupFailure = error; }
        if (assertCleanupFailure is null || failures.Count != 0)
        { if (cleanupFailure is not null) failures.Add(cleanupFailure); }
        else
        {
            try
            {
                Assert.NotNull(cleanupFailure);
                assertCleanupFailure(cleanupFailure!);
                rig.DeleteTemporaryFixtureAfterExpectedDrain();
            }
            catch (Exception assertion)
            { failures.Add(assertion); if (cleanupFailure is not null) failures.Add(cleanupFailure); }
        }
        // Test cleanup must never replace the first failed product assertion.
        // Expected owner refusals remain explicitly asserted in each body.
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("The board workflow and its independent cleanup retained their failures.", failures);
    }

    [LinuxFact]
    public async Task Real_resource_READ_denial_retains_created_receipt_and_fresh_retry_opens_that_same_file()
    {
        await WithRigAsync(async rig =>
        {
            var reviews = 0;
            async Task Review(HomePermissionAuthorization authorization, CancellationToken token)
            { reviews++; Assert.True((await rig.Trust.DecideAsync(authorization.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded); }
            await rig.InitializeLibraryAsync(Review);
            var selected = await rig.SelectBoardsFolderAsync();
            rig.ReadPolicy.DenyBoardReads = true;
            var created = await rig.Keep(rig.Library.CreateInSelectedFolderAsync(selected, "READ retry board",
                BoardBytes(Guid.NewGuid(), "Same created file"), rig.Token));
            Assert.Equal(FilesOriginalBoardDocumentCreateSettlementState.Committed, created.State);
            var receipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(created.Receipt);
            Assert.Null(created.PreparedAddress); Assert.Equal("CreatedOpenFailed", created.OpenFailureCode);
            Assert.True(rig.ReadPolicy.Denials > 0); Assert.Equal(1, reviews);
            var page = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, receipt.ParentFolderId,
                token: rig.Token, expectedStoreId: receipt.FilesStoreId));
            var row = Assert.Single(page.Items, item => item.Id.Value == receipt.FileId);
            var original = rig.Browser.ReadOriginalBoardDocumentWithinSourceAsync(page, row, rig.Workspace.Actor,
                () => true, body => body(), rig.Retain, rig.Token);
            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => rig.Keep(original));
            Assert.True(rig.Browser.IsOriginalBoardDocumentReadRefusal(original, refusal));
            Assert.False(rig.Browser.IsOriginalBoardDocumentReadRefusal(original, new UnauthorizedAccessException("The current Files resource owner denied reading this board.")));
            Assert.False(rig.Browser.IsOriginalBoardDocumentReadRefusal(Task.FromException(refusal), refusal));
            Assert.False(rig.Browser.IsOriginalBoardDocumentReadRefusal(original, new AggregateException(refusal, new IOException("Foreign READ sibling IO."))));

            rig.ReadPolicy.DenyBoardReads = false;
            var address = await rig.Keep(rig.Library.PrepareCreatedBoardOpenAsync(receipt, rig.Token));
            var parsed = BoardsBrowseAppContentAddress.Parse(address);
            Assert.Equal(receipt.FilesStoreId, parsed.StoreId); Assert.Equal(receipt.FileId, parsed.FileId);
            Assert.Equal(receipt.FilesCasRevisionId, parsed.RevisionId);
            await using var lease = rig.Writes.TakePrepared(parsed.ReadToken!.Value, parsed.StoreId!.Value,
                parsed.ParentFolderId!.Value, parsed.FileId!.Value, parsed.RevisionId!.Value);
            Assert.Equal(receipt.DocumentId, HavenBoardDocumentCodec.Deserialize(lease.Bytes).DocumentId);
            Assert.Equal(receipt.FilesCasRevisionId, lease.RevisionId); Assert.Equal(1, reviews);
            Assert.Single(await rig.ListBoardsAsync());
            Assert.Single(Directory.GetFiles(rig.BoardsDirectory, ".9to1-board-*.content"));
        });
    }

    [LinuxFact]
    public async Task Missing_real_immutable_leaf_remains_an_IO_failure_even_after_the_fixture_restores_it()
    {
        FileNotFoundException? actualMissingLeaf = null;
        await WithRigAsync(async rig =>
        {
            async Task Review(HomePermissionAuthorization authorization, CancellationToken token)
            { Assert.True((await rig.Trust.DecideAsync(authorization.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded); }
            await rig.InitializeLibraryAsync(Review);
            var selected = await rig.SelectBoardsFolderAsync();
            var created = await rig.Keep(rig.Library.CreateInSelectedFolderAsync(selected, "Missing leaf board",
                BoardBytes(Guid.NewGuid(), "Real filesystem failure"), rig.Token));
            var receipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(created.Receipt);
            var parsed = BoardsBrowseAppContentAddress.Parse(created.PreparedAddress!);
            await rig.Writes.DiscardPreparedAsync(parsed.ReadToken!.Value);
            var leaf = Assert.Single(Directory.GetFiles(rig.BoardsDirectory, ".9to1-board-*.content"));
            var bytes = await File.ReadAllBytesAsync(leaf, rig.Token);
            File.Delete(leaf);
            try
            {
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => rig.Keep(rig.Library.PrepareCreatedBoardOpenAsync(receipt, rig.Token)));
                var leaves = FailureLeaves(failure).ToArray();
                actualMissingLeaf = Assert.IsType<FileNotFoundException>(leaves.First());
                Assert.All(leaves, item => Assert.Same(actualMissingLeaf, item));
                Assert.False(rig.Library.IsExpectedRefusal(failure));
            }
            finally { await File.WriteAllBytesAsync(leaf, bytes, rig.Token); }
            Assert.Single(await rig.ListBoardsAsync());
        }, failure =>
        {
            Assert.NotNull(actualMissingLeaf);
            Assert.All(FailureLeaves(failure), item => Assert.Same(actualMissingLeaf, item));
        });
    }
    private static IEnumerable<Exception> FailureLeaves(Exception failure) => failure is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(FailureLeaves) : [failure];

    [LinuxFact]
    public async Task Exact_caller_cancelled_original_READ_is_joined_without_converting_a_foreign_cancellation()
    {
        await WithRigAsync(async rig =>
        {
            async Task Review(HomePermissionAuthorization request, CancellationToken token)
            { Assert.True((await rig.Trust.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded); }
            await rig.InitializeLibraryAsync(Review);
            var folder = await rig.SelectBoardsFolderAsync();
            var created = await rig.Keep(rig.Library.CreateInSelectedFolderAsync(folder, "Cancelled READ board",
                BoardBytes(Guid.NewGuid(), "Retain caller cancellation"), rig.Token));
            var receipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(created.Receipt);
            var page = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, receipt.ParentFolderId,
                token: rig.Token, expectedStoreId: receipt.FilesStoreId));
            var row = Assert.Single(page.Items, item => item.Id.Value == receipt.FileId);
            using var caller = new CancellationTokenSource(); caller.Cancel();
            var ledger = new CloudflareOriginalTaskLedger();
            var original = rig.Read.ReadSelectedOutcomeAsync(page, row, () => true, rig.IsHomeAuthorityCurrentAsync,
                body => body(), rig.Retain, ledger, caller.Token);
            var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Keep(original));
            Assert.True(original.IsCanceled); Assert.Equal(caller.Token, cancelled.CancellationToken);
            await rig.Read.DrainPreparedAsync();
        });
    }

    [LinuxFact]
    public async Task Foreign_READ_scope_cancellation_remains_a_fault_and_cannot_retire_as_caller_cancellation()
    {
        using var foreign = new CancellationTokenSource(); foreign.Cancel();
        var refusal = new OperationCanceledException("Foreign READ source callback cancellation.", foreign.Token);
        await WithRigAsync(async rig =>
        {
            async Task Review(HomePermissionAuthorization request, CancellationToken token)
            { Assert.True((await rig.Trust.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded); }
            await rig.InitializeLibraryAsync(Review);
            var folder = await rig.SelectBoardsFolderAsync();
            var created = await rig.Keep(rig.Library.CreateInSelectedFolderAsync(folder, "Foreign cancellation board",
                BoardBytes(Guid.NewGuid(), "Refuse foreign cancellation"), rig.Token));
            var receipt = Assert.IsType<FilesOriginalBoardDocumentCreateReceipt>(created.Receipt);
            var page = await rig.Keep(rig.Browser.ListAsync(rig.Workspace.Actor, receipt.ParentFolderId,
                token: rig.Token, expectedStoreId: receipt.FilesStoreId));
            var row = Assert.Single(page.Items, item => item.Id.Value == receipt.FileId);
            var original = rig.Read.ReadSelectedOutcomeAsync(page, row, () => true, rig.IsHomeAuthorityCurrentAsync,
                _ => throw refusal, rig.Retain, new CloudflareOriginalTaskLedger(), rig.Token);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => rig.Keep(original));
            Assert.True(original.IsFaulted); Assert.False(original.IsCanceled);
            Assert.All(FailureLeaves(failure), item => Assert.Same(refusal, item));
        }, failure => Assert.All(FailureLeaves(failure), item => Assert.Same(refusal, item)));
    }

    // Deny-only policy injection over the genuine private Files issuer and its
    // actual canonical ACL/currentness checks. Every positive answer is forwarded.
    private sealed class ReadPolicyFixture(FilesArtifactResourceResolver actual) :
        IOriginalCanonicalReadResolver, IOriginalCanonicalWriteResolver, IOriginalScopedCanonicalResourceAccessResolver
    {
        internal bool DenyBoardReads;
        internal int Denials;
        public string ResourceKind => actual.ResourceKind;
        private ResourceAccessDecision Filter(string action, ResourceScope scope, ResourceAccessDecision decision)
        {
            if (decision.Allowed && DenyBoardReads && action == "files.board.read" && scope.Access == ResourceAccess.Read)
            { Interlocked.Increment(ref Denials); return decision with { Allowed = false, Code = "FixtureOriginalBoardReadDenied" }; }
            return decision;
        }
        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) =>
            Filter(action, scope, await actual.EvaluateAsync(actor, action, scope, token));
        public async Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource,
            Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            Filter(action, resource, await actual.EvaluateWithinOriginalSourceAsync(actor, action, resource, scope, retain, token));
        public bool IsIssuedOriginalRead(IOriginalCanonicalReadContext context, AuthenticatedResourceActor actor, string action, ResourceScope scope) =>
            actual.IsIssuedOriginalRead(context, actor, action, scope);
        public bool IsIssuedOriginalReadOwnerBinding(IOriginalCanonicalReadContext context, object provider, object directories, Guid store) =>
            actual.IsIssuedOriginalReadOwnerBinding(context, provider, directories, store);
        public async ValueTask<ResourceAccessDecision> EvaluateOriginalReadAsync(IOriginalCanonicalReadContext context,
            AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) =>
            Filter(action, scope, await actual.EvaluateOriginalReadAsync(context, actor, action, scope, token));
        public bool IsIssuedOriginalWrite(IOriginalCanonicalWriteContext context, AuthenticatedResourceActor actor, string action, ResourceScope scope) =>
            actual.IsIssuedOriginalWrite(context, actor, action, scope);
        public ValueTask<ResourceAccessDecision> EvaluateOriginalWriteAsync(IOriginalCanonicalWriteContext context,
            AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) =>
            actual.EvaluateOriginalWriteAsync(context, actor, action, scope, token);
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
        private FailBoardCreateAuditObserver? _auditObserver;
        private FilesNativeBrowserService.BoardDocumentCreateOwner? _creator;
        private FilesNativeBrowserService.BoardDocumentWriteOwner? _writer;
        private NativeHomeFilesBoardReadSource? _read;
        private NativeHomeFilesBoardWriteSource? _writes;
        private NativeHomeFilesLibrarySource? _library;
        private ReadPolicyFixture? _readPolicy;

        private Rig()
        { _chosen = Path.Combine(_root, "chosen"); HomePath = Path.Combine(_root, "home.json"); }

        internal string HomePath { get; }
        internal string BoardsDirectory { get; private set; } = string.Empty;
        internal CancellationToken Token => _deadline.Token;
        internal NativeFilesWorkspace Workspace { get; private set; } = null!;
        internal FilesNativeBrowserService Browser { get; private set; } = null!;
        internal FilesOriginalBoardDocumentPhysicalOwner Physical => _physical ?? throw new InvalidOperationException();
        internal HomePermissionTrustService Trust { get; private set; } = null!;
        internal FileHomeCoreStateStore Home { get; private set; } = null!;
        internal int BoardCreateAuditFailureCount => _auditObserver?.BoardCreateAuditFailureCount ?? 0;
        internal NativeHomeFilesBoardWriteSource Writes => _writes ?? throw new InvalidOperationException();
        internal NativeHomeFilesBoardReadSource Read => _read ?? throw new InvalidOperationException();
        internal NativeHomeFilesLibrarySource Library => _library ?? throw new InvalidOperationException();
        internal ReadPolicyFixture ReadPolicy => _readPolicy ?? throw new InvalidOperationException();
        internal async Task<bool> IsHomeAuthorityCurrentAsync(CancellationToken token) =>
            await (_graph ?? throw new InvalidOperationException()).GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token) == Workspace.Actor;
        internal void DeleteTemporaryFixtureAfterExpectedDrain()
        { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

        internal void Retain(Task task) { lock (_gate) _retained.Add(task); }
        internal void FailNextBoardCreateAudits(int count) =>
            (_auditObserver ?? throw new InvalidOperationException("The audit observer is unavailable.")).FailNextBoardCreateAudits(count);
        internal Task<T> Keep<T>(Task<T> task) { Retain(task); return task; }
        internal Task Keep(Task task) { Retain(task); return task; }

        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                Directory.CreateDirectory(rig._chosen);
                rig._auditObserver = new FailBoardCreateAuditObserver();
                rig.Home = new FileHomeCoreStateStore(rig.HomePath, rig._auditObserver);
                rig._auditObserver.Bind(rig.Home);
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
                services.RemoveAll<ICanonicalResourceAccessResolver>();
                services.AddSingleton<ReadPolicyFixture>(provider => {
                    var readPolicy = new ReadPolicyFixture(provider.GetRequiredService<FilesArtifactResourceResolver>());
                    rig._readPolicy = readPolicy; return readPolicy;
                });
                services.AddSingleton<ICanonicalResourceAccessResolver>(provider => provider.GetRequiredService<ReadPolicyFixture>());
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

    private sealed class FailBoardCreateAuditObserver : IHomePersistedWriteObserver
    {
        private FileHomeCoreStateStore? _store;
        private int _remainingFailures;
        private int _failureCount;
        internal int BoardCreateAuditFailureCount => Volatile.Read(ref _failureCount);
        internal void FailNextBoardCreateAudits(int count) => Interlocked.Exchange(ref _remainingFailures, count);
        internal void Bind(FileHomeCoreStateStore store)
        {
            if (Interlocked.CompareExchange(ref _store, store, null) is not null)
                throw new InvalidOperationException("Bind the actual Home store once.");
        }

        public async Task OnPersistedWriteAsync(string recordId, long actualRecordRevision, CancellationToken cancellationToken)
        {
            var store = Volatile.Read(ref _store) ?? throw new InvalidOperationException("The actual Home store was not bound.");
            var read = await store.ReadAsync(cancellationToken);
            if (!read.IsSuccess) throw new IOException("Could not inspect the persisted Home audit fixture record.");
            var record = read.State!.Records.SingleOrDefault(item =>
                item.RecordId == recordId && item.Revision == actualRecordRevision);
            if (record?.RecordType != "home.permissions-trust" ||
                !record.Payload.GetRawText().Contains("FILES_BOARD_CREATED", StringComparison.Ordinal)) return;
            while (true)
            {
                var remaining = Volatile.Read(ref _remainingFailures);
                if (remaining == 0) return;
                if (Interlocked.CompareExchange(ref _remainingFailures, remaining - 1, remaining) != remaining) continue;
                Interlocked.Increment(ref _failureCount);
                // This callback runs only after the real FileHome atomic write and locks have
                // completed. Throwing models a lost acknowledgement; it never fabricates a
                // rejected write or substitutes a different Home store.
                throw new IOException("Controlled lost acknowledgement for the persisted board-create audit.");
            }
        }
    }
}
