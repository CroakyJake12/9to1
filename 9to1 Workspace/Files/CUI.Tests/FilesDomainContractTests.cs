using HavenOS.Files;
using System.Security.Cryptography;

namespace HavenOS.Files.CUI.Tests;

internal static class FilesDomainContractTests
{
	public static async Task RunAllAsync()
	{
		HostedIdentitySurvivesRenameAndMoveProjections();
		ProviderCapabilitiesRequireTheEntireRequestedSet();
		DragContractPreservesStableHostedIdentityAndIntent();
		FolderColorValidationAcceptsOnlyRgbHex();
		ActionCatalogDeclaresPurgeAsDestructiveAndPagesItsActions();
		ProviderRegistryReportsUnavailableAndUnsupportedOperations();
		await OperationJournalPersistsAndDeduplicatesOnlyIdenticalRequests();
		await SyncCursorPersistsAndCannotRegress();
		await FolderColorMetadataUsesStableIdentityAndResets();
		await MaterializationRegistryPreservesCanonicalIdentityAndProtectsLocalChanges();
		await StateStoreRejectsAnUnknownSchemaVersion();
		await DurableDriveSurvivesRestartAndRejectsStaleMutations();
		await DurableDriveBroadcastsAcrossProviderInstances();
		await WorkspaceDirectoryBindingsRemainCanonicalAcrossRestart();
		await OwningArtifactRevisionsPreserveIdentityAndRejectStaleOrChangedReplays();
		await LocalProfileDirectoryBindingsDoNotCreateAccountIdentity();
	}

    private static async Task LocalProfileDirectoryBindingsDoNotCreateAccountIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-local-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var profile = Guid.NewGuid();
            var owner = "local-profile:" + profile.ToString("D");
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(Path.Combine(directory, "drive.json"), location, owner);
            var now = DateTimeOffset.UtcNow;
            var folder = new FilesOperation(new(Guid.NewGuid()), owner, HostedItemId.New(), null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null);
            Check.True((await provider.MutateAsync(folder, "Documents", default)).IsSuccess);
            var path = Path.Combine(directory, "bindings.json");
            var resolver = new FilesWorkspaceDirectoryResolver(path, _ => null, id => id == profile ? provider : null);
            var registered = await resolver.RegisterProfileAsync(profile, folder.ItemId, "write", directory);
            Check.True(registered.IsSuccess);
            Check.Equal(Guid.Empty, registered.Value!.AccountId);
            Check.Equal(profile, registered.Value.ProfileId!.Value);
            Check.Equal(FilesErrorCode.PermissionDenied, (await resolver.RegisterAsync(profile, folder.ItemId, "write", directory)).Error!.Code);
            var restarted = new FilesWorkspaceDirectoryResolver(path, _ => null, id => id == profile ? provider : null);
            Check.True((await restarted.ResolveProfileAsync(profile, "write")).IsSuccess);
            Check.Equal(FilesErrorCode.DestinationUnavailable, (await restarted.ResolveAsync(profile, "write")).Error!.Code);
            if (!OperatingSystem.IsWindows())
            {
                var child = Path.Combine(directory, "physical-child");
                Directory.CreateDirectory(child);
                var alias = Path.Combine(directory, "redirecting-parent");
                Directory.CreateSymbolicLink(alias, directory);
                var throughAlias = Path.Combine(alias, "physical-child");
                Check.Equal(FilesErrorCode.DestinationUnavailable,
                    (await resolver.RegisterProfileAsync(profile, folder.ItemId, "redirected", throughAlias)).Error!.Code);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task OwningArtifactRevisionsPreserveIdentityAndRejectStaleOrChangedReplays()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-files-artifact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "drive.json");
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(path, location, "owner");
            var reference = new FilesArtifactReference("write", Guid.NewGuid().ToString("N"), HostedItemId.New(), null, "WriteDocument", "Research.9to1w");
            Check.Equal(FilesErrorCode.PermissionDenied, (await provider.RegisterArtifactAsync(reference, "outsider")).Error!.Code);
            Check.True((await provider.RegisterArtifactAsync(reference, "owner")).IsSuccess);
            Check.Equal(FilesErrorCode.InvalidState, (await provider.RegisterArtifactAsync(reference with { FileId = HostedItemId.New() }, "owner")).Error!.Code);
            var commit = new FilesOwningAppRevisionCommit(reference.FileId, "write", "write-version-1", "owner", DateTimeOffset.UtcNow,
                100, "hash-1", "canonical-package-1", null);
            var first = await provider.CommitDurableRevisionAsync(commit, default);
            Check.True(first.IsSuccess);
            Check.Equal(first.Value!.Id, (await provider.CommitDurableRevisionAsync(commit, default)).Value!.Id);
            Check.Equal(FilesErrorCode.InvalidState, (await provider.CommitDurableRevisionAsync(commit with { ContentHash = "changed" }, default)).Error!.Code);
            Check.Equal(FilesErrorCode.RevisionConflict, (await provider.CommitDurableRevisionAsync(commit with { OwningAppRevisionId = "write-version-2" }, default)).Error!.Code);
            Check.Equal(FilesErrorCode.PermissionDenied, (await provider.CommitDurableRevisionAsync(commit with { OwningAppId = "present" }, default)).Error!.Code);
            var now = DateTimeOffset.UtcNow;
            var rename = new FilesOperation(new(Guid.NewGuid()), "owner", reference.FileId, null, null, "Rename", first.Value.Id, null,
                FilesOperationState.Pending, now, now, null, null);
            Check.True((await provider.MutateAsync(rename, "Evidence.9to1w", default)).IsSuccess);
            var restarted = new DurableDriveProvider(path, location, "owner");
            var canonical = (await restarted.GetArtifactAsync(reference.FileId)).Value!;
            Check.Equal(reference.ArtifactId, canonical.ArtifactId);
            Check.Equal("Evidence.9to1w", canonical.DisplayName);
            var current = (await restarted.GetAsync(reference.FileId, default)).Value!;
            Check.True((await restarted.CommitDurableRevisionAsync(commit with { OwningAppRevisionId = "write-version-2", ExpectedBaseRevisionId = current.CurrentRevisionId }, default)).IsSuccess);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task WorkspaceDirectoryBindingsRemainCanonicalAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-files-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var account = Guid.NewGuid();
            var other = Guid.NewGuid();
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(Path.Combine(directory, "drive.json"), location, account.ToString("N"));
            var now = DateTimeOffset.UtcNow;
            var create = new FilesOperation(new(Guid.NewGuid()), account.ToString("N"), HostedItemId.New(), null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null);
            Check.True((await provider.MutateAsync(create, "Sites", default)).IsSuccess);
            var bindingPath = Path.Combine(directory, "bindings.json");
            var resolver = new FilesWorkspaceDirectoryResolver(bindingPath, id => id == account ? provider : null);
            Check.Equal(FilesErrorCode.DestinationUnavailable, (await resolver.ResolveAsync(account, "sites")).Error!.Code);
            Check.Equal(FilesErrorCode.PermissionDenied, (await resolver.RegisterAsync(other, create.ItemId, "sites", directory)).Error!.Code);
            Check.True((await resolver.RegisterAsync(account, create.ItemId, "sites", directory)).IsSuccess);
            var restarted = new FilesWorkspaceDirectoryResolver(bindingPath, id => id == account ? provider : null);
            var resolved = await restarted.ResolveAsync(account, "sites");
            Check.True(resolved.IsSuccess);
            Check.Equal(create.ItemId, resolved.Value!.FolderId);
            Check.Equal(Path.GetFullPath(directory), resolved.Value.DirectoryPath);
            var folder = (await provider.GetAsync(create.ItemId, default)).Value!;
            Check.True((await provider.MutateAsync(create with { Id = new(Guid.NewGuid()), Operation = "Delete", BaseRevisionId = folder.CurrentRevisionId }, null, default)).IsSuccess);
            Check.Equal(FilesErrorCode.ItemNotFound, (await restarted.ResolveAsync(account, "sites")).Error!.Code);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task DurableDriveBroadcastsAcrossProviderInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-drive-feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "drive.json");
            var location = new FilesLocationId(Guid.NewGuid());
            var observing = new DurableDriveProvider(path, location, "owner");
            var mutating = new DurableDriveProvider(path, location, "owner");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var events = observing.SubscribeAsync(null, cancellation.Token).GetAsyncEnumerator();
            var next = events.MoveNextAsync().AsTask();
            var now = DateTimeOffset.UtcNow;
            var create = new FilesOperation(new(Guid.NewGuid()), "owner", HostedItemId.New(), null, null, "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Check.True((await mutating.MutateAsync(create, "Parent", default)).IsSuccess);
            Check.True(await next);
            Check.Equal(create.ItemId, events.Current.ItemId);
            var parent = (await mutating.GetAsync(create.ItemId, default)).Value!;
            var child = create with { Id = new(Guid.NewGuid()), ItemId = HostedItemId.New(), DestinationParentId = parent.Id };
            Check.True((await mutating.MutateAsync(child, "Child", default)).IsSuccess);
            var childMetadata = (await mutating.GetAsync(child.ItemId, default)).Value!;
            Check.True((await mutating.MutateAsync(create with { Id = new(Guid.NewGuid()), Operation = "Delete", BaseRevisionId = parent.CurrentRevisionId }, null, default)).IsSuccess);
            Check.True(!(await mutating.GetAsync(child.ItemId, default)).IsSuccess);
            Check.Equal(FilesErrorCode.ItemNotFound, (await mutating.MutateAsync(child with { Id = new(Guid.NewGuid()), Operation = "Move", DestinationParentId = null, BaseRevisionId = childMetadata.CurrentRevisionId }, null, default)).Error!.Code);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task DurableDriveSurvivesRestartAndRejectsStaleMutations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-drive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "drive.json");
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(path, location, "owner");
            var id = HostedItemId.New();
            var now = DateTimeOffset.UtcNow;
            var create = new FilesOperation(new(Guid.NewGuid()), "owner", id, null, null, "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            var created = await provider.MutateAsync(create, "Project", default);
            Check.True(created.IsSuccess);
            Check.Equal(created.Value, (await provider.MutateAsync(create, "Project", default)).Value);
            Check.True(!(await provider.MutateAsync(create, "Different", default)).IsSuccess);
            var revision = created.Value!.ResultRevisionId;
            var rename = create with { Id = new(Guid.NewGuid()), Operation = "Rename", BaseRevisionId = revision };
            Check.True((await provider.MutateAsync(rename, "Renamed", default)).IsSuccess);
            Check.Equal(FilesErrorCode.RevisionConflict, (await provider.MutateAsync(rename with { Id = new(Guid.NewGuid()) }, "Stale", default)).Error!.Code);
            var restarted = new DurableDriveProvider(path, location, "owner");
            var item = (await restarted.GetAsync(id, default)).Value!;
            Check.Equal("Renamed", item.Name);
            var delete = rename with { Id = new(Guid.NewGuid()), Operation = "Delete", BaseRevisionId = item.CurrentRevisionId };
            var deleted = await restarted.MutateAsync(delete, null, default);
            Check.True(deleted.IsSuccess);
            Check.True(!(await restarted.GetAsync(id, default)).IsSuccess);
            var restored = await restarted.MutateAsync(delete with { Id = new(Guid.NewGuid()), Operation = "Restore", BaseRevisionId = deleted.Value!.ResultRevisionId }, null, default);
            Check.True(restored.IsSuccess);
            Check.Equal(id, (await restarted.GetAsync(id, default)).Value!.Id);
            Check.Equal(4, (await restarted.GetChangesAsync(null, 50, default)).Items.Count);
            Check.Equal(2, (await restarted.GetChangesAsync(new(2), 50, default)).Items.Count);
            Check.Equal(FilesErrorCode.PermissionDenied, (await restarted.MutateAsync(create with { Id = new(Guid.NewGuid()), ItemId = HostedItemId.New(), ActorId = "outsider" }, "Private", default)).Error!.Code);
        }
        finally { Directory.Delete(directory, true); }
    }

	private static void HostedIdentitySurvivesRenameAndMoveProjections()
	{
		HostedItemId id = HostedItemId.New();
		var item = new HostedItemMetadata(id, new FilesLocationId(Guid.NewGuid()), null, "Before.txt", HostedItemKind.File,
			"text/plain", "owner", "personal", 12, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
			new FilesRevisionId(Guid.NewGuid()), SyncAvailability.Synced, false, "hash");

		HostedItemMetadata renamed = item with { Name = "After.txt", ParentId = new HostedItemId(Guid.NewGuid()) };

		Check.Equal(id, renamed.Id);
		Check.Equal("After.txt", renamed.Name);
	}

	private static void ProviderCapabilitiesRequireTheEntireRequestedSet()
	{
		FilesProviderCapabilities available = FilesProviderCapabilities.Read | FilesProviderCapabilities.Streaming;

		Check.True(available.Supports(FilesProviderCapabilities.Read | FilesProviderCapabilities.Streaming));
		Check.False(available.Supports(FilesProviderCapabilities.Read | FilesProviderCapabilities.Sharing));
		Check.False(available.Supports(FilesProviderCapabilities.None));
	}

	private static void DragContractPreservesStableHostedIdentityAndIntent()
	{
		var itemId = HostedItemId.New();
		var locationId = new FilesLocationId(Guid.NewGuid());
		var entry = new FileEntry("C:\\Sync\\report.txt", "report.txt", FileItemKind.File, 42, DateTimeOffset.UtcNow,
			FileItemCapabilities.Open | FileItemCapabilities.Drag, ItemId: itemId, LocationId: locationId);
		FileDragDescriptor descriptor = FileDragDescriptor.Create([entry], FileDropEffect.Move, locationId, "operation-1");
		FileDropContract.Validate(descriptor, FileDropEffect.Move);
		Check.Equal(itemId, descriptor.Items[0].ItemId);
		Check.Equal(locationId, descriptor.SourceLocationId);
		Check.Equal("operation-1", descriptor.OperationIdempotencyKey);
		Check.Throws<InvalidOperationException>(() => FileDropContract.Validate(descriptor, FileDropEffect.Copy));
	}

	private static void FolderColorValidationAcceptsOnlyRgbHex()
	{
		Check.Equal("#00AAFF", FilesFolderPresentation.ValidateColor("#00Aaff"));
		Check.Throws<ArgumentException>(() => FilesFolderPresentation.ValidateColor("#12X456"));
		Check.Throws<ArgumentException>(() => FilesFolderPresentation.ValidateColor("#12345"));
	}

	private static void ActionCatalogDeclaresPurgeAsDestructiveAndPagesItsActions()
	{
		FilesPage<FilesActionDefinition> first = FilesServiceActionCatalog.GetPage(pageSize: 5);
		Check.True(first.NextPageToken is not null);
		Check.Equal(5, first.Items.Count);

		FilesActionDefinition purge = GetAllActions().Single(action => action.Name == "Purge");
		Check.Equal(FilesRiskLevel.Destructive, purge.Risk);
		Check.False(purge.IsReversible);
		Check.True(purge.AffectedObjects.Contains("impact", StringComparison.OrdinalIgnoreCase));
	}

	private static void ProviderRegistryReportsUnavailableAndUnsupportedOperations()
	{
		FilesLocationId locationId = new(Guid.NewGuid());
		var provider = new StubProvider(new FilesLocation(locationId, "Drive", FilesLocationKind.Drive,
			FilesProviderCapabilities.Read | FilesProviderCapabilities.ChangeFeed, "9to1-drive"));
		var secondLocation = new FilesLocation(new FilesLocationId(Guid.NewGuid()), "Drive archive", FilesLocationKind.Drive,
			FilesProviderCapabilities.Read, "9to1-drive");
		var registry = new FilesProviderRegistry([provider, new StubProvider(secondLocation)]);
		Check.True(registry.GetCapabilities(locationId).IsSuccess);
		Check.Equal(FilesErrorCode.ProviderCapabilityUnsupported,
			registry.RequireCapability(locationId, FilesProviderCapabilities.Write, "Write").Error?.Code);
		Check.Equal(FilesErrorCode.ItemNotFound, registry.GetCapabilities(new FilesLocationId(Guid.NewGuid())).Error?.Code);
		Check.Equal(2, registry.ListLocations().Items.Count);
		Check.Throws<ArgumentException>(() => new FilesProviderRegistry([provider, new StubProvider(provider.Location)]));
	}

	private static async Task OperationJournalPersistsAndDeduplicatesOnlyIdenticalRequests()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "journal.json");
			var operation = NewOperation("rename");
			var firstJournal = new FilesOperationJournal(path);
			FilesOperation first = await firstJournal.EnqueueAsync(operation);
			FilesOperation replay = await new FilesOperationJournal(path).EnqueueAsync(operation);
			Check.Equal(first, replay);
			FilesOperation second = await firstJournal.EnqueueAsync(NewOperation("move") with { CreatedAt = operation.CreatedAt.AddDays(-1) });
			IReadOnlyList<FilesOperation> ordered = await new FilesOperationJournal(path).ListPendingAsync();
			Check.Equal(first.Id, ordered[0].Id);
			Check.Equal(second.Id, ordered[1].Id);

			var secondInstance = new FilesOperationJournal(path);
			Task<FilesOperation>[] parallelEnqueues = Enumerable.Range(0, 10)
				.Select(index => (index % 2 == 0 ? firstJournal : secondInstance).EnqueueAsync(NewOperation($"operation-{index}")))
				.ToArray();
			await Task.WhenAll(parallelEnqueues);
			IReadOnlyList<FilesOperation> afterParallel = await new FilesOperationJournal(path).ListPendingAsync();
			Check.Equal(12, afterParallel.Count);
			Check.True(afterParallel.Select(operation => operation.Sequence).SequenceEqual(Enumerable.Range(1, 12).Select(value => (long)value)));

			var changed = operation with { Operation = "move" };
			await Check.ThrowsAsync<InvalidDataException>(() => firstJournal.EnqueueAsync(changed));
			await Check.ThrowsAsync<InvalidDataException>(() => firstJournal.EnqueueAsync(operation with
			{
				Payload = new FilesOperationPayload(NewName: "different-name"),
			}));

			FilesOperation committed = await firstJournal.TransitionAsync(operation.Id, FilesOperationState.Committed, DateTimeOffset.UtcNow);
			Check.Equal(FilesOperationState.Committed, committed.State);
			await Check.ThrowsAsync<InvalidOperationException>(() => firstJournal.TransitionAsync(operation.Id, FilesOperationState.Pending, DateTimeOffset.UtcNow));
			Check.Equal(11, (await new FilesOperationJournal(path).ListPendingAsync()).Count);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static async Task StateStoreRejectsAnUnknownSchemaVersion()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "state.json");
			await File.WriteAllTextAsync(path, "{\"schemaVersion\":99,\"state\":{}}");
			var store = new VersionedJsonStateStore<FilesOperationJournalState>(path, 1, static () => new FilesOperationJournalState());
			await Check.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static async Task SyncCursorPersistsAndCannotRegress()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "cursor.json");
			var firstStore = new FilesSyncCursorStore(path);
			Check.Equal(new FilesChangeCursor(42), await firstStore.AcknowledgeAsync(new FilesChangeCursor(42)));
			Check.Equal(new FilesChangeCursor(42), await new FilesSyncCursorStore(path).GetAsync());
			await Check.ThrowsAsync<InvalidOperationException>(() => firstStore.AcknowledgeAsync(new FilesChangeCursor(41)));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static async Task FolderColorMetadataUsesStableIdentityAndResets()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "presentation.json");
			var folderId = HostedItemId.New();
			var store = new FilesFolderPresentationStore(path);
			FilesFolderPresentation? saved = await store.SetAsync(folderId, "#f0c033", DateTimeOffset.UtcNow, "principal-1");
			Check.Equal("#F0C033", saved?.ColorHex);
			Check.Equal("#F0C033", (await new FilesFolderPresentationStore(path).GetAsync(folderId))?.ColorHex);
			Check.Equal<FilesFolderPresentation?>(null, await store.SetAsync(folderId, null, DateTimeOffset.UtcNow, "principal-1"));
			Check.Equal<FilesFolderPresentation?>(null, await store.GetAsync(folderId));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static async Task MaterializationRegistryPreservesCanonicalIdentityAndProtectsLocalChanges()
	{
		string directory = CreateTempDirectory();
		try
		{
			string root = Path.Combine(directory, "drive");
			Directory.CreateDirectory(root);
			string path = Path.Combine(root, "one.txt");
			await File.WriteAllTextAsync(path, "verified content");
			var registry = new FilesMaterializationRegistry(root, Path.Combine(directory, "materializations.json"));
			var itemId = HostedItemId.New();
			var remoteRevision = new FilesRevisionId(Guid.NewGuid());
			var proof = new FilesMaterializationProof(itemId, remoteRevision, ContentHash("verified content"), 16, DateTimeOffset.UtcNow);
			await registry.RegisterValidatedAsync(path, proof, SyncAvailability.AvailableOffline);
			Check.True((await registry.CheckEvictionAsync(itemId, remoteRevision)).IsSuccess);
			await Check.ThrowsAsync<InvalidDataException>(() => registry.RegisterValidatedAsync(path,
				proof with { ContentHash = "sha256:00" }, SyncAvailability.AvailableOffline));

			string movedPath = Path.Combine(root, "renamed.txt");
			FilesMaterializedFile moved = await registry.MoveMappingAsync(itemId, movedPath);
			Check.Equal(itemId, moved.ItemId);
			Check.Equal(itemId, (await new FilesMaterializationRegistry(root, Path.Combine(directory, "materializations.json")).GetByPathAsync(movedPath))?.ItemId);

			string localContent = "local content changed";
			await File.WriteAllTextAsync(movedPath, localContent);
			FilesResult<FilesMaterializedFile> local = await registry.MarkLocalChangesAsync(itemId, new FilesRevisionId(Guid.NewGuid()), ContentHash(localContent), localContent.Length);
			Check.True(local.IsSuccess);
			Check.Equal(FilesErrorCode.SyncConflict, (await registry.CheckEvictionAsync(itemId, remoteRevision)).Error?.Code);
			var syncedRevision = new FilesRevisionId(Guid.NewGuid());
			Check.True((await registry.MarkSyncedAsync(itemId, syncedRevision, ContentHash(localContent), localContent.Length)).IsSuccess);
			Check.True((await registry.CheckEvictionAsync(itemId, syncedRevision)).IsSuccess);

			string pinnedPath = Path.Combine(root, "pinned.txt");
			await File.WriteAllTextAsync(pinnedPath, "pinned");
			var pinnedId = HostedItemId.New();
			var pinnedRevision = new FilesRevisionId(Guid.NewGuid());
			await registry.RegisterValidatedAsync(pinnedPath,
				new FilesMaterializationProof(pinnedId, pinnedRevision, ContentHash("pinned"), 6, DateTimeOffset.UtcNow),
				SyncAvailability.AlwaysAvailable);
			string pinnedEdit = "pinned edited";
			await File.WriteAllTextAsync(pinnedPath, pinnedEdit);
			Check.True((await registry.MarkLocalChangesAsync(pinnedId, new FilesRevisionId(Guid.NewGuid()), ContentHash(pinnedEdit), pinnedEdit.Length)).IsSuccess);
			var pinnedSyncedRevision = new FilesRevisionId(Guid.NewGuid());
			FilesResult<FilesMaterializedFile> pinnedSynced = await registry.MarkSyncedAsync(pinnedId, pinnedSyncedRevision, ContentHash(pinnedEdit), pinnedEdit.Length);
			Check.Equal(SyncAvailability.AlwaysAvailable, pinnedSynced.Value?.State);
			Check.Equal(FilesErrorCode.InvalidState, (await registry.CheckEvictionAsync(pinnedId, pinnedSyncedRevision)).Error?.Code);
			Check.Equal(SyncAvailability.AlwaysAvailable,
				(await new FilesMaterializationRegistry(root, Path.Combine(directory, "materializations.json")).GetByItemIdAsync(pinnedId))?.State);
			await Check.ThrowsAsync<UnauthorizedAccessException>(() => registry.GetByPathAsync(Path.Combine(directory, "outside.txt")));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static IReadOnlyList<FilesActionDefinition> GetAllActions()
	{
		var actions = new List<FilesActionDefinition>();
		string? token = null;
		do
		{
			FilesPage<FilesActionDefinition> page = FilesServiceActionCatalog.GetPage(token, 7);
			actions.AddRange(page.Items);
			token = page.NextPageToken;
		} while (token is not null);
		return actions;
	}

	private static FilesOperation NewOperation(string kind)
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		return new FilesOperation(new FilesOperationId(Guid.NewGuid()), "test-actor", HostedItemId.New(), null, null,
			kind, null, null, FilesOperationState.Pending, now, now, null, null);
	}

	private static string CreateTempDirectory()
	{
		string path = Path.Combine(Path.GetTempPath(), $"files-cui-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static string ContentHash(string content) =>
		"sha256:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

	private sealed class StubProvider(FilesLocation location) : IFilesProvider
	{
		public FilesLocation Location { get; } = location;
		public Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parentId, FilesSearchQuery? query, string? pageToken, CancellationToken cancellationToken) =>
			Task.FromResult(new FilesPage<HostedItemMetadata>(Array.Empty<HostedItemMetadata>(), null));
		public Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken cancellationToken) =>
			Task.FromResult(FilesResult<HostedItemMetadata>.Failure(new FilesError(FilesErrorCode.ItemNotFound, "Missing", "Files.Get", itemId.ToString(), false, false)));
		public Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? newName, CancellationToken cancellationToken) =>
			Task.FromResult(FilesResult<FilesOperation>.Failure(new FilesError(FilesErrorCode.ProviderCapabilityUnsupported, "Unsupported", "Files.Mutate", operation.Id.ToString(), false, false)));
		public IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after, CancellationToken cancellationToken) => EmptyChanges();
		public Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after, int limit, CancellationToken cancellationToken) =>
			Task.FromResult(new FilesPage<FilesChangeEvent>(Array.Empty<FilesChangeEvent>(), null));

		private static async IAsyncEnumerable<FilesChangeEvent> EmptyChanges()
		{
			await Task.CompletedTask;
			yield break;
		}
	}
}

internal static class Check
{
	public static void True(bool condition)
	{
		if (!condition) throw new InvalidOperationException("Expected condition to be true.");
	}
	public static void False(bool condition) => True(!condition);
	public static void Equal<T>(T expected, T actual)
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
			throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
	}
	public static void Throws<TException>(Action action) where TException : Exception
	{
		try { action(); }
		catch (TException) { return; }
		throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
	}
	public static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
	{
		try { await action().ConfigureAwait(false); }
		catch (TException) { return; }
		throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
	}
}
