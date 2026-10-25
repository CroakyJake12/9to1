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
		await OperationJournalSequenceSurvivesTransitionsAndRestart();
		await OperationJournalRecoversStaleCursorAndRejectsDuplicateLegacySequences();
		await SyncCursorPersistsAndCannotRegress();
		await FolderColorMetadataUsesStableIdentityAndResets();
		await MaterializationRegistryPreservesCanonicalIdentityAndProtectsLocalChanges();
		await StateStoreRejectsAnUnknownSchemaVersion();
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

	private static async Task OperationJournalSequenceSurvivesTransitionsAndRestart()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "journal.json");
			var journal = new FilesOperationJournal(path);
			FilesOperation first = await journal.EnqueueAsync(NewOperation("first"));
			FilesOperation second = await journal.EnqueueAsync(NewOperation("second"));
			Check.Equal(1L, first.Sequence);
			Check.Equal(2L, second.Sequence);
			await journal.TransitionAsync(first.Id, FilesOperationState.Running, DateTimeOffset.UtcNow.AddSeconds(1));

			FilesOperation third = await journal.EnqueueAsync(NewOperation("third"));
			FilesOperation fourth = await new FilesOperationJournal(path).EnqueueAsync(NewOperation("fourth"));
			Check.Equal(3L, third.Sequence);
			Check.Equal(4L, fourth.Sequence);
			IReadOnlyList<FilesOperation> pending = await new FilesOperationJournal(path).ListPendingAsync();
			Check.True(pending.Select(operation => operation.Sequence).SequenceEqual(new long[] { 1, 2, 3, 4 }));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static async Task OperationJournalRecoversStaleCursorAndRejectsDuplicateLegacySequences()
	{
		string directory = CreateTempDirectory();
		try
		{
			string stalePath = Path.Combine(directory, "stale-cursor.json");
			FilesOperation acknowledged = NewOperation("acknowledged") with
			{
				Sequence = 1,
				State = FilesOperationState.Committed,
			};
			FilesOperation pending = NewOperation("pending") with { Sequence = 2 };
			var staleStore = new VersionedJsonStateStore<FilesOperationJournalState>(stalePath, 3, static () => new FilesOperationJournalState());
			await staleStore.UpdateAsync(_ => new FilesOperationJournalState
			{
				Operations = new Dictionary<string, FilesOperation>(StringComparer.Ordinal)
				{
					[acknowledged.Id.ToString()] = acknowledged,
					[pending.Id.ToString()] = pending,
				},
				NextSequence = 1,
			});
			FilesOperation recovered = await new FilesOperationJournal(stalePath).EnqueueAsync(NewOperation("after-stale-cursor"));
			Check.Equal(3L, recovered.Sequence);
			FilesOperationJournalState recoveredState = await staleStore.ReadAsync();
			Check.Equal(1L, recoveredState.Operations[acknowledged.Id.ToString()].Sequence);
			Check.Equal(FilesOperationState.Committed, recoveredState.Operations[acknowledged.Id.ToString()].State);

			string duplicatePath = Path.Combine(directory, "duplicate-sequence.json");
			FilesOperation duplicateA = NewOperation("legacy-a") with { Sequence = 7 };
			FilesOperation duplicateB = NewOperation("legacy-b") with { Sequence = 7 };
			var duplicateStore = new VersionedJsonStateStore<FilesOperationJournalState>(duplicatePath, 3, static () => new FilesOperationJournalState());
			await duplicateStore.UpdateAsync(_ => new FilesOperationJournalState
			{
				Operations = new Dictionary<string, FilesOperation>(StringComparer.Ordinal)
				{
					[duplicateA.Id.ToString()] = duplicateA,
					[duplicateB.Id.ToString()] = duplicateB,
				},
				NextSequence = 8,
			});
			string before = await File.ReadAllTextAsync(duplicatePath);
			await Check.ThrowsAsync<InvalidDataException>(() => new FilesOperationJournal(duplicatePath).EnqueueAsync(NewOperation("must-not-repeat")));
			Check.Equal(before, await File.ReadAllTextAsync(duplicatePath));
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
