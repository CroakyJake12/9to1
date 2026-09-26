using HavenOS.Files;

namespace HavenOS.Files.CUI.Tests;

internal static class FilesDomainContractTests
{
	public static async Task RunAllAsync()
	{
		HostedIdentitySurvivesRenameAndMoveProjections();
		ProviderCapabilitiesRequireTheEntireRequestedSet();
		FolderColorValidationAcceptsOnlyRgbHex();
		ActionCatalogDeclaresPurgeAsDestructiveAndPagesItsActions();
		await OperationJournalPersistsAndDeduplicatesOnlyIdenticalRequests();
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

			var changed = operation with { Operation = "move" };
			await Check.ThrowsAsync<InvalidDataException>(() => firstJournal.EnqueueAsync(changed));

			FilesOperation committed = await firstJournal.TransitionAsync(operation.Id, FilesOperationState.Committed, DateTimeOffset.UtcNow);
			Check.Equal(FilesOperationState.Committed, committed.State);
			Check.Equal(second.Id, (await new FilesOperationJournal(path).ListPendingAsync()).Single().Id);
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
