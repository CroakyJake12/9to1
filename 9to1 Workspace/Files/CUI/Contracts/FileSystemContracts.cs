namespace HavenOS.Files;

public enum FileItemKind
{
	File,
	Directory,
	SymbolicLink,
	Other,
}

[Flags]
public enum FileItemCapabilities
{
	None = 0,
	Open = 1 << 0,
	Rename = 1 << 1,
	Duplicate = 1 << 2,
	Trash = 1 << 3,
	Drag = 1 << 4,
	Drop = 1 << 5,
	Copy = 1 << 6,
	Move = 1 << 7,
	Restore = 1 << 8,
	Share = 1 << 9,
	Versions = 1 << 10,
	SetAvailability = 1 << 11,
	SetFolderColor = 1 << 12,
	PermanentDelete = 1 << 13,
}

public enum FileOperationKind
{
	Rename,
	Duplicate,
	Copy,
	Move,
	Trash,
	Restore,
	Purge,
	Create,
	Upload,
	Download,
	Sync,
	Share,
	SetFolderColor,
	CreateArtifact,
	CreateStack,
}

[Flags]
public enum FileDropEffect
{
	None = 0,
	Copy = 1 << 0,
	Move = 1 << 1,
}

public sealed record FileEntry(
	string Path,
	string Name,
	FileItemKind Kind,
	long? Size,
	DateTimeOffset Modified,
	FileItemCapabilities Capabilities,
	string? LinkTarget = null,
	HostedItemId? ItemId = null,
	string? ContentType = null,
	string? Revision = null,
	SyncAvailability? SyncState = null,
	FilesLocationId? LocationId = null);

public sealed record FilePlace(string Key, string Label, string Path, string IconKey);

public sealed record FileBreadcrumb(string Label, string Path);

public sealed record DirectoryListing(
	string Path,
	IReadOnlyList<FileBreadcrumb> Breadcrumbs,
	IReadOnlyList<FileEntry> Entries,
	DateTimeOffset ObservedAt);

public sealed record FileOperationResult(
	FileOperationKind Kind,
	string SourcePath,
	string? DestinationPath,
	DateTimeOffset ObservedAt,
	string Message,
	FilesError? Error = null,
	FilesOperationId? OperationId = null,
	FilesRevisionId? BaseRevisionId = null,
	FilesRevisionId? ResultRevisionId = null);

public sealed record FileDragItem(
	string Path,
	string DisplayName,
	FileItemKind Kind,
	HostedItemId? ItemId = null,
	FilesLocationId? LocationId = null);

public sealed record FileDragDescriptor(
	string Format,
	IReadOnlyList<FileDragItem> Items,
	FileDropEffect AllowedEffects,
	FilesLocationId? SourceLocationId = null,
	string? OperationIdempotencyKey = null)
{
	public const string FilesFormat = "application/vnd.haven.files+json";

	public static FileDragDescriptor Create(
		IEnumerable<FileEntry> entries,
		FileDropEffect allowedEffects = FileDropEffect.Copy | FileDropEffect.Move,
	FilesLocationId? sourceLocationId = null,
	string? operationIdempotencyKey = null)
	{
		ArgumentNullException.ThrowIfNull(entries);
		var items = entries
			.Select(entry => new FileDragItem(entry.Path, entry.Name, entry.Kind, entry.ItemId, entry.LocationId))
			.ToArray();
		if (items.Length == 0)
			throw new ArgumentException("A file drag must contain at least one item.", nameof(entries));
		return new FileDragDescriptor(FilesFormat, items, allowedEffects, sourceLocationId, operationIdempotencyKey);
	}
}

public interface IFileSystemAdapter
{
	string RootPath { get; }

	string NormalizePath(string path);

	Task<IReadOnlyList<FilePlace>> GetPlacesAsync(CancellationToken cancellationToken);

	Task<DirectoryListing> ListAsync(string path, CancellationToken cancellationToken);

	Task<FileOperationResult> RenameAsync(
		string sourcePath,
		string newName,
		CancellationToken cancellationToken);

	Task<FileOperationResult> DuplicateAsync(string sourcePath, CancellationToken cancellationToken);

	Task<FileOperationResult> CopyAsync(
		string sourcePath,
		string destinationDirectory,
		CancellationToken cancellationToken);

	Task<FileOperationResult> MoveAsync(
		string sourcePath,
		string destinationDirectory,
		CancellationToken cancellationToken);

	Task<FileOperationResult> TrashAsync(string sourcePath, CancellationToken cancellationToken);
}

public static class FileDropContract
{
	public static void Validate(FileDragDescriptor descriptor, FileDropEffect effect)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		if (!string.Equals(descriptor.Format, FileDragDescriptor.FilesFormat, StringComparison.Ordinal))
			throw new NotSupportedException($"Unsupported drop format '{descriptor.Format}'.");
		if (effect is not (FileDropEffect.Copy or FileDropEffect.Move))
			throw new ArgumentOutOfRangeException(nameof(effect), "A drop must select exactly copy or move.");
		if (!descriptor.AllowedEffects.HasFlag(effect))
			throw new InvalidOperationException($"The drag descriptor does not permit {effect}.");
		if (descriptor.Items.Count == 0)
			throw new InvalidDataException("A file drop cannot be empty.");
	}
}
