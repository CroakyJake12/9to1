namespace HavenOS.Files;

/// <summary>First-party action metadata consumed by the Home permission-broker adapter.</summary>
public static class FilesServiceActionCatalog
{
	private static readonly IReadOnlyList<FilesActionDefinition> Actions =
	[
		Read("ListLocations", "location metadata", "locations"),
		Read("GetProviderCapabilities", "location ID", "provider capabilities"),
		Read("List", "location, optional parent, query and page token", "paged item metadata"),
		Read("Get", "item ID", "item metadata"),
		Mutate("CreateFolder", "parent ID and name", "folder metadata", "folder"),
		Mutate("CreateArtifact", "parent ID, artifact type and config", "canonical artifact reference", "artifact"),
		Mutate("Rename", "item ID, name and expected revision", "operation and item result", "item"),
		Mutate("Move", "item ID, destination folder and expected revision", "operation and item result", "item"),
		Mutate("Copy", "item ID, destination folder and conflict policy", "transfer or operation result", "source and destination items"),
		Mutate("Delete", "item ID and expected revision", "recoverable trash result", "item", FilesRiskLevel.Elevated),
		new("Purge", "item ID and expected revision", "structured operation result", ["files.purge"], FilesRiskLevel.Destructive, false, false, "item; impact count must be previewed"),
		Mutate("Restore", "item ID", "restored item metadata", "item"),
		Mutate("Upload", "destination folder, source and explicit conflict policy", "durable transfer job", "source and destination", FilesRiskLevel.Elevated),
		Mutate("Download", "item ID and destination", "durable transfer job", "item and destination", FilesRiskLevel.Elevated),
		Read("Transfer.Get", "transfer ID", "transfer state"),
		Mutate("Transfer.Pause", "transfer ID", "transfer state", "transfer"),
		Mutate("Transfer.Resume", "transfer ID", "transfer state", "transfer"),
		Mutate("Transfer.Cancel", "transfer ID", "transfer state", "transfer", FilesRiskLevel.Elevated),
		Read("Sync.GetState", "none", "sync state"),
		Read("Sync.GetItemState", "item ID", "item sync state"),
		Mutate("Sync.Now", "none", "sync state and unresolved errors", "pending eligible operations", FilesRiskLevel.Elevated),
		Mutate("Sync.Item", "item ID", "item sync state", "item", FilesRiskLevel.Elevated),
		Mutate("Sync.Folder", "folder ID", "sync state", "folder descendants", FilesRiskLevel.Elevated),
		Mutate("Sync.Pause", "none", "sync state", "device sync session"),
		Mutate("Sync.Resume", "none", "sync state", "device sync session"),
		Mutate("Sync.SetAvailability", "item ID and availability", "item sync state", "item"),
		Mutate("Sync.Hydrate", "item ID", "hydration transfer job", "item content", FilesRiskLevel.Elevated),
		Mutate("Sync.FreeUpSpace", "item ID and expected cloud revision", "verified eviction result", "local cache bytes", FilesRiskLevel.Elevated),
		Read("Sync.ListPending", "page token", "paged operations"),
		Read("Sync.ListConflicts", "page token", "paged conflicts"),
		Mutate("Sync.ResolveConflict", "conflict ID and resolution", "structured conflict result", "conflict"),
		Read("GetVersions", "item ID and page token", "paged durable revisions"),
		Mutate("RestoreVersion", "item ID and revision ID", "new current revision", "item and revision history"),
		new("CommitOwningAppRevision", "file ID, owning app and revision IDs, actor, content identity and expected base", "durable Files revision", ["files.revision.commit"], FilesRiskLevel.Ordinary, true, false, "file and revision history"),
		Read("Share.Get", "item ID and page token", "effective and direct grants"),
		new("Share.Grant", "item ID, stable principal ID and role", "share grant", ["files.share"], FilesRiskLevel.SecuritySensitive, true, false, "item and principal"),
		new("Share.Revoke", "item ID and stable principal ID", "revocation result", ["files.share"], FilesRiskLevel.SecuritySensitive, true, false, "item and principal"),
		Read("Search", "query, optional scope and page token", "permission-filtered paged item metadata"),
		Read("Changes.Subscribe", "optional durable cursor", "resumable change stream"),
		Read("Changes.GetSince", "cursor and page size", "paged change events"),
		Mutate("SetFolderColor", "folder ID and RGB color or null to reset", "folder presentation metadata", "folder"),
		Mutate("CreateStack", "folder ID and options", "canonical Stack project reference", "folder and Stack project"),
	];

	public static FilesPage<FilesActionDefinition> GetPage(string? pageToken = null, int pageSize = 100)
	{
		if (pageSize is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		int offset = ParsePageToken(pageToken);
		if (offset > Actions.Count)
			throw new ArgumentException("The Files action catalog page token is invalid.", nameof(pageToken));
		FilesActionDefinition[] page = Actions.Skip(offset).Take(pageSize).ToArray();
		int nextOffset = offset + page.Length;
		return new FilesPage<FilesActionDefinition>(page, nextOffset < Actions.Count ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
	}

	private static int ParsePageToken(string? token)
	{
		if (token is null)
			return 0;
		if (int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int offset) && offset >= 0)
			return offset;
		throw new ArgumentException("The Files action catalog page token is invalid.", nameof(token));
	}

	private static FilesActionDefinition Read(string name, string arguments, string result) =>
		new(name, arguments, result, ["files.read"], FilesRiskLevel.Ordinary, true, false, "requested location, item or cursor scope");

	private static FilesActionDefinition Mutate(
		string name,
		string arguments,
		string result,
		string affected,
		FilesRiskLevel risk = FilesRiskLevel.Ordinary) =>
		new(name, arguments, result, ["files.write"], risk, risk is not FilesRiskLevel.Destructive, false, affected);
}
