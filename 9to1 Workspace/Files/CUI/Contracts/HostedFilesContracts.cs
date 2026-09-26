namespace HavenOS.Files;

/// <summary>Stable identity for a hosted file or folder. It is never derived from a path or name.</summary>
public readonly record struct HostedItemId(Guid Value)
{
	public static HostedItemId New() => new(Guid.NewGuid());
	public override string ToString() => Value.ToString("N");
}

public readonly record struct FilesLocationId(Guid Value) { public override string ToString() => Value.ToString("N"); }
public readonly record struct FilesRevisionId(Guid Value) { public override string ToString() => Value.ToString("N"); }
public readonly record struct FilesOperationId(Guid Value) { public override string ToString() => Value.ToString("N"); }
public readonly record struct FilesTransferId(Guid Value) { public override string ToString() => Value.ToString("N"); }
public readonly record struct FilesChangeCursor(long Revision);
public readonly record struct FilesShareId(Guid Value) { public override string ToString() => Value.ToString("N"); }

public enum FilesLocationKind { Drive, Local, Removable, Network, ConnectedCloud }
public enum HostedItemKind { File, Folder, Artifact, Reference, SymbolicLink }
public enum SyncAvailability
{
	CloudOnly,
	Hydrating,
	AvailableOffline,
	AlwaysAvailable,
	LocalChanges,
	Uploading,
	Synced,
	Conflict,
	Paused,
	Error,
}
public enum FilesOperationState { Pending, Running, Committed, Rejected, Conflict, Cancelled }
public enum FilesTransferState { Queued, Running, Paused, Completed, Failed, Cancelled }
public enum FilesConflictPolicy { Ask, Replace, KeepBoth, Skip }
public enum FilesAccessRole { Viewer, Editor, Owner }
public enum FilesGrantOrigin { Direct, Inherited }
public enum FilesRiskLevel { Ordinary, Elevated, Destructive, SecuritySensitive }
public enum FilesPermissionInheritancePolicy { RecomputeEffectiveGrants, PreserveExplicitGrants, RequireApprovalOnChange }
public enum FilesArtifactType { WriteDocument, Presentation, Spreadsheet, Board, Canvas }
public enum FilesErrorCode
{
	ItemNotFound, RevisionConflict, NameConflict, ConflictRequiresDecision, PermissionDenied,
	ProviderUnavailable, ProviderCapabilityUnsupported, QuotaExceeded, Offline, TransferInterrupted,
	IntegrityFailed, SyncConflict, FileInUse, StorageUnavailable, InvalidName,
	DestinationUnavailable, HomeServiceUnavailable, InvalidCursor, InvalidState,
	PermissionRequired,
}

[Flags]
public enum FilesProviderCapabilities : ulong
{
	None = 0, Read = 1UL << 0, Write = 1UL << 1, Rename = 1UL << 2, Move = 1UL << 3,
	Copy = 1UL << 4, Trash = 1UL << 5, PermanentDelete = 1UL << 6, Versions = 1UL << 7,
	Sharing = 1UL << 8, OfflineAvailability = 1UL << 9, Search = 1UL << 10,
	Streaming = 1UL << 11, ServerSideCopy = 1UL << 12, ServerSideMove = 1UL << 13,
	ChangeFeed = 1UL << 14, ResumableTransfers = 1UL << 15,
}

public sealed record FilesLocation(
	FilesLocationId Id,
	string Name,
	FilesLocationKind Kind,
	FilesProviderCapabilities Capabilities,
	string ProviderId,
	string? CredentialReference = null,
	bool IsAvailable = true,
	string? UnavailableReason = null);

public sealed record HostedItemMetadata(
	HostedItemId Id,
	FilesLocationId LocationId,
	HostedItemId? ParentId,
	string Name,
	HostedItemKind Kind,
	string? ContentType,
	string OwnerPrincipalId,
	string Scope,
	long? SizeBytes,
	DateTimeOffset CreatedAt,
	DateTimeOffset ModifiedAt,
	FilesRevisionId? CurrentRevisionId,
	SyncAvailability Availability,
	bool IsShared,
	string? ContentHash,
	int MetadataSchemaVersion = 1);

public sealed record FilesRevision(
	FilesRevisionId Id,
	HostedItemId ItemId,
	FilesRevisionId? ParentRevisionId,
	DateTimeOffset CreatedAt,
	string ActorId,
	string Source,
	string? ContentHash,
	long? SizeBytes,
	string? OwningAppId,
	string? OwningAppRevisionId,
	bool IsCurrent);

/// <summary>Durable revision boundary supplied by an owning app; Files never merges app semantics.</summary>
public sealed record FilesOwningAppRevisionCommit(
	HostedItemId FileId,
	string OwningAppId,
	string OwningAppRevisionId,
	string ActorId,
	DateTimeOffset CommittedAt,
	long? SizeBytes,
	string? ContentHash,
	string? ProviderContentReference,
	FilesRevisionId? ExpectedBaseRevisionId);

public sealed record FilesHydrationJob(
	FilesTransferId TransferId,
	HostedItemId ItemId,
	SyncAvailability State,
	long? TotalBytes,
	long CompletedBytes,
	bool IsRetryable,
	FilesError? Error);

public sealed record FilesEvictionResult(HostedItemId ItemId, FilesRevisionId VerifiedCloudRevisionId, long FreedBytes, DateTimeOffset EvictedAt);

public sealed record FilesMaterializationProof(
	HostedItemId ItemId,
	FilesRevisionId RemoteRevisionId,
	string ContentHash,
	long SizeBytes,
	DateTimeOffset VerifiedAt);

public sealed record FilesMaterializedFile(
	string LocalPath,
	HostedItemId ItemId,
	FilesRevisionId BaseRemoteRevisionId,
	FilesRevisionId? CurrentRemoteRevisionId,
	FilesRevisionId? LocalRevisionId,
	string? ContentHash,
	long SizeBytes,
	SyncAvailability State,
	DateTimeOffset MaterializedAt,
	bool IsAlwaysAvailable);

public sealed record FilesOperation(
	FilesOperationId Id,
	string ActorId,
	HostedItemId ItemId,
	HostedItemId? SourceParentId,
	HostedItemId? DestinationParentId,
	string Operation,
	FilesRevisionId? BaseRevisionId,
	FilesRevisionId? ResultRevisionId,
	FilesOperationState State,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	string? InversePayload,
	FilesError? Error,
	long Sequence = 0,
	FilesPermissionInheritancePolicy? PermissionInheritancePolicy = null,
	FilesOperationPayload? Payload = null);

public sealed record FilesOperationPayload(
	string? NewName = null,
	FilesConflictPolicy? ConflictPolicy = null,
	SyncAvailability? Availability = null,
	string? ConflictResolution = null,
	string? DestinationReference = null);

public sealed record FilesTransfer(
	FilesTransferId Id,
	IReadOnlyList<HostedItemId> ItemIds,
	FilesLocationId SourceLocationId,
	FilesLocationId DestinationLocationId,
	HostedItemId? DestinationFolderId,
	long? TotalBytes,
	long CompletedBytes,
	int? TotalItems,
	int CompletedItems,
	FilesTransferState State,
	FilesConflictPolicy ConflictPolicy,
	bool IsRetryable,
	IReadOnlyList<FilesError> Errors,
	DateTimeOffset UpdatedAt);

public sealed record FilesQuota(long? AllocationBytes, long UsedBytes, long ReservedBytes);

public sealed record FilesFolderPresentation(HostedItemId FolderId, string? ColorHex, DateTimeOffset UpdatedAt, string UpdatedByPrincipalId)
{
	public static string ValidateColor(string colorHex)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
		if (colorHex.Length != 7 || colorHex[0] != '#' || !colorHex.AsSpan(1).ToString().All(Uri.IsHexDigit))
			throw new ArgumentException("Folder colour must be a six-digit RGB value such as #4477AA.", nameof(colorHex));
		return colorHex.ToUpperInvariant();
	}
}

public sealed record FilesPrincipalGrant(
	string PrincipalId,
	FilesAccessRole Role,
	FilesGrantOrigin Origin,
	HostedItemId GrantedOnItemId,
	DateTimeOffset GrantedAt,
	string GrantedByPrincipalId);

public sealed record FilesPermissionChangePreview(
	HostedItemId ItemId,
	HostedItemId? SourceParentId,
	HostedItemId? DestinationParentId,
	FilesPermissionInheritancePolicy Policy,
	IReadOnlyList<FilesPrincipalGrant> CurrentEffectiveGrants,
	IReadOnlyList<FilesPrincipalGrant> ResultingEffectiveGrants,
	IReadOnlyList<string> ChangedPrincipalIds,
	bool RequiresApproval);

public sealed record FilesShare(
	FilesShareId Id,
	HostedItemId ItemId,
	string PrincipalId,
	FilesAccessRole Role,
	DateTimeOffset CreatedAt,
	DateTimeOffset? ExpiresAt,
	string? PasswordCredentialReference,
	bool IsRevoked);

public sealed record FilesError(
	FilesErrorCode Code,
	string Message,
	string Action,
	string? TargetId,
	bool IsRecoverable,
	bool CanRetry,
	TimeSpan? RetryAfter = null,
	IReadOnlyDictionary<string, string>? Details = null);

public sealed record FilesResult<T>(T? Value, FilesError? Error)
{
	public bool IsSuccess => Error is null;
	public static FilesResult<T> Success(T value) => new(value, null);
	public static FilesResult<T> Failure(FilesError error) => new(default, error);
}

public sealed record FilesChangeEvent(
	string EventId,
	FilesChangeCursor Cursor,
	FilesOperationId? OperationId,
	HostedItemId ItemId,
	string ActorId,
	string Kind,
	FilesRevisionId? BaseRevisionId,
	FilesRevisionId? ResultRevisionId,
	DateTimeOffset OccurredAt,
	HostedItemMetadata? Metadata);

public sealed record FilesConflict(
	string ConflictId,
	HostedItemId ItemId,
	string Kind,
	FilesRevisionId? LocalRevisionId,
	FilesRevisionId? RemoteRevisionId,
	FilesOperationId? OperationId,
	DateTimeOffset DetectedAt,
	string? Resolution);

public sealed record FilesSyncState(
	bool IsOnline,
	bool IsPaused,
	FilesChangeCursor? Cursor,
	int PendingOperations,
	int PendingUploads,
	int PendingDownloads,
	int Conflicts,
	long PinnedBytes,
	long CacheBytes,
	DateTimeOffset? LastServerContact,
	FilesError? LastError);

public sealed record FilesSearchQuery(string Text, FilesLocationId? LocationId = null, HostedItemId? ParentId = null, int Limit = 50, string? PageToken = null);
public sealed record FilesPage<T>(IReadOnlyList<T> Items, string? NextPageToken);

public sealed record FilesArtifactCreateRequest(
	HostedItemId? ParentFolderId,
	FilesArtifactType ArtifactType,
	IReadOnlyDictionary<string, System.Text.Json.JsonElement> Configuration);

public sealed record FilesArtifactReference(
	string OwnerAppId,
	string ArtifactId,
	HostedItemId FileId,
	HostedItemId? ParentFolderId,
	string ArtifactType,
	string DisplayName);

public sealed record FilesStackCreateRequest(HostedItemId FolderId, bool RegisterExistingSource, string? ProjectName = null);
public sealed record FilesStackProjectReference(string StackProjectId, HostedItemId BackingFolderId, bool RegisteredExistingFolder);

public sealed record FilesAiItemContext(HostedItemId ItemId, string ArtifactType, SyncAvailability SyncState, FilesRevisionId? RevisionId, bool IsPrivateLocal);

/// <summary>Metadata-only selection context. File bytes are requested separately through a permission-checked action.</summary>
public sealed record FilesAiContextSnapshot(
	FilesLocationId LocationId,
	IReadOnlyList<FilesAiItemContext> Items,
	IReadOnlyList<FilesPrincipalGrant> EffectivePermissions,
	bool ContainsPrivateLocalContent);

public sealed record FilesPreviewDescriptor(
	HostedItemId ItemId,
	FilesRevisionId? RevisionId,
	string MediaType,
	string RendererId,
	bool IsReadOnly,
	string? ThumbnailReference);
public sealed record FilesActionDefinition(
	string Name,
	string ArgumentsSchema,
	string ResultSchema,
	IReadOnlyList<string> RequiredPermissions,
	FilesRiskLevel Risk,
	bool IsReversible,
	bool HasExternalSideEffects,
	string AffectedObjects);

public interface IFilesProvider
{
	FilesLocation Location { get; }
	Task<FilesPage<HostedItemMetadata>> ListAsync(HostedItemId? parentId, FilesSearchQuery? query, string? pageToken, CancellationToken cancellationToken);
	Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken cancellationToken);
	Task<FilesResult<FilesOperation>> MutateAsync(FilesOperation operation, string? newName, CancellationToken cancellationToken);
	IAsyncEnumerable<FilesChangeEvent> SubscribeAsync(FilesChangeCursor? after, CancellationToken cancellationToken);
	Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? after, int limit, CancellationToken cancellationToken);
}

/// <summary>Canonical Files domain surface. UI and automation adapters must call the same operations.</summary>
public interface IFilesService
{
	Task<FilesPage<FilesLocation>> ListLocationsAsync(string? pageToken, CancellationToken cancellationToken);
	Task<FilesResult<FilesProviderCapabilities>> GetProviderCapabilitiesAsync(FilesLocationId locationId, CancellationToken cancellationToken);
	Task<FilesPage<HostedItemMetadata>> ListAsync(FilesLocationId locationId, HostedItemId? parentId, FilesSearchQuery? query, string? pageToken, CancellationToken cancellationToken);
	Task<FilesResult<HostedItemMetadata>> GetAsync(HostedItemId itemId, CancellationToken cancellationToken);
	Task<FilesResult<FilesOperation>> CreateFolderAsync(HostedItemId? parentId, string name, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesPermissionChangePreview> PreviewMovePermissionsAsync(HostedItemId itemId, HostedItemId? destinationFolderId, FilesPermissionInheritancePolicy policy, CancellationToken cancellationToken);
	Task<FilesResult<FilesOperation>> RenameAsync(HostedItemId itemId, string name, FilesRevisionId? expectedRevision, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesResult<FilesOperation>> MoveAsync(HostedItemId itemId, HostedItemId? destinationFolderId, FilesRevisionId? expectedRevision, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesSyncState> GetSyncStateAsync(CancellationToken cancellationToken);
	Task<FilesPage<FilesOperation>> ListPendingOperationsAsync(string? pageToken, CancellationToken cancellationToken);
	Task<FilesPage<FilesConflict>> ListConflictsAsync(string? pageToken, CancellationToken cancellationToken);
	Task<FilesPage<FilesChangeEvent>> GetChangesAsync(FilesChangeCursor? cursor, int limit, CancellationToken cancellationToken);
	IAsyncEnumerable<FilesChangeEvent> SubscribeChangesAsync(FilesChangeCursor? cursor, CancellationToken cancellationToken);
	Task<FilesResult<FilesOperation>> CopyAsync(HostedItemId itemId, HostedItemId? destinationFolderId, FilesConflictPolicy? policy, FilesRevisionId? expectedRevision, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesResult<FilesOperation>> DeleteAsync(HostedItemId itemId, FilesRevisionId? expectedRevision, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesResult<FilesOperation>> RestoreAsync(HostedItemId itemId, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesResult<FilesOperation>> PurgeAsync(HostedItemId itemId, FilesRevisionId? expectedRevision, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesResult<FilesArtifactReference>> CreateArtifactAsync(FilesArtifactCreateRequest request, CancellationToken cancellationToken);
	Task<FilesResult<FilesStackProjectReference>> CreateStackAsync(FilesStackCreateRequest request, CancellationToken cancellationToken);
	Task<FilesResult<FilesTransfer>> UploadAsync(HostedItemId? destinationFolderId, string sourceReference, FilesConflictPolicy? policy, string actorId, CancellationToken cancellationToken);
	Task<FilesResult<FilesTransfer>> DownloadAsync(HostedItemId itemId, string destinationReference, CancellationToken cancellationToken);
	Task<FilesResult<FilesTransfer>> GetTransferAsync(FilesTransferId transferId, CancellationToken cancellationToken);
	Task<FilesResult<FilesTransfer>> PauseTransferAsync(FilesTransferId transferId, CancellationToken cancellationToken);
	Task<FilesResult<FilesTransfer>> ResumeTransferAsync(FilesTransferId transferId, CancellationToken cancellationToken);
	Task<FilesResult<FilesTransfer>> CancelTransferAsync(FilesTransferId transferId, CancellationToken cancellationToken);
	Task<FilesSyncState> SyncNowAsync(CancellationToken cancellationToken);
	Task<FilesSyncState> SyncItemAsync(HostedItemId itemId, CancellationToken cancellationToken);
	Task<FilesSyncState> SyncFolderAsync(HostedItemId folderId, CancellationToken cancellationToken);
	Task<FilesSyncState> PauseSyncAsync(CancellationToken cancellationToken);
	Task<FilesSyncState> ResumeSyncAsync(CancellationToken cancellationToken);
	Task<FilesResult<HostedItemMetadata>> SetAvailabilityAsync(HostedItemId itemId, SyncAvailability availability, CancellationToken cancellationToken);
	Task<FilesPage<FilesRevision>> GetVersionsAsync(HostedItemId itemId, string? pageToken, CancellationToken cancellationToken);
	Task<FilesResult<HostedItemMetadata>> RestoreVersionAsync(HostedItemId itemId, FilesRevisionId versionId, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesPage<FilesPrincipalGrant>> GetGrantsAsync(HostedItemId itemId, string? pageToken, CancellationToken cancellationToken);
	Task<FilesResult<FilesShare>> GrantAccessAsync(HostedItemId itemId, string principalId, FilesAccessRole role, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesResult<bool>> RevokeAccessAsync(HostedItemId itemId, string principalId, FilesOperationId operationId, string actorId, CancellationToken cancellationToken);
	Task<FilesResult<FilesFolderPresentation>> SetFolderColorAsync(HostedItemId folderId, string? colorHex, string actorId, CancellationToken cancellationToken);
	Task<FilesPage<HostedItemMetadata>> SearchAsync(FilesSearchQuery query, string? pageToken, CancellationToken cancellationToken);
	Task<FilesResult<FilesQuota>> GetQuotaAsync(FilesLocationId locationId, CancellationToken cancellationToken);
	Task<FilesPage<FilesActionDefinition>> GetActionCatalogAsync(string? pageToken, CancellationToken cancellationToken);
	Task<FilesResult<FilesAiContextSnapshot>> GetAiContextAsync(FilesLocationId locationId, IReadOnlyList<HostedItemId> selectedItemIds, CancellationToken cancellationToken);
	Task<FilesResult<FilesPreviewDescriptor>> GetPreviewAsync(HostedItemId itemId, FilesRevisionId? revisionId, CancellationToken cancellationToken);
	Task<FilesResult<FilesHydrationJob>> HydrateAsync(HostedItemId itemId, CancellationToken cancellationToken);
	Task<FilesResult<FilesEvictionResult>> FreeUpSpaceAsync(HostedItemId itemId, FilesRevisionId expectedCloudRevisionId, CancellationToken cancellationToken);
}

public interface IFilesOwningAppRevisionSink
{
	Task<FilesResult<FilesRevision>> CommitDurableRevisionAsync(FilesOwningAppRevisionCommit commit, CancellationToken cancellationToken);
}

/// <summary>Routes creation to the canonical owning app; Files never fabricates app-specific internals.</summary>
public interface IFilesOwningAppCreationRouter
{
	Task<FilesResult<FilesArtifactReference>> CreateArtifactAsync(FilesArtifactCreateRequest request, CancellationToken cancellationToken);
	Task<FilesResult<FilesStackProjectReference>> CreateStackAsync(FilesStackCreateRequest request, CancellationToken cancellationToken);
}

public static class FilesProviderCapabilityExtensions
{
	public static bool Supports(this FilesProviderCapabilities capabilities, FilesProviderCapabilities operation) =>
		operation != FilesProviderCapabilities.None && (capabilities & operation) == operation;

	public static FilesResult<T> Unsupported<T>(FilesLocation location, string action) =>
		FilesResult<T>.Failure(new FilesError(
			FilesErrorCode.ProviderCapabilityUnsupported,
			$"Provider '{location.ProviderId}' does not support {action}.",
			action,
			location.Id.ToString(),
			false,
			false));
}
