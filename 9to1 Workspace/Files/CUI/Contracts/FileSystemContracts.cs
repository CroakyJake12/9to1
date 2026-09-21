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
}

public enum FileOperationKind
{
	Rename,
	Duplicate,
	Copy,
	Move,
	Trash,
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
	string? LinkTarget = null);

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
	string Message);

public sealed record FileDragItem(string Path, string DisplayName, FileItemKind Kind);

public sealed record FileDragDescriptor(
	string Format,
	IReadOnlyList<FileDragItem> Items,
	FileDropEffect AllowedEffects)
{
	public const string FilesFormat = "application/vnd.haven.files+json";

	public static FileDragDescriptor Create(
		IEnumerable<FileEntry> entries,
		FileDropEffect allowedEffects = FileDropEffect.Copy | FileDropEffect.Move)
	{
		ArgumentNullException.ThrowIfNull(entries);
		var items = entries
			.Select(entry => new FileDragItem(entry.Path, entry.Name, entry.Kind))
			.ToArray();
		if (items.Length == 0)
			throw new ArgumentException("A file drag must contain at least one item.", nameof(entries));
		return new FileDragDescriptor(FilesFormat, items, allowedEffects);
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
